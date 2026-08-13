using BratnavaFC.Domain.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BratnavaFC.Domain.Dtos.Users
{
    public sealed class ListUsersRequestDto
    {
        public string? Search { get; set; }           // nome/email/username

        /// <summary>
        /// Busca só pelo handle, para as telas que escolhem UMA pessoa para conceder algo num
        /// grupo (convite, admin, financeiro). O UserName tem índice único, então ele identifica
        /// sem ambiguidade — enquanto o Search casa nome e e-mail e devolve gente parecida, que é
        /// justamente o ruído que faz escolher a pessoa errada.
        ///
        /// Tem precedência sobre o Search quando ambos vêm preenchidos.
        /// </summary>
        public string? UserName { get; set; }
        public Status? Status { get; set; }            // seu Status (smallint)
        public int? Role { get; set; }                // UserRole (int)
        public bool IncludeInactive { get; set; }     // inclui soft-deleted/inativados via query filter

        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;
    }
}
