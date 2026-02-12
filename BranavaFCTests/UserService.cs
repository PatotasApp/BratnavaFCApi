using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Dtos.Users;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BranavaFC.Tests;

public class UserService : IUserService
{
    private readonly AppDbContext _db;
    private readonly IRepositoryBase<UserEntity> _repository;
    private readonly ILogger<UserService> _logger;
    private readonly PasswordHasher<UserEntity> _passwordHasher;

    public UserService(AppDbContext db, IRepositoryBase<UserEntity> repository, ILogger<UserService> logger, PasswordHasher<UserEntity> passwordHasher)
    {
        _repository = repository;
        _logger = logger;
        _passwordHasher = passwordHasher;
        _db = db;
    }

    public async Task CreateUserAsync(CreateUserDto dto, CancellationToken cancellationToken)
    {
        try
        {
            var existing = await _db.Users
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(x => x.Email == dto.Email || x.UserName == dto.UserName, cancellationToken);

            if (existing?.UserName == dto.UserName)
                throw new ApplicationException($"User already exists with the user name '{dto.UserName}'.");

            if (existing?.Email == dto.Email)
                throw new ApplicationException($"User already exists with the email '{dto.Email}'.");

            var tempUser = new UserEntity(
                dto.UserName,
                dto.FirstName,
                dto.LastName,
                dto.Email,
                passwordHashed: "temp",
                phone: dto.Phone,
                birthDate: dto.BirthDate);

            var hashed = _passwordHasher.HashPassword(tempUser, dto.Password);
            tempUser.SetPasswordHash(hashed);

            _repository.Add(tempUser);
            await _repository.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error trying to register user.");
            throw;
        }
    }

    public Task<UserDto?> GetUserByIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        return _db.Users
            .Include(x => x.Admins)
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
            })
            .FirstOrDefaultAsync(x => x.Id == userId, cancellationToken);
    }

    public async Task InactivateAsync(Guid userId, CancellationToken cancellationToken)
    {
        try
        {
            var user = await _repository.GetByIdIncludingInactiveAsync(userId, cancellationToken);
            if (user == null) throw new ApplicationException("User not found.");

            user.Inactivate();

            _repository.Update(user);
            await _repository.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error trying to inactivate user.");
            throw;
        }
    }

    public async Task ReactivateAsync(Guid userId, CancellationToken cancellationToken)
    {
        try
        {
            var user = await _repository.GetByIdIncludingInactiveAsync(userId, cancellationToken);
            if (user == null) throw new ApplicationException("User not found.");

            user.Reactivate();

            _repository.Update(user);
            await _repository.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error trying to reactivate user.");
            throw;
        }
    }
}
