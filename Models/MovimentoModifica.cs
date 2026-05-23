namespace RegistroCassa.Models;

public class MovimentoModifica
{
    public int Id { get; set; }
    public int MovimentoId { get; set; }
    public Movimento? Movimento { get; set; }

    public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;
    public string ModifiedByUserId { get; set; } = string.Empty;
    public ApplicationUser? ModifiedByUser { get; set; }

    public decimal OldAmount { get; set; }
    public string OldDescription { get; set; } = string.Empty;
    public string? OldInvoiceNumber { get; set; }
    public TipoMovimento OldType { get; set; }

    public string Justification { get; set; } = string.Empty;
}
