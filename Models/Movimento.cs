namespace RegistroCassa.Models;

public enum TipoMovimento
{
    Uscita = 0,
    Entrata = 1
}

public class Movimento {
  public int Id { get; set; }
  public DateTime Date { get; set; }
  public TipoMovimento Type { get; set; }
  public decimal Amount { get; set; }
  public string Description { get; set; } = string.Empty;
  public string? InvoiceNumber { get; set; }
	public string Sede { get; set; } = string.Empty;

	public string CreatedByUserId { get; set; } = string.Empty;
  public ApplicationUser? CreatedByUser { get; set; }
  public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

  public ICollection<MovimentoModifica> Modifiche { get; set; } = new List<MovimentoModifica>();
  public bool IsDeleted { get; set; } = false;
  public DateTime? DeletedAt { get; set; }
  public string? DeletedByUserId { get; set; }
  public ApplicationUser? DeletedByUser { get; set; }
}
