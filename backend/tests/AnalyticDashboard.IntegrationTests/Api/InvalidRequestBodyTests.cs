using System.Net;
using System.Text;

namespace AnalyticDashboard.IntegrationTests.Api;

public sealed class InvalidRequestBodyTests
    : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;

    private static CancellationToken CancellationToken =>
        TestContext.Current.CancellationToken;

    public InvalidRequestBodyTests(ApiFixture fixture)
    {
        _fixture = fixture;
    }

    private static HttpRequestMessage CreateRawPostRequest(
        string path,
        string json)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            path
        );

        request.Content = new StringContent(
            json,
            Encoding.UTF8,
            "application/json"
        );

        return request;
    }

    [Fact]
    public async Task JsonEndpoint_ShouldReturnBadRequest_WhenRequestBodyContainsMalformedJson()
    {
        using var request = CreateRawPostRequest(
            "/auth/resend-confirmation",
            "{ email: null }"
        );

        using var response = await _fixture.Client.SendAsync(
            request,
            CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode
        );
    }
}
