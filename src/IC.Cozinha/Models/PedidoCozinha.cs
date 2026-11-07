namespace IC.Cozinha.Models;

public enum StatusCozinha
{
    Preparando = 1,
    Pronto = 2
}

public class PedidoCozinha
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PedidoId { get; set; }
    public StatusCozinha Status { get; set; } = StatusCozinha.Preparando;
    public DateTime RecebidoEm { get; set; } = DateTime.UtcNow;
    public DateTime? FinalizadoEm { get; set; }

    public void Finalizar()
    {
        Status = StatusCozinha.Pronto;
        FinalizadoEm = DateTime.UtcNow;
    }
}
