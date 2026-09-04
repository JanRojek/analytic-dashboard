namespace AnalyticDashboard.Api.Auth;

public interface IRegistrationSessionService
{
    void Create(Guid userId);

    bool TryGetUserId(out Guid userId);

    void Delete();
}
