using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Dtos;
using BratnavaFC.Domain.Dtos.Users;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Enums;
using BratnavaFC.Infrastructure.Cloudflare;
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
    private readonly IImageStorageService _images;
    private readonly IGroupService _groups;

    public UserService(
        AppDbContext db,
        IRepositoryBase<UserEntity> repository,
        ILogger<UserService> logger,
        IImageStorageService images,
        IGroupService groups)
    {
        _repository = repository;
        _logger = logger;
        _db = db;
        _images = images;
        _groups = groups;
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
            user.Status,
            user.ProfilePhotoKey is { } photoKey ? _images.BuildPublicUrl(photoKey) : null);

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
                PhotoUrl = u.ProfilePhotoKey != null ? _images.PublicBaseUrl + "/" + u.ProfilePhotoKey : null,
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

        // Prefixo, não Contains: o handle é único e identifica a pessoa, então casar no meio da
        // string só traria de volta o ruído que este filtro existe para eliminar.
        if (!string.IsNullOrWhiteSpace(req.UserName))
        {
            var handle = req.UserName.Trim().ToLower();
            q = q.Where(u => u.UserName.ToLower().StartsWith(handle));
        }
        else if (!string.IsNullOrWhiteSpace(req.Search))
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
                PhotoUrl = u.ProfilePhotoKey != null ? _images.PublicBaseUrl + "/" + u.ProfilePhotoKey : null,
                PhotoUpdatedAt = u.ProfilePhotoUpdatedAt,
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
        Stream image,
        CancellationToken cancellationToken)
    {
        var user = await _repository.GetByIdIncludingInactiveAsync(userId, cancellationToken);
        if (user is null)
            return Result<UserPhotoDto>.Fail("Usuário não encontrado.", ResultStatus.NotFound);

        var previousKey = user.ProfilePhotoKey;

        string objectKey;
        try
        {
            objectKey = await _images.UploadAsync(ImageKind.Avatar, userId, image, cancellationToken);
        }
        catch (InvalidImageException ex)
        {
            return Result<UserPhotoDto>.Fail(ex.Message, ResultStatus.BadRequest);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Users] Falha ao enviar avatar do usuário {UserId} para o R2.", userId);
            return Result<UserPhotoDto>.Fail("Falha ao enviar a foto para o storage. Tente novamente.");
        }

        user.SetProfilePhoto(objectKey);
        _repository.Update(user);
        await _repository.SaveChangesAsync(cancellationToken);

        // Só depois do commit: se o banco falhar, a foto antiga ainda é a que o usuário vê,
        // e apagá-la antes deixaria o avatar quebrado sem nada ter sido trocado.
        await DeletePreviousPhotoAsync(previousKey, userId, cancellationToken);

        var updatedAt = user.ProfilePhotoUpdatedAt ?? DateTimeOffset.UtcNow;
        return Result<UserPhotoDto>.Ok(new UserPhotoDto(
            _images.BuildPublicUrl(objectKey),
            updatedAt));
    }

    /// <summary>
    /// Exclusão definitiva da conta, exigida pela Google Play.
    ///
    /// A ORDEM importa e é contraintuitiva: o Firebase é apagado ANTES do SQL. Se fosse o
    /// contrário e o Firebase falhasse, a pessoa ainda conseguiria logar, e o
    /// UserProvisioningService criaria uma linha nova em branco no primeiro acesso — ela
    /// "some" e volta como um fantasma vazio. No sentido atual, uma falha no meio deixa a
    /// pessoa sem acesso e o dado intacto até alguém limpar. Falhar fechado é melhor que
    /// falhar aberto.
    ///
    /// O que sobrevive à exclusão: o jogador vira convidado e mantém nome, gols e
    /// estatísticas — é o histórico da PATOTA, não só o dele, e é o que permite revincular
    /// a conta no futuro. Essa retenção precisa estar declarada na política de privacidade.
    /// </summary>
    public async Task<Result<List<AccountDeletionBlockerDto>>> DeleteMyAccountAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var user = await _repository.GetByIdIncludingInactiveAsync(userId, cancellationToken);
        if (user is null)
            return Result<List<AccountDeletionBlockerDto>>.Fail("Usuário não encontrado.", ResultStatus.NotFound);

        var blockers = await FindSoleAdminGroupsAsync(userId, cancellationToken);
        if (blockers.Count > 0)
        {
            return Result<List<AccountDeletionBlockerDto>>.FailWith(
                blockers,
                $"Você é o único administrador de {blockers.Count} patota(s). " +
                "Promova outro administrador antes de excluir sua conta.",
                ResultStatus.Conflict);
        }

        if (!string.IsNullOrWhiteSpace(user.FirebaseUid))
        {
            try
            {
                await FirebaseAuth.DefaultInstance.DeleteUserAsync(user.FirebaseUid, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Users] Falha ao apagar o usuário {UserId} no Firebase. Exclusão abortada.", userId);
                return Result<List<AccountDeletionBlockerDto>>.Fail(
                    "Não foi possível excluir a conta agora. Tente novamente.");
            }
        }

        var photoKey = user.ProfilePhotoKey;

        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);

        // Tudo abaixo roda como UPDATE/DELETE em conjunto, dentro da transação aberta acima:
        // ExecuteUpdate e ExecuteDelete usam a mesma conexão, então participam dela. Nada é
        // carregado para a memória e o change tracker não é consultado — por isso também não
        // passam pelos métodos da entidade, e o setter privado deixa de ser obstáculo.
        //
        // O preço está nos testes: o provider InMemory da suíte não traduz nenhuma das duas.
        // Os casos que exercitam a exclusão bem-sucedida estão marcados como Skip em
        // UserServiceDeleteAccountTests, à espera de Postgres real. Os de recusa continuam
        // valendo, porque retornam antes de qualquer escrita.

        // Patotas que ficam sem ninguém somem junto. Precisa ser o primeiro passo: é o vínculo
        // dos jogadores que identifica quem ainda está lá, e ele é desfeito logo abaixo.
        await _groups.DeleteManyAsync(
            await _groups.FindAbandonedByAsync(userId, cancellationToken),
            cancellationToken);

        // Jogadores viram convidados: o histórico da patota continua de pé, sem identidade.
        // Mesmo movimento do LeaveGroupAsync, aplicado a todas as patotas de uma vez.
        await _db.Players
            .Where(x => x.UserId == userId)
            .ExecuteUpdateAsync(set => set
                .SetProperty(x => x.IsGuest, true)
                .SetProperty(x => x.UserId, (Guid?)null), cancellationToken);

        // Autoria de conteúdo que a patota continua usando: o registro fica, o autor sai.
        // Quem exibir esses campos mostra "Usuário deletado" ao encontrar nulo. A patota
        // sobrevive sem dono — só é possível porque Groups.CreatedByUserId virou anulável.
        await _db.Groups
            .Where(x => x.CreatedByUserId == userId)
            .ExecuteUpdateAsync(set => set.SetProperty(x => x.CreatedByUserId, (Guid?)null), cancellationToken);

        await _db.Polls
            .Where(x => x.CreatedByUserId == userId)
            .ExecuteUpdateAsync(set => set.SetProperty(x => x.CreatedByUserId, (Guid?)null), cancellationToken);

        await _db.CalendarEvents
            .Where(x => x.CreatedByUserId == userId)
            .ExecuteUpdateAsync(set => set.SetProperty(x => x.CreatedByUserId, (Guid?)null), cancellationToken);

        await _db.GroupTransactions
            .Where(x => x.CreatedByUserId == userId)
            .ExecuteUpdateAsync(set => set.SetProperty(x => x.CreatedByUserId, (Guid?)null), cancellationToken);

        // Estas três não têm chave estrangeira: sem isto, apontariam para um usuário
        // inexistente. São trilha de auditoria financeira, então o lançamento fica e some só
        // o autor. ExtraCharges foi a última descoberta — uma varredura de todas as colunas
        // de usuário contra o Postgres a flagrou órfã depois de um teste ponta a ponta.
        await _db.MonthlyPayments
            .Where(x => x.MarkedByAdminId == userId)
            .ExecuteUpdateAsync(set => set.SetProperty(x => x.MarkedByAdminId, (Guid?)null), cancellationToken);

        await _db.ExtraChargePayments
            .Where(x => x.MarkedByAdminId == userId)
            .ExecuteUpdateAsync(set => set.SetProperty(x => x.MarkedByAdminId, (Guid?)null), cancellationToken);

        await _db.ExtraCharges
            .Where(x => x.CreatedByAdminId == userId)
            .ExecuteUpdateAsync(set => set.SetProperty(x => x.CreatedByAdminId, (Guid?)null), cancellationToken);

        // Dado pessoal sem valor para a patota. Aposta e saldo são moeda fictícia; curtida sem
        // dono não significa nada; notificação é pessoal. MatchBetSelections sai junto com
        // MatchBets, pelo CASCADE do banco.
        await _db.MatchBets.Where(x => x.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await _db.UserBetBalances.Where(x => x.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await _db.ReplayLikes.Where(x => x.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await _db.ReplayFavorites.Where(x => x.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await _db.UserNotifications.Where(x => x.UserId == userId).ExecuteDeleteAsync(cancellationToken);

        // GroupAdmins, GroupFinanceiros, GroupInvites, PushTokens e UserAbsences saem pelo
        // CASCADE do banco — inclusive as funções de quem administra sem jogar. E a ausência
        // indo embora anula MatchPlayers.AutoRejectedByAbsenceId, por SET NULL.
        //
        // Pelo contexto e não pelo repositório: a operação atravessa uma dúzia de tabelas, e
        // salvar pelo repositório de UserEntity seria porta estreita para mudança larga.
        await _db.Users.Where(x => x.Id == userId).ExecuteDeleteAsync(cancellationToken);

        await tx.CommitAsync(cancellationToken);

        // Depois do commit e best-effort: objeto órfão custa alguns KB, derrubar a exclusão
        // que a pessoa pediu custa a exclusão — e a política exige que ela funcione.
        if (!string.IsNullOrWhiteSpace(photoKey))
        {
            try
            {
                await _images.DeleteAsync(photoKey, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Users] Avatar do usuário excluído {UserId} não pôde ser removido do R2.", userId);
            }
        }

        _logger.LogInformation("[Users] Conta {UserId} excluída a pedido do próprio usuário.", userId);

        return Result<List<AccountDeletionBlockerDto>>.Ok([], "Conta excluída com sucesso.");
    }

    /// <summary>
    /// Patotas onde ele é o único administrador. Conta os admins e compara: ser admin entre
    /// vários não trava, porque a patota continua administrável por quem ficar.
    /// </summary>
    private async Task<List<AccountDeletionBlockerDto>> FindSoleAdminGroupsAsync(
        Guid userId,
        CancellationToken cancellationToken) =>
        await _db.Groups
            .AsNoTracking()
            .Where(g => g.Admins.Any(a => a.UserId == userId)
                     && g.Admins.Count == 1
                     // Só trava se SOBRAR alguém. Numa patota onde ele é a única conta não há
                     // quem promover — a mensagem pediria o impossível e a pessoa ficaria presa
                     // entre excluir a patota na mão ou não excluir a conta. Essa patota é
                     // apagada junto, por AbandonedGroupCleanup.
                     && _db.Players.Any(p => p.GroupId == g.Id
                                          && p.UserId != null
                                          && p.UserId != userId))
            .Select(g => new AccountDeletionBlockerDto(g.Id, g.Name))
            .ToListAsync(cancellationToken);

    public async Task<Result> RemovePhotoAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await _repository.GetByIdIncludingInactiveAsync(userId, cancellationToken);
        if (user is null)
            return Result.Fail("Usuário não encontrado.", ResultStatus.NotFound);

        var previousKey = user.ProfilePhotoKey;

        user.RemoveProfilePhoto();
        _repository.Update(user);
        await _repository.SaveChangesAsync(cancellationToken);

        await DeletePreviousPhotoAsync(previousKey, userId, cancellationToken);
        return Result.Ok("Foto removida com sucesso.");
    }

    /// <summary>
    /// Best-effort, no mesmo espírito do descarte de clip no MatchService: a linha já foi
    /// gravada, e um objeto órfão de algumas dezenas de KB no bucket é um problema menor do
    /// que derrubar a troca de foto que o usuário acabou de fazer.
    /// </summary>
    private async Task DeletePreviousPhotoAsync(string? previousKey, Guid userId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(previousKey)) return;

        try
        {
            await _images.DeleteAsync(previousKey, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "[Users] Avatar anterior do usuário {UserId} não pôde ser removido do R2. Key={ObjectKey}",
                userId,
                previousKey);
        }
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
