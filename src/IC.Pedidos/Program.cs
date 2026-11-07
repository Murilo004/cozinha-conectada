using System.Text.Json.Serialization;
using IC.Pedidos.Consumers;
using IC.Pedidos.Data;
using IC.Pedidos.Models;
using IC.Shared;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<PedidosDbContext>(o =>
    o.UseSqlServer(builder.Configuration.GetConnectionString("PedidosDb")));

builder.Services.AddRedisStreams(builder.Configuration);
builder.Services.AddHostedService<PagamentoEventsConsumer>();
builder.Services.AddHostedService<CozinhaEventsConsumer>();

// Mostra o status como texto ("Aguardando") em vez de número.
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var app = builder.Build();

await app.Services.AguardarBancoAsync<PedidosDbContext>();

app.MapGet("/", () => "Microsserviço de Pedido no ar.");

// Cria o pedido (status Aguardando) e publica "PedidoCriado" para o serviço de Pagamento.
app.MapPost("/pedidos", async (CriarPedidoRequest req, PedidosDbContext db, IEventPublisher publisher) =>
{
    if (string.IsNullOrWhiteSpace(req.Cliente) || req.Valor <= 0)
        return Results.BadRequest(new { erro = "Informe o cliente e um valor maior que zero." });

    var pedido = new Pedido
    {
        Cliente = req.Cliente.Trim(),
        Descricao = req.Descricao?.Trim() ?? string.Empty,
        Valor = req.Valor
    };

    db.Pedidos.Add(pedido);
    await db.SaveChangesAsync();

    await publisher.PublishAsync(Streams.Pedidos, EventTypes.PedidoCriado,
        new PedidoCriadoEvent(pedido.Id, pedido.Cliente, pedido.Valor));

    return Results.Created($"/pedidos/{pedido.Id}", pedido);
});

app.MapGet("/pedidos", async (PedidosDbContext db) =>
    await db.Pedidos.AsNoTracking().OrderByDescending(p => p.CriadoEm).ToListAsync());

app.MapGet("/pedidos/{id:guid}", async (Guid id, PedidosDbContext db) =>
    await db.Pedidos.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id) is { } pedido
        ? Results.Ok(pedido)
        : Results.NotFound());

app.Run();

public record CriarPedidoRequest(string Cliente, string? Descricao, decimal Valor);
