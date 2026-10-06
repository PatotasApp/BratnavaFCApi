namespace BratnavaFC.Domain.Dtos.Users;

/// <summary>
/// Patota que impede a exclusão da conta por ficar sem nenhum administrador.
///
/// Carrega o Id junto do nome de propósito: a recusa precisa ser acionável, e o cliente
/// usa o Id para levar a pessoa direto às configurações daquela patota, onde ela promove
/// outro administrador. Uma mensagem genérica obrigaria a caçar quais são — fricção que a
/// política da Play lê como obstáculo intencional à exclusão.
/// </summary>
public sealed record AccountDeletionBlockerDto(Guid GroupId, string GroupName);
