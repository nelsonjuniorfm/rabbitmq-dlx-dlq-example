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

# region retry definitions
const string retryExchangeName = "pedido.retry.exchange";
const string retryQueueName = "pedido.retry";
const string retryRoutingKey = "pedido.retry";
const int maxRetryAttempts = 3;
const int retryDelayMs = 5000;
bool simularFalha = false;
#endregion

Console.WriteLine("Deseja provocar um erro para observar o uso do retry ?");
Console.WriteLine("1 - Sim");
Console.WriteLine("2 - Não");
if (int.TryParse(Console.ReadLine(), out var resposta))
    simularFalha = resposta == 1;

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

#region DLX e DLQ

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

#endregion



# region Retry

await chanel.ExchangeDeclareAsync(
    exchange: retryExchangeName,
    type: ExchangeType.Direct,
    durable: true,
    autoDelete: false
);

var retryQueueArgs = new Dictionary<string, object?>
{
    { "x-message-ttl", retryDelayMs },
    { "x-dead-letter-exchange", exchangeName },
    { "x-dead-letter-routing-key", routingKey }
};

await chanel.QueueDeclareAsync(
    queue: retryQueueName,
    durable: true,
    exclusive: false,
    autoDelete: false,
    arguments: retryQueueArgs
);

await chanel.QueueBindAsync(
    queue: retryQueueName,
    exchange: retryExchangeName,
    routingKey: retryRoutingKey
);

#endregion


#region Exchange, Queue e Bindings - Fila principal
await chanel.ExchangeDeclareAsync(
    exchange: exchangeName,
    type: ExchangeType.Direct,
    durable: true,
    autoDelete: false
);

// argumentos para configurar a fila com dead letter exchange
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

#endregion




var consumer = new AsyncEventingBasicConsumer(chanel);

consumer.ReceivedAsync += async (model, ea) =>
{

    var body = ea.Body.ToArray();
    var json = Encoding.UTF8.GetString(body);

    int retryCount = 0;
    if (ea.BasicProperties.Headers != null && ea.BasicProperties.Headers.ContainsKey("x-retry-count"))
    {
        retryCount = Convert.ToInt32(ea.BasicProperties.Headers["x-retry-count"]);
        Console.WriteLine($"[Consumer] Mensagem recebida com retry. Tentativa {retryCount + 1} de {maxRetryAttempts}");
    }

    try
    {
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
            throw new InvalidOperationException("Pedido está nulo");
        }

        if (pedido.ValorTotal < 0)
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

        if (simularFalha)
        {
            Console.WriteLine($"[Consumer] Simulando falha temporária no processamento do pedido {pedido.Id}");
            Console.WriteLine();
            throw new Exception("Erro simulado para teste de retry");
        }


        await Task.Delay(2000);

        // confirma o processamento da mensagem
        await chanel.BasicAckAsync(deliveryTag: ea.DeliveryTag, multiple: false);
    }
    catch (JsonException jex)
    {
        Console.WriteLine($"[Consumer] Erro ao deserializar o pedido {jex.Message} - Envia para a DLQ");
        await chanel.BasicNackAsync(deliveryTag: ea.DeliveryTag, multiple: false, requeue: false);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[Consumer] Ocorreu um erro temporário ao processar o pedido. Erro: {ex.Message}");
        Console.WriteLine();
        //await chanel.BasicNackAsync(deliveryTag: ea.DeliveryTag, multiple: false, requeue: true);
        // Implementação de retry com contagem de tentativas usando header

        if (retryCount < maxRetryAttempts - 1)
        {
            var newRetryCount = retryCount + 1;
            var headers = new Dictionary<string, object?>
            {
                { "x-retry-count", newRetryCount }
            };

            var retryProperties = new BasicProperties
            {
                Persistent = true,
                Headers = headers,
                ContentType = "application/json",
                ContentEncoding = "utf-8",
            };

            await chanel.BasicPublishAsync(
                exchange: retryExchangeName,
                routingKey: retryRoutingKey,
                mandatory: false,
                basicProperties: retryProperties,
                body: ea.Body
            );
            Console.WriteLine($"[Consumer] Mensagem enviada para retry em {retryDelayMs}ms");
            Console.WriteLine();

            await chanel.BasicAckAsync(deliveryTag: ea.DeliveryTag, multiple: false);
        }
        else
        {
            Console.WriteLine($"[Consumer] Número máximo de tentativas de retry atingido.\n Enviando para DLX: {dlxExchangeName} com routing key: {dlxRoutingKey}");
            Console.WriteLine();
            await chanel.BasicNackAsync(deliveryTag: ea.DeliveryTag, multiple: false, requeue: false);
        }

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