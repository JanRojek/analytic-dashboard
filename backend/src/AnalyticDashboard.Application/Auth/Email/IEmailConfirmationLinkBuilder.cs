namespace AnalyticDashboard.Application.Auth.Email;

public interface IEmailConfirmationLinkBuilder
{
    string Build(
        Guid userId,
        string token
    );
}
