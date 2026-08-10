using BratnavaFC.Domain.Dtos.Profile;

namespace BratnavaFC.Application.Abstractions;

public interface IProfileService
{
    Task<UserProfileDto> GetPublicProfileAsync(Guid userId, Guid requesterId, bool canOverridePrivacy, CancellationToken ct = default);
    Task<ProfilePrivacyDto> GetPrivacyAsync(Guid userId, CancellationToken ct = default);
    Task<ProfilePrivacyDto> UpdatePrivacyAsync(Guid userId, UpdateProfilePrivacyDto dto, CancellationToken ct = default);
}
