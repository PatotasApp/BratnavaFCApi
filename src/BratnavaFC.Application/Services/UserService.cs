using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Dtos;
using BratnavaFC.Domain.Dtos.Users;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Enums;
using BratnavaFC.Infrastructure.Data;
using FirebaseAdmin.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BratnavaFC.Application.Services;

public class UserService : IUserService
{
    private const string RoleClaim = UserProvisioningService.RoleClaim;
    private const string InternalIdClaim = UserProvisioningService.InternalIdClaim;

    private readonly AppDbContext _db;
    private readonly IRepositoryBase<UserEntity> _repository;
    private readonly ILogger<UserService> _logger;

    public UserService(
        AppDbContext db,
        IRepositoryBase<UserEntity> repository,
        ILogger<UserService> logger)
    {
        _repository = repository;
        _logger = logger;
        _db = db;
    }

    public async Task<Result<MeDto>> GetMeAsync(Guid userId, string? tokenEmail, CancellationToken cancellationToken)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user is null)
            return Result<MeDto>.Fail("Usuário não encontrado.", ResultStatus.NotFound);

        await SyncEmailFromTokenAsync(user, tokenEmail, cancellationToken);

        var me = new MeDto(
            user.Id,
            user.Email,
            user.UserName,
            user.FirstName,
            user.LastName,
            user.Phone,
            user.Role,
            user.Status);

        return Result<MeDto>.Ok(me);
    }

    /// <summary>
    /// Alinha a coluna Email com o e-mail do ID token.
    ///
    /// Precisa acontecer aqui porque o FirebaseIdentityMiddleware tem um caminho rápido: assim
    /// que o token carrega internal_id e role, ele responde sem chamar o
    /// UserProvisioningService — e a sincronização que existe lá deixa de rodar. Como o /me é
    /// chamado uma vez por sessão e já carregou a linha, é o ponto natural para reconciliar.
    ///
    /// Sentido único, do Firebase para cá: é lá que a troca de e-mail é confirmada pelo dono
    /// do endereço novo (verifyBeforeUpdateEmail). Escrever no sentido inverso deixaria os
    /// dois lados divergentes.
    /// </summary>
    private async Task SyncEmailFromTokenAsync(UserEntity user, string? tokenEmail, CancellationToken cancellationToken)
    {
        var email = tokenEmail?.Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(email) || string.Equals(user.Email, email, StringComparison.OrdinalIgnoreCase))
            return;

        // A coluna tem índice único: sem esta checagem, o SaveChanges estouraria e derrubaria
        // o /me inteiro por causa de um e-mail duplicado. Acontece quando o endereço novo já
        // pertence a uma linha antiga que ainda não migrou.
        var emailTaken = await _db.Users
            .AnyAsync(x => x.Id != user.Id && x.Email.ToLower() == email, cancellationToken);

        if (emailTaken)
        {
            _logger.LogError(
                "[Users] E-mail do usuário {UserId} não pôde ser sincronizado: {Email} já " +
                "pertence a outra linha. Resolva o duplicado no banco.",
                user.Id,
                email);

            return;
        }

        var previous = user.Email;
        user.SetEmail(email);

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "[Users] E-mail do usuário {UserId} sincronizado de {Previous} para {Current}.",
            user.Id,
            previous,
            email);
    }

    /// <summary>
    /// E-mail não entra aqui: é gerenciado no Firebase e read-only nesta fase. Role e status
    /// também não — são do fluxo administrativo.
    /// </summary>
    public async Task<Result<bool>> UpdateMeAsync(Guid userId, UpdateMeDto dto, CancellationToken cancellationToken)
    {
        var user = await _db.Users.FirstOrDefaultAsync(x => x.Id == userId, cancellationToken);

        if (user is null)
            return Result<bool>.Fail("Usuário não encontrado.", ResultStatus.NotFound);

        var previousUserName = user.UserName;
        var previousFirstName = user.FirstName;
        var previousLastName = user.LastName;
        var previousPhone = user.Phone;

        if (!string.IsNullOrWhiteSpace(dto.UserName))
        {
            var userName = dto.UserName.Trim().ToLower();

            var userNameTaken = await _db.Users.AnyAsync(
                x => x.Id != userId && x.UserName.ToLower() == userName,
                cancellationToken);

            if (userNameTaken)
                return Result<bool>.Fail($"Já existe um usuário com o nome '{dto.UserName}'.");

            user.SetUserName(dto.UserName);
        }

        user.UpdateProfile(
            firstName: string.IsNullOrWhiteSpace(dto.FirstName) ? user.FirstName : dto.FirstName,
            lastName: string.IsNullOrWhiteSpace(dto.LastName) ? user.LastName : dto.LastName,
            birthDate: dto.BirthDate.HasValue ? dto.BirthDate : user.BirthDate,
            phone: dto.Phone);

        await _db.SaveChangesAsync(cancellationToken);

        // Mantém o DisplayName do Firebase alinhado. Só tem o que sincronizar se o usuário já
        // tem FirebaseUid; quem ainda não migrou não existe lá.
        if (user.FirebaseUid is not null)
        {
            try
            {
                await FirebaseAuth.DefaultInstance.UpdateUserAsync(
                    new UserRecordArgs
                    {
                        Uid = user.FirebaseUid,
                        DisplayName = user.DisplayName,

                        // O SDK exige E.164 e LANÇA se o número não começar com '+'. Nossa
                        // coluna aceita qualquer formato ("11999999999"), então só enviamos o
                        // que já é válido — do contrário toda edição de perfil de quem tem
                        // telefone em formato local cairia no rollback abaixo.
                        PhoneNumber = ToE164OrNull(user.Phone)
                    },
                    cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "[Users] Falha ao sincronizar o perfil do usuário {UserId} no Firebase. Revertendo o SQL.",
                    userId);

                user.SetUserName(previousUserName);
                user.UpdateProfile(previousFirstName, previousLastName, user.BirthDate, previousPhone);
                await _db.SaveChangesAsync(cancellationToken);

                return Result<bool>.Fail("Não foi possível atualizar o perfil. Tente novamente.");
            }
        }

        return Result<bool>.Ok(true, "Perfil atualizado com sucesso.");
    }

    /// <summary>
    /// Devolve o telefone só quando ele já está em E.164 (o que o Firebase aceita); nulo em
    /// qualquer outro caso, o que para o SDK significa remover o telefone do registro.
    /// Não inferimos o DDI: adivinhar +55 gravaria número errado para quem não é do Brasil.
    /// </summary>
    private static string? ToE164OrNull(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
            return null;

        var trimmed = phone.Trim();

        if (!trimmed.StartsWith('+'))
            return null;

        var digits = trimmed.Count(char.IsDigit);

        return digits is >= 8 and <= 15 && trimmed.Skip(1).All(char.IsDigit)
            ? trimmed
            : null;
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

    /// <summary>
    /// Fluxo administrativo: perfil, role e status. O e-mail não é alterável — é a
    /// identidade do usuário no Firebase. Perfil do próprio usuário passa pelo
    /// <see cref="UpdateMeAsync"/>, que também sincroniza o DisplayName no Firebase.
    /// </summary>
    public async Task<Result> UpdateAsync(Guid userId, UpdateUserDto dto, CancellationToken cancellationToken)
    {
        var user = await _repository.GetByIdIncludingInactiveAsync(userId, cancellationToken);
        if (user == null)
            return Result.Fail("Usuário não encontrado.", ResultStatus.NotFound);

        if (!string.IsNullOrWhiteSpace(dto.UserName))
        {
            var username = dto.UserName.Trim().ToLower();
            var existsUserName = await _db.Users
                .AnyAsync(u => u.Id != userId && u.UserName.ToLower() == username, cancellationToken);

            if (existsUserName)
                return Result.Fail($"User already exists with the user name '{dto.UserName}'.", ResultStatus.BadRequest);

            user.SetUserName(dto.UserName);
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

        var previousRole = user.Role;

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

        if (user.Role != previousRole)
            await SyncRoleClaimAsync(user, cancellationToken);

        return Result.Ok("Usuário atualizado com sucesso.");
    }

    /// <summary>
    /// A autorização lê a claim "role" do token do Firebase, não a coluna do SQL. Sem este
    /// sincronismo a role mudaria no banco e todo [Authorize(Roles = ...)] continuaria
    /// decidindo pelo valor antigo.
    /// </summary>
    private async Task SyncRoleClaimAsync(UserEntity user, CancellationToken cancellationToken)
    {
        // Quem ainda não migrou não existe no Firebase; a role do banco vale e o middleware a
        // injeta pelo caminho lento.
        if (user.FirebaseUid is null)
            return;

        try
        {
            await FirebaseAuth.DefaultInstance.SetCustomUserClaimsAsync(
                // Identidade EXTERNA. Usar user.Id aqui só funcionaria para os migrados, em
                // que os dois coincidem por acidente do script.
                user.FirebaseUid,
                new Dictionary<string, object>
                {
                    [InternalIdClaim] = user.Id.ToString(),
                    [RoleClaim] = user.Role.ToString()
                },
                cancellationToken);

            _logger.LogInformation(
                "[Users] Role do usuário {UserId} sincronizada no Firebase como {Role}.",
                user.Id,
                user.Role);
        }
        catch (Exception ex)
        {
            // O SQL já foi salvo. Falhar aqui deixa a role divergente até a próxima
            // atualização, então precisa aparecer no log.
            _logger.LogError(
                ex,
                "[Users] Role do usuário {UserId} mudou no SQL mas não foi sincronizada no Firebase.",
                user.Id);
        }
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
