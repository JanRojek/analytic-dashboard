using System.Diagnostics;
using System.Security.Claims;
using AnalyticDashboard.Api.Auth;
using AnalyticDashboard.Api.Contracts.Auth;
using AnalyticDashboard.Application.Auth.Accounts;
using AnalyticDashboard.Application.Auth.CompleteRegistration;
using AnalyticDashboard.Application.Auth.ConfirmEmail;
using AnalyticDashboard.Application.Auth.CurrentUser;
using AnalyticDashboard.Application.Auth.ForgotPassword;
using AnalyticDashboard.Application.Auth.Login;
using AnalyticDashboard.Application.Auth.Logout;
using AnalyticDashboard.Application.Auth.Register;
using AnalyticDashboard.Application.Auth.RegistrationStatus;
using AnalyticDashboard.Application.Auth.ResendConfirmation;
using AnalyticDashboard.Application.Auth.ResetPassword;

namespace AnalyticDashboard.Api.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(
        this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/auth")
            .WithTags("Auth")
            .ProducesProblem(
                StatusCodes.Status500InternalServerError
            );

        auth.MapPost("/register", async (
            RegisterUserRequest request,
            RegisterUserHandler handler,
            IRegistrationSessionService registrationSession,
            CancellationToken cancellationToken) =>
        {
            var command = new RegisterUserCommand(
                request.Email,
                request.DisplayName,
                request.Password
            );

            var result = await handler.HandleAsync(
                command,
                cancellationToken
            );

            if (result is not RegisterUserResult.Success success)
            {
                return result switch
                {
                    RegisterUserResult.InvalidDisplayName invalidDisplayName =>
                        Results.Problem(
                            statusCode: StatusCodes.Status400BadRequest,
                            title: "Invalid display name.",
                            detail: invalidDisplayName.Message
                        ),

                    RegisterUserResult.InvalidEmail invalidEmail =>
                        Results.Problem(
                            statusCode: StatusCodes.Status400BadRequest,
                            title: "Invalid email.",
                            detail: invalidEmail.Message
                        ),

                    RegisterUserResult.InvalidPassword invalidPassword =>
                        Results.Problem(
                            statusCode: StatusCodes.Status400BadRequest,
                            title: "Invalid password.",
                            detail: string.Join(
                                " ",
                                invalidPassword.Messages
                            )
                        ),

                    RegisterUserResult.EmailAlreadyExists =>
                        Results.Problem(
                            statusCode: StatusCodes.Status409Conflict,
                            title: "Email already exists.",
                            detail: "An account with this email already exists."
                        ),

                    _ => throw new UnreachableException()
                };
            }

            registrationSession.Create(
                success.Id
            );

            return Results.Created(
                uri: (string?)null,
                value: new RegisterUserResponse(
                    success.Id,
                    success.Email,
                    success.DisplayName,
                    success.CreatedAtUtc
                )
            );
        })
        .WithName("RegisterUser")
        .AllowAnonymous()
        .Produces<RegisterUserResponse>(
            StatusCodes.Status201Created
        )
        .ProducesProblem(
            StatusCodes.Status400BadRequest
        )
        .ProducesProblem(
            StatusCodes.Status409Conflict
        );

        auth.MapPost("/confirm-email", async (
            ConfirmEmailRequest request,
            ConfirmEmailHandler handler,
            CancellationToken cancellationToken) =>
        {
            var command = new ConfirmEmailCommand(
                request.UserId,
                request.Token
            );

            var result = await handler.HandleAsync(
                command,
                cancellationToken
            );

            return result switch
            {
                UserEmailConfirmationResult.Success =>
                    Results.NoContent(),

                UserEmailConfirmationResult.AlreadyConfirmed =>
                    Results.NoContent(),

                UserEmailConfirmationResult.InvalidToken =>
                    Results.Problem(
                        statusCode: StatusCodes.Status400BadRequest,
                        title: "Invalid confirmation link.",
                        detail: "The email confirmation link is invalid or has expired."
                    ),

                UserEmailConfirmationResult.UserNotFound =>
                    Results.Problem(
                        statusCode: StatusCodes.Status400BadRequest,
                        title: "Invalid confirmation link.",
                        detail: "The email confirmation link is invalid or has expired."
                    ),

                _ => throw new UnreachableException()
            };
        })
        .WithName("ConfirmEmail")
        .AllowAnonymous()
        .Produces(
            StatusCodes.Status204NoContent
        )
        .ProducesProblem(
            StatusCodes.Status400BadRequest
        );

        auth.MapGet("/registration-status", async (
            IRegistrationSessionService registrationSession,
            GetRegistrationStatusHandler handler) =>
        {
            if (!registrationSession.TryGetUserId(out var userId))
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status401Unauthorized,
                    title: "Invalid registration session.",
                    detail: "The registration session is missing, invalid, or expired."
                );
            }

            var query = new GetRegistrationStatusQuery(
                userId
            );

            var result = await handler.HandleAsync(
                query
            );

            if (result is not GetRegistrationStatusResult.UserNotFound)
            {
                return result switch
                {
                    GetRegistrationStatusResult.Pending =>
                        Results.Ok(
                            new GetRegistrationStatusResponse(
                                RegistrationStatus.Pending
                            )
                        ),

                    GetRegistrationStatusResult.Confirmed =>
                        Results.Ok(
                            new GetRegistrationStatusResponse(
                                RegistrationStatus.Confirmed
                            )
                        ),

                    _ => throw new UnreachableException()
                };
            }

            registrationSession.Delete();

            return Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Invalid registration session.",
                detail: "The registration session is missing, invalid, or expired."
            );
        })
        .WithName("GetRegistrationStatus")
        .AllowAnonymous()
        .Produces<GetRegistrationStatusResponse>()
        .ProducesProblem(
            StatusCodes.Status401Unauthorized
        );

        auth.MapPost("/complete-registration", async (
            IRegistrationSessionService registrationSession,
            CompleteRegistrationHandler handler) =>
        {
            if (!registrationSession.TryGetUserId(out var userId))
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status401Unauthorized,
                    title: "Invalid registration session.",
                    detail: "The registration session is missing, invalid, or expired."
                );
            }

            var command = new CompleteRegistrationCommand(
                userId
            );

            var result = await handler.HandleAsync(
                command
            );

            switch (result)
            {
                case UserSignInResult.Success:
                    registrationSession.Delete();

                    return Results.NoContent();

                case UserSignInResult.EmailNotConfirmed:
                    return Results.Problem(
                        statusCode: StatusCodes.Status409Conflict,
                        title: "Email not confirmed.",
                        detail: "The email address must be confirmed before registration can be completed."
                    );

                case UserSignInResult.UserNotFound:
                    registrationSession.Delete();

                    return Results.Problem(
                        statusCode: StatusCodes.Status401Unauthorized,
                        title: "Invalid registration session.",
                        detail: "The registration session is missing, invalid, or expired."
                    );

                default:
                    throw new UnreachableException();
            }
        })
        .WithName("CompleteRegistration")
        .AllowAnonymous()
        .Produces(
            StatusCodes.Status204NoContent
        )
        .ProducesProblem(
            StatusCodes.Status401Unauthorized
        )
        .ProducesProblem(
            StatusCodes.Status409Conflict
        );

        auth.MapPost("/login", async (
            IRegistrationSessionService registrationSession,
            LoginUserRequest request,
            LoginUserHandler handler) =>
        {
            var command = new LoginUserCommand(
                request.Email,
                request.Password,
                request.RememberMe
            );

            var result = await handler.HandleAsync(
                command
            );

            switch (result)
            {
                case UserPasswordSignInResult.Success:
                    registrationSession.Delete();
                    return Results.NoContent();

                case UserPasswordSignInResult.InvalidCredentials:
                    return Results.Problem(
                        statusCode: StatusCodes.Status401Unauthorized,
                        title: "Invalid credentials.",
                        detail: "The email or password is incorrect."
                    );

                case UserPasswordSignInResult.EmailNotConfirmed:
                    return Results.Problem(
                        statusCode: StatusCodes.Status409Conflict,
                        title: "Email not confirmed.",
                        detail: "The email address must be confirmed before signing in."
                    );

                default:
                    throw new UnreachableException();
            }
        })
        .WithName("LoginUser")
        .AllowAnonymous()
        .Produces(
            StatusCodes.Status204NoContent
        )
        .ProducesProblem(
            StatusCodes.Status401Unauthorized
        )
        .ProducesProblem(
            StatusCodes.Status409Conflict
        );

        auth.MapGet("/me", async (
            ClaimsPrincipal user,
            GetCurrentUserHandler handler) =>
        {
            if (!user.TryGetUserId(out var userId))
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status401Unauthorized,
                    title: "Invalid authentication session.",
                    detail: "The authentication session is invalid or expired."
                );
            }

            var query = new GetCurrentUserQuery(
                userId
            );

            var result = await handler.HandleAsync(
                query
            );

            return result switch
            {
                UserAccountDetailsResult.Success success =>
                    Results.Ok(
                        new GetCurrentUserResponse(
                            success.Id,
                            success.Email,
                            success.DisplayName,
                            success.CreatedAtUtc
                        )
                    ),

                UserAccountDetailsResult.UserNotFound =>
                    Results.Problem(
                        statusCode: StatusCodes.Status401Unauthorized,
                        title: "Invalid authentication session.",
                        detail: "The authentication session is invalid or expired."
                    ),

                _ => throw new UnreachableException()
            };
        })
        .WithName("GetCurrentUser")
        .RequireAuthorization()
        .Produces<GetCurrentUserResponse>()
        .ProducesProblem(
            StatusCodes.Status401Unauthorized
        );

        auth.MapPost("/resend-confirmation", async (
            ResendConfirmationRequest request,
            ResendConfirmationHandler handler,
            CancellationToken cancellationToken) =>
        {
            var command = new ResendConfirmationCommand(
                request.Email
            );

            await handler.HandleAsync(
                command,
                cancellationToken
            );

            return TypedResults.NoContent();
        })
        .WithName("ResendConfirmation")
        .AllowAnonymous()
        .Produces(
            StatusCodes.Status204NoContent
        );

        auth.MapPost("/forgot-password", async (
            ForgotPasswordRequest request,
            ForgotPasswordHandler handler,
            CancellationToken cancellationToken) =>
        {
            var command = new ForgotPasswordCommand(
                request.Email
            );

            await handler.HandleAsync(
                command,
                cancellationToken
            );

            return TypedResults.NoContent();
        })
        .WithName("ForgotPassword")
        .AllowAnonymous()
        .Produces(
            StatusCodes.Status204NoContent
        );

        auth.MapPost("/reset-password", async (
            ResetPasswordRequest request,
            ResetPasswordHandler handler,
            CancellationToken cancellationToken) =>
        {
            var command = new ResetPasswordCommand(
                request.UserId,
                request.Token,
                request.NewPassword
            );

            var result = await handler.HandleAsync(
                command,
                cancellationToken
            );

            return result switch
            {
                UserPasswordResetResult.Success =>
                    TypedResults.NoContent(),

                UserPasswordResetResult.UserNotFound or
                    UserPasswordResetResult.InvalidToken =>
                    Results.Problem(
                        statusCode: StatusCodes.Status400BadRequest,
                        title: "Invalid reset link.",
                        detail: "The password reset link is invalid or has expired."
                    ),

                UserPasswordResetResult.InvalidPassword invalid =>
                    Results.Problem(
                        statusCode: StatusCodes.Status400BadRequest,
                        title: "Invalid password.",
                        detail: string.Join(" ", invalid.Messages)
                    ),

                _ => throw new UnreachableException()
            };
        })
        .WithName("ResetPassword")
        .AllowAnonymous()
        .Produces(
            StatusCodes.Status204NoContent
        )
        .ProducesProblem(
            StatusCodes.Status400BadRequest
        );

        auth.MapPost("/logout", async (
            IRegistrationSessionService registrationSession,
            LogoutUserHandler handler) =>
        {
            await handler.HandleAsync();

            registrationSession.Delete();

            return TypedResults.NoContent();
        })
        .WithName("LogoutUser")
        .RequireAuthorization()
        .Produces(
            StatusCodes.Status204NoContent
        )
        .ProducesProblem(
            StatusCodes.Status401Unauthorized
        );

        return app;
    }
}
