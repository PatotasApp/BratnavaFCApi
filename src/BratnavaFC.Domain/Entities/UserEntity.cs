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
    /// <summary>
    /// Sentinela gravada na coluna Password dos usuários cuja senha vive no Firebase.
    /// </summary>
    public const string FirebaseManagedPassword = "SECURED_BY_FIREBASE";


    /// <summary>
    /// UID do usuário no Firebase Auth — identidade EXTERNA. É o único elo entre o token
    /// recebido e a linha desta tabela.
    ///
    /// ATENÇÃO: para os usuários migrados em lote este valor COINCIDE com o
    /// <see cref="BaseEntity.Id"/>, porque o script os importa forçando Uid = GUID. Essa
    /// coincidência é acidental e temporária: usuário novo que entra por social login
    /// recebe um UID gerado pelo Firebase (alfanumérico de 28 chars), completamente
    /// diferente do GUID interno. NUNCA escreva código que dependa de firebaseUid == Id,
    /// e nunca use este campo como chave estrangeira — a PK continua sendo o Id.
    ///
    /// Nulo enquanto o usuário não tiver passado pela migração.
    /// </summary>
    public string? FirebaseUid { get; private set; }

    /// <summary>
    /// Identificador único do usuário dentro do app (apelido exibido em listas, apostas e
    /// notificações). NÃO é credencial: o login é exclusivamente por e-mail, validado no
    /// Firebase. A unicidade é garantida na camada de serviço, antes de gravar.
    /// </summary>
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

    public void SetUserName(string userName)
    {
        if (string.IsNullOrWhiteSpace(userName))
            throw new InvalidOperationException("UserName is required.");

        UserName = userName.Trim();
    }

    /// <summary>
    /// Normaliza para minúsculas porque o e-mail é a identidade de autenticação: ele é a
    /// chave de busca no login e precisa casar com o e-mail gravado no Firebase.
    /// </summary>
    public void SetEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            throw new InvalidOperationException("Email is required.");

        Email = email.Trim().ToLowerInvariant();
    }

    /// <summary>
    /// Nome de exibição enviado ao Firebase (campo DisplayName do UserRecord). Derivado do
    /// perfil, não do UserName — é dado de apresentação, não identificador.
    /// </summary>
    public string DisplayName => $"{FirstName} {LastName}".Trim();

    public void SetFirebaseUid(string firebaseUid)
    {
        if (string.IsNullOrWhiteSpace(firebaseUid))
            throw new InvalidOperationException("FirebaseUid is required.");

        FirebaseUid = firebaseUid.Trim();
    }

    /// <summary>
    /// OBSOLETO. A senha é do Firebase — nada na API valida hash. A coluna sobrevive apenas
    /// até o script de migração rodar, e sai numa migration seguinte. Não escreva lógica
    /// nova em cima dela.
    /// </summary>
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
