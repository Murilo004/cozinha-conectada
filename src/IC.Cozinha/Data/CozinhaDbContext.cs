using IC.Cozinha.Models;
using Microsoft.EntityFrameworkCore;

namespace IC.Cozinha.Data;

public class CozinhaDbContext(DbContextOptions<CozinhaDbContext> options) : DbContext(options)
{
    public DbSet<PedidoCozinha> Pedidos => Set<PedidoCozinha>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PedidoCozinha>(e =>
        {
            e.HasKey(p => p.Id);
            e.HasIndex(p => p.PedidoId).IsUnique(); // um item de cozinha por pedido
            e.Property(p => p.Status).HasConversion<string>().HasMaxLength(20);
        });
    }
}
