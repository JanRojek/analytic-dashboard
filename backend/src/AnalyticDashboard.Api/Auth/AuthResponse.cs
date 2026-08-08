namespace AnalyticDashboard.Api.Auth;
    
public record AuthResponse(string Token, DateTime ExpiresAt);