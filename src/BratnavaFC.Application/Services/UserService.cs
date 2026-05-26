using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Dtos;
using BratnavaFC.Domain.Dtos.Authentication;
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
    private readonly IEmailService _emailService;

    public UserService(
        AppDbContext db,
        IRepositoryBase<UserEntity> repository,
        ILogger<UserService> logger,
        PasswordHasher<UserEntity> passwordHasher,
        IPushService push,
        IEmailService emailService)
    {
        _repository = repository;
        _logger = logger;
        _passwordHasher = passwordHasher;
        _db = db;
        _push = push;
        _emailService = emailService;
    }

    public async Task<Result> CreateUserAsync(CreateUserDto dto, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(dto.Email))
            return Result.Fail("O email é obrigatório.", ResultStatus.BadRequest);

        try { _ = new System.Net.Mail.MailAddress(dto.Email); }
        catch { return Result.Fail("Email inválido.", ResultStatus.BadRequest); }

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
                Role = (int)u.Role,
                Status = u.Status,
                CreateDate = u.CreateDate,
                UpdateDate = u.UpdateDate ?? u.CreateDate,
                InactivatedAt = u.InactivatedAt
            })
            .ToListAsync(cancellationToken);

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

    public async Task<Result> RequestPasswordResetAsync(string email, CancellationToken cancellationToken)
    {
        var user = await _db.Users
            .FirstOrDefaultAsync(u => u.Email == email, cancellationToken);

        if (user is null)
            return Result.Fail("E-mail informado não foi encontrado.", ResultStatus.NotFound);

        var oldTokens = await _db.PasswordResetTokens
            .Where(t => t.UserId == user.Id && !t.IsUsed)
            .ToListAsync(cancellationToken);

        foreach (var t in oldTokens)
            t.MarkAsUsed();

        var code = Random.Shared.Next(100000, 999999).ToString();
        var token = new PasswordResetTokenEntity(user.Id, code, DateTimeOffset.UtcNow.AddMinutes(15));
        _db.PasswordResetTokens.Add(token);
        await _db.SaveChangesAsync(cancellationToken);

        try
        {
            await _emailService.SendPasswordResetEmailAsync(user.Email, code);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao enviar email de recuperação para {Email}", email);
        }

        return Result.Ok();
    }

    public async Task<Result> ResetPasswordAsync(ResetPasswordDto dto, CancellationToken cancellationToken)
    {
        var user = await _db.Users
            .FirstOrDefaultAsync(u => u.Email == dto.Email, cancellationToken);

        if (user is null)
            return Result.Fail("Código inválido ou expirado.", ResultStatus.BadRequest);

        var token = await _db.PasswordResetTokens
            .Where(t =>
                t.UserId == user.Id &&
                t.Code == dto.Code &&
                !t.IsUsed &&
                t.ExpiresAt > DateTimeOffset.UtcNow)
            .FirstOrDefaultAsync(cancellationToken);

        if (token is null)
            return Result.Fail("Código inválido ou expirado.", ResultStatus.BadRequest);

        var hashed = _passwordHasher.HashPassword(user, dto.NewPassword);
        user.SetPasswordHash(hashed);
        token.MarkAsUsed();

        await _db.SaveChangesAsync(cancellationToken);
        return Result.Ok("Senha redefinida com sucesso.");
    }
}
