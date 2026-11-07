namespace IC.Pedidos.Models;

public enum StatusPedido
{
    Aguardando = 1, // criado, esperando o pagamento
    Recusado = 2,   // pagamento recusado
    Aprovado = 3,   // pagamento aprovado, liberado para a cozinha
    Pronto = 4      // cozinha finalizou
}

public class Pedido
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Cliente { get; set; } = string.Empty;
    public string Descricao { get; set; } = string.Empty;
    public decimal Valor { get; set; }
    public StatusPedido Status { get; set; } = StatusPedido.Aguardando;
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
    public DateTime AtualizadoEm { get; set; } = DateTime.UtcNow;

    public void AlterarStatus(StatusPedido novoStatus)
    {
        Status = novoStatus;
        AtualizadoEm = DateTime.UtcNow;
    }
}
