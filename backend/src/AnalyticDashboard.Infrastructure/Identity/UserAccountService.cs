using AnalyticDashboard.Application.Auth.Accounts;
using Microsoft.AspNetCore.Identity;
using AnalyticDashboard.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AnalyticDashboard.Infrastructure.Identity;

public sealed class UserAccountService : IUserAccountService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly AppDbContext _dbContext;

    public UserAccountService(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        AppDbContext dbContext)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _dbContext = dbContext;
    }

    public async Task<UserAccountCreationResult> CreateAsync(
        string email,
        string displayName,
        string password)
    {
        var user = new ApplicationUser
        {
            Email = email,
            UserName = email,
            DisplayName = displayName
        };

        IdentityResult result;

        try
        {
            result = await _userManager.CreateAsync(
                user,
                password
            );
        }
        catch (DbUpdateException exception)
            when (exception is
                  {
                      InnerException: PostgresException
                      {
                          SqlState: PostgresErrorCodes.UniqueViolation,
                          ConstraintName: ApplicationUserDatabaseNames.EmailUniqueIndex
                          or ApplicationUserDatabaseNames.UserNameUniqueIndex
                      }
                  })
        {
            _dbContext.Entry(user).State = EntityState.Detached;

            return new UserAccountCreationResult.EmailAlreadyExists(
                email
            );
        }

        if (result.Succeeded)
        {
            return new UserAccountCreationResult.Success(
                user.Id,
                user.CreatedAtUtc
            );
        }

        var errors = result.Errors.ToList();

        if (errors.Any(error =>
                error.Code is "DuplicateEmail" or "DuplicateUserName"))
        {
            return new UserAccountCreationResult.EmailAlreadyExists(
                email
            );
        }

        if (errors.Any(error =>
                error.Code is "InvalidEmail" or "InvalidUserName"))
        {
            var error = errors.First(error =>
                error.Code is "InvalidEmail" or "InvalidUserName"
            );

            return new UserAccountCreationResult.InvalidEmail(
                error.Description
            );
        }

        var passwordErrors = errors
            .Where(error =>
                error.Code.StartsWith(
                    "Password",
                    StringComparison.Ordinal
                )
            )
            .Select(error => error.Description)
            .ToList();

        if (passwordErrors.Count > 0)
        {
            return new UserAccountCreationResult.InvalidPassword(
                passwordErrors
            );
        }

        throw new InvalidOperationException(
            $"Unexpected Identity error(s): {string.Join(
                ", ",
                errors.Select(error =>
                    $"{error.Code}: {error.Description}"
                )
            )}"
        );
    }

    public async Task<UserEmailConfirmationStatusResult> GetEmailConfirmationStatusAsync(
        Guid userId)
    {
        var user = await _userManager.FindByIdAsync(
            userId.ToString()
        );

        if (user == null)
        {
            return new UserEmailConfirmationStatusResult.UserNotFound();
        }

        return user.EmailConfirmed
            ? new UserEmailConfirmationStatusResult.Confirmed()
            : new UserEmailConfirmationStatusResult.NotConfirmed();
    }

    public async Task<UserSignInResult> SignInAsync(
        Guid userId)
    {
        var user = await _userManager.FindByIdAsync(
            userId.ToString()
        );

        if (user == null)
        {
            return new UserSignInResult.UserNotFound();
        }

        if (!user.EmailConfirmed)
        {
            return new UserSignInResult.EmailNotConfirmed();
        }

        await _signInManager.SignInAsync(
            user,
            isPersistent: false
        );

        return new UserSignInResult.Success();
    }

    public async Task<UserPasswordSignInResult> SignInWithPasswordAsync(
        string email,
        string password,
        bool rememberMe)
    {
        var user = await _userManager.FindByEmailAsync(
            email
        );

        if (user == null)
        {
            return new UserPasswordSignInResult.InvalidCredentials();
        }

        var isPasswordCorrect = await _userManager.CheckPasswordAsync(
            user,
            password
        );

        if (!isPasswordCorrect)
        {
            return new UserPasswordSignInResult.InvalidCredentials();
        }

        if (!user.EmailConfirmed)
        {
            return new UserPasswordSignInResult.EmailNotConfirmed();
        }

        await _signInManager.SignInAsync(
            user,
            isPersistent: rememberMe
        );

        return new UserPasswordSignInResult.Success();
    }

    public async Task<UserAccountDetailsResult> GetByIdAsync(
        Guid userId)
    {
        var user = await _userManager.FindByIdAsync(
            userId.ToString()
        );

        if (user == null)
        {
            return new UserAccountDetailsResult.UserNotFound();
        }

        return new UserAccountDetailsResult.Success(
            user.Id,
            user.Email ?? throw new InvalidOperationException(
                $"User '{user.Id}' does not have an email address."
            ),
            user.DisplayName,
            user.CreatedAtUtc
        );
    }

    public async Task<UserEmailConfirmationLookupResult> FindForConfirmationAsync(
        string email)
    {
        var user = await _userManager.FindByEmailAsync(
            email
        );

        if (user == null)
        {
            return new UserEmailConfirmationLookupResult.UserNotFound();
        }

        if (user.EmailConfirmed)
        {
            return new UserEmailConfirmationLookupResult.AlreadyConfirmed();
        }

        return new UserEmailConfirmationLookupResult.Unconfirmed(
            user.Id,
            user.Email ?? throw new InvalidOperationException(
                $"User '{user.Id}' does not have an email address."
            )
        );
    }

    public async Task<UserPasswordResetLookupResult> FindForPasswordResetAsync(
        string email)
    {
        var user = await _userManager.FindByEmailAsync(
            email
        );

        if (user == null)
        {
            return new UserPasswordResetLookupResult.UserNotFound();
        }

        return new UserPasswordResetLookupResult.Found(
            user.Id,
            user.Email ?? throw new InvalidOperationException(
                $"User '{user.Id}' does not have an email address."
            )
        );
    }

    public async Task SignOutAsync()
    {
        await _signInManager.SignOutAsync();
    }
}
