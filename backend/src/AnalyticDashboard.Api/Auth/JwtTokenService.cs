using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using AnalyticDashboard.Domain.Entities;
using Microsoft.IdentityModel.Tokens;

namespace AnalyticDashboard.Api.Auth;

public class JwtTokenService
{
    private readonly IConfiguration _configuration;

    public JwtTokenService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public (string Token, DateTime ExpiresAt) GenerateToken(User user)
    {
        var key = _configuration["Jwt:Key"];
        var keyBytes = Encoding.UTF8.GetBytes(key!);
        var securityKey = new SymmetricSecurityKey(keyBytes);
        
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Username)
        };

        var credentials = new SigningCredentials(
            securityKey,
            SecurityAlgorithms.HmacSha256
        );
        
        var expiresAt = DateTime.UtcNow.AddMinutes(30);
        
        var token = new JwtSecurityToken(
            issuer: _configuration["Jwt:Issuer"],
            audience: _configuration["Jwt:Audience"],
            claims: claims,
            expires: expiresAt,
            signingCredentials: credentials);

        var tokenString = new JwtSecurityTokenHandler().WriteToken(token);

        return (tokenString, expiresAt);
    }
}