namespace IC.Shared;

/// <summary>Nomes dos Redis Streams usados na comunicação entre os serviços.</summary>
public static class Streams
{
    /// <summary>Eventos publicados pelo serviço de Pedido (PedidoCriado, PedidoLiberado).</summary>
    public const string Pedidos = "pedidos-eventos";

    /// <summary>Eventos publicados pelo serviço de Pagamento (PagamentoProcessado).</summary>
    public const string Pagamentos = "pagamentos-eventos";

    /// <summary>Eventos publicados pelo serviço de Cozinha (PedidoPronto).</summary>
    public const string Cozinha = "cozinha-eventos";
}

/// <summary>Tipo do evento. Vai no campo "type" de cada entrada do stream.</summary>
public static class EventTypes
{
    public const string PedidoCriado = "PedidoCriado";
    public const string PedidoLiberado = "PedidoLiberado";
    public const string PagamentoProcessado = "PagamentoProcessado";
    public const string PedidoPronto = "PedidoPronto";
}

// ---- Contratos dos eventos (vão serializados em JSON no campo "payload") ----

public record PedidoCriadoEvent(Guid PedidoId, string Cliente, decimal Valor);

public record PedidoLiberadoEvent(Guid PedidoId);

public record PagamentoProcessadoEvent(Guid PedidoId, bool Aprovado, string Motivo);

public record PedidoProntoEvent(Guid PedidoId);
