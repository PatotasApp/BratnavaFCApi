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
        public Status? Status { get; set; }            // seu Status (smallint)
        public int? Role { get; set; }                // UserRole (int)
        public bool IncludeInactive { get; set; }     // inclui soft-deleted/inativados via query filter

        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;
    }
}
