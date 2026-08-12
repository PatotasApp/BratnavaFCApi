using BratnavaFC.Domain.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BratnavaFC.Domain.Dtos.Users
{
    /// <summary>
    /// O e-mail não está aqui de propósito: é a identidade de autenticação no Firebase e
    /// alterá-lo neste fluxo abriria caminho para account takeover. O UserName pode mudar —
    /// é apelido de identificação no app, não credencial. Role e Status só têm efeito no
    /// fluxo administrativo do UserService.
    /// </summary>
    public sealed class UpdateUserDto
    {
        public string? UserName { get; set; }     // opcional; precisa ser único
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public DateTimeOffset? BirthDate { get; set; }
        public string? Phone { get; set; }
        public int? Role { get; set; }           // opcional: troca role
        public Status? Status { get; set; }       // opcional: troca status
    }
}
