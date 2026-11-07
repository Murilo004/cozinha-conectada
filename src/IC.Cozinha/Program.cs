using System.Text.Json.Serialization;
using IC.Cozinha.Consumers;
using IC.Cozinha.Data;
using IC.Shared;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<CozinhaDbContext>(o =>
    o.UseSqlServer(builder.Configuration.GetConnectionString("CozinhaDb")));

builder.Services.AddRedisStreams(builder.Configuration);
builder.Services.AddHostedService<PedidosEventsConsumer>();

builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var app = builder.Build();

await app.Services.AguardarBancoAsync<CozinhaDbContext>();

app.MapGet("/", () => "Microsserviço de Cozinha no ar.");

// Apenas consulta: os itens são criados pelos eventos vindos do Redis.
app.MapGet("/cozinha", async (CozinhaDbContext db) =>
    await db.Pedidos.AsNoTracking().OrderByDescending(p => p.RecebidoEm).ToListAsync());

app.Run();
