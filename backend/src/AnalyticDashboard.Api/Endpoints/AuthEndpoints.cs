using AnalyticDashboard.Api.Auth;
using AnalyticDashboard.Application.Auth.Login;
using AnalyticDashboard.Application.Auth.Register;

namespace AnalyticDashboard.Api.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/auth/register", async (
            RegisterRequest request,
            RegisterUserHandler handler,
            JwtTokenService tokenService,
            CancellationToken cancellationToken) =>
        {
            var command = new RegisterUserCommand(
                request.Username,  
                request.Password
            );
            
            var user = await handler.Handle(command, cancellationToken);

            if (user is null)
            {
                return Results.BadRequest("Username already exists");
            }

            var result = tokenService.GenerateToken(user);

            return Results.Created("/auth/register", new AuthResponse(result.Token, result.ExpiresAt));
        })
        .WithName("Register")
        .WithTags("Auth");
        
        app.MapPost("/auth/login", async (
            LoginRequest request,
            LoginUserHandler handler,
            JwtTokenService tokenService,
            CancellationToken cancellationToken) =>
        {
            var command = new LoginUserCommand(
                request.Username,
                request.Password
            );
            
            var user = await handler.Handle(command, cancellationToken);

            if (user is null)
            {
                return Results.BadRequest("Invalid username or password");
            }
            
            var result = tokenService.GenerateToken(user);
            
            return Results.Ok(new AuthResponse(result.Token, result.ExpiresAt));
        })
        .WithName("Login")
        .WithTags("Auth");
        
        return app;
    }
}