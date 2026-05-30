# PagueAres — Documentação Técnica

> Sistema de Gestão de Reembolsos e Despesas — Ares

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
13. [Configuração e Deploy](#13-configuração-e-deploy)

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
| Frontend | Angular | 12.1.2 |
| Frontend UI | Angular Material + Bootstrap | 12.1.2 / 4.5.2 |
| Frontend Linguagem | TypeScript | ~4.x |
| Backend Framework | ASP.NET Core | 8.0 |
| Backend Linguagem | C# | 11+ |
| ORM | Entity Framework Core | 8.0 |
| Banco de Dados | SQL Server | 2019+ |
| Autenticação | JWT (HS256) + Active Directory (LDAP) | — |
| E-mail | SMTP | — |
| Compressão | SharpZipLib | 1.4.2 |
| Excel | FreeSpire.XLS | 14.2.0 |
| Serialização | Newtonsoft.Json | 13.0.4 |
| Documentação API | Swagger / OpenAPI | 9.0.6 |

---

## 3. Arquitetura do Sistema

```
┌─────────────────────────────────────────────────────────────┐
│                      USUÁRIO (Navegador)                    │
└──────────────────────────┬──────────────────────────────────┘
                           │ HTTPS
┌──────────────────────────▼──────────────────────────────────┐
│                FRONTEND — Angular 12                        │
│  ┌──────────┐  ┌──────────┐  ┌──────────┐  ┌──────────┐   │
│  │  Login   │  │Dashboard │  │Pagamentos│  │  RDDV    │   │
│  └──────────┘  └──────────┘  └──────────┘  └──────────┘   │
│  ┌─────────────────────────────────────────────────────┐   │
│  │  Services (HTTP) + AuthInterceptor (JWT Bearer)     │   │
│  └─────────────────────────────────────────────────────┘   │
└──────────────────────────┬──────────────────────────────────┘
                           │ REST JSON
┌──────────────────────────▼──────────────────────────────────┐
│                BACKEND — ASP.NET Core 8                     │
│                                                             │
│  ┌─────────────────── API Layer ──────────────────────┐    │
│  │  Controllers  │  JwtMiddleware  │  [Authorize]     │    │
│  └───────────────────────┬────────────────────────────┘    │
│                          │                                  │
│  ┌─────────────── Application Layer ──────────────────┐    │
│  │  AuthenticationService │ EmailService │ ADService  │    │
│  └───────────────────────┬────────────────────────────┘    │
│                          │                                  │
│  ┌─────────────── Infrastructure Layer ───────────────┐    │
│  │  PagueAresContext (EF Core) │ SQL Server            │    │
│  └───────────────────────┬────────────────────────────┘    │
│                          │                                  │
│  ┌────────────── Domain Layer (DTOs/Models) ───────────┐   │
│  │  Entidades │ DTOs │ Enums │ GenericResponse<T>      │   │
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

O backend adota **Clean Architecture** em 4 camadas:

- **API**: Ponto de entrada HTTP, roteamento, middleware de autenticação
- **Application**: Lógica de negócio, serviços de autenticação e e-mail
- **Infrastructure**: Acesso a dados (EF Core), integração com AD
- **Domain**: Contratos compartilhados — modelos, DTOs, enums, resposta genérica

---

## 4. Estrutura de Pastas

### Backend (`back-end/`)

```
back-end/
├── Ares.PagueAres.sln
│
├── Ares.PagueAres.API/
│   ├── Program.cs                  # Composição DI, JWT, CORS, Swagger
│   ├── appsettings.json            # JWT, SMTP, Active Directory
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
│   │   ├── PreviewController.cs
│   │   └── PaymentController.cs
│   ├── Middlewares/
│   │   └── Authentication/
│   │       └── JwtMiddleware.cs
│   └── Dtos/
│       ├── ListFilterDto.cs
│       └── PaymentListItem.cs
│
├── Ares.PagueAres.Application/
│   ├── Authentication/
│   │   ├── IAuthenticationService.cs
│   │   └── AuthenticationService.cs
│   ├── Email/
│   │   └── EmailService.cs
│   ├── ActiveDirectory/
│   │   └── ActiveDirectoryService.cs
│   ├── Settings/
│   │   ├── JwtSettings.cs
│   │   ├── ActiveDirectorySettings.cs
│   │   ├── SmtpSettings.cs
│   │   └── DomainServer.cs
│   └── Exceptions/
│       └── LoginFailedException.cs
│
├── Ares.PagueAres.Infrastructure/
│   └── Context/
│       └── PagueAresContext.cs      # DbContext com 16 DbSets
│
└── Ares.PagueAres.Domain/
    ├── Models/
    │   ├── Funcoes.cs               # Constantes de perfis
    │   └── GenericResponse.cs       # Wrapper de resposta API
    ├── Dtos/                        # Todos os DTOs de transferência
    └── Enums/
        ├── EnumRequestType.cs
        └── EnumViewListagemSolicitacoes.cs
```

### Frontend (`front-end/`)

```
front-end/
├── src/
│   ├── app/
│   │   ├── app.module.ts            # Módulo raiz
│   │   ├── app.routing.ts           # Rotas raiz (lazy loading)
│   │   │
│   │   ├── layouts/admin-layout/    # Layout principal autenticado
│   │   │
│   │   ├── core/
│   │   │   ├── interceptors/        # AuthInterceptor (Bearer token)
│   │   │   └── intl/                # Localização PT-BR (paginador)
│   │   │
│   │   ├── login/                   # Página de login
│   │   ├── home/                    # Dashboard
│   │   ├── payments/                # Lista de Solicitações
│   │   ├── rddv/                    # Lista de RDDV
│   │   ├── refunds/                 # Reembolsos
│   │   ├── create-request/          # Formulário de Solicitação
│   │   ├── create-rddv/             # Formulário de RDDV
│   │   ├── create-user/             # Gestão de Usuários
│   │   ├── payment-report/          # Relatório imprimível — Solicitação
│   │   ├── rddv-report/             # Relatório imprimível — RDDV
│   │   ├── configuration/           # Configurações do sistema
│   │   │   └── travel-type-list/    # Sub-componente: Tipos de Viagem
│   │   │
│   │   ├── services/                # Todos os serviços HTTP
│   │   ├── models/                  # Interfaces TypeScript
│   │   └── utils/                   # Utilitários de data
│   │
│   ├── environments/
│   │   ├── environment.ts           # Dev: baseUrl = localhost:7219
│   │   └── environment.prod.ts
│   │
│   └── assets/
│       ├── config.json              # Config carregada no boot
│       ├── css/ scss/ fonts/ img/
```

---

## 5. Backend — Camadas e Responsabilidades

### 5.1 Controllers

#### AuthController — `/api/auth`
| Método | Rota | Descrição |
|--------|------|-----------|
| POST | `/api/auth` | Autenticação (login). Retorna JWT token. |

Suporta autenticação local (banco de dados) e via Active Directory (LDAP). Valida se o usuário está ativo antes de emitir o token.

---

#### RequestController — `/api/request`
| Método | Rota | Descrição |
|--------|------|-----------|
| GET | `/api/request` | Lista solicitações com filtros e paginação |
| GET | `/api/request/{id}` | Retorna solicitação completa com documentos |
| POST | `/api/request` | Cria ou atualiza solicitação (suporta rascunho) |
| GET | `/api/request/dashboard` | Estatísticas: pendentes, aprovadas, reprovadas |
| GET | `/api/request/{id}/user` | Atribui solicitação a um usuário |

**Funcionalidades:** rascunho, upload de anexos, workflow de aprovação com transação de banco, notificações por e-mail.

---

#### RddvController — `/api/rddv`
| Método | Rota | Descrição |
|--------|------|-----------|
| GET | `/api/rddv` | Lista relatórios de viagem com filtros |
| GET | `/api/rddv/{id}` | Retorna relatório com despesas e documentos |
| POST | `/api/rddv` | Cria ou atualiza relatório |
| GET | `/api/rddv/dashboard` | Estatísticas do RDDV |
| GET | `/api/rddv/{id}/user` | Atribui relatório a um usuário |

---

#### ListingController — `/api/listing`
| Método | Rota | Descrição |
|--------|------|-----------|
| GET | `/api/listing` | Listagem unificada com modos de visualização |

**Modos de Visualização (ViewMode):**
- `MyView` — Solicitações do próprio usuário
- `ManagerView` — Solicitações sob responsabilidade do gestor
- `FinancialView` — Solicitações aprovadas pelo gestor (visão financeira)
- `Dashboard` — Visão consolidada para administradores

Suporta filtros avançados (15+ parâmetros), ordenação dinâmica e paginação.

---

#### UsersController — `/api/users`
| Método | Rota | Descrição |
|--------|------|-----------|
| GET | `/api/users` | Lista todos os usuários |
| GET | `/api/users/{id}` | Retorna usuário com papéis e funções |
| POST | `/api/users` | Cria ou atualiza usuário |
| GET | `/api/users/directory` | Busca usuário no Active Directory |
| GET | `/api/users/list` | Lista usuários filtrados por papel |

**Funcionalidades:** hash SHA256 de senha, cadastro duplo (local + AD), atribuição de papéis.

---

#### ExportController — `/api/export`
| Método | Rota | Descrição |
|--------|------|-----------|
| GET | `/api/export/payments` | Exporta lista de pagamentos em Excel |
| GET | `/api/export/rddv/attachment/{id}` | Exporta anexos de RDDV como ZIP |
| GET | `/api/export/request/attachment/{id}` | Exporta anexos de solicitação como ZIP |

---

#### ConfigController — `/api/config`
| Método | Rota | Descrição |
|--------|------|-----------|
| GET | `/api/config` | Retorna todos os parâmetros de configuração |
| POST | `/api/config` | Define ou atualiza parâmetros |

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

### 5.2 Serviços de Aplicação

#### AuthenticationService
- `TryAuthenticate(username, password)` — autenticação local (SHA256) ou LDAP
- `GenerateAuthenticationToken(userData)` — gera JWT com claims: Subject, NameId, Roles, JTI
- `GetUserById(id)` — retorna usuário com papéis carregados

#### EmailService
Envia e-mails assíncronos via SMTP para os seguintes eventos:
| Método | Evento |
|--------|--------|
| SendNewRequestEmail | Nova solicitação/relatório criado |
| SendApprovalEmail | Aprovação pelo gestor |
| SendDenialEmail | Reprovação pelo gestor |
| SendPaidEmail | Pagamento autorizado pelo financeiro |
| SendPaymentDeniedEmail | Pagamento negado pelo financeiro |
| SendAccountingApprovedEmail | Lançado pela contabilidade |
| SendAccountingDenialEmail | Reprovado pela contabilidade |
| SendRequestAssignedEmail | Solicitação atribuída a processador |

#### ActiveDirectoryService
- `GetUserInformation()` — retorna SID, Login, Nome, E-mail do AD
- `TryAuthenticate()` — validação via bind LDAP
- Novos usuários do AD são criados automaticamente no banco na primeira autenticação

---

### 5.3 Middleware

**JwtMiddleware** (`Middlewares/Authentication/JwtMiddleware.cs`)

Executado antes de cada requisição:
1. Extrai o token do header `Authorization: Bearer <token>`
2. Valida assinatura, issuer, audience e expiração
3. Extrai claims: `nameid` (userId), `sub` (userName), `Roles` (aliases separados por vírgula)
4. Injeta `UsuarioDto` em `HttpContext.Items["User"]`
5. Em caso de falha de validação, a requisição continua mas sem usuário autenticado (o `[Authorize]` retorna 401)

---

### 5.4 Resposta Genérica da API

Todas as respostas seguem o contrato `GenericResponse<T>`:

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

---

## 6. Frontend — Módulos e Componentes

### 6.1 Roteamento

```
/login                    → LoginComponent (público)
/payment-report/:id       → PaymentReportComponent (acesso via preview token)
/rddv-report/:id          → RddvReportComponent (acesso via preview token)
/                         → AdminLayoutComponent (autenticado)
  /dashboard              → HomeComponent
  /payments               → PaymentsComponent
  /rddv                   → RddvComponent
  /refunds                → RefundsComponent
  /create-request         → CreateRequestComponent
  /create-rddv            → CreateRddvComponent
  /users                  → CreateUserComponent
  /config                 → ConfigurationComponent
  /icons                  → IconsComponent
```

O `AdminLayoutModule` é carregado via **lazy loading** para otimizar o tempo de inicialização.

---

### 6.2 Componentes Principais

#### LoginComponent
- Formulário de usuário e senha
- Chama `AuthService.login()`, armazena token, redireciona para `/dashboard`

#### HomeComponent (Dashboard)
- Exibe cards de estatísticas: Pendentes, Aprovados, Reprovados
- Dados separados para Solicitações e RDDVs
- Acesso rápido às listagens

#### PaymentsComponent
- Lista de Solicitações com colunas: Código, Data, Fornecedor, Valor, Status (Gestor/Financeiro/Contabilidade), Solicitante, Atribuído
- Filtros: período, número de documento, parceiro, status, modo de visualização
- Ações: Visualizar, Editar, Exportar, Atribuir

#### RddvComponent
- Lista de Relatórios de Viagem
- Colunas: Código, Data, Funcionário, Destino, Valor, Status, Atribuído
- Mesmas funcionalidades de filtros e ações de `PaymentsComponent`

#### CreateRequestComponent
- Formulário completo de criação/edição de Solicitação
- Campos: tipo (adiantamento/pagamento), data, fornecedor, valor, documentos, datas, banco, tipo de autorização
- Upload de múltiplos arquivos
- Botões de aprovação por estágio (Gestor, Financeiro, Contabilidade)
- Suporte a modo rascunho

#### CreateRddvComponent
- Formulário de Relatório de Despesas com Viagem
- Campos: funcionário, CPF, tipo de viagem, datas, destino, finalidade, tabela de despesas
- Linhas de despesa: data, tipo, moeda, valor, quantidade
- Upload de documentos + workflow de aprovação

#### CreateUserComponent
- Cadastro e edição de usuários
- Busca no Active Directory
- Atribuição de papéis via checkboxes
- Gestão de senha (somente para usuários locais)

#### ConfigurationComponent
- Gerenciamento de parâmetros do sistema
- Interface em abas
- Sub-componente `TravelTypeListComponent` para gerenciar tipos de viagem e valores de diária

#### PaymentReportComponent / RddvReportComponent
- Relatórios de impressão/visualização
- Acesso público via **preview token** temporário
- Exibe informações completas com status de aprovações

---

### 6.3 Serviços

| Serviço | Responsabilidade |
|---------|-----------------|
| `AuthService` | Login, armazenamento e validação de token JWT |
| `PaymentService` | CRUD de Solicitações, listagem, dashboard, atribuição |
| `RddvService` | CRUD de RDDVs, listagem, dashboard, atribuição |
| `UsersService` | Gestão de usuários, busca no AD |
| `ProviderService` | Consulta de fornecedores por ID ou CPF/CNPJ |
| `TravelTypeService` | CRUD de tipos de viagem |
| `ExportService` | Exportação Excel e ZIP de anexos |
| `PreviewTokenService` | Geração de tokens temporários para links públicos |
| `ConfigService` | Leitura e escrita de configurações do sistema |
| `SystemService` | Inicialização da aplicação (`APP_INITIALIZER`) — carrega `assets/config.json` |
| `LocalStorageService` | Wrapper para operações no `localStorage` |
| `BaseUrlService` | Provê URL base da API a partir do `environment` |
| `UtilsService` | Funções utilitárias gerais |

---

### 6.4 Interceptor HTTP

**ApiAuthenticationInterceptor** (`core/interceptors/ApiAuthenticationInterceptor.ts`)

Adiciona automaticamente o token JWT em todas as requisições HTTP:
```
Authorization: Bearer <token>
```

**Exclusões** (rotas sem o header):
- `POST /api/Auth` (login)
- `GET assets/config.json` (configuração de boot)

---

## 7. Banco de Dados

### 7.1 Entidades Principais

#### Usuario
| Campo | Tipo | Descrição |
|-------|------|-----------|
| IdUsuario | INT PK | Identificador |
| Email | varchar | E-mail único |
| Nome | varchar | Nome completo |
| Senha | varchar | Hash SHA256 |
| Codigo | varchar | Código interno |
| IdDepartamento | INT FK | Departamento |
| Ativo | bit | Usuário habilitado |
| IdExterno | varchar | SID do Active Directory |
| Origem | int | 0 = Local, 1 = AD |

#### Solicitacao
| Campo | Tipo | Descrição |
|-------|------|-----------|
| IdSolicitacao | INT PK | Identificador |
| IdUsuario | INT FK | Solicitante |
| IdGestor | INT FK | Gestor responsável |
| NumeroDocumento | varchar | Número do documento |
| DataSolicitacao | datetime | Data de criação |
| DataDocumento | datetime | Data do documento |
| DataVencimento | datetime | Data de vencimento |
| Valor | decimal | Valor solicitado |
| Descricao | varchar | Descrição |
| StatusGestor | int | 1=Pendente, 2=Aprovado, 3=Reprovado |
| StatusFinanceiro | int | 1=Pendente, 2=Aprovado, 3=Reprovado |
| StatusContabilidade | int | 1=Pendente, 2=Lançado, 3=Recusado |
| Rascunho | bit | Se é rascunho (não enviado) |
| Cancelado | bit | Se foi cancelado |
| TipoSolicitacao | int | 1=Adiantamento, 2=Pagamento, 3=Inventário |
| TipoAutorizacao | int | 1=Auto-aprovado por anexo |
| IdUsuarioAtribuido | INT FK | Responsável pelo processamento |
| NumDocParceiro | varchar | CPF/CNPJ do fornecedor |
| NomeParceiro | varchar | Nome do fornecedor |
| BancoParceiro | varchar | Banco do fornecedor |
| AgenciaParceiro | varchar | Agência |
| ContaParceiro | varchar | Conta |
| CentroCusto | varchar | Centro de custo contábil |
| Projeto | varchar | Projeto contábil |
| PCA | varchar | PCA contábil |

#### Rddv (Relatório de Despesas com Deslocamento e Viagem)
| Campo | Tipo | Descrição |
|-------|------|-----------|
| IdRelatorio | INT PK | Identificador |
| IdUsuario | INT FK | Funcionário |
| NomeFuncionario | varchar | Nome do funcionário |
| CPF | varchar | CPF |
| IdDepartamento | INT FK | Departamento |
| Finalidade | varchar | Objetivo da viagem |
| TipoRelatorio | int | 1=Adiantamento, 2=Reembolso, 3=Devolução |
| TipoViagem | INT FK | Tipo de viagem (define valor da diária) |
| DataInicio | datetime | Início da viagem |
| DataFim | datetime | Fim da viagem |
| LocalViagem | varchar | Destino |
| Diarias | int | Número de diárias |
| ValorDiaria | decimal | Valor por diária |
| StatusGestor | int | (mesmos valores de Solicitacao) |
| StatusFinanceiro | int | |
| StatusContabilidade | int | |
| Rascunho | bit | |
| Cancelado | bit | |
| CentroCusto / Projeto / PCA | varchar | Dados contábeis |
| Banco / Agencia / Conta | varchar | Dados bancários |
| Moeda | varchar | Moeda utilizada |

#### DespesaRddv
| Campo | Tipo | Descrição |
|-------|------|-----------|
| IdDespesaRddv | INT PK | Identificador |
| IdRelatorio | INT FK | Relatório pai |
| DataDespesa | datetime | Data da despesa |
| TipoDespesa | varchar | Categoria da despesa |
| Moeda | varchar | Moeda |
| Valor | decimal | Valor unitário |
| Quantidade | decimal | Quantidade |

#### DocumentoSolicitacao / DocumentoRddv
| Campo | Tipo | Descrição |
|-------|------|-----------|
| Id | INT PK | |
| IdSolicitacao / IdRelatorio | INT FK | Vínculo com entidade pai |
| IdTipoDocumento | INT FK | Tipo do documento |
| NomeArquivo | varchar | Nome original do arquivo |
| CaminhoArquivo | varchar | Caminho no servidor |
| Arquivo | varchar | Conteúdo em base64 |
| DataCriacao | datetime | |

---

### 7.2 Tabelas de Suporte

| Tabela | Descrição |
|--------|-----------|
| Funcao | Papéis do sistema: ADM, GES, FIN, CON, USR |
| UsuarioFuncao | Relacionamento N:N usuário ↔ papel |
| Departamento | Departamentos com código de lançamento |
| TipoDocumento | Tipos de documentos aceitos |
| TipoViagem | Tipos de viagem com valor de diária |
| Fornecedor | Cadastro de fornecedores (CPF/CNPJ, nome, código) |
| Configuracao | Parâmetros do sistema (chave-valor) |

---

### 7.3 Views

| View | Descrição |
|------|-----------|
| vDespesasRddv | Total de despesas por relatório (agregação) |
| ViewListagemSolicitacoes | View unificada para listagem |
| vListagemSolicitacoes | View base para listagem |

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
  │ FK: IdGestor ──────► Usuario
  │ FK: IdUsuarioAtribuido ─► Usuario

Usuario
  │ 1:N
  ▼
Rddv
  │ 1:N
  ├─► DespesaRddv
  ├─► DocumentoRddv
  │
  │ FK: TipoViagem ──────► TipoViagem
  │ FK: IdGestor ─────────► Usuario

DocumentoSolicitacao / DocumentoRddv
  │ FK: IdTipoDocumento ─► TipoDocumento

Solicitacao
  │ FK: CodigoParceiro ──► Fornecedor

Usuario
  │ FK: IdDepartamento ──► Departamento
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
       │ E-mail → Gestor
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
  E-mail → Usuário    E-mail → Usuário
  [Pgt. Autorizado]   [Pgt. Negado - ENCERRADO]
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

**Exceção — Tipo de Autorização Automática:**
Se `TipoAutorizacao = 1` e o documento de autorização for anexado, o estágio do Gestor é pulado automaticamente.

### 8.2 RDDV (Relatório de Viagem)

Fluxo idêntico ao da Solicitação, com os mesmos três estágios de aprovação.

**Tipos de Relatório:**
| Código | Tipo | Descrição |
|--------|------|-----------|
| 1 | Adiantamento | Solicitação de dinheiro antes da viagem |
| 2 | Reembolso | Prestação de contas após a viagem |
| 3 | Devolução | Devolução de saldo não utilizado |

### 8.3 Valores de Status

| Código | Solicitação/Gestor/Financeiro | Contabilidade |
|--------|-------------------------------|---------------|
| 1 | Pendente (Aguardando) | Pendente |
| 2 | Aprovado / Autorizado | Lançado |
| 3 | Reprovado / Negado | Recusado |

### 8.4 Atribuição de Responsáveis

Qualquer solicitação pode ser atribuída a um processador específico (`IdUsuarioAtribuido`). O sistema registra data da atribuição (`DataAtribuicao`) e quem atribuiu (`IdUsuarioAtribuidor`). O atribuído recebe notificação por e-mail.

---

## 9. Segurança e Autenticação

### 9.1 Autenticação JWT

**Configuração:**
```json
{
  "JwtSettings": {
    "Secret": "45AE44EA-8470-44C2-9A61-8283410570A6",
    "Issuer": "pagueares.local",
    "Audience": "pagueares.local",
    "ExpiryMinutes": 720
  }
}
```

- Algoritmo: **HS256** (HMAC SHA-256)
- Expiração: **12 horas**
- Claims no token: `sub` (nome), `nameid` (ID), `Roles` (aliases separados por vírgula), `jti` (UUID único)

### 9.2 Autenticação Dupla

| Tipo | Mecanismo | Identificação no banco |
|------|-----------|----------------------|
| Local | SHA256 da senha comparado com `Senha` | `Origem = 0` |
| Active Directory | LDAP bind + verificação de credenciais | `Origem = 1`, `IdExterno = SID` |

Usuários AD são criados automaticamente no primeiro login com dados do diretório.

### 9.3 Papéis (Roles)

| Alias | Nome | Acesso |
|-------|------|--------|
| ADM | Administrador | Acesso total ao sistema |
| GES | Gestor | Aprova solicitações de sua equipe |
| FIN | Financeiro | Aprova pagamentos e gestão financeira |
| CON | Contabilidade | Lança e finaliza solicitações |
| USR | Usuário | Cria e acompanha solicitações próprias |

### 9.4 Controle de Acesso por Visualização

- **MyView**: Vê apenas as próprias solicitações
- **ManagerView**: Vê solicitações onde é o gestor atribuído
- **FinancialView**: Vê solicitações aprovadas pelo gestor (todas se FIN, somente atribuídas caso contrário)
- **Dashboard**: Visão consolidada (ADM)

### 9.5 Preview Token

Para acesso público a relatórios impressos (`/payment-report/:id`, `/rddv-report/:id`):
- Token temporário com expiração de 5 minutos (cache)
- Sem necessidade de autenticação completa
- Tipo validado: `Exportacao`, `Solicitacao`, `Rddv`

---

## 10. Notificações por E-mail

Configuração SMTP em `appsettings.json`:
```json
{
  "SmtpSettings": {
    "Host": "...",
    "Port": 587,
    "User": "...",
    "Password": "...",
    "EnableSSL": true,
    "DefaultSender": "noreply@ares.com.br"
  }
}
```

**Tabela de Eventos e Destinatários:**

| Evento | Destinatário(s) |
|--------|----------------|
| Nova solicitação criada | Solicitante + Gestor |
| Aprovação pelo gestor | Solicitante |
| Reprovação pelo gestor | Solicitante |
| Pagamento autorizado (financeiro) | Solicitante |
| Pagamento negado (financeiro) | Solicitante |
| Lançado pela contabilidade | Solicitante |
| Recusado pela contabilidade | Solicitante |
| Solicitação atribuída | Responsável atribuído |

Todos os métodos de envio são **assíncronos** (`async/await`).

---

## 11. Exportação de Dados

### Excel — Lista de Pagamentos
- Endpoint: `GET /api/export/payments`
- Biblioteca: **FreeSpire.XLS 14.2.0**
- Autenticação: via `previewKey` na query string
- Conteúdo: lista filtrada de pagamentos em formato planilha

### ZIP — Anexos de Solicitação / RDDV
- Endpoints: `GET /api/export/request/attachment/{id}` e `GET /api/export/rddv/attachment/{id}`
- Biblioteca: **SharpZipLib 1.4.2**
- Conteúdo: todos os arquivos anexados à solicitação/relatório compactados em ZIP
- Autenticação: via `previewKey`

---

## 12. API — Referência de Endpoints

**Base URL (dev):** `https://localhost:7219`

Todos os endpoints (exceto `/api/auth`) exigem o header:
```
Authorization: Bearer <token>
```

| Controller | Método | Rota | Descrição |
|-----------|--------|------|-----------|
| Auth | POST | `/api/auth` | Login |
| Request | GET | `/api/request` | Lista solicitações |
| Request | GET | `/api/request/{id}` | Detalhe da solicitação |
| Request | POST | `/api/request` | Criar/Atualizar solicitação |
| Request | GET | `/api/request/dashboard` | Dashboard de solicitações |
| Request | GET | `/api/request/{id}/user` | Atribuir responsável |
| Rddv | GET | `/api/rddv` | Lista relatórios RDDV |
| Rddv | GET | `/api/rddv/{id}` | Detalhe do relatório |
| Rddv | POST | `/api/rddv` | Criar/Atualizar relatório |
| Rddv | GET | `/api/rddv/dashboard` | Dashboard de RDDV |
| Rddv | GET | `/api/rddv/{id}/user` | Atribuir responsável |
| Listing | GET | `/api/listing` | Listagem unificada |
| Users | GET | `/api/users` | Lista usuários |
| Users | GET | `/api/users/{id}` | Detalhe do usuário |
| Users | POST | `/api/users` | Criar/Atualizar usuário |
| Users | GET | `/api/users/directory` | Busca no AD |
| Users | GET | `/api/users/list` | Usuários por papel |
| Export | GET | `/api/export/payments` | Exportar Excel |
| Export | GET | `/api/export/rddv/attachment/{id}` | ZIP de anexos RDDV |
| Export | GET | `/api/export/request/attachment/{id}` | ZIP de anexos solicitação |
| Config | GET | `/api/config` | Ler configurações |
| Config | POST | `/api/config` | Salvar configurações |
| Provider | GET | `/api/provider` | Lista fornecedores |
| Provider | GET | `/api/provider/{id}` | Fornecedor por ID |
| Provider | GET | `/api/provider/find/{numDoc}` | Fornecedor por documento |
| TravelType | GET | `/api/traveltype` | Lista tipos de viagem |
| TravelType | GET | `/api/traveltype/{id}` | Tipo por ID |
| TravelType | POST | `/api/traveltype` | Criar/Atualizar tipo |

---

## 13. Configuração e Deploy

### Backend

**1. String de Conexão** (atualmente hardcoded em `PagueAresContext.cs`):
```
Server=localhost;Database=PagueAres;UID=sa;PWD=@12344321@
```
> Recomendado: mover para `appsettings.json` com variável de ambiente em produção.

**2. appsettings.json — Variáveis de Ambiente:**
```json
{
  "JwtSettings": {
    "Secret": "<trocar em produção>",
    "Issuer": "pagueares.local",
    "Audience": "pagueares.local",
    "ExpiryMinutes": 720
  },
  "ActiveDirectorySettings": {
    "EnableDomainAuthentication": false,
    "DomainServers": []
  },
  "SmtpSettings": {
    "Host": "<servidor SMTP>",
    "Port": 587,
    "User": "<usuário>",
    "Password": "<senha>",
    "EnableSSL": true,
    "DefaultSender": "<remetente>"
  }
}
```

**3. CORS** — Atualmente configurado para permitir qualquer origem (`AllowAnyOrigin`). Restringir em produção.

**4. Build:**
```powershell
dotnet publish -c Release -r win-x64 --self-contained true
```

---

### Frontend

**1. Configurar URL da API** em `src/environments/environment.prod.ts`:
```typescript
export const environment = {
  production: true,
  baseUrl: 'https://api.pagueares.ares.com.br'
};
```

**2. Configurar `src/assets/config.json`** (carregado no boot via `SystemService`).

**3. Build:**
```bash
ng build --configuration production
```
O output estará em `dist/` para deploy em servidor web (Nginx, IIS, etc.).

---

*Documentação gerada em 30/05/2026 — PagueAres v1.x*
