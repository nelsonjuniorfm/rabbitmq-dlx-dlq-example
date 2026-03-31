using RabbitMQ.Client;
using RMQ.Model;
using System.Text.Json;

const string exchangeName = "pedido.exchange";
const string queueName = "pedido.criados";
const string routingKey = "pedido.criado";

var factory = new ConnectionFactory()
{
    HostName = "localhost",
    Port = 5672,
    UserName = "guest",
    Password = "guest",
    VirtualHost = "/",
    NetworkRecoveryInterval = TimeSpan.FromSeconds(10)
};

await using var connection = await factory.CreateConnectionAsync();
await using var chanel = await connection.CreateChannelAsync();

await chanel.ExchangeDeclareAsync(
    exchange: exchangeName,
    type: ExchangeType.Direct,
    durable: true,
    autoDelete: false
);

await chanel.QueueDeclareAsync(
    queue: queueName,
    durable: true,
    exclusive: false,
    autoDelete: false
);

await chanel.QueueBindAsync(
    queue: queueName,
    exchange: exchangeName,
    routingKey: routingKey
);

static Pedido CriarPedidoFake(int index)
{
    return new Pedido()
    {
        Id = Guid.NewGuid(),
        ClienteEmail = $"cliente_{index}@email.com",
        ValorTotal = Random.Shared.Next(100, 5000),
        DataCriacao = DateTime.UtcNow,
        Itens =
        [
            new Item
            {
                NomeProduto = $"Produto {index}",
                Quantidade = 2,
                PrecoUnitario = Random.Shared.Next(20, 1000),
            }
        ]
    };
}

Console.WriteLine("App Produtor de Pedidos está rodando...");
Console.WriteLine();
Console.WriteLine("Quantos pedidos quer enviar ?");

if (!int.TryParse(Console.ReadLine(), out var quantidadeDePedidos))
{
    quantidadeDePedidos = 3;
}

for (int i = 0; i <= quantidadeDePedidos; i++)
{
    var pedido = CriarPedidoFake(i);
    var body = JsonSerializer.SerializeToUtf8Bytes(pedido);
    var properties = new BasicProperties
    {
        Persistent = true,
        ContentType = "application/json",
        ContentEncoding = "utf-8"
    };

    await chanel.BasicPublishAsync(
        exchange: exchangeName,
        routingKey: routingKey,
        mandatory: false,
        basicProperties: properties,
        body: body
    );

    Console.WriteLine($"[x] Pedido {pedido.Id} enviado.");

    Console.WriteLine("Pressione ENTER para enviar o proximo pedido...");
    Console.ReadLine();
}