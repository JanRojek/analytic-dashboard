using AnalyticDashboard.Domain.Entities;
using AnalyticDashboard.Domain.Repositories;

namespace AnalyticDashboard.Application.Auth.Register;

public class RegisterUserHandler
{
    private readonly IUserRepository _userRepository;

    public RegisterUserHandler(IUserRepository userRepository)
    {
        _userRepository = userRepository;
    }

    public async Task<User?> Handle(
        RegisterUserCommand command,
        CancellationToken cancellationToken)
    {
        if (await _userRepository.ExistsByUsernameAsync(command.Username, cancellationToken))
        {
            return null;
        }
        
        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = command.Username.Trim(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(command.Password),
            CreatedAtUtc = DateTime.UtcNow
        };
        
        await _userRepository.AddAsync(user, cancellationToken);
        
        await _userRepository.SaveChangesAsync(cancellationToken);

        return user;
    }
}