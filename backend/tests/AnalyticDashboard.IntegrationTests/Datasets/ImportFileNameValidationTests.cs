using System.Net;
using System.Net.Http.Headers;
using System.Text;

using AnalyticDashboard.Application.Storage;
using AnalyticDashboard.Domain.Entities;
using AnalyticDashboard.Infrastructure.Data;

using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AnalyticDashboard.IntegrationTests.Datasets;

public sealed class ImportFileNameValidationTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;

    private static CancellationToken CancellationToken =>
        TestContext.Current.CancellationToken;

    public ImportFileNameValidationTests(ApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Theory]
    [InlineData("EmptyFileName")]
    [InlineData("EmptyDatasetName")]
    [InlineData("Whitespace")]
    [InlineData("TooLong")]
    [InlineData("TooLongUnicode")]
    [InlineData("TooLongFileName")]
    public async Task Import_ShouldRejectInvalidName_WithoutRecordsOrFileAccess(string scenario)
    {
        var fileName = scenario switch
        {
            "EmptyFileName" => "",
            "EmptyDatasetName" => ".csv",
            "Whitespace" => "   .csv",
            "TooLong" => new string('a', 201) + ".csv",
            "TooLongUnicode" => string.Concat(Enumerable.Repeat("🚀", 201)) + ".csv",
            "TooLongFileName" => new string('a', 200) + new string(' ', 52) + ".csv",
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
        var ownerId = Guid.NewGuid();
        var project = new Project(ownerId, "Invalid import name");
        await using var scope = _fixture.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        dbContext.Projects.Add(project);
        await dbContext.SaveChangesAsync(CancellationToken);
        await using var baseFactory = new CustomWebApplicationFactory(dbContext.Database.GetConnectionString()!);
        await using var factory = baseFactory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IFileStorage>();
            services.AddSingleton<IFileStorage, ForbiddenFileStorage>();
        }));
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/projects/{project.Id}/datasets/import/csv"
        );
        using var multipart = new MultipartFormDataContent();
        using var file = new ByteArrayContent(Encoding.UTF8.GetBytes("Name,Amount\nA,1\n"));
        file.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data");
        file.Headers.ContentDisposition.Name = "\"file\"";
        file.Headers.ContentDisposition.FileName = $"\"{fileName}\"";
        multipart.Add(file);
        request.Content = multipart;
        request.Headers.Add(TestAuthHandler.UserIdHeader, ownerId.ToString());

        using var response = await client.SendAsync(request, CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(await dbContext.Datasets.AnyAsync(CancellationToken));
        Assert.False(await dbContext.DatasetVersions.AnyAsync(CancellationToken));
        Assert.False(await dbContext.ImportJobs.AnyAsync(CancellationToken));
    }

    private sealed class ForbiddenFileStorage : IFileStorage
    {
        public Task SaveAsync(string storageKey, Stream content, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Invalid names must not access file storage.");

        public Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Invalid names must not access file storage.");

        public Task<bool> ExistsAsync(string storageKey, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Invalid names must not access file storage.");

        public Task DeleteAsync(string storageKey, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Invalid names must not access file storage.");

        public Task DeleteDirectoryAsync(string storageKey, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Invalid names must not access file storage.");
    }
}
