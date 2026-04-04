# RabbitMQ DLX/DLQ Example (Produtor + Consumidor)

Uma solução .NET com dois projetos principais:

- `RMQ.Producer`: produz mensagens `Pedido` no exchange `pedido.exchange` e rota para `pedido.criados`.
- `RMQ.Consumer`: consome mensagens de `pedido.criados`, valida e confirma via `Ack`, implementa mecanismo de retry para erros temporários, ou envia para a DLX (Dead Letter Exchange) com `Nack` após tentativas esgotadas.

> Requer um broker RabbitMQ rodando em container Docker (ou local `localhost:5672`).

## 🔧 Arquitetura

- Exchange principal: `pedido.exchange` (type: `direct`)
- Queue principal: `pedido.criados` (durável, com argumentos de DLX)
- DLX: `pedido.dlx` (type: `direct`)
- DLQ: `pedido.dlq`
- Exchange de retry: `pedido.retry.exchange` (type: `direct`)
- Queue de retry: `pedido.retry` (com TTL de 5s e DLX de volta para a fila principal)
- Routing Keys:
  - principal: `pedido.criado`
  - dlx: `pedido.nao.entregue`
  - retry: `pedido.retry`

## 🔄 Mecanismo de Retry

O consumidor implementa um padrão de retry para lidar com erros temporários:

- **Tentativas**: Máximo de 3 tentativas por mensagem.
- **Delay**: 5 segundos entre tentativas.
- **Funcionamento**: Mensagens com erro são publicadas na fila de retry com TTL. Após expirar, retornam automaticamente para a fila principal via DLX.
- **Cabeçalhos**: Utiliza `x-retry-count` para rastrear tentativas.
- **Fallback**: Após esgotar tentativas, mensagens vão para DLX/DLQ.

## 🧩 Modelos

- `RMQ.Model.Pedido`
  - `Id`: Guid
  - `ClienteEmail`: string
  - `ValorTotal`: decimal
  - `DataCriacao`: DateTime
  - `Itens`: List<Item>

- `RMQ.Model.Item`
  - `NomeProduto`: string
  - `Quantidade`: int
  - `PrecoUnitario`: decimal

## 🐇 Requisitos

- .NET 9 SDK
- Docker
- RabbitMQ (via container preferido)

## 🚀 Instruções de execução

### 1) Iniciar RabbitMQ com Docker

```bash
docker run -d --hostname rabbitmq-host --name rabbitmq \
  -p 5672:5672 -p 15672:15672 \
  -e RABBITMQ_DEFAULT_USER=guest \
  -e RABBITMQ_DEFAULT_PASS=guest \
  rabbitmq:3-management
```

- Console de gerenciamento: `http://localhost:15672`
- Credenciais: `guest` / `guest`

### 2) Build

```bash
cd /home/nelson-fernandes/learning/exampleRabbitMq
dotnet build
```

### 3) Executar o consumidor

No terminal 1:

```bash
cd src/RMQ.Consumer
dotnet run
```

### 4) Executar o produtor

No terminal 2:

```bash
cd src/RMQ.Producer
dotnet run
```

- Informe a quantidade de pedidos que deseja enviar.
- O produtor envia mensagens alternando entre pedidos válidos e inválidos (`ValorTotal` negativo).

## 🧠 Comportamento esperado

- Pedidos válidos: processados e `ACK` no consumidor.
- Pedidos inválidos (valor negativo ou email vazio): `NACK` com `requeue = false` -> vão para DLX/DLQ.
- Exceções de desserialização: `NACK` para DLQ.
- Erros de processamento temporários: implementa retry até 3 tentativas com delay de 5 segundos. Após esgotar tentativas, envia para DLX/DLQ.
- Simulação de falha: o consumidor permite simular erros temporários para testar o mecanismo de retry.

## 🔍 Verificação

No RabbitMQ Management UI:
- Exchange `pedido.dlx`
- Queue `pedido.dlq`: mensagens não processadas após retry.
- Exchange `pedido.retry.exchange`
- Queue `pedido.retry`: mensagens aguardando retry (com TTL).

## 🧪 Teste manual rápido

1. Abrir consumer (escolha simular falha para testar retry)
2. Abrir producer e enviar 5 mensagens
3. Verificar no Management UI as filas de retry e DLQ
3. Verificar logs:
   - `Pedido com valor inválido` -> NACK para DLQ
   - Processamento normal -> ACK
4. Verificar no UI do RabbitMQ se `pedido.dlq` cresceu

## 🧾 Código relevante

- `src/RMQ.Producer/Program.cs`
- `src/RMQ.Consumer/Program.cs`
- `src/RMQ.Model/Pedido.cs`
- `src/RMQ.Model/Item.cs`

---

## 💡 Dicas

- Se o RabbitMQ estiver em outra máquina, ajuste `ConnectionFactory.HostName`.
- Para usar um `virtual host` customizado, atualize nos dois projetos.
- Para produção, prefira usar variáveis de ambiente ou `IConfiguration` para credenciais.
