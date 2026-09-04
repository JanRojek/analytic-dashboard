using Microsoft.AspNetCore.DataProtection;
using System.Security.Cryptography;

namespace AnalyticDashboard.Api.Auth;

public sealed class RegistrationSessionService : IRegistrationSessionService
{
    private readonly IHttpContextAccessor _accessor;
    private readonly ITimeLimitedDataProtector _protector;
    private readonly TimeSpan _sessionLifetime = TimeSpan.FromMinutes(30);

    private const string CookieName = "registration_session";

    private HttpContext HttpContext =>
        _accessor.HttpContext
        ?? throw new InvalidOperationException("No access to HTTP context.");

    public RegistrationSessionService(
        IHttpContextAccessor accessor,
        IDataProtectionProvider provider)
    {
        _accessor = accessor;

        _protector = provider
            .CreateProtector(
                "AnalyticDashboard.Auth.RegistrationSession"
            )
            .ToTimeLimitedDataProtector();
    }

    public void Create(Guid userId)
    {
        var encryptedData = _protector.Protect(
            userId.ToString(),
            _sessionLifetime
        );

        var cookieOptions = new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            Path = "/auth",
            MaxAge = _sessionLifetime
        };

        HttpContext.Response.Cookies.Append(
            CookieName,
            encryptedData,
            cookieOptions
        );
    }

    public bool TryGetUserId(out Guid userId)
    {
        userId = Guid.Empty;

        if (!HttpContext.Request.Cookies.TryGetValue(
                CookieName,
                out var encryptedData))
        {
            return false;
        }

        try
        {
            var rawUserId = _protector.Unprotect(
                encryptedData
            );

            if (!Guid.TryParse(
                    rawUserId,
                    out userId
                ) ||
                userId == Guid.Empty)
            {
                Delete();
                userId = Guid.Empty;

                return false;
            }

            return true;
        }
        catch (Exception ex)
            when (ex is CryptographicException or FormatException)
        {
            Delete();

            return false;
        }
    }

    public void Delete()
    {
        HttpContext.Response.Cookies.Delete(
            CookieName,
            new CookieOptions { Path = "/auth" }
        );
    }
}
