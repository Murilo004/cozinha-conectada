# Registro de testes — IC (Microsserviços com Redis Streams)

Data dos testes: 04/11/2026
Ambiente: Windows + Docker Desktop 4.98.0 (Docker 29.9.2, Compose v5.6.1), WSL 2
Stack: .NET 10, EF Core, SQL Server 2022, Redis 7

Legenda: ✅ executado e confirmado | ⏳ ainda não executado

---

## 1. Subida do ambiente ✅

Comando (na pasta que contém o `docker-compose.yml`):

```
docker compose up --build
```

Resultado observado nos logs:

- Bancos criados: `PedidosDbContext`, `PagamentosDbContext` (e Cozinha) — mensagem `Banco ... pronto.`
- Consumer groups criados automaticamente nos streams:

| Serviço   | Grupo              | Stream              |
|-----------|--------------------|---------------------|
| Pedido    | `pedidos-group`    | `pagamentos-eventos`|
| Pedido    | `pedidos-group`    | `cozinha-eventos`   |
| Pagamento | `pagamentos-group` | `pedidos-eventos`   |
| Cozinha   | `cozinha-group`    | `pedidos-eventos`   |

- Os três serviços iniciaram (`Now listening on: http://[::]:8080`, expostos em 5001, 5002 e 5003).

---

## 2. Pedido aprovado (valor <= R$ 500) ✅

Comando (Prompt de Comando do Windows):

```
curl -X POST http://localhost:5001/pedidos -H "Content-Type: application/json" -d "{\"cliente\":\"Maria\",\"descricao\":\"2x Hamburguer\",\"valor\":89.90}"
```

Resposta imediata:

```json
{"id":"19444026-1a70-46e0-a3a7-4bf60ecdc53f","cliente":"Maria","descricao":"2x Hamburguer","valor":89.90,"status":"Aguardando","criadoEm":"2026-11-05T00:43:28.6675309Z","atualizadoEm":"2026-11-05T00:43:28.6675309Z"}
```

Consulta alguns segundos depois (`curl http://localhost:5001/pedidos`):

```json
[{"id":"19444026-1a70-46e0-a3a7-4bf60ecdc53f","cliente":"Maria","descricao":"2x Hamburguer","valor":89.90,"status":"Pronto","criadoEm":"2026-11-05T00:43:28.6675309","atualizadoEm":"2026-11-05T00:43:36.1044547"}]
```

Conclusão: o pedido passou de `Aguardando` para `Pronto` em cerca de 8 segundos
(polling do consumidor + 5 s de preparo simulado na cozinha), percorrendo o fluxo
Pedido → Pagamento → Pedido → Cozinha → Pedido apenas por eventos nos Redis Streams.

Sequência de eventos:

1. `PedidoCriado` publicado em `pedidos-eventos` (Pedido)
2. `PagamentoProcessado` (aprovado) publicado em `pagamentos-eventos` (Pagamento)
3. `PedidoLiberado` publicado em `pedidos-eventos` (Pedido)
4. `PedidoPronto` publicado em `cozinha-eventos` (Cozinha)
5. Status do pedido atualizado para `Pronto` (Pedido)

---

## 3. Pedido recusado (valor > R$ 500) ✅

Comando:

```
curl -X POST http://localhost:5001/pedidos -H "Content-Type: application/json" -d "{\"cliente\":\"Joao\",\"descricao\":\"Banquete\",\"valor\":800}"
```

Resposta imediata:

```json
{"id":"197ac390-664c-4ed1-91fa-cac4db8964d0","cliente":"Joao","descricao":"Banquete","valor":800,"status":"Aguardando","criadoEm":"2026-11-05T00:44:18.966569Z","atualizadoEm":"2026-11-05T00:44:18.966569Z"}
```

Consulta logo depois (`curl http://localhost:5001/pedidos`):

```json
[{"id":"197ac390-664c-4ed1-91fa-cac4db8964d0","cliente":"Joao","descricao":"Banquete","valor":800.00,"status":"Recusado","criadoEm":"2026-11-05T00:44:18.966569","atualizadoEm":"2026-11-05T00:44:19.6416217"},{"id":"19444026-1a70-46e0-a3a7-4bf60ecdc53f","cliente":"Maria","descricao":"2x Hamburguer","valor":89.90,"status":"Pronto","criadoEm":"2026-11-05T00:43:28.6675309","atualizadoEm":"2026-11-05T00:43:36.1044547"}]
```

Conclusão: o pagamento recusou o pedido (valor acima do limite de R$ 500) e o serviço de Pedido
atualizou o status para `Recusado` em menos de 1 segundo. Nenhum `PedidoLiberado` foi publicado para
esse pedido, então ele não chegou à cozinha (ver `XRANGE` no teste 4: só existe o `PedidoCriado` do João).

---

## 4. Inspeção do Redis ✅

### 4.1 Quantidade de mensagens

```
docker compose exec redis redis-cli XLEN pedidos-eventos
(integer) 3
```

São 3 mensagens: `PedidoCriado` e `PedidoLiberado` da Maria, e `PedidoCriado` do João
(o pedido do João foi recusado, por isso não há `PedidoLiberado` dele).

### 4.2 Conteúdo do stream

```
docker compose exec redis redis-cli XRANGE pedidos-eventos - +
1) 1) "1793839408922-0"
   2) 1) "type"
      2) "PedidoCriado"
      3) "payload"
      4) "{\"PedidoId\":\"19444026-1a70-46e0-a3a7-4bf60ecdc53f\",\"Cliente\":\"Maria\",\"Valor\":89.90}"
2) 1) "1793839410036-0"
   2) 1) "type"
      2) "PedidoLiberado"
      3) "payload"
      4) "{\"PedidoId\":\"19444026-1a70-46e0-a3a7-4bf60ecdc53f\"}"
3) 1) "1793839458993-0"
   2) 1) "type"
      2) "PedidoCriado"
      3) "payload"
      4) "{\"PedidoId\":\"197ac390-664c-4ed1-91fa-cac4db8964d0\",\"Cliente\":\"Joao\",\"Valor\":800}"
```

Cada entrada tem um id gerado pelo Redis (timestamp em ms + sequência), o campo `type` e o `payload` em JSON.
O `PedidoLiberado` da Maria foi publicado cerca de 1,1 s depois do `PedidoCriado`
(1793839410036 − 1793839408922 = 1114 ms), que é o tempo da ida e volta pelo serviço de Pagamento.

### 4.3 Consumer groups

```
docker compose exec redis redis-cli XINFO GROUPS pedidos-eventos
1)  1) "name"
    2) "cozinha-group"
    3) "consumers"
    4) (integer) 1
    5) "pending"
    6) (integer) 0
    7) "last-delivered-id"
    8) "1793839458993-0"
    9) "entries-read"
   10) (integer) 3
   11) "lag"
   12) (integer) 0
2)  1) "name"
    2) "pagamentos-group"
    3) "consumers"
    4) (integer) 1
    5) "pending"
    6) (integer) 0
    7) "last-delivered-id"
    8) "1793839458993-0"
    9) "entries-read"
   10) (integer) 3
   11) "lag"
   12) (integer) 0
```

Os dois grupos leram as 3 mensagens (`entries-read = 3`), cada um com seu próprio ponteiro,
sem atraso (`lag = 0`) e sem pendências (`pending = 0`). Isso mostra que o mesmo stream foi
entregue por inteiro para os dois serviços (Pagamento e Cozinha), cada um ignorando os tipos
que não lhe interessam.

### 4.4 Mensagens pendentes

```
docker compose exec redis redis-cli XPENDING pedidos-eventos cozinha-group
1) (integer) 0
2) (nil)
3) (nil)
4) (nil)
```

Nenhuma mensagem pendente: tudo o que foi lido foi confirmado com `XACK`.

---

## 5. Resiliência: cozinha parada ✅

Objetivo: mostrar que o evento espera no stream enquanto o consumidor está fora.

**Passo 1: parar a cozinha**

```
docker compose stop cozinha
[+] stop 1/1
 ✔ Container ic-cozinha-1 Stopped
```

**Passo 2: criar um pedido com a cozinha fora do ar**

```
curl -X POST http://localhost:5001/pedidos -H "Content-Type: application/json" -d "{\"cliente\":\"Ana\",\"descricao\":\"Pizza\",\"valor\":50}"
```

```json
{"id":"56fc6401-36d0-4d9f-8762-c83ca044658c","cliente":"Ana","descricao":"Pizza","valor":50,"status":"Aguardando","criadoEm":"2026-11-05T01:15:56.8137742Z","atualizadoEm":"2026-11-05T01:15:56.8137744Z"}
```

**Passo 3: consultar com a cozinha parada**

```
curl http://localhost:5001/pedidos
```

O pedido da Ana ficou `Aprovado` (pagamento processado em ~0,7 s) e não avançou:

```json
{"id":"56fc6401-36d0-4d9f-8762-c83ca044658c","cliente":"Ana","descricao":"Pizza","valor":50.00,"status":"Aprovado","criadoEm":"2026-11-05T01:15:56.8137742","atualizadoEm":"2026-11-05T01:15:57.5303654"}
```

**Passo 4: subir a cozinha de novo**

```
docker compose start cozinha
[+] start 3/3
 ✔ Container ic-redis-1     Healthy
 ✔ Container ic-sqlserver-1 Healthy
 ✔ Container ic-cozinha-1   Started
```

**Passo 5: consultar depois que a cozinha voltou**

```
curl http://localhost:5001/pedidos
```

```json
{"id":"56fc6401-36d0-4d9f-8762-c83ca044658c","cliente":"Ana","descricao":"Pizza","valor":50.00,"status":"Pronto","criadoEm":"2026-11-05T01:15:56.8137742","atualizadoEm":"2026-11-05T01:17:21.9225681"}
```

Conclusão: com a cozinha parada, o `PedidoLiberado` ficou guardado no stream `pedidos-eventos`.
Quando a cozinha subiu, o consumer group `cozinha-group` retomou de onde parou, leu o evento,
preparou o pedido e publicou `PedidoPronto`. O pedido passou de `Aprovado` para `Pronto` (01:17:21)
sem nenhuma mensagem perdida e sem o serviço de Pedido precisar reenviar nada. É a principal
vantagem da comunicação assíncrona com Redis Streams: os serviços ficam desacoplados no tempo.

### 5.1 Repetição medindo o lag ✅

Na primeira execução não foi registrado o `XINFO GROUPS` durante a parada. O teste foi repetido
com um pedido novo (Bia, R$ 30), consultando os grupos antes e depois de subir a cozinha.

**Cozinha parada** (`docker compose stop cozinha`), pedido da Bia criado às 01:24:18 e `Aprovado` em ~0,8 s:

```
docker compose exec redis redis-cli XINFO GROUPS pedidos-eventos
```

| Grupo              | entries-read | lag | pending | last-delivered-id |
|--------------------|--------------|-----|---------|-------------------|
| `cozinha-group`    | 5            | **2** | 0     | 1793841357537-0   |
| `pagamentos-group` | 7            | 0   | 0       | 1793841858799-0   |

O `cozinha-group` ficou 2 mensagens atrás. O `lag` conta todas as mensagens do stream que o grupo
ainda não leu, e não só as do tipo que interessa a ele. O pedido da Bia gerou duas mensagens em
`pedidos-eventos` (`PedidoCriado` e `PedidoLiberado`), por isso o lag é 2. O `pagamentos-group`,
que continuou rodando, leu tudo (7 mensagens, lag 0).

**Cozinha de volta** (`docker compose start cozinha`):

| Grupo              | entries-read | lag | pending | last-delivered-id |
|--------------------|--------------|-----|---------|-------------------|
| `cozinha-group`    | 7            | **0** | 0     | 1793841858799-0   |
| `pagamentos-group` | 7            | 0   | 0       | 1793841858799-0   |

O `cozinha-group` alcançou o `pagamentos-group`: leu as 2 mensagens que estavam esperando,
voltou a `lag = 0` e ficou sem pendências.

**Status final do pedido da Bia:**

```json
{"id":"c2bf71ba-0103-4597-9ba8-b698795b950c","cliente":"Bia","descricao":"Salada","valor":30.00,"status":"Pronto","criadoEm":"2026-11-05T01:24:18.0274794","atualizadoEm":"2026-11-05T01:26:32.413358"}
```

O pedido ficou `Aprovado` às 01:24:18 e só virou `Pronto` às 01:26:32, depois que a cozinha voltou.
Nenhum evento foi perdido enquanto o consumidor esteve fora do ar.

---

## Como guardar as evidências

- Logs de todos os serviços em arquivo: `docker compose logs --no-color > docs/logs.txt`
- Logs de um serviço só: `docker compose logs pagamentos > docs/logs-pagamentos.txt`
- Prints: `Win + Shift + S`, salvando em `docs/img/` e referenciando aqui com `![descrição](img/arquivo.png)`.
- Saída de um comando em arquivo (Prompt de Comando): `curl http://localhost:5001/pedidos > docs/pedidos.json`
