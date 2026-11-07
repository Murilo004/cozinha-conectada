namespace IC.Pagamentos.Models;

public enum StatusPagamento
{
    Aprovado = 1,
    Recusado = 2
}

public class Pagamento
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PedidoId { get; set; }
    public decimal Valor { get; set; }
    public StatusPagamento Status { get; set; }
    public string Motivo { get; set; } = string.Empty;
    public DateTime ProcessadoEm { get; set; } = DateTime.UtcNow;
}
