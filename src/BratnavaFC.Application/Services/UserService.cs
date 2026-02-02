using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Abstractions;
using BratnavaFC.Domain.Dtos.Users;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BratnavaFC.Application.Services;

public class UserService : IUserService
{
    private readonly AppDbContext _appDbContext;
    private readonly IRepositoryBase<UserEntity> _repository;
    private readonly ILogger<UserService> _logger;
    private readonly PasswordHasher<UserEntity> _passwordHasher;

    public UserService(AppDbContext db, IRepositoryBase<UserEntity> repository, ILogger<UserService> logger, PasswordHasher<UserEntity> passwordHasher)
    {
        _repository = repository;
        _logger = logger;
        _passwordHasher = passwordHasher;
        _appDbContext = db;
    }

    public async Task CreateUserAsync(CreateUserDto registerUserDto, CancellationToken cancellationToken)
    {
        try
        {
            var user = await _appDbContext.Users.FirstOrDefaultAsync(x => x.Email == registerUserDto.Email, cancellationToken);

            if (user is not null)
            {
                throw new ApplicationException("User already exists.");
            }

            UserEntity newUser = new()
            {
                Email = registerUserDto.Email,
                FirstName = registerUserDto.FirstName,
                LastName = registerUserDto.LastName,
                Phone = registerUserDto.Phone,
                BirthDate = registerUserDto.BirthDate
            };

            var hashedPassword = _passwordHasher.HashPassword(newUser, registerUserDto.Password);

            newUser.Password = hashedPassword;

            _repository.Add(newUser);
            await _repository.SaveChangesAsync(cancellationToken);
        }
        catch (System.Exception ex)
        {
            _logger.LogError(ex, "Error trying to register user.");
            throw;
        }
    }
}
