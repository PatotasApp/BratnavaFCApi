namespace BratnavaFC.Domain.Enums;

/// <summary>O que foi denunciado. TargetId aponta para a entidade do tipo.</summary>
public enum ContentReportTargetType
{
    /// <summary>Usuário (perfil, nome, foto de perfil). TargetId = UserId.</summary>
    User = 1,
    /// <summary>Vídeo de replay. TargetId = ReplayClipId.</summary>
    Replay = 2,
    /// <summary>Enquete ou imagem de enquete. TargetId = PollId.</summary>
    Poll = 3,
    /// <summary>Patota (nome, logo). TargetId = GroupId.</summary>
    Group = 4,
}
