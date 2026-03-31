namespace RMQ.Model;

public class Pedido
{
    public required Guid Id { get; set; }
    public required string ClienteEmail { get; set; }
    public required decimal ValorTotal { get; set; }
    public required DateTime DataCriacao { get; set; }
    public required List<Item> Itens { get; set; }
}
