# RabbitMQ DLX/DLQ Example (Produtor + Consumidor)

Uma solução .NET com dois projetos principais:

- `RMQ.Producer`: produz mensagens `Pedido` no exchange `pedido.exchange` e rota para `pedido.criados`.
- `RMQ.Consumer`: consome mensagens de `pedido.criados`, valida e confirma via `Ack` ou envia para a DLX (Dead Letter Exchange) com `Nack`.

> Requer um broker RabbitMQ rodando em container Docker (ou local `localhost:5672`).

## 🔧 Arquitetura

- Exchange principal: `pedido.exchange` (type: `direct`)
- Queue principal: `pedido.criados` (durável, com argumentos de DLX)
- DLX: `pedido.dlx` (type: `direct`)
- DLQ: `pedido.dlq`
- Routing Keys:
  - principal: `pedido.criado`
  - dlx: `pedido.nao.entregue`

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
- Erros de processamento inesperados: `NACK` com `requeue = true` (tenta reprocessar no mesmo queue).

## 🔍 Verificação da DLQ

No RabbitMQ Management UI:
- Exchange `pedido.dlx`
- Queue `pedido.dlq`
- Mensagens não processadas do consumidor devem aparecer em `pedido.dlq`

## 🧪 Teste manual rápido

1. Abrir consumer
2. Abrir producer e enviar 5 mensagens
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
