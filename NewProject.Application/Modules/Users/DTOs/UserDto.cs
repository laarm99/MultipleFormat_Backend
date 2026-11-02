using System;
using System.Collections.Generic;

namespace NewProject.Application.Modules.Users.DTOs
{
    public class UserDto
    {
        public int Id { get; set; }
        public string DomainUserName { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public List<string> Roles { get; set; } = new();
        public DateTime CreatedDate { get; set; }
        public string? EmployeeNumber { get; set; }
        public string? Shift { get; set; }
        public string? Department { get; set; }
        public string? Position { get; set; }
        public DateTime? HireDate { get; set; }
        public string? SupervisorName { get; set; }
    }
}
