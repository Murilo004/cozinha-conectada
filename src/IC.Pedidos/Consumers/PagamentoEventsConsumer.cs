using System.Text.Json;
using IC.Pedidos.Data;
using IC.Pedidos.Models;
using IC.Shared;
using StackExchange.Redis;

namespace IC.Pedidos.Consumers;

/// <summary>
/// Lê o stream "pagamentos-eventos".
/// Pagamento aprovado -> pedido vira Aprovado e é liberado para a cozinha (evento PedidoLiberado).
/// Pagamento recusado -> pedido vira Recusado.
/// </summary>
public class PagamentoEventsConsumer(
    IConnectionMultiplexer redis,
    IServiceScopeFactory scopeFactory,
    IEventPublisher publisher,
    ILogger<PagamentoEventsConsumer> logger) : RedisStreamConsumer(redis, logger)
{
    protected override string Stream => Streams.Pagamentos;
    protected override string Group => "pedidos-group";

    protected override async Task HandleAsync(string type, string payload, CancellationToken ct)
    {
        if (type != EventTypes.PagamentoProcessado) return;

        var evento = JsonSerializer.Deserialize<PagamentoProcessadoEvent>(payload)!;

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PedidosDbContext>();

        var pedido = await db.Pedidos.FindAsync([evento.PedidoId], ct);
        if (pedido is null)
        {
            Logger.LogWarning("Pedido {PedidoId} não encontrado.", evento.PedidoId);
            return;
        }

        // Idempotência: se o pedido já saiu de "Aguardando", este evento é repetido.
        if (pedido.Status != StatusPedido.Aguardando) return;

        if (evento.Aprovado)
        {
            pedido.AlterarStatus(StatusPedido.Aprovado);
            await db.SaveChangesAsync(ct);

            await publisher.PublishAsync(Streams.Pedidos, EventTypes.PedidoLiberado, new PedidoLiberadoEvent(pedido.Id));
            Logger.LogInformation("Pedido {PedidoId} aprovado e liberado para a cozinha.", pedido.Id);
        }
        else
        {
            pedido.AlterarStatus(StatusPedido.Recusado);
            await db.SaveChangesAsync(ct);

            Logger.LogInformation("Pedido {PedidoId} recusado: {Motivo}", pedido.Id, evento.Motivo);
        }
    }
}
