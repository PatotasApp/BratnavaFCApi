using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Common;
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
    private readonly IPushService _push;

    public UserService(
        AppDbContext db,
        IRepositoryBase<UserEntity> repository,
        ILogger<UserService> logger,
        PasswordHasher<UserEntity> passwordHasher,
        IPushService push)
    {
        _repository = repository;
        _logger = logger;
        _passwordHasher = passwordHasher;
        _db = db;
        _push = push;
    }

    public async Task<Result> CreateUserAsync(CreateUserDto dto, CancellationToken cancellationToken)
    {
        var username = dto.UserName?.Trim().ToLower();

        var existing = await _db.Users
            .FirstOrDefaultAsync(x => x.Email == dto.Email || x.UserName == username, cancellationToken);

        if (existing?.UserName == username)
            return Result.Fail($"User already exists with the user name '{dto.UserName}'.", ResultStatus.BadRequest);

        if (existing?.Email == dto.Email)
            return Result.Fail($"User already exists with the email '{dto.Email}'.", ResultStatus.BadRequest);

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

        return Result.Ok("Usuário criado com sucesso.");
    }

    public async Task<Result<UserDto>> GetUserByIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await _db.Users
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
                PhotoUrl = null,
                PhotoUpdatedAt = u.ProfilePhotoUpdatedAt,

                Role = u.Role,
                Status = u.Status,

                CreateDate = u.CreateDate,
                UpdateDate = u.UpdateDate,
                InactivatedAt = u.InactivatedAt,

                PlayerIds = u.Players.Select(x => x.Id).ToArray(),
                GroupAdminIds = u.Admins.Select(x => x.GroupId).ToArray()
            })
            .FirstOrDefaultAsync(x => x.Id == userId, cancellationToken);

        if (user is null)
            return Result<UserDto>.Fail("Usuário não encontrado.", ResultStatus.NotFound);

        if (user.PhotoUpdatedAt.HasValue)
            user.PhotoUrl = $"/api/Users/{user.Id}/photo?v={user.PhotoUpdatedAt.Value.ToUnixTimeMilliseconds()}";

        return Result<UserDto>.Ok(user);
    }

    public async Task<Result<PagedResultDto<UserListItemDto>>> GetAllAsync(ListUsersRequestDto req, CancellationToken cancellationToken)
    {
        var page = req.Page <= 0 ? 1 : req.Page;
        var pageSize = req.PageSize <= 0 ? 20 : req.PageSize;
        if (pageSize > 2000) pageSize = 2000;

        IQueryable<UserEntity> q = _db.Users;

        if (!req.IncludeInactive)
            q = q.Where(u => u.Status != Status.Inactive);

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
                PhotoUrl = null,
                PhotoUpdatedAt = u.ProfilePhotoUpdatedAt,
                Role = (int)u.Role,
                Status = u.Status,
                CreateDate = u.CreateDate,
                UpdateDate = u.UpdateDate ?? u.CreateDate,
                InactivatedAt = u.InactivatedAt
            })
            .ToListAsync(cancellationToken);

        foreach (var item in items)
        {
            if (item.PhotoUpdatedAt.HasValue)
                item.PhotoUrl = $"/api/Users/{item.Id}/photo?v={item.PhotoUpdatedAt.Value.ToUnixTimeMilliseconds()}";
        }

        var pagedResult = new PagedResultDto<UserListItemDto>
        {
            Page = page,
            PageSize = pageSize,
            Total = total,
            Items = items
        };

        return Result<PagedResultDto<UserListItemDto>>.Ok(pagedResult);
    }

    public async Task<Result> UpdateAsync(Guid userId, UpdateUserDto dto, CancellationToken cancellationToken)
    {
        var user = await _repository.GetByIdIncludingInactiveAsync(userId, cancellationToken);
        if (user == null)
            return Result.Fail("Usuário não encontrado.", ResultStatus.NotFound);

        // username/email duplicados (se vierem)
        if (!string.IsNullOrWhiteSpace(dto.UserName))
        {
            var username = dto.UserName.Trim().ToLower();
            var existsUserName = await _db.Users
                .AnyAsync(u => u.Id != userId && u.UserName.ToLower() == username, cancellationToken);

            if (existsUserName)
                return Result.Fail($"User already exists with the user name '{dto.UserName}'.", ResultStatus.BadRequest);

            user.SetUserName(dto.UserName);
        }

        if (!string.IsNullOrWhiteSpace(dto.Email))
        {
            var email = dto.Email.Trim().ToLower();
            var existsEmail = await _db.Users
                .AnyAsync(u => u.Id != userId && u.Email.ToLower() == email, cancellationToken);

            if (existsEmail)
                return Result.Fail($"User already exists with the email '{dto.Email}'.", ResultStatus.BadRequest);

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
                return Result.Fail("Invalid role.", ResultStatus.BadRequest);

            user.SetRole((UserRole)dto.Role.Value);
        }

        if (dto.Status.HasValue)
        {
            if (!Enum.IsDefined(typeof(Status), dto.Status.Value))
                return Result.Fail("Invalid status.", ResultStatus.BadRequest);

            user.ChangeStatus((Status)dto.Status.Value);
        }

        _repository.Update(user);
        await _repository.SaveChangesAsync(cancellationToken);

        return Result.Ok("Usuário atualizado com sucesso.");
    }

    public async Task<Result> ChangePasswordAsync(Guid userId, ChangePasswordDto dto, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(dto.CurrentPassword))
            return Result.Fail("CurrentPassword is required.", ResultStatus.BadRequest);

        if (string.IsNullOrWhiteSpace(dto.NewPassword))
            return Result.Fail("NewPassword is required.", ResultStatus.BadRequest);

        var user = await _repository.GetByIdIncludingInactiveAsync(userId, cancellationToken);
        if (user == null)
            return Result.Fail("Usuário não encontrado.", ResultStatus.NotFound);

        var verify = _passwordHasher.VerifyHashedPassword(user, user.Password, dto.CurrentPassword);
        if (verify == PasswordVerificationResult.Failed)
            return Result.Fail("Current password is invalid.", ResultStatus.BadRequest);

        var newHash = _passwordHasher.HashPassword(user, dto.NewPassword);
        user.SetPasswordHash(newHash);

        _repository.Update(user);
        await _repository.SaveChangesAsync(cancellationToken);

        _ = NotifyPasswordChangedAsync(userId, cancellationToken);

        return Result.Ok("Senha atualizada com sucesso.");
    }

    public async Task<Result<UserPhotoDto>> SetPhotoAsync(
        Guid userId,
        byte[] data,
        string contentType,
        CancellationToken cancellationToken)
    {
        if (data.Length == 0 || data.Length > 5 * 1024 * 1024)
            return Result<UserPhotoDto>.Fail("A foto deve ter no máximo 5 MB.", ResultStatus.BadRequest);

        var normalizedContentType = contentType.Trim().ToLowerInvariant();
        if (!IsSupportedImage(data, normalizedContentType))
            return Result<UserPhotoDto>.Fail("Envie uma imagem JPEG, PNG ou WebP válida.", ResultStatus.BadRequest);

        var user = await _repository.GetByIdIncludingInactiveAsync(userId, cancellationToken);
        if (user is null)
            return Result<UserPhotoDto>.Fail("Usuário não encontrado.", ResultStatus.NotFound);

        user.SetProfilePhoto(data, normalizedContentType);
        _repository.Update(user);
        await _repository.SaveChangesAsync(cancellationToken);

        var updatedAt = user.ProfilePhotoUpdatedAt ?? DateTimeOffset.UtcNow;
        return Result<UserPhotoDto>.Ok(new UserPhotoDto(
            $"/api/Users/{user.Id}/photo?v={updatedAt.ToUnixTimeMilliseconds()}",
            updatedAt));
    }

    public async Task<Result<(byte[] Data, string ContentType, DateTimeOffset UpdatedAt)>> GetPhotoAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var photo = await _db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId && u.ProfilePhotoData != null)
            .Select(u => new
            {
                Data = u.ProfilePhotoData!,
                ContentType = u.ProfilePhotoContentType!,
                UpdatedAt = u.ProfilePhotoUpdatedAt!.Value
            })
            .FirstOrDefaultAsync(cancellationToken);

        return photo is null
            ? Result<(byte[], string, DateTimeOffset)>.Fail("Foto não encontrada.", ResultStatus.NotFound)
            : Result<(byte[], string, DateTimeOffset)>.Ok((photo.Data, photo.ContentType, photo.UpdatedAt));
    }

    public async Task<Result> RemovePhotoAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await _repository.GetByIdIncludingInactiveAsync(userId, cancellationToken);
        if (user is null)
            return Result.Fail("Usuário não encontrado.", ResultStatus.NotFound);

        user.RemoveProfilePhoto();
        _repository.Update(user);
        await _repository.SaveChangesAsync(cancellationToken);
        return Result.Ok("Foto removida com sucesso.");
    }

    private static bool IsSupportedImage(byte[] data, string contentType) => contentType switch
    {
        "image/jpeg" => data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF,
        "image/png" => data.Length >= 8 && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47,
        "image/webp" => data.Length >= 12 && data[0] == 0x52 && data[1] == 0x49 && data[2] == 0x46 && data[3] == 0x46
            && data[8] == 0x57 && data[9] == 0x45 && data[10] == 0x42 && data[11] == 0x50,
        _ => false
    };

    private async Task NotifyPasswordChangedAsync(Guid userId, CancellationToken ct)
    {
        try
        {
            await _push.SendToUserAsync(
                userId,
                title: "Senha alterada",
                body:  "Sua senha foi alterada. Se não foi você, entre em contato.",
                data:  new Dictionary<string, string> { ["type"] = "password_changed" },
                ct,
                groupId: null);
        }
        catch { /* notificação não crítica */ }
    }

    public async Task<Result> InactivateAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await _repository.GetByIdIncludingInactiveAsync(userId, cancellationToken);
        if (user == null)
            return Result.Fail("Usuário não encontrado.", ResultStatus.NotFound);

        user.Inactivate();

        _repository.Update(user);
        await _repository.SaveChangesAsync(cancellationToken);

        return Result.Ok("Usuário removido com sucesso.");
    }

    public async Task<Result> ReactivateAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await _repository.GetByIdIncludingInactiveAsync(userId, cancellationToken);
        if (user == null)
            return Result.Fail("Usuário não encontrado.", ResultStatus.NotFound);

        user.Reactivate();

        _repository.Update(user);
        await _repository.SaveChangesAsync(cancellationToken);

        return Result.Ok("Usuário atualizado com sucesso.");
    }
}
