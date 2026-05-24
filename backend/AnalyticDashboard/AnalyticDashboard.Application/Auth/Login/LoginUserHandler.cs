using AnalyticDashboard.Domain.Entities;
using AnalyticDashboard.Domain.Repositories;

namespace AnalyticDashboard.Application.Auth.Login;

public class LoginUserHandler
{
    private readonly IUserRepository _userRepository;
    
    public  LoginUserHandler(IUserRepository userRepository)
    {
        _userRepository = userRepository;
    }

    public async Task<User?> Handle(LoginUserCommand command, CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByUsernameAsync(command.Username.Trim(), cancellationToken);

        if (user == null)
        {
            return null;
        }
        
        var isPasswordValid = BCrypt.Net.BCrypt.Verify(
            command.Password,
            user.PasswordHash
        );

        if (!isPasswordValid)
        {
            return null;
        }

        return user;
    }
}