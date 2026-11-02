namespace MultipleFormat.Application.Modules.Supervisors.DTOs
{
    public class SupervisorDto
    {
        public int Id { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string? DomainUserName { get; set; }
        public string? Email { get; set; }
        public bool IsActive { get; set; }
        public string? EmployeeNumber { get; set; }
        public string? Shift { get; set; }
        public string? Department { get; set; }
        public string? Position { get; set; }
        public DateTime? HireDate { get; set; }
        public string? SupervisorName { get; set; }
    }
}
