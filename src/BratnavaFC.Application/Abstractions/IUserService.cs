using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Dtos;
using BratnavaFC.Domain.Dtos.Users;

namespace BratnavaFC.Application.Abstractions;

public interface IUserService
{
    Task<Result> CreateUserAsync(CreateUserDto registerUserDto, CancellationToken cancellationToken);
    Task<Result<UserDto>> GetUserByIdAsync(Guid userId, CancellationToken cancellationToken);

    Task<Result<PagedResultDto<UserListItemDto>>> GetAllAsync(ListUsersRequestDto req, CancellationToken cancellationToken);

    Task<Result> UpdateAsync(Guid userId, UpdateUserDto dto, CancellationToken cancellationToken);
    Task<Result> ChangePasswordAsync(Guid userId, ChangePasswordDto dto, CancellationToken cancellationToken);
    Task<Result<UserPhotoDto>> SetPhotoAsync(Guid userId, byte[] data, string contentType, CancellationToken cancellationToken);
    Task<Result<(byte[] Data, string ContentType, DateTimeOffset UpdatedAt)>> GetPhotoAsync(Guid userId, CancellationToken cancellationToken);
    Task<Result> RemovePhotoAsync(Guid userId, CancellationToken cancellationToken);

    Task<Result> InactivateAsync(Guid userId, CancellationToken cancellationToken);
    Task<Result> ReactivateAsync(Guid userId, CancellationToken cancellationToken);
}
