using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace IC.Shared;

public static class DatabaseExtensions
{
    /// <summary>
    /// Espera o SQL Server aceitar conexões e cria o banco/tabelas (EnsureCreated).
    /// Para estudo, EnsureCreated basta; em um projeto real use Migrations.
    /// </summary>
    public static async Task AguardarBancoAsync<TContext>(this IServiceProvider services) where TContext : DbContext
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Database");

        for (var tentativa = 1; tentativa <= 30; tentativa++)
        {
            try
            {
                await db.Database.EnsureCreatedAsync();
                logger.LogInformation("Banco {Context} pronto.", typeof(TContext).Name);
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning("SQL Server indisponível (tentativa {Tentativa}/30): {Mensagem}", tentativa, ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(3));
            }
        }

        throw new InvalidOperationException("Não foi possível conectar ao SQL Server.");
    }
}
