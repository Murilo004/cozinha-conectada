using IC.Pagamentos.Models;
using Microsoft.EntityFrameworkCore;

namespace IC.Pagamentos.Data;

public class PagamentosDbContext(DbContextOptions<PagamentosDbContext> options) : DbContext(options)
{
    public DbSet<Pagamento> Pagamentos => Set<Pagamento>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Pagamento>(e =>
        {
            e.HasKey(p => p.Id);
            e.HasIndex(p => p.PedidoId).IsUnique(); // um pagamento por pedido
            e.Property(p => p.Valor).HasPrecision(18, 2);
            e.Property(p => p.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(p => p.Motivo).HasMaxLength(200);
        });
    }
}
