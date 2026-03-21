using AnalyticDashboard.Domain.Entities;
using AnalyticDashboard.Domain.Repositories;
using AnalyticDashboard.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AnalyticDashboard.Infrastructure.Repositories;

public sealed class DatasetRepository : IDatasetRepository
{
    private readonly AppDbContext _dbContext;

    public DatasetRepository(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AddAsync(Dataset dataset, CancellationToken cancellationToken)
    {
        await _dbContext.Datasets.AddAsync(dataset, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Dataset>> GetAllAsync(CancellationToken cancellationToken)
    {
        return await _dbContext.Datasets
            .AsNoTracking()
            .OrderByDescending(d => d.CreatedAtUtc)
            .ToListAsync(cancellationToken);
    }

    public async Task<Dataset?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        return await _dbContext.Datasets
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var dataset = await _dbContext.Datasets
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

        if (dataset == null)
        {
            return false;
        }
        
        _dbContext.Datasets.Remove(dataset);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }
}