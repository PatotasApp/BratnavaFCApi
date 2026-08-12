using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Dtos.Conquistas;
using BratnavaFC.Domain.Dtos.Profile;
using BratnavaFC.Domain.Enums;
using BratnavaFC.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace BratnavaFC.Application.Services;

public sealed class ProfileService : IProfileService
{
    private readonly AppDbContext _db;
    private readonly IConquistaService _conquistas;

    public ProfileService(AppDbContext db, IConquistaService conquistas)
    {
        _db = db;
        _conquistas = conquistas;
    }

    public async Task<UserProfileDto> GetPublicProfileAsync(Guid userId, Guid requesterId,
        bool canOverridePrivacy, CancellationToken ct = default)
    {
        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == userId, ct)
            ?? throw new KeyNotFoundException("Perfil não encontrado.");
        var isOwner = requesterId == userId;
        if (!isOwner && !canOverridePrivacy)
        {
            if (user.ProfileVisibility == ProfileVisibility.Private)
                throw new UnauthorizedAccessException("Este perfil é privado.");
            if (user.ProfileVisibility == ProfileVisibility.SharedPatotas &&
                !await ShareAGroupAsync(userId, requesterId, ct))
                throw new UnauthorizedAccessException("Este perfil é visível apenas para integrantes das mesmas patotas.");
        }

        var players = await _db.Players.AsNoTracking()
            .Where(p => p.UserId == userId && !p.IsGuest)
            .Select(p => new { p.Id, p.GroupId, p.IsGoalkeeper })
            .ToListAsync(ct);
        var profile = new UserProfileDto
        {
            UserId = user.Id,
            Name = $"{user.FirstName} {user.LastName}".Trim(),
            UserName = user.UserName,
            PhotoUrl = user.ProfilePhotoData != null
                ? $"/api/Users/{user.Id}/photo?v={user.ProfilePhotoUpdatedAt?.ToUnixTimeMilliseconds()}" : null,
            Age = CalculateAge(user.BirthDate),
            Position = ResolvePosition(players.Select(x => x.IsGoalkeeper)),
        };
        if (players.Count == 0) return profile;

        var groupIds = players.Select(x => x.GroupId).Distinct().ToList();
        var groupNames = await _db.Groups.AsNoTracking()
            .Where(g => groupIds.Contains(g.Id))
            .ToDictionaryAsync(g => g.Id, g => g.Name, ct);
        var groupData = new Dictionary<Guid, GroupConquistasDto>();
        foreach (var groupId in groupIds)
            groupData[groupId] = await _conquistas.GetGroupConquistasAsync(groupId, ct);

        var feitosById = new Dictionary<string, ConquistaDto>();
        foreach (var player in players)
        {
            var pc = groupData[player.GroupId].Players.FirstOrDefault(x => x.PlayerId == player.Id);
            if (pc is null) continue;
            if (!user.ShowZoeiraAchievementsOnProfile && !isOwner)
                pc.Eventos = pc.Eventos.Where(x => x.Categoria != "Zoeira").ToList();

            var games = MarcoVal(pc, "marco-presenca");
            var goals = MarcoVal(pc, "marco-gols");
            var assists = MarcoVal(pc, "marco-assist");
            var mvps = MarcoVal(pc, "marco-mvp");
            profile.Games += games;
            profile.Goals += goals;
            profile.Assists += assists;
            profile.Mvps += mvps;

            var realGroupName = groupNames.GetValueOrDefault(player.GroupId, "Patota");
            var showName = isOwner || user.ShowPatotaNamesOnProfile;
            foreach (var title in pc.Titulos)
            {
                profile.Titulos.Add(Clone(title,
                    description: showName ? $"{title.Descricao} · {realGroupName}" : title.Descricao));
            }
            foreach (var achievement in pc.Eventos.Where(x => (x.Count ?? 0) > 0))
            {
                if (feitosById.TryGetValue(achievement.Id, out var accumulated))
                    accumulated.Count = (accumulated.Count ?? 0) + (achievement.Count ?? 0);
                else
                    feitosById[achievement.Id] = Clone(achievement, rarity: "Comum");
            }
            profile.Patotas.Add(new PatotaProfileDto
            {
                GroupId = showName ? player.GroupId : null,
                GroupName = showName ? realGroupName : "Patota privada",
                Games = games, Goals = goals, Assists = assists, Mvps = mvps,
                Conquistas = pc,
            });
        }

        profile.Marcos = ConquistaService.BuildCareerMarcos(profile.Games, profile.Goals, profile.Assists, profile.Mvps);
        profile.Feitos = feitosById.Values.OrderByDescending(x => x.Count).ToList();
        profile.Titulos = profile.Titulos.OrderByDescending(x => x.Ano).ThenBy(x => x.Posicao).ToList();
        profile.Patotas = profile.Patotas.OrderByDescending(x => x.Games).ToList();
        profile.Destaques = TopHighlights(profile).ToList();
        return profile;
    }

    public async Task<ProfilePrivacyDto> GetPrivacyAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == userId, ct)
            ?? throw new KeyNotFoundException("Usuário não encontrado.");
        return MapPrivacy(user.ProfileVisibility, user.ShowPatotaNamesOnProfile, user.ShowZoeiraAchievementsOnProfile);
    }

    public async Task<ProfilePrivacyDto> UpdatePrivacyAsync(Guid userId, UpdateProfilePrivacyDto dto, CancellationToken ct = default)
    {
        if (!Enum.TryParse<ProfileVisibility>(dto.Visibility, true, out var visibility))
            throw new ArgumentException("Visibilidade inválida.");
        var user = await _db.Users.FirstOrDefaultAsync(x => x.Id == userId, ct)
            ?? throw new KeyNotFoundException("Usuário não encontrado.");
        user.SetProfilePrivacy(visibility, dto.ShowPatotaNames, dto.ShowZoeiraAchievements);
        await _db.SaveChangesAsync(ct);
        return MapPrivacy(visibility, dto.ShowPatotaNames, dto.ShowZoeiraAchievements);
    }

    private async Task<bool> ShareAGroupAsync(Guid userId, Guid requesterId, CancellationToken ct)
    {
        var ownerGroups = _db.Players.AsNoTracking().Where(x => x.UserId == userId).Select(x => x.GroupId);
        return await _db.Players.AsNoTracking()
            .AnyAsync(x => x.UserId == requesterId && ownerGroups.Contains(x.GroupId), ct);
    }

    private static IEnumerable<ConquistaDto> TopHighlights(UserProfileDto profile) =>
        profile.Titulos.Concat(profile.Feitos).Concat(profile.Marcos.Where(x => x.Desbloqueada))
            .OrderByDescending(x => RarityRank(x.Raridade))
            .ThenByDescending(x => x.Ano ?? 0)
            .ThenByDescending(x => x.Count ?? x.Nivel ?? 0)
            .Take(3);

    private static int RarityRank(string value) => value switch
    {
        "Lendaria" => 4, "Epica" => 3, "Rara" => 2, _ => 1,
    };

    private static int MarcoVal(PlayerConquistasDto pc, string id) =>
        pc.Marcos.FirstOrDefault(x => x.Id == id)?.Valor ?? 0;

    private static int? CalculateAge(DateTimeOffset? birthDate)
    {
        if (birthDate is null) return null;
        var today = DateTimeOffset.UtcNow.Date;
        var birth = birthDate.Value.Date;
        var age = today.Year - birth.Year;
        if (birth > today.AddYears(-age)) age--;
        return age >= 0 ? age : null;
    }

    private static string? ResolvePosition(IEnumerable<bool> goalkeeperFlags)
    {
        var positions = goalkeeperFlags.Distinct().ToList();
        if (positions.Count == 0) return null;
        if (positions.Count > 1) return "Goleiro e linha";
        return positions[0] ? "Goleiro" : "Linha";
    }

    private static ProfilePrivacyDto MapPrivacy(ProfileVisibility visibility, bool names, bool zoeira) => new()
    {
        Visibility = visibility.ToString(), ShowPatotaNames = names, ShowZoeiraAchievements = zoeira,
    };

    private static ConquistaDto Clone(ConquistaDto value, string? description = null, string? rarity = null) => new()
    {
        Id = value.Id, Nome = value.Nome, Descricao = description ?? value.Descricao,
        Categoria = value.Categoria, Icone = value.Icone, Raridade = rarity ?? value.Raridade,
        PctPatota = value.PctPatota, Desbloqueada = value.Desbloqueada, Count = value.Count,
        Valor = value.Valor, Meta = value.Meta, Nivel = value.Nivel, TotalNiveis = value.TotalNiveis,
        ProximoNome = value.ProximoNome, Ano = value.Ano, Posicao = value.Posicao,
    };
}
