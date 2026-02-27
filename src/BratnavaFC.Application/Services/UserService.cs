using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Dtos;
using BratnavaFC.Domain.Dtos.Users;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Enums;
using BratnavaFC.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BratnavaFC.Application.Services;

public class UserService : IUserService
{
    private readonly AppDbContext _db;
    private readonly IRepositoryBase<UserEntity> _repository;
    private readonly ILogger<UserService> _logger;
    private readonly PasswordHasher<UserEntity> _passwordHasher;

    public UserService(
        AppDbContext db,
        IRepositoryBase<UserEntity> repository,
        ILogger<UserService> logger,
        PasswordHasher<UserEntity> passwordHasher)
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
            var username = dto.UserName?.Trim().ToLower();

            var existing = await _db.Users
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(x => x.Email == dto.Email || x.UserName == username, cancellationToken);

            if (existing?.UserName == username)
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
                UserName = u.UserName,
                Email = u.Email,
                Phone = u.Phone,

                FirstName = u.FirstName,
                LastName = u.LastName,
                BirthDate = u.BirthDate,

                Role = u.Role,
                Status = u.Status,

                CreateDate = u.CreateDate,
                UpdateDate = u.UpdateDate,
                InactivatedAt = u.InactivatedAt,

                PlayerIds = u.Players.Select(x => x.Id).ToArray(),
                GroupAdminIds = u.Admins.Select(x => x.GroupId).ToArray()
            })
            .FirstOrDefaultAsync(x => x.Id == userId, cancellationToken);
    }

    public async Task<PagedResultDto<UserListItemDto>> GetAllAsync(ListUsersRequestDto req, CancellationToken cancellationToken)
    {
        var page = req.Page <= 0 ? 1 : req.Page;
        var pageSize = req.PageSize <= 0 ? 20 : req.PageSize;
        if (pageSize > 200) pageSize = 200;

        IQueryable<UserEntity> q = _db.Users;

        if (req.IncludeInactive)
            q = q.IgnoreQueryFilters();

        if (!string.IsNullOrWhiteSpace(req.Search))
        {
            var s = req.Search.Trim().ToLower();
            q = q.Where(u =>
                u.UserName.ToLower().Contains(s) ||
                u.FirstName.ToLower().Contains(s) ||
                u.LastName.ToLower().Contains(s) ||
                u.Email.ToLower().Contains(s));
        }

        if (req.Status.HasValue)
            q = q.Where(u => u.Status == req.Status.Value);

        if (req.Role.HasValue)
            q = q.Where(u => (int)u.Role == req.Role.Value);

        var total = await q.CountAsync(cancellationToken);

        var items = await q
            .OrderBy(u => u.FirstName).ThenBy(u => u.LastName)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(u => new UserListItemDto
            {
                Id = u.Id,
                UserName = u.UserName,
                FirstName = u.FirstName,
                LastName = u.LastName,
                Email = u.Email,
                Phone = u.Phone,
                BirthDate = u.BirthDate,
                Role = (int)u.Role,
                Status = u.Status,
                CreateDate = u.CreateDate,
                UpdateDate = u.UpdateDate ?? u.CreateDate,
                InactivatedAt = u.InactivatedAt
            })
            .ToListAsync(cancellationToken);

        return new PagedResultDto<UserListItemDto>
        {
            Page = page,
            PageSize = pageSize,
            Total = total,
            Items = items
        };
    }

    public async Task UpdateAsync(Guid userId, UpdateUserDto dto, CancellationToken cancellationToken)
    {
        try
        {
            var user = await _repository.GetByIdIncludingInactiveAsync(userId, cancellationToken);
            if (user == null) throw new ApplicationException("User not found.");

            // username/email duplicados (se vierem)
            if (!string.IsNullOrWhiteSpace(dto.UserName))
            {
                var username = dto.UserName.Trim().ToLower();
                var existsUserName = await _db.Users
                    .IgnoreQueryFilters()
                    .AnyAsync(u => u.Id != userId && u.UserName.ToLower() == username, cancellationToken);

                if (existsUserName)
                    throw new ApplicationException($"User already exists with the user name '{dto.UserName}'.");
                user.SetUserName(dto.UserName);
            }

            if (!string.IsNullOrWhiteSpace(dto.Email))
            {
                var email = dto.Email.Trim().ToLower();
                var existsEmail = await _db.Users
                    .IgnoreQueryFilters()
                    .AnyAsync(u => u.Id != userId && u.Email.ToLower() == email, cancellationToken);

                if (existsEmail)
                    throw new ApplicationException($"User already exists with the email '{dto.Email}'.");
                user.SetEmail(dto.Email);
            }

            // profile (só aplica se tiver algo)
            var anyProfile =
                !string.IsNullOrWhiteSpace(dto.FirstName) ||
                !string.IsNullOrWhiteSpace(dto.LastName) ||
                dto.BirthDate.HasValue ||
                dto.Phone != null; // null pode ser intenção de limpar

            if (anyProfile)
            {
                user.UpdateProfile(
                    firstName: string.IsNullOrWhiteSpace(dto.FirstName) ? user.FirstName : dto.FirstName!,
                    lastName: string.IsNullOrWhiteSpace(dto.LastName) ? user.LastName : dto.LastName!,
                    birthDate: dto.BirthDate.HasValue ? dto.BirthDate : user.BirthDate,
                    phone: dto.Phone // pode vir null pra limpar
                );
            }

            if (dto.Role.HasValue)
            {
                if (!Enum.IsDefined(typeof(UserRole), dto.Role.Value))
                    throw new ApplicationException("Invalid role.");

                user.SetRole((UserRole)dto.Role.Value);
            }

            if (dto.Status.HasValue)
            {
                if (!Enum.IsDefined(typeof(Status), dto.Status.Value))
                    throw new ApplicationException("Invalid status.");

                user.ChangeStatus((Status)dto.Status.Value);
            }

            _repository.Update(user);
            await _repository.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error trying to update user.");
            throw;
        }
    }

    public async Task ChangePasswordAsync(Guid userId, ChangePasswordDto dto, CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(dto.CurrentPassword))
                throw new ApplicationException("CurrentPassword is required.");

            if (string.IsNullOrWhiteSpace(dto.NewPassword))
                throw new ApplicationException("NewPassword is required.");

            var user = await _repository.GetByIdIncludingInactiveAsync(userId, cancellationToken);
            if (user == null) throw new ApplicationException("User not found.");

            var verify = _passwordHasher.VerifyHashedPassword(user, user.Password, dto.CurrentPassword);
            if (verify == PasswordVerificationResult.Failed)
                throw new ApplicationException("Current password is invalid.");

            var newHash = _passwordHasher.HashPassword(user, dto.NewPassword);
            user.SetPasswordHash(newHash);

            _repository.Update(user);
            await _repository.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error trying to change password.");
            throw;
        }
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