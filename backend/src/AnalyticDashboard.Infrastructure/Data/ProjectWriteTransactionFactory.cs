using System.Data;

using AnalyticDashboard.Application.Common.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace AnalyticDashboard.Infrastructure.Data;

public sealed class ProjectWriteTransactionFactory : IProjectWriteTransactionFactory
{
    private readonly AppDbContext _dbContext;

    public ProjectWriteTransactionFactory(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IProjectWriteTransaction?> BeginAsync(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var transaction = await _dbContext.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken
        );

        try
        {
            var projects = await _dbContext.Projects
                .FromSqlInterpolated($"""
                    SELECT project.*
                    FROM projects AS project
                    WHERE project."Id" = {projectId}
                    FOR NO KEY UPDATE OF project
                    """)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            if (projects.Count == 0)
            {
                await transaction.DisposeAsync();
                return null;
            }

            return new ProjectWriteTransaction(transaction);
        }
        catch
        {
            await transaction.DisposeAsync();
            throw;
        }
    }

    private sealed class ProjectWriteTransaction : IProjectWriteTransaction
    {
        private readonly IDbContextTransaction _transaction;

        public ProjectWriteTransaction(IDbContextTransaction transaction)
        {
            _transaction = transaction;
        }

        public Task CommitAsync(CancellationToken cancellationToken)
        {
            return _transaction.CommitAsync(cancellationToken);
        }

        public ValueTask DisposeAsync()
        {
            return _transaction.DisposeAsync();
        }
    }
}
