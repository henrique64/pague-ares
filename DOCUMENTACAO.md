# PagueAres — Documentação Técnica

> Sistema de Gestão de Reembolsos e Despesas — Ares
>
> Monorepo `pague-ares` (`back-end/` .NET 10 + `front-end/` Angular 20). Documento atualizado em 23/11/2026 a partir do código da branch `main` (commit `4a0c32a`, merge de `feature/general-improvements`).

---

## Sumário

1. [Visão Geral](#1-visão-geral)
2. [Stack Tecnológica](#2-stack-tecnológica)
3. [Arquitetura do Sistema](#3-arquitetura-do-sistema)
4. [Estrutura de Pastas](#4-estrutura-de-pastas)
5. [Backend — Camadas e Responsabilidades](#5-backend--camadas-e-responsabilidades)
6. [Frontend — Módulos e Componentes](#6-frontend--módulos-e-componentes)
7. [Banco de Dados](#7-banco-de-dados)
8. [Fluxo de Aprovação](#8-fluxo-de-aprovação)
9. [Segurança e Autenticação](#9-segurança-e-autenticação)
10. [Notificações por E-mail](#10-notificações-por-e-mail)
11. [Exportação de Dados](#11-exportação-de-dados)
12. [API — Referência de Endpoints](#12-api--referência-de-endpoints)
13. [Configuração e Execução](#13-configuração-e-execução)
14. [Testes](#14-testes)
15. [Limitações Conhecidas](#15-limitações-conhecidas)

---

## 1. Visão Geral

O **PagueAres** é um sistema de gestão de reembolsos e despesas desenvolvido para a empresa Ares. Ele centraliza dois fluxos principais:

| Módulo | Nome | Descrição |
|--------|------|-----------|
| **Solicitação** | Solicitação de Pagamento | Pedidos de pagamento/adiantamento a fornecedores ou para adiantamentos internos |
| **RDDV** | Relatório de Despesas com Deslocamento e Viagem | Prestação de contas de viagens corporativas |

Ambos os módulos seguem um **fluxo de aprovação em três estágios**: Gestor → Financeiro → Contabilidade, com suporte a rascunhos, anexos, atribuição de responsáveis e notificações automáticas por e-mail.

---

## 2. Stack Tecnológica

| Camada | Tecnologia | Versão |
|--------|-----------|--------|
| Frontend | Angular (NgModules, builder `application`/esbuild) | ~20.2.0 |
| Frontend UI | Angular Material / CDK | ~20.2.14 |
| Frontend UI | Bootstrap (+ bootstrap-material-design) | 4.5.2 |
| Frontend Linguagem | TypeScript | ~5.9.0 |
| Frontend Utilitários | RxJS / zone.js / Moment.js / ngx-mask / jwt-decode | 6.6.3 / 0.15.1 / ^2.29.1 / ^20.0.3 / ^4.0.0 |
| Testes Frontend | Karma + Jasmine / Cypress | Karma 6.3.4, jasmine-core 3.8.0 / Cypress ^13.17.0 |
| Backend Framework | ASP.NET Core | 10.0 (`net10.0`) |
| Backend Linguagem | C# | padrão do .NET 10 |
| ORM | Entity Framework Core (SQL Server) | 10.0.8 |
| Banco de Dados | SQL Server | 2019+ (imagem Docker `mssql/server:2022-latest`) |
| Autenticação | JWT HS256 (`JwtBearer` 10.0.8) + Active Directory (LDAP) | — |
| Hash de senha | BCrypt.Net-Next | 4.0.3 |
| E-mail | SMTP (`System.Net.Mail`) | — |
| Compressão | SharpZipLib (ZIPs legados por título) / `System.IO.Compression` (pacote da exportação) | 1.4.2 / .NET 10 |
| Excel | ClosedXML (planilha da exportação de títulos e Excel de pagamentos) | ver `Ares.PagueAres.Application.csproj` |
| Serialização | Newtonsoft.Json | 13.0.4 |
| Documentação da API | OpenAPI (`Microsoft.AspNetCore.OpenApi`) + Scalar UI (`Scalar.AspNetCore`) | 10.0.8 / 2.14.14 |
| Testes Backend | xUnit, FluentAssertions, Moq, EF Core InMemory, `Mvc.Testing` | xUnit 2.9.2, FluentAssertions 6.12.2, Moq 4.20.72 |
| Contêineres | Docker Compose (SQL Server, API, Nginx) | — |

> A API **não usa Swagger**. A documentação interativa é o Scalar (`/scalar/v1`) e o documento OpenAPI fica em `/openapi/v1.json`; ambos só são publicados quando `ASPNETCORE_ENVIRONMENT=Development`.

---

## 3. Arquitetura do Sistema

```
┌─────────────────────────────────────────────────────────────┐
│                      USUÁRIO (Navegador)                    │
└──────────────────────────┬──────────────────────────────────┘
                           │ HTTP(S)
┌──────────────────────────▼──────────────────────────────────┐
│                FRONTEND — Angular 20 (SPA)                  │
│  ┌──────────┐  ┌──────────┐  ┌──────────┐  ┌──────────┐   │
│  │  Login   │  │  Home    │  │Pagamentos│  │ Criar    │   │
│  │          │  │(Dashboard)│ │ (listas) │  │ RDDV/Sol.│   │
│  └──────────┘  └──────────┘  └──────────┘  └──────────┘   │
│  ┌─────────────────────────────────────────────────────┐   │
│  │  Services (HTTP) + AuthInterceptor (JWT Bearer)     │   │
│  └─────────────────────────────────────────────────────┘   │
│  Em Docker: servido pelo Nginx, que faz proxy de /api/ → api│
└──────────────────────────┬──────────────────────────────────┘
                           │ REST JSON
┌──────────────────────────▼──────────────────────────────────┐
│                BACKEND — ASP.NET Core 10                    │
│                                                             │
│  ┌─────────────────── API Layer ──────────────────────┐    │
│  │  Controllers │ JwtMiddleware │ JwtBearer/[Authorize]│    │
│  └───────────────────────┬────────────────────────────┘    │
│                          │                                  │
│  ┌─────────────── Application Layer ──────────────────┐    │
│  │  AuthenticationService │ EmailService │ ADService  │    │
│  └───────────────────────┬────────────────────────────┘    │
│                          │                                  │
│  ┌─────────────── Infrastructure Layer ───────────────┐    │
│  │  PagueAresContext (EF Core) │ Migrations            │    │
│  └───────────────────────┬────────────────────────────┘    │
│                          │                                  │
│  ┌────────────── Domain Layer (DTOs/Enums) ────────────┐   │
│  │  DTOs │ Enums │ Funcoes │ GenericResponse<T>        │   │
│  └─────────────────────────────────────────────────────┘   │
└──────────────────────────┬──────────────────────────────────┘
                           │
          ┌────────────────┴───────────────┐
          │                                │
   ┌──────▼──────┐                 ┌───────▼──────┐
   │  SQL Server │                 │ Active Dir.  │
   │  (PagueAres)│                 │    (LDAP)    │
   └─────────────┘                 └──────────────┘
```

### Padrão Arquitetural

O backend adota **Clean Architecture** em 4 projetos (mais 2 projetos de teste). Referências reais entre projetos (`.csproj`):

- **API** → Application, Domain, Infrastructure: ponto de entrada HTTP, roteamento, middleware de autenticação, `Program.cs`
- **Application** → Domain, Infrastructure: serviços de autenticação, e-mail e integração com Active Directory
- **Infrastructure**: acesso a dados (EF Core, entidades, migrations); não referencia outros projetos
- **Domain**: contratos compartilhados — DTOs, enums, constantes de papéis, resposta genérica; não referencia outros projetos

### Topologia Docker (`docker-compose.yml`)

| Serviço | Imagem/Build | Porta no host (padrão) | Observação |
|---------|--------------|------------------------|------------|
| `db` | `mssql/server:2022-latest` | só no override de desenvolvimento: `${DB_PORT:-14330}` → 1433 | volume `pagueares-db-data`, healthcheck com `sqlcmd`. O compose base não publica a porta do banco; usado sozinho (`docker compose -f docker-compose.yml up`), o SQL fica acessível apenas na rede interna `pagueares-net` |
| `api` | `back-end/Dockerfile` (.NET 10) | `${API_PORT:-5000}` → 8080 | `ConnectionStrings__ConexaoPadrao` apontando para `db,1433`; roda migrations no startup; volume `pagueares-exportacao` em `/var/pagueares/exportacao` (`Exportacao__Pasta`) para os pacotes da exportação |
| `frontend` | `front-end/Dockerfile` (Node build + Nginx) | `${FRONTEND_PORT:-80}` → 80 | Nginx faz proxy de `/api/` para `http://api:8080` |

---

## 4. Estrutura de Pastas

### Raiz do repositório

```
pague-ares/
├── back-end/                        # Solução .NET
├── front-end/                       # Aplicação Angular
├── docker-compose.yml               # SQL Server + API + frontend
├── docker-compose.override.yml      # Dev local: SQL na porta 14330, API em Development
├── .env.example                     # Variáveis do compose (copiar para .env)
├── docs/implantacao/iis-exportacao.md  # Guia de implantação da exportação no IIS
├── DOCUMENTACAO.md                  # Este documento
├── RELATORIO_TESTES.md              # Relatório de execução de testes
└── RELATORIO_MELHORIAS_CLIENTE.md   # Relatório de melhorias (versão para o cliente; há também um PDF)
```

### Backend (`back-end/`)

```
back-end/
├── Ares.PagueAres.sln
├── Dockerfile / entrypoint.sh       # Imagem da API (aguarda o SQL Server em db:1433)
├── PagueAres.sql                    # Script manual alternativo de criação do banco
├── azure-pipelines.yml
│
├── Ares.PagueAres.API/
│   ├── Program.cs                   # Composição DI, JWT, CORS, OpenAPI/Scalar, migrations
│   ├── appsettings.json             # JwtSettings, RunMigrations, AllowedOrigins
│   ├── appsettings.Development.json # Connection string local (localhost,14330), AD/SMTP desligados
│   ├── Properties/launchSettings.json
│   ├── Controllers/
│   │   ├── AuthController.cs
│   │   ├── RequestController.cs
│   │   ├── RddvController.cs
│   │   ├── UsersController.cs
│   │   ├── ListingController.cs
│   │   ├── ExportController.cs
│   │   ├── ConfigController.cs
│   │   ├── ProviderController.cs
│   │   ├── TravelTypeController.cs
│   │   ├── DepartmentController.cs
│   │   └── PreviewController.cs
│   ├── Extensions/ControllerBaseExtensions.cs   # GetUserFromToken / CurrentUserIsInRole
│   ├── Helpers/                     # AuthenticationHelper, ReportModel
│   ├── Middlewares/Authentication/JwtMiddleware.cs
│   └── Dtos/                        # ListFilterDto, PaymentListItem
│
├── Ares.PagueAres.Application/
│   ├── Authentication/              # IAuthenticationService, AuthenticationService
│   ├── Services/EmailService.cs
│   ├── External/ActiveDirectory/    # ActiveDirectoryService, Dtos/AdUserInfo
│   ├── Configuration/               # JwtSettings, ActiveDirectorySettings, DomainServer, SmtpSettings
│   ├── Exceptions/LoginFailedException.cs
│   └── Extensions/ApplicationServiceExtensions.cs
│
├── Ares.PagueAres.Infrastructure/
│   ├── PagueAresContext.cs          # DbContext com 15 DbSets (13 tabelas + 2 views)
│   ├── Models/                      # Entidades EF (Usuario, Solicitacao, Rddv, ...)
│   └── Migrations/                  # InitialCreate, DecimalPrecision_Indexes
│
├── Ares.PagueAres.Domain/
│   ├── Funcoes.cs                   # Constantes de papéis (ADM, SOL, GES, FIN, CON)
│   ├── GenericResponse.cs           # Wrapper de resposta da API
│   ├── Dtos/                        # DTOs de transferência (inclui Authentication/ e Listing/)
│   └── Enums/                       # EnumRequestType, EnumViewListagemSolicitacoes (PaymentListView)
│
├── Ares.PagueAres.Tests.Unit/       # Testes unitários (xUnit)
└── Ares.PagueAres.Tests.Integration/# Testes de integração (xUnit + WebApplicationFactory)
```

### Frontend (`front-end/`)

```
front-end/
├── package.json / package-lock.json
├── angular.json / karma.conf.js / cypress.config.ts
├── Dockerfile / nginx.conf / docker-entrypoint.sh
├── cypress/                         # Specs E2E (e2e/{auth,payments,rddv,security,workflow}) e support/
├── src/
│   ├── main.ts                      # Bootstrap do AppModule
│   ├── app/
│   │   ├── app.module.ts            # Módulo raiz (APP_INITIALIZER, interceptor, locale pt-BR)
│   │   ├── app.routing.ts           # Rotas raiz (hash routing, lazy loading do layout)
│   │   │
│   │   ├── layouts/admin-layout/    # Layout principal autenticado
│   │   ├── components/              # dialog, footer, loading, navbar, sidebar
│   │   │
│   │   ├── core/
│   │   │   ├── interceptors/        # ApiAuthenticationInterceptor.ts (AuthInterceptor)
│   │   │   └── intl/                # Localização PT-BR (paginador)
│   │   │
│   │   ├── login/                   # Página de login
│   │   ├── home/                    # Dashboard
│   │   ├── payments/                # Listagem por visão (Solicitações + RDDVs)
│   │   ├── rddv/ refunds/ icons/    # Componentes declarados no módulo (rddv/icons com rota)
│   │   ├── create-request/          # Formulário de Solicitação
│   │   ├── create-rddv/             # Formulário de RDDV
│   │   ├── create-user/             # Cadastro de usuário
│   │   ├── payment-report/          # Relatório imprimível — Solicitação
│   │   ├── rddv-report/             # Relatório imprimível — RDDV
│   │   ├── configuration/           # Configurações do sistema
│   │   │   └── travel-type-list/    # Sub-componente: Tipos de Viagem
│   │   │
│   │   ├── services/                # Serviços HTTP e utilitários
│   │   ├── models/                  # Interfaces/enums TypeScript
│   │   └── utils/                   # Utilitários de data
│   │
│   ├── environments/                # environment.ts / environment.prod.ts (não definem a URL da API)
│   └── assets/
│       ├── config.json              # BaseUrl da API, carregado no boot
│       └── css/ scss/ fonts/ img/
```

---

## 5. Backend — Camadas e Responsabilidades

### 5.1 Controllers

Todos usam `[Route("api/[controller]")]` e `[Authorize]`, salvo `AuthController` (`[AllowAnonymous]`). **Não há versionamento de rotas** (nada como `/api/v1`): a "versão da API" é a propriedade `<Version>` do `Ares.PagueAres.API.csproj` (atualmente `26.6.1.2`), e o documento OpenAPI declara `Info.Version = "v1"`.

#### AuthController — `/api/auth`
| Método | Rota | Descrição |
|--------|------|-----------|
| POST | `/api/auth` | Autenticação (login). Retorna JWT no `GenericResponse`. |

Suporta autenticação local (banco de dados, BCrypt) e via Active Directory (LDAP). Valida se o usuário está ativo antes de emitir o token e aplica bloqueio por tentativas (ver 9.2).

---

#### RequestController — `/api/request`
| Método | Rota | Descrição |
|--------|------|-----------|
| GET | `/api/request/{id}` | Retorna solicitação completa com documentos (dono ou ADM/GES/FIN/CON) |
| POST | `/api/request` | Cria ou atualiza solicitação (suporta rascunho, cancelamento e transições de aprovação) |
| GET | `/api/request/dashboard` | Estatísticas consolidadas (soma de Solicitações e RDDVs): pendentes, aprovados, reprovados |
| GET | `/api/request/{id}/user?userId=` | Atribui solicitação a um usuário |

**Funcionalidades:** rascunho, upload de anexos, workflow de aprovação com verificação de papel e transação de banco, notificações por e-mail. A listagem de solicitações é feita por `/api/listing`; os antigos `GET /api/request` e `GET /api/rddv` (listagem crua, sem escopo de usuário) foram **removidos**.

---

#### RddvController — `/api/rddv`
| Método | Rota | Descrição |
|--------|------|-----------|
| GET | `/api/rddv/{id}` | Retorna relatório com despesas e documentos (dono ou ADM/GES/FIN/CON) |
| POST | `/api/rddv` | Cria ou atualiza relatório |
| GET | `/api/rddv/dashboard` | Estatísticas apenas de RDDVs: pendentes, aprovados, reprovados |
| GET | `/api/rddv/{id}/user?userId=` | Atribui relatório a um usuário |

---

#### ListingController — `/api/listing`
| Método | Rota | Descrição |
|--------|------|-----------|
| GET | `/api/listing` | Listagem unificada (Solicitações e RDDVs, view `vListagemSolicitacoes`) com modos de visualização |

**Modos de Visualização (`ViewMode`):**
- `MyView` (1) — itens do próprio usuário (inclui rascunhos)
- `ManagerView` (2) — itens em que o usuário é o gestor
- `FinancialView` (3) — itens aprovados pelo gestor (todos se FIN; só os atribuídos ao usuário caso contrário)
- `Dashboard` (4) — aprovados pelo gestor, ainda sem responsável atribuído e não negados por Financeiro/Contabilidade

Rascunhos e cancelados **nunca** aparecem em `ManagerView`, `FinancialView` e `Dashboard`.

Filtros (query string): `DocumentNumber`, `DocumentId`, `RequestStart`/`RequestEnd`, `DueStart`/`DueEnd`, `PartnerName`, `RequesterId`, `AssigneeId`, `IsAssigned`, `ManagerStatus`, `FinanceStatus`, `AccountingStatus`, `ViewMode`, `SortField`, `SortOrder` (`asc`/`desc`), `PageSize` (padrão 100) e `Page` (padrão 1).

---

#### UsersController — `/api/users`
| Método | Rota | Descrição |
|--------|------|-----------|
| GET | `/api/users` | Lista todos os usuários — **somente ADM** |
| GET | `/api/users/{id}` | Retorna usuário com papéis — o próprio perfil ou ADM |
| POST | `/api/users` | Cria ou atualiza usuário — **somente ADM** |
| GET | `/api/users/directory?userName=` | Busca usuário no Active Directory |
| GET | `/api/users/list?roles=` | Lista (id, nome) de usuários ativos, opcionalmente filtrada por papéis (ex.: `GES,FIN`) |

**Funcionalidades:** cadastro duplo (local + AD), atribuição de papéis. Senhas informadas em `POST /api/users` são gravadas como SHA256 e convertidas para BCrypt no primeiro login do usuário.

---

#### ExportController — `/api/export`
| Método | Rota | Descrição |
|--------|------|-----------|
| GET | `/api/export/payments?sFilter=&userId=&key=` | Exporta lista de pagamentos em Excel (`Pagamentos.xlsx`); `userId` é aceito mas ignorado |
| GET | `/api/export/rddv/attachment/{id}?key=` | Exporta anexos de RDDV como ZIP |
| GET | `/api/export/request/attachment/{id}?key=` | Exporta anexos de solicitação como ZIP |

As três rotas são `[AllowAnonymous]` e protegidas pela *preview key* (ver 9.5). A exportação Excel só inclui títulos não rascunho, não cancelados e aprovados pelo gestor, para quem tem chave `Exportacao` (somente FIN e ADM). Erros nunca devolvem texto de exceção: filtro inválido → 400 `"Filtro de exportação inválido."`; demais falhas → 500 `"Não foi possível concluir a operação. Tente de novo."` (a exceção completa vai para o log).

---

#### ExportacaoController — `/api/exportacao`
Exportação de títulos com anexos em pacote ZIP gerado em segundo plano (ver seção 11.1). Todas as rotas exigem JWT e papel **FIN ou ADM** vindo do token, exceto o download. Respostas no envelope `GenericResponse<T>`. O parâmetro `{id}` é sempre `{id:int}`; a identidade do usuário vem sempre do JWT (nunca do corpo ou da query).

| Método | Rota | Descrição | Respostas |
|--------|------|-----------|-----------|
| POST | `/api/exportacao` | Corpo `FiltroExportacaoDto`. Cria e enfileira a exportação | 202 com `ExportacaoJobDto` (Pendente); 401; 403; 400 (filtro inválido, nenhum título ou mais que `MaxTitulosPorExportacao`); 429 (usuário já tem `MaxJobsAtivosPorUsuario` exportações Pendente/Processando) |
| GET | `/api/exportacao` | Exportações **do próprio usuário** dos últimos 30 dias, mais recentes primeiro | 200 com lista de `ExportacaoJobDto`; 401; 403 |
| GET | `/api/exportacao/{id}` | Status e progresso de uma exportação | 200; 401; 403; 404 (não existe **ou é de outro usuário**) |
| POST | `/api/exportacao/{id}/link-download` | Gera o link de download (token de uso único) | 200 com `LinkDownloadExportacaoDto` (`token`, `expiraEm`); 401; 403; 404; 409 (não está Concluída); 410 (expirada ou arquivo ausente) |
| GET | `/api/exportacao/download?token=` | `[AllowAnonymous]`. Baixa o ZIP em streaming | 200 `application/zip`; 401 (token ausente, inválido, já usado ou vencido); 410 (arquivo expirado ou ausente) |
| DELETE | `/api/exportacao/{id}` | Pendente → Cancelada; Processando → pede cancelamento; Concluída → apaga o arquivo e vira Expirada | 204; 401; 403; 404; 409 (já Falhou, Expirada ou Cancelada) |

---

#### ConfigController — `/api/config`
| Método | Rota | Descrição |
|--------|------|-----------|
| GET | `/api/config` | Retorna todos os parâmetros de configuração (qualquer usuário autenticado) |
| POST | `/api/config` | Define ou atualiza parâmetros — **somente ADM** |

---

#### ProviderController — `/api/provider`
| Método | Rota | Descrição |
|--------|------|-----------|
| GET | `/api/provider` | Lista fornecedores (paginado) |
| GET | `/api/provider/{id}` | Retorna fornecedor por ID |
| GET | `/api/provider/find/{numDoc}` | Busca fornecedor por CPF/CNPJ |

---

#### TravelTypeController — `/api/traveltype`
| Método | Rota | Descrição |
|--------|------|-----------|
| GET | `/api/traveltype` | Lista tipos de viagem |
| GET | `/api/traveltype/{id}` | Retorna tipo por ID |
| POST | `/api/traveltype` | Cria ou atualiza tipo com valor de diária |

---

#### DepartmentController — `/api/department`
| Método | Rota | Descrição |
|--------|------|-----------|
| GET | `/api/department` | Lista departamentos (id, nome, código de lançamento), ordenados por nome |

Substitui as listas fixas de departamentos que existiam no frontend.

---

#### PreviewController — `/api/preview`
| Método | Rota | Descrição |
|--------|------|-----------|
| POST | `/api/preview/generate-preview-key?entityId=&requestType=` | Gera a chave temporária (autenticado). Solicitação/RDDV: dono do registro ou ADM/GES/FIN/CON, e título em rascunho só dá chave ao dono. `Exportacao`: somente FIN ou ADM (403 para os demais) |
| GET | `/api/preview/payment/{id}/{attachmentId}?key=` | Serve um anexo de solicitação (`[AllowAnonymous]` + chave) |
| GET | `/api/preview/rddv/{id}/{attachmentId}?key=` | Serve um anexo de RDDV (`[AllowAnonymous]` + chave) |

---

### 5.2 Serviços de Aplicação

#### AuthenticationService
- `TryAuthenticate(username, password)` — autenticação local (BCrypt; hashes SHA256 legados são aceitos e migrados para BCrypt) ou LDAP; localiza o usuário por e-mail ou código
- `GenerateAuthenticationToken(userData)` — gera JWT com claims: Subject, NameId, Roles, JTI
- `GetUserById(id)` — retorna usuário com papéis carregados

#### EmailService
Envia e-mails por SMTP para os eventos abaixo (o envio só ocorre se `Smtp:SmtpHost`, `SmtpUser`, `SmtpPassword` e `EnableSsl` estiverem configurados):
| Método | Evento |
|--------|--------|
| SendNewRequestEmail | Nova solicitação/relatório enviado (saiu de rascunho) |
| SendApprovalEmail | Aprovação pelo gestor |
| SendDenialEmail | Reprovação pelo gestor |
| SendPaidEmail | Pagamento autorizado pelo financeiro |
| SendPaymentDeniedEmail | Pagamento negado pelo financeiro |
| SendAccountingPaidEmail | Lançado pela contabilidade |
| SendAccountingDenialEmail | Recusado pela contabilidade |
| SendRequestAssignedEmail | Solicitação atribuída a processador |

#### ActiveDirectoryService
- `GetUserInformation()` — retorna SID, Login, Nome, E-mail do AD
- `TryAuthenticate()` — validação via bind LDAP
- Novos usuários do AD são criados automaticamente no banco na primeira autenticação
- Baseado em `System.DirectoryServices.AccountManagement` (API específica do Windows)

---

### 5.3 Autenticação e Middleware

A API usa **dois mecanismos complementares**:

1. **Esquema `JwtBearer`** (`Program.cs`, `AddAuthentication(...).AddJwtBearer`): valida assinatura, issuer, audience e expiração. É ele que faz o `[Authorize]` responder **401** para token ausente, adulterado ou expirado.
2. **`JwtMiddleware`** (`Middlewares/Authentication/JwtMiddleware.cs`): extrai o token de `Authorization: Bearer <token>`, valida assinatura e expiração (sem `ClockSkew`), lê as claims `nameid` (userId), `sub` (userName) e `Roles` (aliases separados por vírgula) e injeta um `UsuarioDto` em `HttpContext.Items["User"]`. Os controllers obtêm usuário e papéis via `this.GetUserFromToken()` e `this.CurrentUserIsInRole(...)` (`Extensions/ControllerBaseExtensions.cs`). Em falha de validação a requisição segue sem usuário no contexto.

---

### 5.4 Resposta Genérica da API

Os endpoints retornam o contrato `GenericResponse<T>` (`Domain/GenericResponse.cs`):

```json
{
  "success": true,
  "message": "Operação realizada com sucesso",
  "data": { ... },
  "records": 150,
  "pages": 15,
  "page": 1
}
```

Erros de negócio (registro não encontrado, falta de permissão, validação) normalmente voltam com **HTTP 200** e `success = false`; HTTP 401 indica falta de autenticação ou preview key inválida.

---

## 6. Frontend — Módulos e Componentes

### 6.1 Roteamento

O app usa **rotas com hash** (`useHash: true`), por exemplo `http://localhost:4200/#/payments`.

```
/login                    → LoginComponent
/payment-report/:id       → PaymentReportComponent (fora do layout; carrega dados com o JWT do localStorage)
/rddv-report/:id          → RddvReportComponent    (idem)
/                         → AdminLayoutComponent (redireciona para /login se não autenticado)
  /home                   → HomeComponent
  /payments               → PaymentsComponent
  /rddv                   → RddvComponent
  /config                 → ConfigurationComponent
  /icons                  → IconsComponent
  /create-request[/:id]   → CreateRequestComponent
  /create-rddv[/:id]      → CreateRddvComponent
  /create-user[/:id]      → CreateUserComponent
```

O `AdminLayoutModule` é carregado via **lazy loading**. Após login bem-sucedido o app navega para `/payments`. O menu lateral varia por papel: ADM (Home, Solicitações, Configurações), FIN/CON (Home, Solicitações), GES/SOL (Solicitações).

---

### 6.2 Componentes Principais

#### LoginComponent
- Formulário de usuário e senha
- Chama `AuthService.Authenticate()`, armazena o token e redireciona para `/payments`
- Distingue credenciais inválidas/bloqueio (mensagem da API) de erro de conexão

#### HomeComponent (Dashboard)
- Exibe cards de estatísticas: Pendentes, Aprovados, Reprovados (somando Solicitações e RDDVs)
- Atualização automática a cada 30 segundos
- Lista de itens aguardando atribuição de responsável (visão `Dashboard`)

#### PaymentsComponent
- Lista unificada de Solicitações e RDDVs, em abas por visão: "Minhas Solicitações", "Gestor" (GES/ADM) e "Financeiro/Contábil" (FIN/CON/ADM)
- Filtros: período, número de documento, parceiro, status etc.; o filtro é persistido por usuário no `localStorage`
- Badge "Rascunho", indicador de carregamento
- Ações: abrir/editar, abrir relatório, exportar Excel (visão financeira), atribuir; botões "Solicitar Pagamento" e "Solicitar RDDV"

#### RddvComponent / RefundsComponent
Componentes declarados no módulo do layout (`/rddv` tem rota; `RefundsComponent` não tem rota própria). A listagem principal de RDDVs é a de `PaymentsComponent`.

#### CreateRequestComponent
- Formulário completo de criação/edição de Solicitação
- Campos: tipo de solicitação (Adiantamento, Pagamento, Movimento Almoxarifado), tipo de pagamento (Nacional/Internacional), forma de pagamento (Boleto/Transferência), fornecedor, valor, parcelamento, datas, dados bancários, tipo de autorização (por anexo/por sistema)
- Upload de múltiplos arquivos
- Botões de aprovação por estágio (Gestor, Financeiro, Contabilidade); etapa já decidida não é mais editável
- **Rascunho:** salva sem validação; ao reabrir os campos continuam editáveis; cancelar não inicia o fluxo de aprovação

#### CreateRddvComponent
- Formulário de Relatório de Despesas com Viagem
- Campos: funcionário, CPF, tipo de relatório (Adiantamento, Prestação de Contas, Reembolso), tipo de viagem, datas, destino, finalidade, moeda, tabela de despesas
- Linhas de despesa: data, tipo, moeda, valor, quantidade
- Upload de documentos + workflow de aprovação, com as mesmas regras de rascunho/etapas da Solicitação

#### CreateUserComponent
- Cadastro e edição de usuários (a gravação exige ADM no backend)
- Busca no Active Directory
- Atribuição de papéis
- Gestão de senha (somente para usuários locais)
- Departamentos carregados de `/api/department`

#### ConfigurationComponent
- Gerenciamento de parâmetros do sistema, lista de usuários (links para `/create-user`)
- Sub-componente `TravelTypeListComponent` para gerenciar tipos de viagem e valores de diária

#### PaymentReportComponent / RddvReportComponent
- Relatórios de impressão/visualização, abertos em nova aba
- Carregam os dados com o JWT do usuário (`GET /api/request/{id}` / `/api/rddv/{id}`)
- Exibem informações completas com status de aprovações

---

### 6.3 Serviços

| Serviço | Responsabilidade |
|---------|-----------------|
| `AuthService` | Login, armazenamento e validação de token JWT, verificação de papel |
| `PaymentService` | Solicitações (`/api/Request`), listagem unificada (`/api/Listing`), dashboard, atribuição |
| `RddvService` | RDDVs (`/api/Rddv`), dashboard, atribuição |
| `UsersService` | Gestão de usuários, busca no AD |
| `ProviderService` | Consulta de fornecedores por ID ou CPF/CNPJ |
| `TravelTypeService` | CRUD de tipos de viagem |
| `DepartmentService` | Lista de departamentos |
| `ExportService` | URLs de exportação Excel e ZIP de anexos |
| `PreviewTokenService` | Geração da preview key temporária |
| `ConfigService` | Leitura e escrita de configurações do sistema |
| `SystemService` | Inicialização da aplicação (`APP_INITIALIZER`) — carrega `assets/config.json` |
| `LocalStorageService` | Wrapper para operações no `localStorage` |
| `BaseUrlService` | Monta as URLs dos endpoints; a base (`BaseUrl`) é preenchida pelo `SystemService` a partir do `config.json` |
| `UtilsService` | Funções utilitárias estáticas (ex.: validação de CPF) |

---

### 6.4 Interceptor HTTP

**AuthInterceptor** (`core/interceptors/ApiAuthenticationInterceptor.ts`)

Adiciona automaticamente o token JWT em todas as requisições HTTP:
```
Authorization: Bearer <token>
```

**Exclusões** (URLs que contêm estes trechos ficam sem o header):
- `api/Auth` (login)
- `assets/config.json` (configuração de boot)

---

## 7. Banco de Dados

O esquema é criado pelas **migrations do EF Core** (`Infrastructure/Migrations`): `InitialCreate` (tabelas, views e dados base) e `DecimalPrecision_Indexes` (precisão dos campos monetários e índices `IX_Solicitacao_Status`, `IX_Solicitacao_Usuario`, `IX_Rddv_Status`, `IX_Rddv_Usuario`). O script `back-end/PagueAres.sql` é uma alternativa manual. As migrations são aplicadas no startup quando `RunMigrations=true` ou `ASPNETCORE_RUN_MIGRATIONS=true`. O modelo EF não usa propriedades de navegação; os relacionamentos abaixo são lógicos/chaves estrangeiras no banco.

Dados semeados: papéis (`Funcao`), departamentos, tipos de documento, tipos de viagem e um usuário administrador inicial (ver `InitialCreate.cs`/`PagueAres.sql`; **altere a senha inicial** em qualquer ambiente real).

### 7.1 Entidades Principais

#### Usuario
| Campo | Tipo | Descrição |
|-------|------|-----------|
| IdUsuario | INT PK | Identificador |
| Email | varchar | E-mail |
| Codigo | varchar | Código/login (usado também no login e para usuários AD) |
| Nome | varchar | Nome completo |
| Senha | varchar | Hash BCrypt (SHA256 legado até o primeiro login); vazio para usuários AD |
| IdDepartamento | INT | Departamento |
| Ativo | bit | Usuário habilitado |
| IdExterno | varchar | SID do Active Directory |
| Origem | int | 0 = Local, 1 = AD |
| DataCadastro / UsuarioCadastro | datetime / INT | Auditoria de criação |
| UltimaAlteracao / UsuarioAlteracao | datetime / INT | Auditoria de alteração |

#### Solicitacao
| Campo | Tipo | Descrição |
|-------|------|-----------|
| IdSolicitacao | INT PK | Identificador |
| IdUsuario | INT | Solicitante |
| IdDepartamento | INT | Departamento |
| IdGestor | INT | Gestor responsável |
| NumeroDocumento | varchar | Número do documento |
| DataSolicitacao | datetime | Data da solicitação |
| DataDocumento | datetime | Data do documento |
| DataVencimento | datetime | Data de vencimento |
| Valor | decimal(12,2) | Valor solicitado |
| Descricao | varchar | Descrição |
| StatusGestor | int | 1=Pendente, 2=Aprovado, 3=Reprovado |
| StatusFinanceiro | int | 1=Pendente, 2=Autorizado, 3=Negado |
| StatusContabilidade | int | 1=Pendente, 2=Lançado, 3=Recusado |
| AprovadoGestor / AprovadoSetor | bit? | Decisão do gestor / do financeiro (setor) |
| ObservacaoGestor / ObservacaoSetor | varchar | Observações de cada estágio |
| DataAprovacaoGestor / Financeiro / Contabil | datetime | Datas das decisões |
| Rascunho | bit | Se é rascunho (não enviado); padrão `true` |
| Cancelado | bit | Se foi cancelado (não pode mais ser alterado) |
| TipoSolicitacao | int | 1=Adiantamento, 2=Pagamento, 3=Movimento Almoxarifado |
| TipoPagamento | int | 1=Nacional, 2=Internacional |
| FormaPagamento | int | 1=Boleto, 2=Transferência Bancária |
| Parcelado / Parcelas | bit / int | Parcelamento |
| TipoAutorizacao | int | 1=Por anexo (auto-aprova o gestor ao enviar), 2=Por sistema |
| IdUsuarioAtribuido / DataAtribuicao / IdUsuarioAtribuidor | INT / datetime / INT | Responsável pelo processamento |
| NumDocParceiro / CodigoParceiro / NomeParceiro | varchar | CPF/CNPJ, código e nome do fornecedor |
| BancoParceiro / AgenciaParceiro / ContaParceiro | varchar | Dados bancários |
| CentroCusto / Projeto / Pca | varchar | Dados contábeis |
| Observacao | varchar | Observação geral |

#### Rddv (Relatório de Despesas com Deslocamento e Viagem)
| Campo | Tipo | Descrição |
|-------|------|-----------|
| IdRelatorio | INT PK | Identificador |
| IdUsuario | INT | Funcionário |
| NomeFuncionario / CPF | varchar | Dados do funcionário |
| IdDepartamento | INT | Departamento |
| Finalidade | varchar | Objetivo da viagem |
| TipoRelatorio | int | 1=Adiantamento, 2=Prestação de Contas, 3=Reembolso |
| TipoViagem | INT | Tipo de viagem (define valor da diária) |
| DataInicio / DataFim | datetime | Período da viagem |
| DataCadastro / DataVencimento | datetime | Datas do relatório |
| LocalViagem | varchar | Destino |
| DiariaViagem | bit | Indica viagem com diária |
| Diarias | decimal(5,2) | Número de diárias |
| ValorDiaria / ValorKm | decimal(18,2) | Valores de referência |
| Adiantamento / ValorAdiantamento | bit / decimal(18,2) | Adiantamento recebido |
| SaldoCartaoVtm | decimal(18,2) | Saldo de cartão de vale-transporte |
| StatusGestor / StatusFinanceiro / StatusContabilidade | int | (mesmos valores de Solicitacao) |
| AprovadoGestor / AprovadoSetor, Observacao*, DataAprovacao* | — | Mesma estrutura de decisões de Solicitacao |
| IdUsuarioAtribuido / DataAtribuicao / IdUsuarioAtribuidor | INT / datetime / INT | Responsável pelo processamento |
| Rascunho / Cancelado | bit | Estados do relatório |
| CentroCusto / Projeto / PCA | varchar | Dados contábeis |
| Banco / Agencia / Conta | varchar | Dados bancários |
| Moeda | varchar | Moeda utilizada |

#### DespesaRddv
| Campo | Tipo | Descrição |
|-------|------|-----------|
| IdDespesaRddv | INT PK | Identificador |
| IdRelatorio | INT | Relatório pai |
| DataDespesa | datetime | Data da despesa |
| TipoDespesa | int | Código da categoria da despesa |
| Moeda | varchar | Moeda |
| Valor | decimal(18,2) | Valor unitário |
| Quantidade | decimal(18,4) | Quantidade |

#### DocumentoSolicitacao / DocumentoRddv
| Campo | Tipo | Descrição |
|-------|------|-----------|
| IdDocumentoSolicitacao / IdDocumentoRddv | INT PK | Identificador |
| IdSolicitacao / IdRelatorio | INT | Vínculo com a entidade pai |
| IdTipoDocumento | INT | Tipo do documento (`TipoDocumento`) |
| IdUsuario | INT | Quem anexou |
| Indice | int | Ordem do anexo |
| NomeArquivo | varchar | Nome original do arquivo |
| CaminhoArquivo | varchar | Usado como tipo MIME do arquivo ao servir o download |
| Arquivo | varchar | Conteúdo em base64 |
| DataCriacao | datetime | Data do anexo |

---

### 7.2 Tabelas de Suporte

| Tabela | Descrição |
|--------|-----------|
| Funcao | Papéis do sistema: ADM, SOL, GES, FIN, CON |
| UsuarioFuncao | Relacionamento N:N usuário ↔ papel |
| Departamento | Departamentos com código de lançamento |
| TipoDocumento | Tipos de documentos aceitos (Nota Fiscal, Fatura, Nota de Débito, Boleto, RDDV, Comprovantes, Arquivo Zipado, Outros Documentos, Autorização) |
| TipoViagem | Tipos de viagem com valor de diária (Nacional, Internacional, Internacional Israel, Internacional Euro) |
| Fornecedor | Cadastro de fornecedores (CPF/CNPJ, nome, código) |
| Configuracao | Parâmetros do sistema (chave-valor) |

---

### 7.3 Views

| View | Descrição |
|------|-----------|
| vDespesasRddv | Total de despesas por relatório (agregação); entidade `vDespesasRddv` |
| vListagemSolicitacoes | View unificada de Solicitações e RDDVs usada pela listagem; entidade `ViewListagemSolicitacoes` |

---

### 7.4 Diagrama de Relacionamentos

```
Usuario ──────────────────────────┐
  │                               │
  │ 1:N (solicitante)             │ N:M
  ▼                            UsuarioFuncao
Solicitacao                       │
  │ 1:N                           ▼
  ├─► DocumentoSolicitacao     Funcao
  │
  │ IdGestor ──────────► Usuario
  │ IdUsuarioAtribuido ─► Usuario

Usuario
  │ 1:N
  ▼
Rddv
  │ 1:N
  ├─► DespesaRddv
  ├─► DocumentoRddv
  │
  │ TipoViagem ──────────► TipoViagem
  │ IdGestor ────────────► Usuario

DocumentoSolicitacao / DocumentoRddv
  │ IdTipoDocumento ─────► TipoDocumento

Solicitacao
  │ NumDocParceiro / CodigoParceiro ► Fornecedor (vínculo lógico por documento/código)

Usuario / Solicitacao / Rddv
  │ IdDepartamento ──────► Departamento
```

---

## 8. Fluxo de Aprovação

### 8.1 Solicitação de Pagamento

```
┌──────────────┐
│  Usuário     │
│  Cria        │──── Rascunho = true ────► [Rascunho - não enviado]
│  Solicitação │
└──────┬───────┘
       │ Envia (Rascunho = false)
       │ E-mail → Solicitante e Gestor
       ▼
┌──────────────────────────────────┐
│       ESTÁGIO 1: GESTOR          │
│  StatusGestor = 1 (Pendente)     │
└──────────┬──────────────┬────────┘
           │ Aprova       │ Reprova
           │              │
           ▼              ▼
  StatusGestor=2    StatusGestor=3
  E-mail → Usuário  E-mail → Usuário
  [Aprovado]        [Reprovado - ENCERRADO]
           │
           │ (se StatusGestor=2)
           ▼
┌──────────────────────────────────┐
│    ESTÁGIO 2: FINANCEIRO         │
│  StatusFinanceiro = 1 (Pendente) │
└──────────┬──────────────┬────────┘
           │ Aprova       │ Nega
           │              │
           ▼              ▼
  StatusFinanceiro=2  StatusFinanceiro=3
  E-mail → Usuário    StatusContabilidade=3
  [Pgt. Autorizado]   E-mail → Usuário
                      [Pgt. Negado - ENCERRADO]
           │
           │ (se StatusFinanceiro=2)
           ▼
┌──────────────────────────────────┐
│   ESTÁGIO 3: CONTABILIDADE       │
│ StatusContabilidade = 1 (Pendente│
└──────────┬──────────────┬────────┘
           │ Lança        │ Recusa
           │              │
           ▼              ▼
  StatusContabilidade=2  StatusContabilidade=3
  E-mail → Usuário       E-mail → Usuário
  [CONCLUÍDO - Lançado]  [ENCERRADO - Recusado]
```

**Exceção — Autorização por anexo:**
Se `TipoAutorizacao = 1`, no momento em que o registro sai de rascunho o estágio do Gestor é aprovado automaticamente (`StatusGestor = 2`, observação "Autorização por anexo.").

**Rascunho e cancelamento:**
- Rascunhos e registros cancelados não entram nas visões de fluxo de aprovação, no dashboard nem na exportação.
- Cancelar um registro não dispara e-mail nem auto-aprovação; registro cancelado não pode mais ser alterado.

### 8.2 RDDV (Relatório de Viagem)

Fluxo idêntico ao da Solicitação, com os mesmos três estágios, os mesmos estados de rascunho/cancelamento e o mesmo conjunto de e-mails.

**Tipos de Relatório (`TipoRelatorio`):**
| Código | Tipo |
|--------|------|
| 1 | Adiantamento |
| 2 | Prestação de Contas |
| 3 | Reembolso |

### 8.3 Valores de Status

| Código | Gestor / Financeiro | Contabilidade |
|--------|---------------------|---------------|
| 1 | Pendente (Aguardando) | Pendente (Aguardando Lançamento) |
| 2 | Aprovado / Autorizado | Lançado |
| 3 | Reprovado / Negado | Recusado |

### 8.4 Atribuição de Responsáveis

Qualquer solicitação ou RDDV pode ser atribuído a um processador específico (`IdUsuarioAtribuido`) por `GET /api/request/{id}/user?userId=` (ou `/api/rddv/{id}/user`). O sistema registra a data da atribuição (`DataAtribuicao`). O solicitante e o atribuído recebem notificação por e-mail.

---

## 9. Segurança e Autenticação

### 9.1 Autenticação JWT

**Configuração (`JwtSettings`):**
```json
{
  "JwtSettings": {
    "Secret": "<segredo — defina por variável de ambiente JwtSettings__Secret>",
    "Issuer": "pagueares.local",
    "Audience": "pagueares.local",
    "ExpiryMinutes": 720
  }
}
```

- Algoritmo: **HS256** (HMAC SHA-256)
- Expiração: **12 horas** (`ExpiryMinutes = 720`)
- Claims no token: `sub` (nome), `nameid` (ID), `Roles` (aliases separados por vírgula), `jti` (UUID único)
- O compose injeta o segredo por `JwtSettings__Secret` ← `JWT_SECRET` (`.env`). O `appsettings.json` versionado contém apenas um valor de exemplo que não deve ser usado em ambientes reais.
- Token adulterado ou expirado → HTTP 401

### 9.2 Autenticação Dupla e Bloqueio de Tentativas

| Tipo | Mecanismo | Identificação no banco |
|------|-----------|----------------------|
| Local | BCrypt comparado com `Senha` (SHA256 legado aceito e migrado no login) | `Origem = 0` |
| Active Directory | LDAP bind + verificação de credenciais (requer `ActiveDirectory:EnableDomainAuthentication=true`) | `Origem = 1`, `IdExterno = SID` |

Usuários AD são criados automaticamente no primeiro login com dados do diretório.

**Migração transparente de senha:** se o hash armazenado ainda é SHA256 (64 caracteres hex), após um login válido a senha é regravada com BCrypt.

**Lockout / rate limiting do login:** o `AuthController` conta as tentativas com credenciais inválidas **por endereço IP** (`IMemoryCache`). Ao atingir **10 falhas**, as novas tentativas (inclusive corretas) retornam `success = false` com a mensagem "Muitas tentativas de login. Aguarde alguns minutos e tente novamente." O contador expira 5 minutos após a última falha e é zerado por um login bem-sucedido. Usuário inativo recebe "Usuário inativo.". O contador é local ao processo (não é compartilhado entre instâncias).

### 9.3 Papéis (Roles)

| Alias | Nome | Acesso |
|-------|------|--------|
| ADM | Administrador | Acesso total; único papel que lista/cria/edita usuários e altera configurações |
| SOL | Solicitante | Cria e acompanha solicitações próprias |
| GES | Gestor | Aprova/reprova como gestor |
| FIN | Financeiro | Autoriza/nega pagamentos; vê todas as aprovadas pelo gestor |
| CON | Contabilidade | Lança/recusa |

**Controle de acesso aplicado (RBAC):**

| Operação | Regra |
|----------|-------|
| `GET /api/users`, `POST /api/users` | Somente ADM |
| `GET /api/users/{id}` | O próprio usuário ou ADM |
| `POST /api/config` | Somente ADM |
| `GET /api/request/{id}`, `GET /api/rddv/{id}`, `POST /api/preview/generate-preview-key` (Solicitação/RDDV) | Dono do registro ou ADM/GES/FIN/CON |
| `POST /api/preview/generate-preview-key` (`Exportacao`), `GET /api/export/payments`, todas as rotas de `/api/exportacao` (exceto o download por token) | Somente FIN ou ADM |
| `POST /api/request` — editar registro existente | Dono do registro ou ADM/GES/FIN/CON; registro cancelado é recusado |
| `POST /api/request` — alterar `AprovadoGestor` | ADM ou GES |
| `POST /api/request` — alterar `AprovadoSetor` | ADM ou FIN |
| `POST /api/request` — alterar `StatusContabilidade` | ADM, FIN ou CON |

> `POST /api/rddv`, as rotas de atribuição (`.../{id}/user`) e `POST /api/traveltype` **não** aplicam hoje essas checagens de papel/propriedade — ver [Limitações Conhecidas](#15-limitações-conhecidas).

### 9.4 Controle de Acesso por Visualização

- **MyView**: vê apenas os próprios registros (inclui rascunhos)
- **ManagerView**: registros em que é o gestor, sem rascunhos/cancelados
- **FinancialView**: aprovados pelo gestor, sem rascunhos/cancelados (todos se FIN, somente atribuídos caso contrário)
- **Dashboard**: aprovados pelo gestor, sem responsável atribuído e não negados, sem rascunhos/cancelados

No frontend, o `viewMode` é validado contra o papel do usuário; a proteção efetiva dos dados é a do backend.

### 9.5 Preview Key

Para baixar anexos e exportações em novas abas/links (sem enviar o header `Authorization`):
- `POST /api/preview/generate-preview-key?entityId=&requestType=` (autenticado) devolve uma chave GUID guardada em `IMemoryCache` com validade de **1 minuto**. A chave registra internamente o `IdUsuario` do token (campo não serializado na resposta JSON)
- `requestType` pode ser `Solicitacao` (0), `Rddv` (1) ou `Exportacao` (2)
- Quem pode gerar: para `Solicitacao`/`Rddv`, o dono do registro ou ADM/GES/FIN/CON, e **título em rascunho só dá chave ao dono**; para `Exportacao`, **somente FIN ou ADM** (os demais recebem 403 "Exportação permitida apenas para os perfis Financeiro e Administrador.")
- As rotas anônimas `/api/preview/payment|rddv/...` exigem chave do mesmo tipo **e** do mesmo `entityId`
- `/api/export/payments` exige chave do tipo `Exportacao`, que é de **uso único** (removida do cache ao ser validada: a segunda tentativa recebe 401). O escopo do Excel é sempre o de FIN/ADM; o `userId` da query é ignorado
- Os ZIPs de `/api/export/request/attachment/{id}` e `/api/export/rddv/attachment/{id}` exigem chave do tipo correto **e** do mesmo `entityId`: chave de outro tipo **ou** de outro id → 401
- Chave ausente, inválida, expirada ou de tipo/entidade diferente → HTTP 401
- O download do pacote de `/api/exportacao` **não** usa a preview key: usa um token próprio de uso único (ver 11.1)

---

## 10. Notificações por E-mail

Configuração SMTP (seção `Smtp`, `Application/Configuration/SmtpSettings.cs`):
```json
{
  "Smtp": {
    "EnableEmailService": false,
    "SmtpHost": "<servidor SMTP>",
    "SmtpPort": 587,
    "SmtpUser": "<usuário>",
    "SmtpPassword": "<senha>",
    "EnableSsl": "true",
    "DefaultSender": "<remetente>"
  }
}
```

O e-mail só é enviado quando `SmtpHost`, `SmtpUser`, `SmtpPassword` e `EnableSsl` estão preenchidos. (`EnableEmailService` existe na classe, mas nenhum código a consulta hoje.) Sem `DefaultSender`, o remetente é `SmtpUser`.

**Tabela de Eventos e Destinatários:**

| Evento | Destinatário(s) |
|--------|----------------|
| Nova solicitação/RDDV enviado | Solicitante + Gestor |
| Aprovação pelo gestor | Solicitante |
| Reprovação pelo gestor | Solicitante |
| Pagamento autorizado (financeiro) | Solicitante |
| Pagamento negado (financeiro) | Solicitante |
| Lançado pela contabilidade | Solicitante |
| Recusado pela contabilidade | Solicitante |
| Solicitação atribuída | Solicitante + responsável atribuído |

O envio é feito após a gravação da transação; falhas de SMTP são registradas no console e não interrompem a operação.

---

## 11. Exportação de Dados

### 11.1 Exportação de títulos com anexos (pacote ZIP em segundo plano)

Recurso recorrente do Financeiro: um usuário com papel **FIN ou ADM** (e só esses) escolhe filtros e dispara a exportação; o servidor gera **em segundo plano** um pacote ZIP, que o usuário baixa manualmente quando estiver pronto. Volume de referência: 25 títulos por dia útil com cerca de 5 MB de anexos cada (cerca de 2,5 GB por mês). Os anexos ficam no banco em base64 e são lidos **um a um**, nunca todos em memória.

**Visibilidade.** Entram somente títulos com `Rascunho = false`, `Cancelado = false` e `AprovadoGestor = true`, mais os filtros pedidos.

**Fluxo.**
1. `POST /api/exportacao` com o `FiltroExportacaoDto` cria um registro na tabela `ExportacaoJob` (status Pendente) e o enfileira (202).
2. O `ExportacaoWorker` (`BackgroundService`) processa **um job por vez**: reivindica o job (Pendente → Processando, com o token de concorrência `Versao`), confere o espaço livre em disco, gera o pacote em `exportacao-<id>.zip.parcial` e, ao final, renomeia para `exportacao-<id>.zip` e grava Concluída com tamanho, total de anexos e `DataExpiracao` (conclusão + `RetencaoHoras`). O progresso (`TitulosProcessados`/`TotalTitulos`, `Percentual`) é gravado no máximo a cada 2 s ou 10 títulos.
3. A tela "Exportações" (somente FIN/ADM) acompanha o status por `GET /api/exportacao`.
4. O botão de download chama `POST /api/exportacao/{id}/link-download`, que devolve um token de uso único (32 bytes aleatórios em base64url, válido por `ValidadeLinkDownloadSegundos`, guardado em memória e emitido só ao dono de um job Concluído), e abre `GET /api/exportacao/download?token=`, que serve o ZIP em streaming (sem blob em memória, sem `Range`) com o nome `exportacao-titulos-<id>-<yyyyMMdd-HHmm>.zip`.
5. O `ExportacaoLimpeza` (`BackgroundService`), a cada `IntervaloLimpezaMinutos`, apaga o arquivo dos jobs Concluídos com `DataExpiracao` vencida (viram Expirada) e os `exportacao-*.zip` órfãos mais velhos que a retenção.

**Estados** (`StatusExportacao`): Pendente (0), Processando (1), Concluída (2), Falhou (3), Expirada (4), Cancelada (5). Em toda falha o `.parcial` é apagado. Falha prevista (`ExportacaoException`) mostra a própria mensagem amigável; qualquer outra exceção é registrada por completo no log e o usuário vê "Não foi possível gerar a exportação. Gere de novo; se o erro continuar, acione o suporte.". Se o servidor reiniciar durante a geração, o job vira Falhou com a mensagem de interrupção; os jobs Pendente são reenfileirados na subida.

**Filtros** (`FiltroExportacaoDto`, JSON em camelCase): `tipoTitulo` (`Ambos` 0, `Solicitacao` 1, `Rddv` 2), `codigo`, `numeroDocumento` (só Solicitação, igualdade exata), `dataSolicitacaoInicio/Fim` (Solicitação: `DataSolicitacao`; RDDV: `DataCadastro`), `dataVencimentoInicio/Fim`, `dataAprovacaoFinanceiroInicio/Fim`, `idSolicitante`, `idAtribuido`, `idDepartamento`, `statusGestor`, `statusFinanceiro`, `statusContabil`, `nomeParceiro` (Solicitação: parceiro; RDDV: funcionário; contém, sem diferenciar caixa), `documentoParceiro` (Solicitação: documento do parceiro; RDDV: CPF; contém, ignorando `.`, `-`, `/` e espaços), `tipoSolicitacao`, `tipoPagamento`, `formaPagamento` (só Solicitação), `tipoRelatorio` (só RDDV) e `incluirAnexos` (padrão `true`). Campo nulo ou vazio e inteiro `0` não filtram; datas valem sozinhas e o fim é inclusivo pelo dia inteiro; filtro que só vale para um tipo de título exclui o outro quando informado. Fim antes do início ou `tipoTitulo` fora do enum → 400.

**Limites e proteção.** Máximo de `MaxTitulosPorExportacao` títulos por pedido (400), `MaxJobsAtivosPorUsuario` pedidos ativos por usuário (429), `MaxBytesPorExportacao` de anexos e `EspacoLivreMinimoMb` de disco livre para iniciar. Um usuário só enxerga e baixa as próprias exportações (404 para as de outros). A identidade vem sempre do JWT. Mensagens de erro nunca expõem texto de exceção. O pacote usa só nomes sanitizados (sem `..`, `/`, `\`, `:`, nomes reservados do Windows), então a extração não pode gravar fora da pasta escolhida (proteção contra *zip-slip*).

**Configuração** (seção `Exportacao` do `appsettings.json`, classe `ExportacaoSettings`; sobrescrita por variável de ambiente `Exportacao__<Chave>`):

| Chave | Padrão | Descrição |
|-------|--------|-----------|
| `Pasta` | vazio (`<temp>/PagueAres/Exportacao`) | Pasta dos pacotes. Em produção e no Docker, defina sempre |
| `RetencaoHoras` | 48 | Horas de disponibilidade de um pacote concluído |
| `IntervaloLimpezaMinutos` | 30 | Intervalo da limpeza de pacotes vencidos |
| `MaxTitulosPorExportacao` | 5000 | Máximo de títulos por exportação |
| `MaxJobsAtivosPorUsuario` | 2 | Exportações Pendente + Processando por usuário |
| `MaxBytesPorExportacao` | 21474836480 (20 GiB) | Teto da soma dos anexos |
| `EspacoLivreMinimoMb` | 2048 | Espaço livre mínimo da unidade da pasta para iniciar |
| `ValidadeLinkDownloadSegundos` | 120 | Validade do token de download |
| `WorkerHabilitado` | `true` | Com `false`, worker e limpeza não são registrados (os pedidos ficam Pendente) |

No Docker, o `docker-compose.yml` monta o volume nomeado `pagueares-exportacao` em `/var/pagueares/exportacao` e define `Exportacao__Pasta` com esse caminho. No IIS, siga o guia [`docs/implantacao/iis-exportacao.md`](docs/implantacao/iis-exportacao.md) (pool com `AlwaysRunning`, sem *idle timeout*, **um único processo de trabalho**, pasta fora do site, timeouts de download).

**Tabela `ExportacaoJob`** (migration `AdicionaExportacaoJob`): `IdExportacaoJob` (PK), `IdUsuario` (FK para `Usuario`), `Status`, `FiltrosJson`, `DataCriacao`, `DataInicio`, `DataConclusao`, `DataExpiracao` (todas UTC), `TotalTitulos`, `TitulosProcessados`, `TotalAnexos`, `NomeArquivo` (só o nome, nunca caminho), `TamanhoBytes`, `MensagemErro` (só mensagem amigável), `CancelamentoSolicitado` e `Versao` (token de concorrência trocado a cada gravação). Índices `IX_ExportacaoJob_IdUsuario_DataCriacao` e `IX_ExportacaoJob_Status`.

#### Estrutura do pacote ZIP

```
LEIA-ME.txt          texto curto em pt-BR: extraia o ZIP antes de abrir o Excel (para os links funcionarem), o que é cada arquivo, data de geração e filtros usados
titulos.xlsx         planilha com as abas Solicitações, RDDVs, Despesas dos RDDVs e Anexos
indice.csv           índice dos anexos com tamanho e SHA-256
indice.json          o mesmo índice em JSON, com totais, títulos e filtros
SOL-000123/Nota Fiscal - boleto.pdf
RDDV-000045/Comprovante - recibo.jpg
```

- **Código do título**: `SOL-` + `IdSolicitacao` ou `RDDV-` + `IdRelatorio`, com no mínimo 6 dígitos (`SOL-000123`); ids acima de 999999 saem com todos os dígitos. Só títulos com ao menos um anexo ganham pasta.
- **Nome do anexo**: `<tipo de documento> - <nome original sanitizado>`; sem tipo ou tipo desconhecido, o rótulo é `Documento`; nome original vazio vira `anexo-<IdAnexo>`. Sanitização: remove caracteres de controle e `< > : " / \ | ? *`, troca `..` por `.`, tira espaços e pontos do fim, prefixa `_` em nomes reservados do Windows (`CON`, `PRN`, `AUX`, `NUL`, `COM1`–`COM9`, `LPT1`–`LPT9`) e limita o nome final a 120 caracteres preservando a extensão. Duplicados na mesma pasta (sem diferenciar caixa) ganham `(2)`, `(3)` e assim por diante.
- **Geração**: `System.IO.Compression.ZipArchive` sobre um `FileStream` em disco (Zip64 automático), `CompressionLevel.Fastest`. Ordem de escrita: anexos primeiro (tamanho e SHA-256 calculados durante o streaming, decodificando o base64 em blocos), depois `titulos.xlsx`, `indice.csv`, `indice.json` e `LEIA-ME.txt`. Um anexo com base64 inválido interrompe a exportação com mensagem amigável.
- **`titulos.xlsx`** (ClosedXML): linha 1 é o cabeçalho (negrito, congelado, com filtro automático). Datas são células `DateTime` (`dd/mm/aaaa`; datas e horas em `dd/mm/aaaa hh:mm`), valores são números (`#,##0.00`) e CPF, CNPJ, conta, agência e número de documento são sempre texto, para não perder zeros. Dado nulo = célula vazia; CPF, banco, agência e conta saem completos. A coluna Código das abas Solicitações e RDDVs e a coluna "Arquivo no pacote" da aba Anexos têm hiperlinks relativos para a pasta ou o arquivo no pacote.
  - **Solicitações**: uma linha por solicitação, com identificação, tipo, forma e tipo de pagamento, departamento, solicitante, atribuído, gestor, datas, número do documento, descrição, valor, parcelamento, centro de custo, projeto, PCA, dados do parceiro e bancários, observação, as três trilhas de aprovação (status, data e observação do gestor, do financeiro e da contabilidade) e a quantidade de anexos.
  - **RDDVs**: uma linha por relatório, com tipo de relatório, funcionário e CPF, finalidade, tipo e local da viagem, datas, moeda, diárias, valor do km, adiantamento, saldo do cartão VTM, total das despesas (view `vDespesasRddv`), rateio contábil, dados bancários, as três trilhas de aprovação e a quantidade de anexos.
  - **Despesas dos RDDVs**: código do RDDV, id e data da despesa, tipo, moeda, quantidade e valor.
  - **Anexos**: código e tipo do título, tipo de documento, nome original, arquivo no pacote, tamanho em bytes, SHA-256 e data de envio.
- **`indice.csv`**: UTF-8 com BOM, separador `;` (aspas duplas quando o campo contém `;`, `"` ou quebra de linha). Cabeçalho: `titulo;tipo_titulo;tipo_documento;nome_original;arquivo;caminho_relativo;tamanho_bytes;sha256` (`tipo_titulo` = `Solicitacao` ou `Rddv`; SHA-256 em hexadecimal minúsculo). Uma linha por anexo, ordenada por título e depois por arquivo.
- **`indice.json`**: UTF-8 sem BOM, camelCase, indentado: `versao` (1), `exportacaoId`, `geradoEm`, `filtros`, `totais` (`solicitacoes`, `rddvs`, `despesas`, `anexos`, `bytesAnexos`), `titulos` (todos os exportados, inclusive sem anexo, com `pasta: null`) e `arquivos` (plano, um item por anexo, na mesma ordem do CSV).

### 11.2 Excel — Lista de Pagamentos (legado)
- Endpoint: `GET /api/export/payments?sFilter=<json>&userId=<id>&key=<previewKey>`
- Biblioteca: **ClosedXML**
- Autenticação: preview key do tipo `Exportacao`, **de uso único**, que só é emitida para FIN e ADM. O `userId` da query ainda é aceito (o frontend o envia), mas é ignorado
- Conteúdo: lista filtrada de pagamentos em planilha (`Pagamentos.xlsx`; colunas Código, Tipo, Data de Inclusão, Data de Vencimento, Doc. Número, Solicitante, Parceiro, Valor, Status Gestor/Contábil/Financeiro), com datas e valores tipados, sem rascunhos nem cancelados. O escopo é sempre o de FIN/ADM: títulos aprovados pelo gestor
- No frontend, o botão "Exportar" aparece na visão financeira

### 11.3 ZIP — Anexos de Solicitação / RDDV (legado)
- Endpoints: `GET /api/export/request/attachment/{id}?key=` e `GET /api/export/rddv/attachment/{id}?key=`
- Biblioteca: **SharpZipLib 1.4.2**
- Conteúdo: todos os arquivos anexados à solicitação/relatório compactados em ZIP (`Anexos-Solicitacao-{id}.zip`, `Anexos-RDDV-{id}.zip`)
- Autenticação: preview key do mesmo tipo e do mesmo `entityId` da rota

---

## 12. API — Referência de Endpoints

**URLs em desenvolvimento:** `https://localhost:7219` e `http://localhost:5219` (`dotnet run`); `http://localhost:5000` na API em Docker. Documentação interativa (somente em `Development`): `/scalar/v1`; OpenAPI: `/openapi/v1.json`.

Todos os endpoints exigem o header abaixo, exceto `POST /api/auth`, as rotas marcadas com **chave** (que usam `?key=` — preview key) e `GET /api/exportacao/download` (que usa `?token=`, um token de uso único):
```
Authorization: Bearer <token>
```

| Controller | Método | Rota | Descrição |
|-----------|--------|------|-----------|
| Auth | POST | `/api/auth` | Login |
| Request | GET | `/api/request/{id}` | Detalhe da solicitação |
| Request | POST | `/api/request` | Criar/Atualizar solicitação |
| Request | GET | `/api/request/dashboard` | Dashboard consolidado (Solicitações + RDDVs) |
| Request | GET | `/api/request/{id}/user` | Atribuir responsável |
| Rddv | GET | `/api/rddv/{id}` | Detalhe do relatório |
| Rddv | POST | `/api/rddv` | Criar/Atualizar relatório |
| Rddv | GET | `/api/rddv/dashboard` | Dashboard somente de RDDVs |
| Rddv | GET | `/api/rddv/{id}/user` | Atribuir responsável |
| Listing | GET | `/api/listing` | Listagem unificada |
| Users | GET | `/api/users` | Lista usuários (ADM) |
| Users | GET | `/api/users/{id}` | Detalhe do usuário (próprio ou ADM) |
| Users | POST | `/api/users` | Criar/Atualizar usuário (ADM) |
| Users | GET | `/api/users/directory` | Busca no AD |
| Users | GET | `/api/users/list` | Usuários por papel |
| Export | GET | `/api/export/payments` | Exportar Excel (**chave** `Exportacao`, uso único, FIN/ADM) |
| Export | GET | `/api/export/rddv/attachment/{id}` | ZIP de anexos RDDV (**chave**) |
| Export | GET | `/api/export/request/attachment/{id}` | ZIP de anexos solicitação (**chave**) |
| Exportacao | POST | `/api/exportacao` | Criar exportação de títulos com anexos (FIN/ADM) |
| Exportacao | GET | `/api/exportacao` | Exportações do próprio usuário (30 dias) |
| Exportacao | GET | `/api/exportacao/{id}` | Status e progresso da exportação |
| Exportacao | POST | `/api/exportacao/{id}/link-download` | Gerar link de download de uso único |
| Exportacao | GET | `/api/exportacao/download` | Baixar o pacote ZIP (**token** `?token=`, anônimo) |
| Exportacao | DELETE | `/api/exportacao/{id}` | Cancelar ou apagar a exportação |
| Config | GET | `/api/config` | Ler configurações |
| Config | POST | `/api/config` | Salvar configurações (ADM) |
| Provider | GET | `/api/provider` | Lista fornecedores |
| Provider | GET | `/api/provider/{id}` | Fornecedor por ID |
| Provider | GET | `/api/provider/find/{numDoc}` | Fornecedor por documento |
| TravelType | GET | `/api/traveltype` | Lista tipos de viagem |
| TravelType | GET | `/api/traveltype/{id}` | Tipo por ID |
| TravelType | POST | `/api/traveltype` | Criar/Atualizar tipo |
| Department | GET | `/api/department` | Lista departamentos |
| Preview | POST | `/api/preview/generate-preview-key` | Gerar preview key |
| Preview | GET | `/api/preview/payment/{id}/{attachmentId}` | Anexo de solicitação (**chave**) |
| Preview | GET | `/api/preview/rddv/{id}/{attachmentId}` | Anexo de RDDV (**chave**) |

---

## 13. Configuração e Execução

### 13.1 Backend — configuração

**Connection string.** Lida de `ConnectionStrings:ConexaoPadrao` (`Program.cs`). O `appsettings.json` traz o valor vazio; em `Development`, `appsettings.Development.json` aponta para `localhost,14330`; no Docker é injetada por `ConnectionStrings__ConexaoPadrao` (apontando para `db,1433`). Não há connection string embutida no fluxo de execução (apenas um fallback de *design-time* para `dotnet ef`, via a variável `PAGUEARES_CONNECTION_STRING`).

**Seções de configuração:**
```json
{
  "ConnectionStrings": { "ConexaoPadrao": "<connection string>" },
  "JwtSettings": {
    "Secret": "<definir por variável de ambiente>",
    "Issuer": "pagueares.local",
    "Audience": "pagueares.local",
    "ExpiryMinutes": 720
  },
  "ActiveDirectory": {
    "EnableDomainAuthentication": false,
    "Servers": [
      { "Host": "<servidor>", "Domain": "<domínio>", "Username": "<conta>", "Password": "<senha>" }
    ]
  },
  "Smtp": {
    "EnableEmailService": false,
    "SmtpHost": "<servidor SMTP>",
    "SmtpPort": 587,
    "SmtpUser": "<usuário>",
    "SmtpPassword": "<senha>",
    "EnableSsl": "true",
    "DefaultSender": "<remetente>"
  },
  "RunMigrations": "false",
  "AllowedOrigins": "",
  "Exportacao": {
    "Pasta": "",
    "RetencaoHoras": 48,
    "IntervaloLimpezaMinutos": 30,
    "MaxTitulosPorExportacao": 5000,
    "MaxJobsAtivosPorUsuario": 2,
    "MaxBytesPorExportacao": 21474836480,
    "EspacoLivreMinimoMb": 2048,
    "ValidadeLinkDownloadSegundos": 120,
    "WorkerHabilitado": true
  }
}
```

A seção `Exportacao` configura a exportação de títulos com anexos (ver 11.1). Em produção defina `Exportacao:Pasta` (variável `Exportacao__Pasta`) para uma pasta fora do site; a implantação no IIS está em [`docs/implantacao/iis-exportacao.md`](docs/implantacao/iis-exportacao.md).

**CORS.** `AllowedOrigins` (lista separada por vírgula). Vazio = qualquer origem; preenchido = restrito às origens listadas. Atrás do Nginx do compose (proxy `/api/`) não há chamadas cross-origin.

**HTTPS.** `UseHttpsRedirection()` fica ativo, exceto quando `ASPNETCORE_ENVIRONMENT=Docker`.

### 13.2 Execução com Docker

```bash
cp .env.example .env     # defina SA_PASSWORD e JWT_SECRET
docker compose up --build
```

- `docker compose up` carrega automaticamente `docker-compose.override.yml`: a API roda com `ASPNETCORE_ENVIRONMENT=Development` (OpenAPI/Scalar disponíveis) e o SQL Server é publicado em `${DB_PORT:-14330}` do host (útil para SSMS/Azure Data Studio).
- Endereços: frontend em `http://localhost:${FRONTEND_PORT:-80}`, API em `http://localhost:${API_PORT:-5000}` (Scalar em `http://localhost:5000/scalar/v1`).
- A API aplica as migrations no startup (`ASPNETCORE_RUN_MIGRATIONS=true`) e aguarda o SQL Server (`entrypoint.sh`).
- `FRONTEND_API_BASE_URL` vazio = frontend chama `/api/*` relativo (proxy Nginx). Defina uma URL absoluta se a API estiver em outro domínio.
- Para produção, use apenas o arquivo base: `docker compose -f docker-compose.yml up -d` (ambiente `Production`; o SQL fica acessível só na rede interna `pagueares-net`, sem porta publicada no host).
- O volume nomeado `pagueares-exportacao` (montado em `/var/pagueares/exportacao`, com `Exportacao__Pasta` apontando para ele) guarda os pacotes da exportação de títulos e sobrevive à recriação do contêiner da API; a imagem da API roda como root, então a pasta é gravável. O Nginx do contêiner `frontend` repassa `/api/` para a API com `proxy_read_timeout` de 300 s (intervalo entre leituras, não a duração total do download).
- Variáveis em `.env.example`: `SA_PASSWORD`, `MSSQL_PID`, `ASPNETCORE_ENVIRONMENT`, `JWT_SECRET`, `ASPNETCORE_RUN_MIGRATIONS`, `FRONTEND_API_BASE_URL`, `DB_PORT`, `API_PORT`, `FRONTEND_PORT`.

### 13.3 Execução local sem Docker para a aplicação (SQL Server em Docker)

Banco: sobe só o SQL Server do compose (porta 14330 do host, definida pelo override):
```bash
docker compose up -d db
```

Backend (a partir de `back-end/`; usa `appsettings.Development.json`, `localhost,14330` e `RunMigrations=true`):
```bash
dotnet restore Ares.PagueAres.sln
dotnet run --project Ares.PagueAres.API      # https://localhost:7219 | http://localhost:5219
```
A senha do `sa` na connection string de `appsettings.Development.json` precisa coincidir com a `SA_PASSWORD` do `.env` usada pelo contêiner `db`. É possível usar outro SQL Server sobrescrevendo `ConnectionStrings__ConexaoPadrao`. Os perfis do `launchSettings.json` abrem o navegador em `/scalar/v1`.

Frontend (a partir de `front-end/`):
```bash
npm ci
npx ng serve                                  # http://localhost:4200  (rotas com hash: /#/login)
```
O frontend lê a URL da API de `src/assets/config.json` (`BaseUrl`, padrão `https://localhost:7219`). Use sempre `npx ng` (CLI local v20), nunca um `ng` global. O backend usa HTTPS local; confie no certificado de desenvolvimento (`dotnet dev-certs https --trust`) ou aponte o `BaseUrl` para `http://localhost:5219`.

### 13.4 Build e publicação

```bash
# Backend
dotnet publish back-end/Ares.PagueAres.API/Ares.PagueAres.API.csproj -c Release -o ./publish

# Frontend (saída em front-end/dist/browser)
cd front-end && npx ng build --configuration production
```
No Docker, os `Dockerfile` de cada projeto fazem esses builds (API: imagem `dotnet/aspnet:10.0` escutando em `8080`; frontend: build em `node:20-alpine` e servido por Nginx).

---

## 14. Testes

Resultados e contagens atuais ficam em `RELATORIO_TESTES.md`.

### Backend (xUnit) — a partir de `back-end/`
```bash
dotnet test Ares.PagueAres.sln
dotnet test Ares.PagueAres.Tests.Unit
dotnet test Ares.PagueAres.Tests.Integration
```
- **`Ares.PagueAres.Tests.Unit`**: `AuthenticationServiceTests` (BCrypt, SHA256 legado, migração para BCrypt, geração de token), `AuthControllerUnitTests` (lockout), `ListingControllerFilterTests` (filtros de visão), `PreviewTokenValidationTests`.
- **`Ares.PagueAres.Tests.Integration`**: sobem a API em memória com `WebApplicationFactory<Program>` (`PagueAresWebAppFactory`), EF Core InMemory e JWTs de teste (ambiente `Test`, sem SQL Server nem Docker): `ApprovalWorkflowTests`, `AuthControllerTests`, `DataSegregationTests`, `DepartmentControllerTests`, `EndpointExposureTests`, `SecurityTests` (autenticação, escalada de privilégios, controle de acesso por papel).

### Frontend — a partir de `front-end/`
```bash
npm ci
npx ng test --watch=false --browsers=ChromeHeadless   # Karma + Jasmine, execução única
npm run e2e                                           # Cypress (headless)
npm run e2e:open                                      # Cypress interativo
```
- **Unitários**: specs `*.spec.ts` de componentes (app, footer, icons, home, login, payments, rddv, refunds, create-request, create-rddv, create-user) e serviços (`AuthService`, `LocalStorageService`, `UtilsService`).
- **E2E (Cypress 13)**: specs em `cypress/e2e/{auth,payments,rddv,security,workflow}`; `baseUrl` `http://localhost:4200`. Exigem frontend e API em execução e usuários de teste existentes no banco.

---

## 15. Limitações Conhecidas

Pontos observados no código atual (documentados para planejamento, sem alteração de comportamento):

1. **RDDV sem checagens de papel/propriedade na gravação**: `POST /api/rddv` não verifica se o usuário é dono do registro nem se possui o papel exigido para alterar `AprovadoGestor`, `AprovadoSetor` ou `StatusContabilidade` (a Solicitação tem essas regras; o RDDV só verifica propriedade em `GET /api/rddv/{id}`).
2. **Atribuição sem verificação de papel**: `GET /api/request/{id}/user` e `GET /api/rddv/{id}/user` aceitam qualquer usuário autenticado.
3. **Rotas sem restrição de papel**: `POST /api/traveltype`, `GET /api/users/list`, `GET /api/users/directory`, `ProviderController` e `GET /api/config` exigem apenas autenticação.
4. **JWT de exemplo versionado**: o `appsettings.json` contém um `JwtSettings:Secret` de exemplo (também usado pelos testes); sobrescreva por variável de ambiente.
5. **E-mail**: a opção `Smtp:EnableEmailService` não tem efeito no código atual.
6. **Estado em memória**: lockout de login e preview keys ficam no `IMemoryCache` do processo; a fila da exportação, o cancelamento em andamento e os tokens de download também ficam em memória e os pacotes em disco local. Nada disso é compartilhado entre instâncias: a API deve rodar em **um único processo** (no IIS, `Maximum Worker Processes = 1`).
7. **Arquivos em base64** no banco: avaliar armazenamento externo (blob storage) para escala.

---

*Documentação atualizada em 23/11/2026 — baseada na branch `main` (commit `4a0c32a`)*
