using RabbitMQ.Client;
using RMQ.Model;
using System.Text.Json;

const string exchangeName = "pedido.exchange";
const string queueName = "pedido.criados";
const string routingKey = "pedido.criado";

const string dlxExchangeName = "pedido.dlx";
const string dlxQueueName = "pedido.dlq";
const string dlxRoutingKey = "pedido.nao.entregue";

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


//dlx
await chanel.ExchangeDeclareAsync(
    exchange: dlxExchangeName,
    type: ExchangeType.Direct,
    durable: true,
    autoDelete: false
);

//dlq
await chanel.QueueDeclareAsync(
    queue: dlxQueueName,
    durable: true,
    exclusive: false,
    autoDelete: false
);

//dlq queue bind
await chanel.QueueBindAsync(
    queue: dlxQueueName,
    exchange: dlxExchangeName,
    routingKey: dlxRoutingKey
);


await chanel.ExchangeDeclareAsync(
    exchange: exchangeName,
    type: ExchangeType.Direct,
    durable: true,
    autoDelete: false
);

var argsDlq = new Dictionary<string, object?>
{
    { "x-dead-letter-exchange", dlxExchangeName },
    { "x-dead-letter-routing-key", dlxRoutingKey }
};

await chanel.QueueDeclareAsync(
    queue: queueName,
    durable: true,
    exclusive: false,
    autoDelete: false,
    arguments: argsDlq
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

static Pedido CriarPedidoFakeComErro(int index)
{
    return new Pedido()
    {
        Id = Guid.NewGuid(),
        ClienteEmail = $"cliente_{index}@email.com",
        ValorTotal = -10, // Valor negativo para simular erro
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

for (int i = 0; i < quantidadeDePedidos; i++)
{
    var pedido = i % 2 == 0 ? CriarPedidoFake(i) : CriarPedidoFakeComErro(i);
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