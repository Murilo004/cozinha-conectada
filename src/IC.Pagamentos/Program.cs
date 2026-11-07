using System.Text.Json.Serialization;
using IC.Pagamentos.Consumers;
using IC.Pagamentos.Data;
using IC.Shared;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<PagamentosDbContext>(o =>
    o.UseSqlServer(builder.Configuration.GetConnectionString("PagamentosDb")));

builder.Services.AddRedisStreams(builder.Configuration);
builder.Services.AddHostedService<PedidosEventsConsumer>();

builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var app = builder.Build();

await app.Services.AguardarBancoAsync<PagamentosDbContext>();

app.MapGet("/", () => "Microsserviço de Pagamento no ar.");

// Apenas consulta: os pagamentos são criados pelos eventos vindos do Redis.
app.MapGet("/pagamentos", async (PagamentosDbContext db) =>
    await db.Pagamentos.AsNoTracking().OrderByDescending(p => p.ProcessadoEm).ToListAsync());

app.MapGet("/pagamentos/pedido/{pedidoId:guid}", async (Guid pedidoId, PagamentosDbContext db) =>
    await db.Pagamentos.AsNoTracking().FirstOrDefaultAsync(p => p.PedidoId == pedidoId) is { } pagamento
        ? Results.Ok(pagamento)
        : Results.NotFound());

app.Run();
