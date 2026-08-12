using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Enums;

namespace BratnavaFC.Domain.Dtos.Users;

/// <summary>
/// Perfil interno do usuário autenticado. O <paramref name="Id"/> é a identidade INTERNA
/// (GUID, o mesmo usado como FK em todo o resto) — não o UID do Firebase.
/// </summary>
public sealed record MeDto(
    Guid Id,
    string Email,
    string UserName,
    string FirstName,
    string LastName,
    string? Phone,
    UserRole Role,
    Status Status);
