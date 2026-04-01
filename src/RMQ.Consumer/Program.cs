using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RMQ.Model;
using System.Text;
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

//quality of serice
await chanel.BasicQosAsync(
    prefetchSize: 0, // limite por tamanho em bytes. 0 == sem limite
    prefetchCount: 1, // recebe apenas uma mensagem por vez
    global: false // limite aplicado ao canal e não a toda conexão
);

var consumer = new AsyncEventingBasicConsumer(chanel);

consumer.ReceivedAsync += async (model, ea) =>
{
    try
    {

        var body = ea.Body.ToArray();
        var json = Encoding.UTF8.GetString(body);
        var pedido = JsonSerializer.Deserialize<Pedido>(json);

        Console.WriteLine("");
        Console.WriteLine("-------------------------------------------------------------");
        Console.WriteLine("---------------- NOVA MENSAGEM RECEBIDA ---------------------");
        Console.WriteLine("-------------------------------------------------------------");
        Console.WriteLine($"[Consumer] Pedido recebido:......: {pedido?.Id}");
        Console.WriteLine($"Cliente..........................: {pedido?.ClienteEmail}");
        Console.WriteLine($"Valor............................: {pedido?.ValorTotal:C}");
        Console.WriteLine($"Criando em.......................: {pedido?.DataCriacao:O}");
        Console.WriteLine("-------------------------------------------------------------");
        Console.WriteLine("");

        if (pedido is null)
        {
            throw new Exception("Pedido está nulo");
        }

        if(pedido.ValorTotal < 0)
        {
            Console.WriteLine($"[Consumer] Pedido com valor inválido: {pedido.ValorTotal}");
            Console.WriteLine($"[Consumer] Enviando para DLX: {dlxExchangeName} com routing key: {dlxRoutingKey}");
            
            await chanel.BasicNackAsync(
                deliveryTag: ea.DeliveryTag, 
                multiple: false, 
                requeue: false); // - Faz ir para DLQ
            
            return;
        }

        if (string.IsNullOrWhiteSpace(pedido.ClienteEmail))
        {
            Console.WriteLine($"[Consumer] Pedido com email inválido: {pedido.ClienteEmail}");
            Console.WriteLine($"[Consumer] Enviando para DLX: {dlxExchangeName} com routing key: {dlxRoutingKey}");
            
            await chanel.BasicNackAsync(
                deliveryTag: ea.DeliveryTag, 
                multiple: false, 
                requeue: false);
            
            return;
        }


        await Task.Delay(2000);

        // confirma o processamento da mensagem
        await chanel.BasicAckAsync(deliveryTag: ea.DeliveryTag, multiple: false);
    }
    catch (JsonException jex)
    {
        Console.WriteLine($"[Consumer] Erro ao deserializar o pedido {jex.Message}");
        await chanel.BasicNackAsync(deliveryTag: ea.DeliveryTag, multiple: false, requeue: false);
        throw;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[Consumer] Erro ao processar o pedido {ex.Message}");
        await chanel.BasicNackAsync(deliveryTag: ea.DeliveryTag, multiple: false, requeue: true);
        throw;
    }
};

await chanel.BasicConsumeAsync(
        queue: queueName,
        autoAck: false,
        consumer: consumer
    );

Console.WriteLine("Consumer iniciado. Aguardando mensagens... Precione ENTER para sair");
Console.WriteLine("");
Console.ReadLine();