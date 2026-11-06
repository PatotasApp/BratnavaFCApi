using BratnavaFC.Domain.Dtos.Profile;

namespace BratnavaFC.Application.Abstractions;

public interface IProfileService
{
    Task<UserProfileDto> GetPublicProfileAsync(Guid userId, Guid requesterId, bool canOverridePrivacy, CancellationToken ct = default);
    /// <summary>
    /// Vitrine do perfil para o link compartilhado. <paramref name="requesterId"/> nulo = sem login.
    /// Respeita ProfileVisibility e os bloqueios (bloqueio responde como inexistente).
    /// </summary>
    Task<ProfileShowcaseResult> GetShowcaseAsync(Guid userId, Guid? requesterId, bool canOverridePrivacy, CancellationToken ct = default);
    Task<ProfilePrivacyDto> GetPrivacyAsync(Guid userId, CancellationToken ct = default);
    Task<ProfilePrivacyDto> UpdatePrivacyAsync(Guid userId, UpdateProfilePrivacyDto dto, CancellationToken ct = default);
}
