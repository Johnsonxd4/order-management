# Ecommerce Test Platform

Ecossistema de microsservicos em .NET 10 para validacoes de ecommerce com bancos isolados em PostgreSQL e `docker-compose` para subir tudo localmente.

## Servicos

- `catalog-api`: cadastro e consulta de produtos.
- `inventory-api`: estoque, reserva e liberacao.
- `orders-api`: validacao ponta a ponta do pedido e orquestracao entre catalogo, estoque e pagamento.
- `payments-api`: autorizacao de pagamento deterministicamente reproduzivel para testes.
- `keycloak`: identidade, autenticacao e roles por perfil.
- `customer-web`: frontend React para cliente.
- `operations-web`: frontend React para operacao.
- `admin-web`: frontend React para administracao e financeiro.

## Documentacao para agents

- arquitetura operacional: [docs/ARCHITECTURE_FOR_AGENTS.md](./docs/ARCHITECTURE_FOR_AGENTS.md)

## Praticas aplicadas

- isolamento de dados por servico, mantendo um database dedicado por bounded context.
- `ProblemDetails`, `health checks`, options fortemente tipadas com `ValidateOnStart`.
- `TimeProvider` para previsibilidade e melhor testabilidade.
- `DbContext` por servico com schema proprio e constraints relevantes.
- `HttpClient` tipado para comunicacao entre servicos.
- imagens Docker multi-stage para build e runtime.
- seeds basicos para permitir validacoes de fluxo imediatamente apos o bootstrap.

## Subida via Docker Compose

```bash
docker compose up --build
```

Portas publicas:

- `catalog-api`: `http://localhost:8081`
- `inventory-api`: `http://localhost:8082`
- `orders-api`: `http://localhost:8083`
- `payments-api`: `http://localhost:8084`
- `keycloak`: `http://localhost:8090`
- `customer-web`: `http://localhost:3001`
- `operations-web`: `http://localhost:3002`
- `admin-web`: `http://localhost:3003`

Documentacao Swagger:

- `catalog-api`: `http://localhost:8081/swagger`
- `inventory-api`: `http://localhost:8082/swagger`
- `orders-api`: `http://localhost:8083/swagger`
- `payments-api`: `http://localhost:8084/swagger`

## Perfis e usuarios padrao

- `customer`: usuario `customer.demo` senha `Customer#123`
- `catalog-manager`: usuario `catalog.manager` senha `Catalog#123`
- `inventory-manager`: usuario `inventory.manager` senha `Inventory#123`
- `order-manager`: usuario `order.manager` senha `Orders#123`
- `finance-analyst`: usuario `finance.analyst` senha `Finance#123`
- `platform-admin`: usuario `platform.admin` senha `Admin#123`

Realm importado automaticamente: `ecommerce-platform`

Clientes SPA:

- `customer-web`
- `operations-web`
- `admin-web`

## Frontends por perfil

- `customer-web`: consumo de catalogo e criacao de pedidos.
- `operations-web`: gestao de catalogo, estoque e acompanhamento de pedidos.
- `admin-web`: visibilidade de pedidos, pagamentos e indicadores financeiros.

## Fluxo de validacao sugerido

1. Consultar catalogo:

```bash
curl http://localhost:8081/api/products
```

2. Consultar estoque:

```bash
curl http://localhost:8082/api/stocks/SKU-CHAIR-001
```

3. Criar pedido:

```bash
curl -X POST http://localhost:8083/api/orders \
  -H "Content-Type: application/json" \
  -d '{
    "CustomerId": "customer-001",
    "Currency": "BRL",
    "PaymentMethodToken": "tok_approved_1234",
    "Lines": [
      {
        "Sku": "SKU-CHAIR-001",
        "Quantity": 2
      }
    ]
  }'
```

4. Forcar negativa de pagamento usando token terminado em `0000`:

```bash
curl -X POST http://localhost:8083/api/orders \
  -H "Content-Type: application/json" \
  -d '{
    "CustomerId": "customer-002",
    "Currency": "BRL",
    "PaymentMethodToken": "tok_declined_0000",
    "Lines": [
      {
        "Sku": "SKU-DESK-001",
        "Quantity": 1
      }
    ]
  }'
```
