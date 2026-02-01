using System;
using BratnavaFC.Domain.Dtos.Users;

namespace BratnavaFC.Application.Abstractions;

public interface IUserService
{
    Task CreateUserAsync(CreateUserDto registerUserDto, CancellationToken cancellationToken);
}
