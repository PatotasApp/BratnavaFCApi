using BratnavaFC.Application.Abstractions;
using BratnavaFC.Application.Validators;
using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Dtos;
using BratnavaFC.Domain.Dtos.Users;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Enums;
using BratnavaFC.Infrastructure.Data;
using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace BratnavaFC.Application.Services;

public class UserService : IUserService
{
    /// <summary>unique_violation do PostgreSQL — o índice único de UserName ou de Email.</summary>
    private const string UniqueViolationSqlState = "23505";

    private readonly AppDbContext _db;
    private readonly IRepositoryBase<UserEntity> _repository;
    private readonly ILogger<UserService> _logger;
    private readonly PasswordHasher<UserEntity> _passwordHasher;
    private readonly IPushService _push;
    private readonly IValidator<CreateUserDto> _createValidator;
    private readonly IValidator<UpdateUserDto> _updateValidator;
    private readonly IValidator<ChangePasswordDto> _changePasswordValidator;

    public UserService(
        AppDbContext db,
        IRepositoryBase<UserEntity> repository,
        ILogger<UserService> logger,
        PasswordHasher<UserEntity> passwordHasher,
        IPushService push,
        IValidator<CreateUserDto> createValidator,
        IValidator<UpdateUserDto> updateValidator,
        IValidator<ChangePasswordDto> changePasswordValidator)
    {
        _createValidator = createValidator;
        _updateValidator = updateValidator;
        _changePasswordValidator = changePasswordValidator;
        _repository = repository;
        _logger = logger;
        _passwordHasher = passwordHasher;
        _db = db;
        _push = push;
    }

    public async Task<Result> CreateUserAsync(CreateUserDto dto, CancellationToken cancellationToken)
    {
        var errors = await _createValidator.CollectErrorsAsync(dto, cancellationToken);

        if (errors is not null)
            return Result.Fail(errors[0], ResultStatus.BadRequest, errors);

        // Compara na forma canônica em que a entidade grava. Sem ToLower() na query, para que
        // a comparação use o índice único em vez de varrer a tabela.
        var username = dto.UserName.Trim().ToLowerInvariant();
        var email = EmailAddress.Normalize(dto.Email);

        if (await _db.Users.AnyAsync(x => x.UserName == username, cancellationToken))
            return Result.Fail($"User already exists with the user name '{username}'.", ResultStatus.BadRequest);

        if (await _db.Users.AnyAsync(x => x.Email == email, cancellationToken))
            return Result.Fail($"User already exists with the email '{email}'.", ResultStatus.BadRequest);

        var tempUser = new UserEntity(
            username,
            dto.FirstName,
            dto.LastName,
            email,
            passwordHashed: "temp",
            phone: dto.Phone,
            birthDate: dto.BirthDate);

        var hashed = _passwordHasher.HashPassword(tempUser, dto.Password);
        tempUser.SetPasswordHash(hashed);

        _repository.Add(tempUser);

        try
        {
            await _repository.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // As checagens acima não fecham a janela entre o SELECT e o INSERT: dois requests
            // simultâneos passam as duas e só o índice único separa um do outro.
            _logger.LogWarning(ex, "Criação de usuário barrada pelo índice único (username ou email já existe).");
            return Result.Fail("Nome de usuário ou email inválido.", ResultStatus.BadRequest);
        }

        return Result.Ok("Usuário criado com sucesso.");
    }

    /// <summary>
    /// Distingue a colisão de índice único de qualquer outra falha de escrita, para não
    /// transformar erro de banco genérico em mensagem de "usuário já existe".
    /// </summary>
    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: UniqueViolationSqlState };

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
        var errors = await _updateValidator.CollectErrorsAsync(dto, cancellationToken);

        if (errors is not null)
            return Result.Fail(errors[0], ResultStatus.BadRequest, errors);

        var user = await _repository.GetByIdIncludingInactiveAsync(userId, cancellationToken);
        if (user == null)
            return Result.Fail("Usuário não encontrado.", ResultStatus.NotFound);

        // username/email duplicados (se vierem)
        if (!string.IsNullOrWhiteSpace(dto.UserName))
        {
            var username = dto.UserName.Trim().ToLowerInvariant();
            var existsUserName = await _db.Users
                .AnyAsync(u => u.Id != userId && u.UserName == username, cancellationToken);

            if (existsUserName)
                return Result.Fail($"User already exists with the user name '{username}'.", ResultStatus.BadRequest);

            user.SetUserName(dto.UserName);
        }

        if (!string.IsNullOrWhiteSpace(dto.Email))
        {
            var email = EmailAddress.Normalize(dto.Email);
            var existsEmail = await _db.Users
                .AnyAsync(u => u.Id != userId && u.Email == email, cancellationToken);

            if (existsEmail)
                return Result.Fail($"User already exists with the email '{email}'.", ResultStatus.BadRequest);

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

        try
        {
            await _repository.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            _logger.LogWarning(ex, "Atualização de usuário barrada pelo índice único (username ou email já existe).");
            return Result.Fail("Usuário já existe com esse nome de usuário ou email.", ResultStatus.BadRequest);
        }

        return Result.Ok("Usuário atualizado com sucesso.");
    }

    public async Task<Result> ChangePasswordAsync(Guid userId, ChangePasswordDto dto, CancellationToken cancellationToken)
    {
        var errors = await _changePasswordValidator.CollectErrorsAsync(dto, cancellationToken);

        if (errors is not null)
            return Result.Fail(errors[0], ResultStatus.BadRequest, errors);

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
}
