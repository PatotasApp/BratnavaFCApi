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
            var user = await _appDbContext.Users.FirstOrDefaultAsync(x => x.Email == registerUserDto.Email || x.UserName == registerUserDto.UserName, cancellationToken);

            if (user?.UserName == registerUserDto.UserName)
            {
                throw new ApplicationException($"User already exists with the user name '{registerUserDto.UserName}'.");
            }
    
            if (user?.Email == registerUserDto.Email)
            {
                throw new ApplicationException($"User already exists with the email '{registerUserDto.Email}'.");
            }

            UserEntity newUser = new()
            {
                UserName = registerUserDto.UserName,
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

    public Task<UserDto?> GetUserByIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        return _appDbContext.Users.Include(x => x.Admins)
                                  .Include(x => x.Players)
                                  .Select(u => new UserDto
                                  {
                                      Id = u.Id,
                                      FirstName = u.FirstName,
                                      LastName = u.LastName,
                                      BirthDate = u.BirthDate,
                                      Role = u.Role,
                                      Status = u.Status,
                                      PlayerIds = u.Players.Select(x => x.Id).ToArray(),
                                      GroupAdminIds = u.Admins.Select(x => x.GroupId).ToArray()
                                  }).FirstOrDefaultAsync(x => x.Id == userId, cancellationToken);
    }

    public async Task InactivateAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await _appDbContext.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.Id == userId, cancellationToken);

        if (user is null) throw new ApplicationException("User not found.");

        user.Inactivate();
        _appDbContext.Users.Update(user);
        await _appDbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task ReactivateAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await _appDbContext.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.Id == userId, cancellationToken);

        if (user is null) throw new ApplicationException("User not found.");

        user.Reactivate();
        _appDbContext.Users.Update(user);
        await _appDbContext.SaveChangesAsync(cancellationToken);
    }

}
