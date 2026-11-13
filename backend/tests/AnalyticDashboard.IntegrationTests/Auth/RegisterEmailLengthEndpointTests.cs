using System.Net;
using System.Net.Http.Json;

using AnalyticDashboard.Api.Contracts.Auth;
using AnalyticDashboard.Infrastructure.Data;

using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AnalyticDashboard.IntegrationTests.Auth;

public sealed class RegisterEmailLengthEndpointTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;

    private static CancellationToken CancellationToken =>
        TestContext.Current.CancellationToken;

    public RegisterEmailLengthEndpointTests(ApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Theory]
    [InlineData(255)]
    [InlineData(256)]
    [InlineData(257)]
    public async Task Register_ShouldRespectEmailColumnLimit(int length)
    {
        using var client = _fixture.CreateRealAuthClient();
        var suffix = $"-{Guid.NewGuid():N}@example.com";
        var email = new string('a', length - suffix.Length) + suffix;
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/auth/register"
        );
        request.Content = JsonContent.Create(new RegisterUserRequest(
            email,
            "Email boundary test",
            "Valid-password!123"
        ));

        using var response = await client.SendAsync(request, CancellationToken);

        Assert.Equal(length <= 256 ? HttpStatusCode.Created : HttpStatusCode.BadRequest, response.StatusCode);
        await using var scope = _fixture.RealAuthServices.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await dbContext.Users.SingleOrDefaultAsync(
            user => user.Email == email,
            CancellationToken
        );
        if (length <= 256)
        {
            Assert.NotNull(user);
            Assert.Equal(email, user.UserName);
            Assert.Equal(email.ToUpperInvariant(), user.NormalizedEmail);
            Assert.Equal(email.ToUpperInvariant(), user.NormalizedUserName);
        }
        else
        {
            Assert.Null(user);
            var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(CancellationToken);
            Assert.NotNull(problem);
            Assert.Equal("Invalid email.", problem.Title);
            Assert.Equal("Email cannot be longer than 256 characters.", problem.Detail);
        }
    }

    [Theory]
    [InlineData("🚀", 257)]
    [InlineData("\u0344", 130)]
    public async Task Register_ShouldRejectOversizedUnicodeOrNormalizedEmail_WithoutSaving(
        string text,
        int count)
    {
        using var client = _fixture.CreateRealAuthClient();
        var email = string.Concat(Enumerable.Repeat(text, count)) + "@example.com";
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/auth/register"
        );
        request.Content = JsonContent.Create(new RegisterUserRequest(
            email,
            "Unicode email test",
            "Valid-password!123"
        ));

        using var response = await client.SendAsync(request, CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(CancellationToken);
        Assert.NotNull(problem);
        Assert.Equal("Invalid email.", problem.Title);
        Assert.Equal("Email cannot be longer than 256 characters.", problem.Detail);
        await using var scope = _fixture.RealAuthServices.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await dbContext.Users.AnyAsync(user => user.Email == email, CancellationToken));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Register_ShouldValidateEachNormalizedColumn(bool normalizeUserName)
    {
        await using var scope = _fixture.RealAuthServices.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var baseFactory = new CustomWebApplicationFactory(
            dbContext.Database.GetConnectionString()!,
            useTestAuthentication: false
        );
        await using var factory = baseFactory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ILookupNormalizer>();
            services.AddSingleton<ILookupNormalizer>(new ExpandingNormalizer(normalizeUserName));
        }));
        using var client = factory.CreateClient();
        var email = $"normalization-{Guid.NewGuid():N}@example.com";
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/auth/register"
        );
        request.Content = JsonContent.Create(new RegisterUserRequest(
            email,
            "Normalization boundary test",
            "Valid-password!123"
        ));

        using var response = await client.SendAsync(request, CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(CancellationToken);
        Assert.NotNull(problem);
        Assert.Equal("Invalid email.", problem.Title);
        Assert.Equal("Email cannot be longer than 256 characters.", problem.Detail);
        Assert.False(await dbContext.Users.AnyAsync(user => user.Email == email, CancellationToken));
    }

    [Fact]
    public async Task Register_ShouldPreserveIdentityCharacterRules_ForUnicodeWithinLengthLimit()
    {
        using var client = _fixture.CreateRealAuthClient();
        var email = string.Concat(Enumerable.Repeat("🚀", 128)) + "@example.com";
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/auth/register"
        );
        request.Content = JsonContent.Create(new RegisterUserRequest(
            email,
            "Unicode character rules test",
            "Valid-password!123"
        ));

        using var response = await client.SendAsync(request, CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(CancellationToken);
        Assert.NotNull(problem);
        Assert.Equal("Invalid email.", problem.Title);
        Assert.NotNull(problem.Detail);
        Assert.NotEqual("Email cannot be longer than 256 characters.", problem.Detail);
        await using var scope = _fixture.RealAuthServices.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await dbContext.Users.AnyAsync(user => user.Email == email, CancellationToken));
    }

    private sealed class ExpandingNormalizer : ILookupNormalizer
    {
        private readonly bool _normalizeUserName;
        private readonly UpperInvariantLookupNormalizer _normalizer = new();

        public ExpandingNormalizer(bool normalizeUserName)
        {
            _normalizeUserName = normalizeUserName;
        }

        public string? NormalizeEmail(string? email) => _normalizeUserName
            ? _normalizer.NormalizeEmail(email)
            : new string('E', 257);

        public string? NormalizeName(string? name) => _normalizeUserName
            ? new string('N', 257)
            : _normalizer.NormalizeName(name);
    }
}
