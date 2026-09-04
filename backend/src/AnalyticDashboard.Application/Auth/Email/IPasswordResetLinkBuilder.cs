namespace AnalyticDashboard.Application.Auth.Email;

public interface IPasswordResetLinkBuilder
{
    string Build(
        Guid userId,
        string token
    );
}
