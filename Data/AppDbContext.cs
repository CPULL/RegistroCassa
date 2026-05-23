using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using RegistroCassa.Models;

namespace RegistroCassa.Data;

public class AppDbContext : IdentityDbContext<ApplicationUser> {
  public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

  public DbSet<Movimento> Movimenti => Set<Movimento>();
  public DbSet<MovimentoModifica> MovimentoModifiche => Set<MovimentoModifica>();
  public DbSet<GiornataContabile> GiornateContabili => Set<GiornataContabile>();

  protected override void OnModelCreating(ModelBuilder builder) {
    base.OnModelCreating(builder);

    builder.Entity<Movimento>(e => {
      e.HasKey(x => x.Id);
      e.Property(x => x.Amount).HasPrecision(18, 2);
      e.Property(x => x.Description).HasMaxLength(200);
      e.Property(x => x.InvoiceNumber).HasMaxLength(100);
      e.HasOne(x => x.CreatedByUser)
       .WithMany()
       .HasForeignKey(x => x.CreatedByUserId)
       .OnDelete(DeleteBehavior.Restrict);
      e.HasOne(x => x.DeletedByUser)
       .WithMany()
       .HasForeignKey(x => x.DeletedByUserId)
       .OnDelete(DeleteBehavior.Restrict);
    });

    builder.Entity<MovimentoModifica>(e => {
      e.HasKey(x => x.Id);
      e.Property(x => x.OldAmount).HasPrecision(18, 2);
      e.Property(x => x.OldDescription).HasMaxLength(200);
      e.Property(x => x.OldInvoiceNumber).HasMaxLength(100);
      e.Property(x => x.Justification).HasMaxLength(500);
      e.HasOne(x => x.Movimento)
       .WithMany(x => x.Modifiche)
       .HasForeignKey(x => x.MovimentoId)
       .OnDelete(DeleteBehavior.Cascade);
      e.HasOne(x => x.ModifiedByUser)
       .WithMany()
       .HasForeignKey(x => x.ModifiedByUserId)
       .OnDelete(DeleteBehavior.Restrict);
    });

    builder.Entity<GiornataContabile>(e => {
      e.HasKey(x => x.Id);
			e.HasIndex(x => new { x.Date, x.Sede }).IsUnique();
			e.Property(x => x.CashAtEndOfDay).HasPrecision(18, 2);
      e.HasOne(x => x.LastModifiedByUser)
       .WithMany()
       .HasForeignKey(x => x.LastModifiedByUserId)
       .OnDelete(DeleteBehavior.Restrict);
    });
  }
}
