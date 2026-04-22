# Architecture For Agents

Este documento existe para orientar agentes futuros que precisem alterar, validar ou estender esta plataforma. O foco aqui nao e marketing nem onboarding humano basico. O objetivo e reduzir ambiguidades operacionais durante manutencao.

## Objetivo do sistema

A aplicacao implementa uma plataforma de testes para fluxos de ecommerce com:

- microsservicos em `.NET 10`
- persistencia isolada em `PostgreSQL`
- autenticacao e autorizacao com `Keycloak`
- frontends React segregados por perfil
- bootstrap local completo via `docker compose`

O sistema foi desenhado para validacoes funcionais de pedidos, estoque, catalogo, pagamentos e acesso por perfil.

## Topologia

Servicos backend:

- `catalog-api`: manutencao e consulta de produtos.
- `inventory-api`: saldo, reserva e liberacao de estoque.
- `orders-api`: orquestracao do fluxo de pedido.
- `payments-api`: autorizacao deterministicamente reproduzivel para testes.

Servicos de apoio:

- `postgres`: banco compartilhando a mesma instancia, mas com databases separados por servico.
- `keycloak`: identidade, autenticacao e roles.

Frontends:

- `customer-web`: portal de compra e criacao de pedidos.
- `operations-web`: portal operacional para catalogo, estoque e pedidos.
- `admin-web`: portal administrativo e financeiro.

## Estrutura de codigo

Raizes principais:

- `src/services`: APIs .NET.
- `src/building-blocks/SharedKernel`: infraestrutura comum das APIs.
- `src/frontends`: app React parametrizado por portal.
- `deploy/keycloak`: realm exportado do Keycloak.
- `deploy/postgres`: inicializacao dos databases.
- `deploy/nginx`: configuracao de runtime dos frontends.
- `docker-compose.yml`: topologia local completa.

Arquivos centrais para agentes:

- `src/building-blocks/SharedKernel/ServiceCollectionExtensions.cs`: ProblemDetails, health checks, options validadas e endpoints padrao.
- `src/building-blocks/SharedKernel/PlatformSecurity.cs`: autenticacao JWT, mapeamento de roles do Keycloak e policies.
- `src/building-blocks/SharedKernel/OpenApiEndpointExtensions.cs`: exposicao da UI de Swagger.
- `src/services/*/Program.cs`: composicao de cada API e mapeamento de endpoints.
- `src/services/Orders.Api/Features.cs`: contratos, clientes HTTP tipados e forwarding do bearer token.
- `src/frontends/src/portal/config.ts`: configuracao de portais, roles permitidas e endpoints consumidos.
- `src/frontends/src/portal/App.tsx`: comportamento funcional de cada portal.
- `deploy/keycloak/realm-export.json`: clients, usuarios e roles padrao.

## Boundaries e ownership

Cada servico possui seu proprio banco e schema logico.

- `catalog-api`: database `catalogdb`, schema `catalog`
- `inventory-api`: database `inventorydb`, schema `inventory`
- `orders-api`: database `ordersdb`, schema `ordering`
- `payments-api`: database `paymentsdb`, schema `payments`
- `keycloak`: database `keycloakdb`

Regra operacional importante:

- nao mover entidades entre servicos sem rever contrato HTTP, dados de seed, frontends e autorizacao.
- nao acoplar um servico diretamente ao banco de outro servico.
- qualquer integracao cross-service deve continuar via HTTP tipado.

## Fluxo funcional principal

Fluxo de pedido autorizado:

1. `customer-web` autentica no Keycloak e recebe access token.
2. frontend chama `orders-api POST /api/orders` com bearer token.
3. `orders-api` consulta `catalog-api` para validar SKU e moeda.
4. `orders-api` reserva estoque em `inventory-api`.
5. `orders-api` solicita autorizacao em `payments-api`.
6. se pagamento aprovar, pedido termina como `authorized`.
7. se pagamento falhar ou validacao quebrar, o pedido e marcado com falha e o estoque reservado e liberado.

Ponto critico:

- o `orders-api` propaga o bearer token recebido para os servicos downstream. Se um agent alterar autenticacao entre servicos, precisa preservar esse comportamento ou substitui-lo por um mecanismo consistente de service-to-service auth.

## Autenticacao e autorizacao

Fonte de verdade:

- `deploy/keycloak/realm-export.json`
- `src/building-blocks/SharedKernel/PlatformSecurity.cs`

Realm:

- `ecommerce-platform`

Roles:

- `customer`
- `catalog-manager`
- `inventory-manager`
- `order-manager`
- `finance-analyst`
- `platform-admin`

Usuarios padrao:

- `customer.demo` / `Customer#123`
- `catalog.manager` / `Catalog#123`
- `inventory.manager` / `Inventory#123`
- `order.manager` / `Orders#123`
- `finance.analyst` / `Finance#123`
- `platform.admin` / `Admin#123`

Clients SPA:

- `customer-web`
- `operations-web`
- `admin-web`

Observacoes operacionais:

- os tokens do Keycloak carregam roles em `realm_access.roles`.
- `PlatformSecurity.cs` converte essas roles em `ClaimTypes.Role`.
- as APIs validam `issuer` e assinatura do token.
- `audience` nao e validada.
- os containers resolvem metadados OIDC via `host.docker.internal` para chegar ao Keycloak exposto em `localhost:8090`.

## Matriz de acesso

Catalogo:

- `GET /api/products`: qualquer usuario autenticado.
- `GET /api/products/{sku}`: qualquer usuario autenticado.
- `POST /api/products`: `catalog-manager` ou `platform-admin`.
- `PUT /api/products/{sku}/price`: `catalog-manager` ou `platform-admin`.

Estoque:

- `GET /api/stocks`: `catalog-manager`, `inventory-manager`, `order-manager`, `platform-admin`.
- `GET /api/stocks/{sku}`: mesmas roles acima.
- `POST /api/stocks/seed`: `inventory-manager` ou `platform-admin`.
- `POST /api/stocks/reservations`: `customer`, `order-manager`, `platform-admin`.
- `POST /api/stocks/releases`: `customer`, `order-manager`, `platform-admin`.

Pedidos:

- `GET /api/orders`: `catalog-manager`, `inventory-manager`, `order-manager`, `finance-analyst`, `platform-admin`.
- `GET /api/orders/{id}`: mesmas roles acima.
- `POST /api/orders`: `customer`, `order-manager`, `platform-admin`.

Pagamentos:

- `GET /api/payments`: `finance-analyst` ou `platform-admin`.
- `GET /api/payments/{orderId}`: `finance-analyst` ou `platform-admin`.
- `POST /api/payments/authorize`: `customer`, `order-manager`, `platform-admin`.

## Frontends por perfil

O frontend e uma unica base React, parametrizada por `VITE_PORTAL_KIND`.

Perfis:

- `customer`: usa `customer-web`
- `operations`: usa `operations-web`
- `admin`: usa `admin-web`

Mapeamento de responsabilidades:

- `customer-web`: lista catalogo e cria pedidos.
- `operations-web`: cria produto, semeia estoque, lista estoque e acompanha pedidos.
- `admin-web`: lista pedidos e pagamentos, com foco em visibilidade financeira.

Fonte de configuracao:

- `src/frontends/src/portal/config.ts`

Regra operacional:

- se um agent alterar roles no Keycloak, precisa atualizar tambem `allowedRoles` no frontend.

## Configuracao de ambiente

As APIs leem:

- `ConnectionStrings__Postgres`
- `Cors__AllowedOrigins`
- `Authentication__Issuer`
- `Authentication__MetadataAddress`
- `Authentication__RequireHttpsMetadata`

Configuracoes adicionais:

- `orders-api`: `DownstreamServices__CatalogBaseUrl`, `DownstreamServices__InventoryBaseUrl`, `DownstreamServices__PaymentsBaseUrl`
- `payments-api`: `PaymentGateway__AutoApproveLimit`, `PaymentGateway__BlockedTokens`

Defaults locais estao nos `appsettings.json` de cada servico. O `docker-compose.yml` e a fonte de verdade para o ambiente integrado local.

## Bootstrap local

Subida completa:

```bash
docker compose up --build -d
```

Portas:

- `3001`: customer web
- `3002`: operations web
- `3003`: admin web
- `8081`: catalog-api
- `8082`: inventory-api
- `8083`: orders-api
- `8084`: payments-api
- `8090`: keycloak

Swagger:

- `http://localhost:8081/swagger`
- `http://localhost:8082/swagger`
- `http://localhost:8083/swagger`
- `http://localhost:8084/swagger`

## Checklist de validacao para agents

Quando alterar backend:

1. rodar `dotnet restore Ecommerce.TestPlatform.slnx`
2. rodar `dotnet build Ecommerce.TestPlatform.slnx -c Release --no-restore`
3. se a alteracao for integrada, rodar `docker compose up --build -d`
4. validar o endpoint alterado no Swagger ou via `curl`
5. se houver impacto de auth, validar pelo menos um caso `401` ou `403` e um caso `200` ou `201`

Quando alterar Keycloak ou roles:

1. revisar `deploy/keycloak/realm-export.json`
2. revisar `PlatformSecurity.cs`
3. revisar `src/frontends/src/portal/config.ts`
4. validar login e permissao por perfil

Quando alterar fluxos de pedido:

1. criar um pedido aprovado
2. criar um pedido recusado
3. confirmar efeito esperado em estoque
4. confirmar visibilidade correta em `admin-web`

## Convenios e restricoes importantes

- os binarios e artefatos gerados em `bin`, `obj`, `dist` e `node_modules` nao sao fonte de verdade.
- o frontend consome tokens reais do Keycloak; nao remover o bearer das chamadas sem rever a seguranca das APIs.
- a importacao do realm do Keycloak ocorre no bootstrap, mas um banco persistido pode reter estado anterior. Se comportamento de realm divergir do arquivo exportado, provavelmente o estado ja foi persistido.
- o `System.Security.Cryptography.Xml` gera warnings `NU1903` conhecidos no build. Hoje o build passa, mas o risco de dependencia continua em aberto.
- os servicos usam `EnsureCreatedAsync`; isso e suficiente para o ambiente de teste local, mas nao equivale a uma estrategia de migracoes versionadas.

## Onde editar por tipo de mudanca

Novo endpoint em uma API:

- editar `src/services/<Service>.Api/Program.cs`
- se houver novos DTOs ou entidades, editar `Features.cs` correspondente
- se houver nova policy, editar `PlatformSecurity.cs`

Nova role ou nova regra de acesso:

- editar `deploy/keycloak/realm-export.json`
- editar `PlatformSecurity.cs`
- editar `src/frontends/src/portal/config.ts`
- revisar `App.tsx` se a role impactar UX

Novo portal frontend:

- editar `src/frontends/src/portal/types.ts`
- editar `src/frontends/src/portal/config.ts`
- editar `src/frontends/src/portal/App.tsx`
- estender `docker-compose.yml` se o portal precisar de container proprio

Novo microsservico:

- criar projeto em `src/services`
- adicionar Dockerfile
- adicionar database de bootstrap em `deploy/postgres/init-multiple-dbs.sh`
- incluir no `docker-compose.yml`
- definir auth, CORS, health checks e Swagger no padrao existente

## Fonte de verdade final

Em caso de conflito entre documentacao e implementacao:

1. o codigo em `src/` prevalece
2. `docker-compose.yml` prevalece para topologia local
3. `deploy/keycloak/realm-export.json` prevalece para identidade pretendida
4. este documento deve ser atualizado junto com qualquer mudanca arquitetural relevante
