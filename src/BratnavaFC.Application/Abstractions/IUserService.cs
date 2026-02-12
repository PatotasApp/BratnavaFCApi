using BratnavaFC.Domain.Dtos.Users;

namespace BratnavaFC.Application.Abstractions;

public interface IUserService
{
    Task CreateUserAsync(CreateUserDto registerUserDto, CancellationToken cancellationToken);
    Task<UserDto?> GetUserByIdAsync(Guid userId, CancellationToken cancellationToken);

    Task InactivateAsync(Guid userId, CancellationToken cancellationToken);
    Task ReactivateAsync(Guid userId, CancellationToken cancellationToken);
}
