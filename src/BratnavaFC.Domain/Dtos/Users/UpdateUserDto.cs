using BratnavaFC.Domain.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BratnavaFC.Domain.Dtos.Users
{
    public sealed class UpdateUserDto
    {
        public string? UserName { get; set; }     // opcional
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public DateTimeOffset? BirthDate { get; set; }
        public string? Phone { get; set; }
        public string? Email { get; set; }
        public int? Role { get; set; }           // opcional: troca role
        public Status? Status { get; set; }       // opcional: troca status
    }
}
