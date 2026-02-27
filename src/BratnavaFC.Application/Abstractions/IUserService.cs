using BratnavaFC.Domain.Dtos;
using BratnavaFC.Domain.Dtos.Users;

namespace BratnavaFC.Application.Abstractions;

public interface IUserService
{
    Task CreateUserAsync(CreateUserDto registerUserDto, CancellationToken cancellationToken);
    Task<UserDto?> GetUserByIdAsync(Guid userId, CancellationToken cancellationToken);

    Task<PagedResultDto<UserListItemDto>> GetAllAsync(ListUsersRequestDto req, CancellationToken cancellationToken);

    Task UpdateAsync(Guid userId, UpdateUserDto dto, CancellationToken cancellationToken);
    Task ChangePasswordAsync(Guid userId, ChangePasswordDto dto, CancellationToken cancellationToken);

    Task InactivateAsync(Guid userId, CancellationToken cancellationToken);
    Task ReactivateAsync(Guid userId, CancellationToken cancellationToken);
}