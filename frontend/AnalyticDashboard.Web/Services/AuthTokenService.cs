using Microsoft.JSInterop;

namespace AnalyticDashboard.Web.Services;

public class AuthTokenService
{
    private readonly IJSRuntime _js;

    public AuthTokenService(IJSRuntime js)
    {
        _js = js;
    }

    public async Task SaveTokenAsync(string token, DateTime expiresAt)
    {
        await _js.InvokeVoidAsync("localStorage.setItem", "authToken", token);
        await _js.InvokeVoidAsync("localStorage.setItem", "authExpiresAt", expiresAt.ToString("O"));
    }

    public async Task<string?> GetTokenAsync()
    {
        return await _js.InvokeAsync<string?>("localStorage.getItem", "authToken");
    }

    public async Task ClearTokenAsync()
    {
        await _js.InvokeVoidAsync("localStorage.removeItem", "authToken");
        await _js.InvokeVoidAsync("localStorage.removeItem", "authExpiresAt");
    }

    public async Task<bool> IsLoggedInAsync()
    {
        var token = await GetTokenAsync();
        return !string.IsNullOrWhiteSpace(token);
    }
}