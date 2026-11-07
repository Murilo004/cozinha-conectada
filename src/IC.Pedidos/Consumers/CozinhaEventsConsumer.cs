using System.Text.Json;
using IC.Pedidos.Data;
using IC.Pedidos.Models;
using IC.Shared;
using StackExchange.Redis;

namespace IC.Pedidos.Consumers;

/// <summary>Lê o stream "cozinha-eventos". Pedido pronto -> status Pronto.</summary>
public class CozinhaEventsConsumer(
    IConnectionMultiplexer redis,
    IServiceScopeFactory scopeFactory,
    ILogger<CozinhaEventsConsumer> logger) : RedisStreamConsumer(redis, logger)
{
    protected override string Stream => Streams.Cozinha;
    protected override string Group => "pedidos-group";

    protected override async Task HandleAsync(string type, string payload, CancellationToken ct)
    {
        if (type != EventTypes.PedidoPronto) return;

        var evento = JsonSerializer.Deserialize<PedidoProntoEvent>(payload)!;

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PedidosDbContext>();

        var pedido = await db.Pedidos.FindAsync([evento.PedidoId], ct);
        if (pedido is null)
        {
            Logger.LogWarning("Pedido {PedidoId} não encontrado.", evento.PedidoId);
            return;
        }

        // Idempotência: só avança se estiver Aprovado.
        if (pedido.Status != StatusPedido.Aprovado) return;

        pedido.AlterarStatus(StatusPedido.Pronto);
        await db.SaveChangesAsync(ct);

        Logger.LogInformation("Pedido {PedidoId} está pronto.", pedido.Id);
    }
}
