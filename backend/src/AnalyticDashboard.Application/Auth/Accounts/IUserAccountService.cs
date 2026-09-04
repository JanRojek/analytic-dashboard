namespace AnalyticDashboard.Application.Auth.Accounts;

public interface IUserAccountService
{
    Task<UserAccountCreationResult> CreateAsync(
        string email,
        string displayName,
        string password
    );

    Task<UserEmailConfirmationStatusResult> GetEmailConfirmationStatusAsync(
        Guid userId
    );

    Task<UserSignInResult> SignInAsync(
        Guid userId
    );

    Task<UserPasswordSignInResult> SignInWithPasswordAsync(
        string email,
        string password,
        bool rememberMe
    );

    Task<UserAccountDetailsResult> GetByIdAsync(
        Guid userId
    );

    Task<UserEmailConfirmationLookupResult> FindForConfirmationAsync(
        string email
    );

    Task<UserPasswordResetLookupResult> FindForPasswordResetAsync(
        string email
    );

    Task SignOutAsync();
}
