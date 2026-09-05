using AnalyticDashboard.Application.Auth.Accounts;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AnalyticDashboard.Infrastructure.Identity;

public sealed class UserAccountTokenService : IUserAccountTokenService
{
    private readonly UserManager<ApplicationUser> _userManager;

    public UserAccountTokenService(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public async Task<string> GenerateEmailConfirmationTokenAsync(
        Guid userId)
    {
        var user = await _userManager.FindByIdAsync(
            userId.ToString()
        );

        if (user == null)
        {
            throw new InvalidOperationException(
                "User was not found while generating an email confirmation token."
            );
        }

        return await _userManager.GenerateEmailConfirmationTokenAsync(
            user
        );
    }

    public async Task<string> GeneratePasswordResetTokenAsync(
        Guid userId)
    {
        var user = await _userManager.FindByIdAsync(
            userId.ToString()
        );

        if (user == null)
        {
            throw new InvalidOperationException(
                "User was not found while generating a password reset token."
            );
        }

        return await _userManager.GeneratePasswordResetTokenAsync(
            user
        );
    }

    public async Task<UserEmailConfirmationResult> ConfirmEmailAsync(
        Guid userId,
        string token,
        CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByIdAsync(
            userId.ToString()
        );

        if (user == null)
        {
            return new UserEmailConfirmationResult.UserNotFound();
        }

        if (user.EmailConfirmed)
        {
            return new UserEmailConfirmationResult.AlreadyConfirmed();
        }

        var result = await _userManager.ConfirmEmailAsync(
            user,
            token
        );

        if (result.Succeeded)
        {
            return new UserEmailConfirmationResult.Success();
        }

        if (result.Errors.Any(
                error => error.Code == "InvalidToken"
            ))
        {
            return new UserEmailConfirmationResult.InvalidToken();
        }

        if (result.Errors.Any(
                error => error.Code == "ConcurrencyFailure"
            ))
        {
            var emailConfirmed = await _userManager.Users
                .AsNoTracking()
                .Where(candidate => candidate.Id == userId)
                .Select(candidate => (bool?)candidate.EmailConfirmed)
                .SingleOrDefaultAsync(
                    cancellationToken
                );

            if (emailConfirmed == true)
            {
                return new UserEmailConfirmationResult.AlreadyConfirmed();
            }

            if (emailConfirmed == null)
            {
                return new UserEmailConfirmationResult.UserNotFound();
            }
        }

        throw new InvalidOperationException(
            $"Unexpected Identity error(s): {string.Join(
                ", ",
                result.Errors.Select(error =>
                    $"{error.Code}: {error.Description}"
                )
            )}"
        );
    }

    public async Task<UserPasswordResetResult> ResetPasswordAsync(
        Guid userId,
        string token,
        string newPassword,
        CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByIdAsync(
            userId.ToString()
        );

        if (user == null)
        {
            return new UserPasswordResetResult.UserNotFound();
        }

        var result = await _userManager.ResetPasswordAsync(
            user,
            token,
            newPassword
        );

        if (result.Succeeded)
        {
            return new UserPasswordResetResult.Success();
        }

        if (result.Errors.Any(error =>
                error.Code == "InvalidToken"))
        {
            return new UserPasswordResetResult.InvalidToken();
        }

        if (result.Errors.Any(error =>
                error.Code == "ConcurrencyFailure"))
        {
            var freshUser = await _userManager.Users
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    candidate => candidate.Id == userId,
                    cancellationToken
                );

            if (freshUser == null)
            {
                return new UserPasswordResetResult.UserNotFound();
            }

            var tokenIsStillValid =
                await _userManager.VerifyUserTokenAsync(
                    freshUser,
                    _userManager.Options.Tokens.PasswordResetTokenProvider,
                    UserManager<ApplicationUser>.ResetPasswordTokenPurpose,
                    token
                );

            if (!tokenIsStillValid)
            {
                return new UserPasswordResetResult.InvalidToken();
            }

            throw new InvalidOperationException(
                "Password reset failed because of an unexpected concurrency conflict."
            );
        }

        if (result.Errors.All(error =>
                error.Code.StartsWith(
                    "Password",
                    StringComparison.Ordinal
                )))
        {
            return new UserPasswordResetResult.InvalidPassword(
                result.Errors
                    .Select(error => error.Description)
                    .ToArray()
            );
        }

        throw new InvalidOperationException(
            $"Unexpected Identity error(s): {string.Join(
                ", ",
                result.Errors.Select(error =>
                    $"{error.Code}: {error.Description}"
                )
            )}"
        );
    }
}
