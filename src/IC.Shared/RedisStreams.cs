using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace IC.Shared;

// ---------------------------------------------------------------------------
// PUBLISHER: escreve um evento no stream (comando XADD)
// ---------------------------------------------------------------------------

public interface IEventPublisher
{
    Task PublishAsync<T>(string stream, string type, T payload);
}

public class RedisEventPublisher(IConnectionMultiplexer redis, ILogger<RedisEventPublisher> logger) : IEventPublisher
{
    public async Task PublishAsync<T>(string stream, string type, T payload)
    {
        var json = JsonSerializer.Serialize(payload);

        var id = await redis.GetDatabase().StreamAddAsync(stream,
        [
            new NameValueEntry("type", type),
            new NameValueEntry("payload", json)
        ]);

        logger.LogInformation("Evento {Type} publicado em {Stream} (id {Id})", type, stream, id);
    }
}

// ---------------------------------------------------------------------------
// CONSUMER: lê o stream usando um Consumer Group (XREADGROUP + XACK)
//
// Como funciona:
//  - Cada serviço tem o SEU grupo. Assim, vários serviços podem ler o mesmo
//    stream e cada um recebe todas as mensagens.
//  - A mensagem só sai da lista de "pendentes" do grupo depois do XACK.
//    Se o serviço cair no meio do processamento, ela é reprocessada ao subir.
//  - Isso é "at-least-once": a mesma mensagem pode chegar mais de uma vez,
//    por isso os handlers são idempotentes.
// ---------------------------------------------------------------------------

public abstract class RedisStreamConsumer : BackgroundService
{
    private readonly IConnectionMultiplexer _redis;

    protected ILogger Logger { get; }

    protected RedisStreamConsumer(IConnectionMultiplexer redis, ILogger logger)
    {
        _redis = redis;
        Logger = logger;
    }

    /// <summary>Stream que será lido.</summary>
    protected abstract string Stream { get; }

    /// <summary>Nome do consumer group (um por serviço).</summary>
    protected abstract string Group { get; }

    /// <summary>Nome do consumidor dentro do grupo. Para escalar, use um nome por instância.</summary>
    protected virtual string ConsumerName => "worker-1";

    /// <summary>Trata a mensagem. Mensagens de tipos que não interessam devem apenas retornar.</summary>
    protected abstract Task HandleAsync(string type, string payload, CancellationToken ct);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var db = _redis.GetDatabase();

                await CriarGrupoAsync(db);
                await ProcessarPendentesAsync(db, ct);
                await ProcessarNovasAsync(db, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Aplicação encerrando.
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Erro no consumidor {Group}. Nova tentativa em 3s.", Group);

                try { await Task.Delay(TimeSpan.FromSeconds(3), ct); }
                catch (OperationCanceledException) { }
            }
        }
    }

    private async Task CriarGrupoAsync(IDatabase db)
    {
        try
        {
            // "0-0" = o grupo começa lendo desde o início do stream.
            // createStream: true cria o stream se ele ainda não existir (MKSTREAM).
            await db.StreamCreateConsumerGroupAsync(Stream, Group, StreamPosition.Beginning, createStream: true);
            Logger.LogInformation("Grupo {Group} criado no stream {Stream}", Group, Stream);
        }
        catch (RedisServerException ex) when (ex.Message.Contains("BUSYGROUP"))
        {
            // O grupo já existe: ok.
        }
    }

    /// <summary>Ao subir, reprocessa o que foi lido antes mas nunca confirmado (XACK).</summary>
    private async Task ProcessarPendentesAsync(IDatabase db, CancellationToken ct)
    {
        var cursor = "0";

        while (!ct.IsCancellationRequested)
        {
            var entries = await db.StreamReadGroupAsync(Stream, Group, ConsumerName, cursor, count: 50);
            if (entries.Length == 0) return;

            foreach (var entry in entries)
                await ProcessarEntradaAsync(db, entry, ct);

            cursor = entries[^1].Id.ToString();
        }
    }

    /// <summary>Loop principal: lê mensagens novas (">") e confirma cada uma depois de tratar.</summary>
    private async Task ProcessarNovasAsync(IDatabase db, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var entries = await db.StreamReadGroupAsync(Stream, Group, ConsumerName, StreamPosition.NewMessages, count: 10);

            if (entries.Length == 0)
            {
                // O StackExchange.Redis não faz comandos bloqueantes (XREAD BLOCK), então fazemos polling.
                await Task.Delay(500, ct);
                continue;
            }

            foreach (var entry in entries)
                await ProcessarEntradaAsync(db, entry, ct);
        }
    }

    private async Task ProcessarEntradaAsync(IDatabase db, StreamEntry entry, CancellationToken ct)
    {
        if (entry.IsNull) return;

        try
        {
            var type = entry["type"].ToString();
            var payload = entry["payload"].ToString();

            await HandleAsync(type, payload, ct);

            // Só confirma depois de tratar com sucesso.
            await db.StreamAcknowledgeAsync(Stream, Group, entry.Id);
            Logger.LogDebug("[{Group}] {Type} confirmado (id {Id})", Group, type, entry.Id);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Sem XACK: a mensagem continua pendente e será reprocessada na próxima subida.
            Logger.LogError(ex, "[{Group}] Falha ao processar a mensagem {Id}. Ela continua pendente.", Group, entry.Id);
        }
    }
}

// ---------------------------------------------------------------------------
// Registro no DI
// ---------------------------------------------------------------------------

public static class RedisStreamsExtensions
{
    public static IServiceCollection AddRedisStreams(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Redis") ?? "localhost:6379";

        services.AddSingleton<IConnectionMultiplexer>(_ =>
        {
            var options = ConfigurationOptions.Parse(connectionString);
            options.AbortOnConnectFail = false; // não derruba o serviço se o Redis ainda estiver subindo
            return ConnectionMultiplexer.Connect(options);
        });

        services.AddSingleton<IEventPublisher, RedisEventPublisher>();

        return services;
    }
}
