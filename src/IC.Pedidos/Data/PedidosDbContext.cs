using IC.Pedidos.Models;
using Microsoft.EntityFrameworkCore;

namespace IC.Pedidos.Data;

public class PedidosDbContext(DbContextOptions<PedidosDbContext> options) : DbContext(options)
{
    public DbSet<Pedido> Pedidos => Set<Pedido>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Pedido>(e =>
        {
            e.HasKey(p => p.Id);
            e.Property(p => p.Cliente).HasMaxLength(100).IsRequired();
            e.Property(p => p.Descricao).HasMaxLength(200);
            e.Property(p => p.Valor).HasPrecision(18, 2);
            e.Property(p => p.Status).HasConversion<string>().HasMaxLength(20);
        });
    }
}
