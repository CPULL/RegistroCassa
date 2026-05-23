namespace RegistroCassa.Models;

public class GiornataContabile {
  public int Id { get; set; }
  public DateTime Date { get; set; }
  public decimal CashAtEndOfDay { get; set; }
  public string? Notes { get; set; }
  public string Sede { get; set; } = string.Empty;

  public string LastModifiedByUserId { get; set; } = string.Empty;
  public ApplicationUser? LastModifiedByUser { get; set; }
  public DateTime LastModifiedAt { get; set; } = DateTime.UtcNow;
}
