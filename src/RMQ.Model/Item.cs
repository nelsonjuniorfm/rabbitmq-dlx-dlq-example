namespace RMQ.Model;

public class Item
{
    public required string NomeProduto { get; set; }
    public required int Quantidade { get; set; }
    public required decimal PrecoUnitario { get; set; }
}
