# CLAUDE.md — PagueAres Backend

Sistema de gestão de reembolsos e despesas corporativas da Ares.
Este repositório contém o backend. O frontend Angular está em repositório separado.

## Visão Geral do Sistema

**PagueAres** centraliza dois fluxos de negócio:

| Módulo | Descrição |
|--------|-----------|
| **Solicitação** | Pedidos de pagamento a fornecedores ou adiantamentos internos |
| **RDDV** | Relatório de Despesas com Deslocamento e Viagem (prestação de contas de viagens) |

Ambos seguem um **fluxo de aprovação em três estágios**: Gestor → Financeiro → Contabilidade, com suporte a rascunhos, upload de anexos, atribuição de responsáveis e notificações por e-mail.

---

## Stack

| Camada | Tecnologia |
|--------|-----------|
| Framework | ASP.NET Core 8.0 |
| Linguagem | C# 11+ |
| ORM | Entity Framework Core 8.0 |
| Banco | SQL Server 2019+ |
| Autenticação | JWT HS256 + Active Directory (LDAP) |
| E-mail | SMTP |
| Excel | FreeSpire.XLS 14.2.0 |
| ZIP | SharpZipLib 1.4.2 |
| Docs API | Swagger / OpenAPI 9.0.6 |

---

## Arquitetura — Clean Architecture em 4 Projetos

```
Ares.PagueAres.sln
├── Ares.PagueAres.API          # Entrada HTTP: controllers, middleware JWT, roteamento
├── Ares.PagueAres.Application  # Lógica de negócio: auth, e-mail, Active Directory
├── Ares.PagueAres.Domain       # Contratos compartilhados: DTOs, enums, GenericResponse<T>
└── Ares.PagueAres.Infrastructure  # Acesso a dados: PagueAresContext (EF Core)
```

**Regra de dependência:** API → Application → Infrastructure → Domain (Domain não depende de ninguém).

---

## Como Executar

```bash
# Restaurar dependências
dotnet restore

# Executar em modo desenvolvimento
dotnet run --project Ares.PagueAres.API

# Build de produção
dotnet publish -c Release -r win-x64 --self-contained true
```

A API sobe em `https://localhost:7219` por padrão.
Swagger disponível em `https://localhost:7219/swagger`.

---

## Configuração (`appsettings.json`)

```json
{
  "JwtSettings": {
    "Secret": "<trocar em produção — usar variável de ambiente>",
    "Issuer": "pagueares.local",
    "Audience": "pagueares.local",
    "ExpiryMinutes": 720
  },
  "ActiveDirectorySettings": {
    "EnableDomainAuthentication": false,
    "DomainServers": []
  },
  "SmtpSettings": {
    "Host": "",
    "Port": 587,
    "User": "",
    "Password": "",
    "EnableSSL": true,
    "DefaultSender": ""
  }
}
```

> **Atenção:** a string de conexão está atualmente hardcoded em `Ares.PagueAres.Infrastructure/Context/PagueAresContext.cs`. Em produção, mover para `appsettings.json` e injetar via `IConfiguration`.

---

## Banco de Dados

Banco: `PagueAres` no SQL Server.
Script de criação: `PagueAres.sql` na raiz do repositório.

### Entidades Principais

| Tabela | Descrição |
|--------|-----------|
| `Usuario` | Usuários do sistema (local ou AD) |
| `Solicitacao` | Solicitações de pagamento |
| `Rddv` | Relatórios de despesas com viagem |
| `DespesaRddv` | Linhas de despesa do RDDV |
| `DocumentoSolicitacao` | Anexos de solicitações (base64) |
| `DocumentoRddv` | Anexos de relatórios (base64) |
| `Funcao` | Papéis: ADM, GES, FIN, CON, USR |
| `UsuarioFuncao` | Relacionamento N:N usuário ↔ papel |
| `Departamento` | Departamentos com código de lançamento |
| `TipoViagem` | Tipos de viagem com valor de diária |
| `Fornecedor` | Fornecedores (CPF/CNPJ) |
| `Configuracao` | Parâmetros do sistema (chave-valor) |

### Valores de Status (Solicitacao e Rddv)

| Código | StatusGestor / StatusFinanceiro | StatusContabilidade |
|--------|---------------------------------|---------------------|
| 1 | Pendente | Pendente |
| 2 | Aprovado / Autorizado | Lançado |
| 3 | Reprovado / Negado | Recusado |

---

## Controllers e Endpoints

Todos os endpoints exigem `Authorization: Bearer <token>` exceto `POST /api/auth`.

| Controller | Rota Base | Responsabilidade |
|-----------|-----------|-----------------|
| `AuthController` | `/api/auth` | Login, geração de JWT |
| `RequestController` | `/api/request` | CRUD de Solicitações + dashboard + atribuição |
| `RddvController` | `/api/rddv` | CRUD de RDDVs + dashboard + atribuição |
| `ListingController` | `/api/listing` | Listagem unificada com filtros e ViewModes |
| `UsersController` | `/api/users` | CRUD de usuários + busca no AD |
| `ExportController` | `/api/export` | Exportação Excel e ZIP de anexos |
| `ConfigController` | `/api/config` | Parâmetros do sistema |
| `ProviderController` | `/api/provider` | Consulta de fornecedores |
| `TravelTypeController` | `/api/traveltype` | CRUD de tipos de viagem |
| `PreviewController` | `/api/preview` | Tokens temporários para links públicos |

---

## Fluxo de Aprovação

```
Criação (Rascunho=true)
  └─► Envio (Rascunho=false) → e-mail para Gestor
        └─► Gestor: Aprovado (2) ou Reprovado (3)
              └─► Financeiro: Autorizado (2) ou Negado (3)
                    └─► Contabilidade: Lançado (2) ou Recusado (3)
```

- `TipoAutorizacao = 1`: pula o estágio do Gestor se o anexo de autorização for enviado.
- Cada transição dispara e-mail para o solicitante via `EmailService`.

---

## Segurança

### Autenticação
- **Local:** hash SHA256 da senha comparado com o campo `Senha` da tabela `Usuario`
- **Active Directory:** bind LDAP; usuários AD são criados automaticamente no primeiro login com `Origem = 1`

### JWT
- Algoritmo: HS256
- Expiração: 12 horas (`ExpiryMinutes: 720`)
- Claims: `sub` (nome), `nameid` (ID), `Roles` (aliases separados por vírgula), `jti`

### Papéis
| Alias | Papel | Acesso |
|-------|-------|--------|
| ADM | Administrador | Total |
| GES | Gestor | Aprova solicitações da equipe |
| FIN | Financeiro | Autoriza pagamentos |
| CON | Contabilidade | Lança e finaliza |
| USR | Usuário | Cria e acompanha as próprias solicitações |

### ViewModes (ListingController)
- `MyView` — Próprias solicitações
- `ManagerView` — Solicitações onde é gestor
- `FinancialView` — Aprovadas pelo gestor (todas se FIN, somente atribuídas caso contrário)
- `Dashboard` — Visão consolidada (ADM)

---

## Padrão de Resposta da API

```json
{
  "success": true,
  "message": "Operação realizada com sucesso",
  "data": {},
  "records": 0,
  "pages": 0,
  "page": 1
}
```

Definido em `Ares.PagueAres.Domain/Models/GenericResponse.cs`.

---

## Serviços Principais

| Serviço | Localização | Responsabilidade |
|---------|-------------|-----------------|
| `AuthenticationService` | Application/Authentication | Login, geração de JWT, busca de usuário |
| `EmailService` | Application/Email | Disparo assíncrono de e-mails por evento |
| `ActiveDirectoryService` | Application/ActiveDirectory | Busca e autenticação LDAP |

---

## Pontos de Atenção para Produção

1. **String de conexão** hardcoded em `PagueAresContext.cs` — mover para `appsettings.json`
2. **CORS** configurado com `AllowAnyOrigin` em `Program.cs` — restringir para a origem do frontend
3. **JWT Secret** em `appsettings.json` — usar variável de ambiente (`ASPNETCORE_`)
4. **HTTPS** obrigatório — `UseHttpsRedirection()` já está configurado
5. **Arquivos em base64** armazenados direto no banco — avaliar armazenamento externo (blob storage) para escala
