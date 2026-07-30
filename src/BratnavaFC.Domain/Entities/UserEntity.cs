using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Enums;

namespace BratnavaFC.Domain.Entities;

public enum UserRole
{
    User = 1,
    Admin = 2,
    GodMode = 3
}

public sealed class UserEntity : InactivatableEntity
{
    public string UserName { get; private set; } = null!;
    public string FirstName { get; private set; } = null!;
    public string LastName { get; private set; } = null!;
    public DateTimeOffset? BirthDate { get; private set; }
    public string Email { get; private set; } = null!;
    public string Password { get; private set; } = null!;
    public string? Phone { get; private set; }

    public UserRole Role { get; private set; } = UserRole.User;

    private readonly List<PlayerEntity> _players = [];
    public IReadOnlyCollection<PlayerEntity> Players => _players;

    private readonly List<GroupAdminEntity> _admins = [];
    public IReadOnlyCollection<GroupAdminEntity> Admins => _admins;

    private readonly List<GroupFinanceiroEntity> _financeiros = [];
    public IReadOnlyCollection<GroupFinanceiroEntity> Financeiros => _financeiros;

    // EF
    private UserEntity() { }

    public UserEntity(
        string userName,
        string firstName,
        string lastName,
        string email,
        string passwordHashed,
        string? phone,
        DateTimeOffset? birthDate,
        UserRole role = UserRole.User)
    {
        SetUserName(userName);
        UpdateProfile(firstName, lastName, birthDate, phone);
        SetEmail(email);
        SetPasswordHash(passwordHashed);
        SetRole(role);
    }

    /// <summary>
    /// Grava o username em minúsculas.
    ///
    /// O login busca por <c>UserName == request.Username.Trim().ToLower()</c>, então gravar
    /// preservando a caixa deixava o usuário inacessível: quem fosse criado como "Joao"
    /// nunca era encontrado, com qualquer caixa digitada no login.
    /// </summary>
    public void SetUserName(string userName)
    {
        if (string.IsNullOrWhiteSpace(userName))
            throw new InvalidOperationException("UserName is required.");

        UserName = userName.Trim().ToLowerInvariant();
    }

    /// <summary>
    /// Grava o email na forma canônica. A normalização acontece aqui, e não só nos
    /// validators, porque é ela que sustenta o índice único da coluna — qualquer caminho de
    /// escrita que não passe pelo controller (seed, job, script) precisa dela também.
    /// </summary>
    public void SetEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            throw new InvalidOperationException("Email is required.");

        Email = EmailAddress.Normalize(email);
    }

    public void SetPasswordHash(string passwordHashed)
    {
        if (string.IsNullOrWhiteSpace(passwordHashed))
            throw new InvalidOperationException("Password hash is required.");

        Password = passwordHashed;
    }

    public void UpdateProfile(string firstName, string lastName, DateTimeOffset? birthDate, string? phone)
    {
        if (string.IsNullOrWhiteSpace(firstName))
            throw new InvalidOperationException("FirstName is required.");

        if (string.IsNullOrWhiteSpace(lastName))
            throw new InvalidOperationException("LastName is required.");

        FirstName = firstName.Trim();
        LastName = lastName.Trim();
        BirthDate = birthDate;
        Phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();
    }

    public void SetRole(UserRole role)
    {
        Role = role;
    }
}
