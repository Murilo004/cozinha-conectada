using System.Text.Json;
using IC.Cozinha.Data;
using IC.Cozinha.Models;
using IC.Shared;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace IC.Cozinha.Consumers;

/// <summary>
/// Lê o stream "pedidos-eventos". Quando o pedido é liberado (pagamento aprovado),
/// a cozinha "prepara" o pedido (só espera alguns segundos) e avisa que ficou pronto.
/// </summary>
public class PedidosEventsConsumer(
    IConnectionMultiplexer redis,
    IServiceScopeFactory scopeFactory,
    IEventPublisher publisher,
    IConfiguration configuration,
    ILogger<PedidosEventsConsumer> logger) : RedisStreamConsumer(redis, logger)
{
    protected override string Stream => Streams.Pedidos;
    protected override string Group => "cozinha-group";

    protected override async Task HandleAsync(string type, string payload, CancellationToken ct)
    {
        // O stream de pedidos também carrega "PedidoCriado", que não interessa aqui.
        if (type != EventTypes.PedidoLiberado) return;

        var evento = JsonSerializer.Deserialize<PedidoLiberadoEvent>(payload)!;

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CozinhaDbContext>();

        var item = await db.Pedidos.FirstOrDefaultAsync(p => p.PedidoId == evento.PedidoId, ct);

        if (item is null)
        {
            item = new PedidoCozinha { PedidoId = evento.PedidoId };
            db.Pedidos.Add(item);
            await db.SaveChangesAsync(ct);
            Logger.LogInformation("Pedido {PedidoId} em preparo.", item.PedidoId);
        }

        if (item.Status == StatusCozinha.Preparando)
        {
            // Simula o tempo de preparo. (Bloqueia este consumidor durante o preparo: ok para estudo.)
            var segundos = configuration.GetValue("Cozinha:TempoPreparoSegundos", 5);
            await Task.Delay(TimeSpan.FromSeconds(segundos), ct);

            item.Finalizar();
            await db.SaveChangesAsync(ct);
        }

        // Republica mesmo se já estava pronto: garante a entrega caso a publicação anterior tenha falhado.
        await publisher.PublishAsync(Streams.Cozinha, EventTypes.PedidoPronto, new PedidoProntoEvent(item.PedidoId));
        Logger.LogInformation("Pedido {PedidoId} pronto.", item.PedidoId);
    }
}
