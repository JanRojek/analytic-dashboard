using System.Net;
using System.Net.Http.Json;

using AnalyticDashboard.Infrastructure.Data;

using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AnalyticDashboard.IntegrationTests.Auth;

public sealed class AuthRateLimitingEndpointTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;

    private static CancellationToken CancellationToken =>
        TestContext.Current.CancellationToken;

    public AuthRateLimitingEndpointTests(ApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Theory]
    [InlineData("login")]
    [InlineData("register")]
    [InlineData("forgot-password")]
    [InlineData("resend-confirmation")]
    public async Task Auth_ShouldLimitEachEndpoint_WithoutTrustingForwardedHeader(string endpoint)
    {
        await using var scope = _fixture.RealAuthServices.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var factory = new CustomWebApplicationFactory(
            context.Database.GetConnectionString()!,
            useTestAuthentication: false,
            authPermitLimit: 2
        );
        using var client = factory.CreateClient();

        for (var attempt = 0; attempt < 3; attempt++)
        {
            using (var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"/auth/{endpoint}"
            ))
            {
                request.Content = JsonContent.Create(new
                {
                    email = $"absent-{Guid.NewGuid()}@example.com",
                    password = "invalid",
                    displayName = "Rate limit test",
                    rememberMe = false
                });

                request.Headers.Add(
                    "X-Forwarded-For",
                    $"192.0.2.{attempt + 1}"
                );

                using var response = await client.SendAsync(request, CancellationToken);

                if (attempt < 2)
                {
                    Assert.NotEqual(HttpStatusCode.TooManyRequests, response.StatusCode);
                    continue;
                }

                Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
                Assert.NotNull(response.Headers.RetryAfter);
                var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(CancellationToken);
                Assert.NotNull(problem);
                Assert.Equal(429, problem.Status);
            }
        }

        var otherEndpoint = endpoint == "forgot-password" ? "resend-confirmation" : "forgot-password";
        using var otherResponse = await client.PostAsJsonAsync(
            $"/auth/{otherEndpoint}",
            new { email = "absent@example.com" },
            CancellationToken
        );

        Assert.Equal(HttpStatusCode.NoContent, otherResponse.StatusCode);
    }

    [Fact]
    public async Task Auth_ShouldKeepIndependentLimits_ForDifferentConnectionAddresses()
    {
        await using var scope = _fixture.RealAuthServices.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var factory = new CustomWebApplicationFactory(
            context.Database.GetConnectionString()!,
            useTestAuthentication: false,
            authPermitLimit: 2
        );
        using var client = factory.CreateClient();

        for (var attempt = 0; attempt < 4; attempt++)
        {
            var response = await factory.Server.SendAsync(httpContext =>
            {
                httpContext.Connection.RemoteIpAddress = IPAddress.Parse(
                    attempt < 3 ? "192.0.2.1" : "192.0.2.2"
                );
                httpContext.Request.Scheme = "https";
                httpContext.Request.Method = "POST";
                httpContext.Request.Path = "/auth/forgot-password";
                httpContext.Request.ContentType = "application/json";
                httpContext.Request.Body = new MemoryStream(
                    System.Text.Encoding.UTF8.GetBytes("{\"email\":\"absent@example.com\"}")
                );
                httpContext.Request.ContentLength = httpContext.Request.Body.Length;
                httpContext.Features.Set<IHttpRequestBodyDetectionFeature>(new RequestBodyDetectionFeature());
            }, CancellationToken);

            Assert.Equal(
                attempt == 2 ? 429 : 204,
                response.Response.StatusCode
            );
        }
    }

    private sealed class RequestBodyDetectionFeature : IHttpRequestBodyDetectionFeature
    {
        public bool CanHaveBody => true;
    }
}
