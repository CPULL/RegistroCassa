namespace RegistroCassa.Models;

public class ClosureDay {
	public int Id { get; set; }
	public DateTime Date { get; set; }
	public string Sede { get; set; } = string.Empty;
	public string? Notes { get; set; }
	public string CreatedByUserId { get; set; } = string.Empty;
	public ApplicationUser? CreatedByUser { get; set; }
	public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
