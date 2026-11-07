using System.Text.Json;
using IC.Pagamentos.Data;
using IC.Pagamentos.Models;
using IC.Shared;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace IC.Pagamentos.Consumers;

/// <summary>
/// Lê o stream "pedidos-eventos" e processa o pagamento dos pedidos criados.
///
/// Regra de negócio (simples): valor até o limite configurado (padrão R$ 500,00) é aprovado;
/// acima disso é recusado. Não existe gateway real: é só uma simulação.
/// </summary>
public class PedidosEventsConsumer(
    IConnectionMultiplexer redis,
    IServiceScopeFactory scopeFactory,
    IEventPublisher publisher,
    IConfiguration configuration,
    ILogger<PedidosEventsConsumer> logger) : RedisStreamConsumer(redis, logger)
{
    protected override string Stream => Streams.Pedidos;
    protected override string Group => "pagamentos-group";

    protected override async Task HandleAsync(string type, string payload, CancellationToken ct)
    {
        // O stream de pedidos também carrega "PedidoLiberado", que não interessa aqui.
        if (type != EventTypes.PedidoCriado) return;

        var evento = JsonSerializer.Deserialize<PedidoCriadoEvent>(payload)!;

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PagamentosDbContext>();

        var pagamento = await db.Pagamentos.FirstOrDefaultAsync(p => p.PedidoId == evento.PedidoId, ct);

        // Idempotência: se já processamos este pedido, não processa de novo.
        // Só republica o resultado (caso a publicação anterior tenha falhado).
        if (pagamento is null)
        {
            var limite = configuration.GetValue("Pagamento:LimiteAprovacao", 500m);
            var aprovado = evento.Valor <= limite;

            pagamento = new Pagamento
            {
                PedidoId = evento.PedidoId,
                Valor = evento.Valor,
                Status = aprovado ? StatusPagamento.Aprovado : StatusPagamento.Recusado,
                Motivo = aprovado
                    ? "Pagamento aprovado."
                    : $"Valor acima do limite de R$ {limite:0.00}."
            };

            db.Pagamentos.Add(pagamento);
            await db.SaveChangesAsync(ct);
        }

        await publisher.PublishAsync(Streams.Pagamentos, EventTypes.PagamentoProcessado,
            new PagamentoProcessadoEvent(pagamento.PedidoId, pagamento.Status == StatusPagamento.Aprovado, pagamento.Motivo));

        Logger.LogInformation("Pagamento do pedido {PedidoId}: {Status}", pagamento.PedidoId, pagamento.Status);
    }
}
