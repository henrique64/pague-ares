# CLAUDE.md — PagueAres Backend

Sistema de gestão de reembolsos e despesas corporativas da Ares.
Este diretório (`back-end/`) é a parte .NET do monorepo `pague-ares`. O frontend Angular fica em `../front-end/` e a orquestração Docker (compose, `.env.example`) na raiz do repositório.

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
| Framework | ASP.NET Core 10.0 (`net10.0`) |
| ORM | Entity Framework Core 10.0.8 (SQL Server) |
| Banco | SQL Server (a imagem Docker usa `mssql/server:2022-latest`) |
| Autenticação | JWT HS256 (`Microsoft.AspNetCore.Authentication.JwtBearer`) + Active Directory (LDAP) |
| Senhas locais | BCrypt (`BCrypt.Net-Next` 4.0.3); hashes SHA256 legados são migrados no login |
| E-mail | SMTP (`System.Net.Mail`) |
| Excel | ClosedXML 0.105.1 |
| ZIP | SharpZipLib 1.4.2 |
| JSON | Newtonsoft.Json 13.0.4 (usado em `ExportController`) |
| Docs da API | `Microsoft.AspNetCore.OpenApi` 10.0.8 + Scalar UI (`Scalar.AspNetCore` 2.14.14) — **não há Swagger** |
| Testes | xUnit 2.9.2, FluentAssertions 6.12.2, Moq 4.20.72, EF Core InMemory, `Microsoft.AspNetCore.Mvc.Testing` |

---

## Arquitetura — Clean Architecture em 4 Projetos + 2 de Teste

```
back-end/
├── Ares.PagueAres.sln
├── Ares.PagueAres.API              # Entrada HTTP: controllers, JwtMiddleware, Program.cs, appsettings
├── Ares.PagueAres.Application      # Auth (JWT/BCrypt), EmailService, Active Directory, Settings
├── Ares.PagueAres.Domain           # DTOs, enums, Funcoes (constantes), GenericResponse<T>
├── Ares.PagueAres.Infrastructure   # PagueAresContext (EF Core), entidades (Models/), Migrations/
├── Ares.PagueAres.Tests.Unit       # xUnit: AuthenticationService, AuthController, filtro de listagem, preview key
└── Ares.PagueAres.Tests.Integration# xUnit + WebApplicationFactory: workflow de aprovação, segurança, segregação de dados
```

**Referências de projeto (conforme os `.csproj`):**
- `API` → `Application`, `Domain`, `Infrastructure`
- `Application` → `Domain`, `Infrastructure`
- `Infrastructure` e `Domain` não referenciam nenhum outro projeto
- `Tests.Unit` → `API`, `Application`, `Domain`, `Infrastructure`
- `Tests.Integration` → `API`, `Domain`, `Infrastructure`

Arquivos relevantes: `Ares.PagueAres.Infrastructure/PagueAresContext.cs` (não existe pasta `Context/`), `Ares.PagueAres.Application/Services/EmailService.cs`, `Ares.PagueAres.Application/External/ActiveDirectory/ActiveDirectoryService.cs`, `Ares.PagueAres.Domain/GenericResponse.cs`.

---

## Como Executar

Todos os comandos abaixo partem de `back-end/` (ou usam o caminho da solução).

```bash
# Restaurar dependências
dotnet restore Ares.PagueAres.sln

# Executar em modo desenvolvimento (perfil "Ares.PagueAres.API" do launchSettings.json)
dotnet run --project Ares.PagueAres.API

# Publicar (a imagem Docker usa: dotnet publish -c Release, sem --self-contained)
dotnet publish Ares.PagueAres.API/Ares.PagueAres.API.csproj -c Release -o ./publish
```

**URLs em desenvolvimento** (`launchSettings.json`): `https://localhost:7219` e `http://localhost:5219`.
No perfil de desenvolvimento (`ASPNETCORE_ENVIRONMENT=Development`) a API publica:
- OpenAPI JSON: `/openapi/v1.json`
- Scalar UI: `/scalar/v1`

Os perfis do `launchSettings.json` abrem o navegador direto em `/scalar/v1`.

**Versionamento:** a versão da API é a propriedade `<Version>` do `Ares.PagueAres.API.csproj` (hoje `26.6.1.2`). O documento OpenAPI declara `Info.Version = "v1"`, mas **as rotas não são versionadas** (não há `/api/v1/...` nem `Asp.Versioning`); todas ficam em `/api/<controller>`.

### Banco de dados em desenvolvimento

`appsettings.json` traz `ConnectionStrings:ConexaoPadrao` vazio. Em `Development`, `appsettings.Development.json` aponta para o SQL Server em `localhost,14330` (porta publicada pelo `docker-compose.override.yml`, variável `DB_PORT`, padrão `14330`) e liga `RunMigrations`. Fluxo típico:

```bash
# na raiz do monorepo
cp .env.example .env        # defina SA_PASSWORD e JWT_SECRET
docker compose up -d db      # sobe só o SQL Server (porta 14330 no host)

# em back-end/
dotnet run --project Ares.PagueAres.API   # aplica migrations no startup (RunMigrations=true)
```

A senha do usuário `sa` configurada em `appsettings.Development.json` precisa coincidir com a `SA_PASSWORD` usada pelo contêiner `db`.

---

## Configuração

A connection string é lida em `Program.cs` por `builder.Configuration.GetConnectionString("ConexaoPadrao")`, ou seja, da chave `ConnectionStrings:ConexaoPadrao`. Em Docker ela é injetada pelo compose como variável de ambiente `ConnectionStrings__ConexaoPadrao` (apontando para `db,1433`). **Não há connection string hardcoded no fluxo de execução.** (`PagueAresContext.OnConfiguring` tem apenas um fallback de *design-time* para `dotnet ef`, usando a variável `PAGUEARES_CONNECTION_STRING`, usado somente quando o contexto não é configurado por DI.)

Seções de configuração reconhecidas (nomes exatos usados em `Program.cs` e nas classes de `Application/Configuration`):

```json
{
  "ConnectionStrings": { "ConexaoPadrao": "<connection string do SQL Server>" },
  "JwtSettings": {
    "Secret": "<segredo do JWT — defina por variável de ambiente (JwtSettings__Secret)>",
    "Issuer": "pagueares.local",
    "Audience": "pagueares.local",
    "ExpiryMinutes": 720
  },
  "ActiveDirectory": {
    "EnableDomainAuthentication": false,
    "Servers": [
      { "Host": "<servidor>", "Domain": "<domínio>", "Username": "<conta de serviço>", "Password": "<senha>" }
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
  "AllowedOrigins": ""
}
```

Pontos importantes:
- **`RunMigrations`** (ou a variável `ASPNETCORE_RUN_MIGRATIONS=true`) faz `Database.Migrate()` no startup. O compose liga isso por padrão.
- **`AllowedOrigins`**: lista de origens separadas por vírgula. Vazio = CORS aberto (`AllowAnyOrigin`); preenchido = restrito às origens listadas. Em Docker o Nginx do frontend faz proxy de `/api/`, então CORS não é necessário.
- **`Smtp`**: o e-mail só é enviado se `SmtpHost`, `SmtpUser`, `SmtpPassword` e `EnableSsl` estiverem preenchidos (checagem em `EmailService.SendEmail`). O campo `EnableEmailService` existe na classe `SmtpSettings`, mas **nenhum código o consulta** hoje.
- **`ActiveDirectory`** usa `System.DirectoryServices.AccountManagement`, API específica do Windows; a imagem Docker da API é Linux.
- **Docker**: `ASPNETCORE_ENVIRONMENT=Docker` desliga o redirecionamento HTTPS (`Program.cs`); a API em contêiner escuta em `http://+:8080`.
- Segredos (JWT, SMTP, AD, senha do banco) não devem ser commitados; use variáveis de ambiente/`.env`. O `appsettings.json` atual traz um `JwtSettings:Secret` de exemplo que deve ser sobrescrito em qualquer ambiente real (compose: `JwtSettings__Secret` ← `JWT_SECRET`).

---

## Testes

Dois projetos xUnit, ambos na solução:

```bash
# todos os testes (a partir de back-end/)
dotnet test Ares.PagueAres.sln

# só um projeto
dotnet test Ares.PagueAres.Tests.Unit
dotnet test Ares.PagueAres.Tests.Integration

# filtrar por nome
dotnet test Ares.PagueAres.sln --filter "FullyQualifiedName~SecurityTests"
```

| Projeto | Conteúdo |
|---------|----------|
| `Ares.PagueAres.Tests.Unit` | `Authentication/AuthenticationServiceTests` (BCrypt, SHA256 legado, migração), `Authentication/AuthControllerUnitTests` (lockout), `Controllers/ListingControllerFilterTests` (filtros de visão), `Preview/PreviewTokenValidationTests` |
| `Ares.PagueAres.Tests.Integration` | `ApprovalWorkflowTests`, `AuthControllerTests`, `DataSegregationTests`, `DepartmentControllerTests`, `EndpointExposureTests`, `SecurityTests` — sobem a API inteira via `PagueAresWebAppFactory` (`WebApplicationFactory<Program>`) com EF Core **InMemory**, ambiente `Test`, `RunMigrations=false`, e geram JWTs de teste. O contexto de teste `PagueAresContextTeste` dá ao InMemory a view `vListagemSolicitacoes` (o InMemory não tem views) |

Os testes de integração **não precisam de SQL Server nem de Docker**. `Program` é exposto com `public partial class Program { }` para essa finalidade. Resultados e contagens atuais: ver `../RELATORIO_TESTES.md`.

---

## Banco de Dados

Banco: `PagueAres` no SQL Server. O esquema é criado pelas **migrations do EF Core** (`Ares.PagueAres.Infrastructure/Migrations`: `InitialCreate`, que também cria as views e semeia dados base, e `DecimalPrecision_Indexes`, que ajusta a precisão dos campos monetários — em geral `decimal(18,2)` — e cria os índices `IX_Solicitacao_Status/Usuario` e `IX_Rddv_Status/Usuario`). O script `PagueAres.sql` (em `back-end/`) é uma alternativa manual para criação do banco.

### Entidades Principais

| Tabela | Descrição |
|--------|-----------|
| `Usuario` | Usuários do sistema (local ou AD) |
| `Solicitacao` | Solicitações de pagamento |
| `Rddv` | Relatórios de despesas com viagem |
| `DespesaRddv` | Linhas de despesa do RDDV |
| `DocumentoSolicitacao` | Anexos de solicitações (base64) |
| `DocumentoRddv` | Anexos de relatórios (base64) |
| `Funcao` | Papéis: ADM, SOL, GES, FIN, CON (seed: `PagueAres.sql` / `InitialCreate`) |
| `UsuarioFuncao` | Relacionamento N:N usuário ↔ papel |
| `Departamento` | Departamentos com código de lançamento |
| `TipoDocumento` | Tipos de documento aceitos como anexo |
| `TipoViagem` | Tipos de viagem com valor de diária |
| `Fornecedor` | Fornecedores (CPF/CNPJ) |
| `Configuracao` | Parâmetros do sistema (chave-valor) |
| `vListagemSolicitacoes` (view) | Listagem unificada de Solicitações e RDDVs (entidade `ViewListagemSolicitacoes`) |
| `vDespesasRddv` (view) | Total de despesas por relatório |

### Valores de Status (Solicitacao e Rddv)

| Código | StatusGestor / StatusFinanceiro | StatusContabilidade |
|--------|---------------------------------|---------------------|
| 1 | Pendente | Pendente |
| 2 | Aprovado / Autorizado | Lançado |
| 3 | Reprovado / Negado | Recusado |

---

## Controllers e Endpoints

Todos os controllers usam `[Route("api/[controller]")]` e `[Authorize]` (JWT Bearer), exceto `AuthController` (`[AllowAnonymous]`). Algumas rotas de `Export` e `Preview` são `[AllowAnonymous]` e exigem a *preview key* na query string (ver Segurança).

| Controller | Rota Base | Endpoints |
|-----------|-----------|-----------|
| `AuthController` | `/api/auth` | `POST /` — login e geração de JWT |
| `RequestController` | `/api/request` | `GET /{id}`, `POST /` (cria/atualiza, rascunho, aprovação), `GET /dashboard` (consolidado: Solicitações + RDDVs), `GET /{id}/user?userId=` (atribuição) |
| `RddvController` | `/api/rddv` | `GET /{id}`, `POST /`, `GET /dashboard` (só RDDVs), `GET /{id}/user?userId=` |
| `ListingController` | `/api/listing` | `GET /` — listagem unificada com filtros, ordenação, paginação e `ViewMode` (sem `ViewMode`, vale `MyView`; ver ViewModes) |
| `UsersController` | `/api/users` | `GET /` (ADM), `GET /{id}` (próprio perfil ou ADM), `POST /` (ADM), `GET /directory?userName=` (busca no AD), `GET /list?roles=` |
| `ExportController` | `/api/export` | `GET /payments` (Excel), `GET /rddv/attachment/{id}` e `GET /request/attachment/{id}` (ZIP) — todos com `?key=` |
| `ConfigController` | `/api/config` | `GET /`, `POST /` (somente ADM) |
| `ProviderController` | `/api/provider` | `GET /`, `GET /{id}`, `GET /find/{numDoc}` |
| `TravelTypeController` | `/api/traveltype` | `GET /`, `GET /{id}`, `POST /` |
| `DepartmentController` | `/api/department` | `GET /` — lista de departamentos (substitui listas fixas no frontend) |
| `PreviewController` | `/api/preview` | `POST /generate-preview-key?entityId=&requestType=`, `GET /payment/{id}/{attachmentId}?key=`, `GET /rddv/{id}/{attachmentId}?key=` |
| `ExportacaoController` | `/api/exportacao` | `POST /` — cria um job de exportação de títulos (202 com o job; 400 para filtro inválido ou sem títulos, 429 ao passar de `MaxJobsAtivosPorUsuario`); `GET /` — jobs do próprio usuário dos últimos 30 dias, menos os removidos da lista; `GET /{id}` — um job do próprio usuário (inclusive um removido da lista); `POST /{id}/link-download` — gera o link de download de uso único (só para job Concluido); `GET /download?token=` — baixa o ZIP pelo token, anônimo na autenticação, consumido na primeira vez; `DELETE /{id}` — cancela (Pendente vira Cancelado, Processando pede cancelamento, Concluido vira Excluido e apaga o arquivo, mantendo a `DataExpiracao` original; job já encerrado responde 409); `DELETE /{id}/lista` — tira da lista um job Falhou, Cancelado, Expirado ou Excluido (204, também na repetição; 409 para Pendente, Processando e Concluido) |

**Exportações (`ExportacaoController`)**: exigem papel **FIN** ou **ADM** do JWT (outros perfis recebem 403), e cada usuário só enxerga e altera os próprios jobs (job de outro usuário responde 404). A identidade vem sempre do token, nunca do corpo ou da query. O download é por navegação direta do navegador, que não envia o cabeçalho `Authorization`, por isso o link de uso único (`TokensDownloadExportacao`, em `API/Services`).

O processamento é em segundo plano, em `Application/Exportacao/`:
- `ExportacaoFila`: fila em memória que recebe os ids dos jobs criados e registra o job em execução, para que o cancelamento interrompa a geração na hora.
- `ExportacaoWorker`: `BackgroundService` que gera o ZIP (planilha Excel com ClosedXML e anexos) job a job, com progresso gravado em `ExportacaoJob`.
- `ExportacaoLimpeza`: `BackgroundService` que, ao subir e a cada `IntervaloLimpezaMinutos`, expira os pacotes vencidos e apaga arquivos órfãos mais velhos que `RetencaoHoras`.

Os jobs ficam na tabela `ExportacaoJob` (mapeada com `ToTable("ExportacaoJob")` em `PagueAresContext`), com o token de concorrência `Versao`. A seção `Exportacao` do `appsettings.json` traz as chaves `Pasta` (vazia = pasta temporária do sistema, `PagueAres/Exportacao`), `RetencaoHoras`, `IntervaloLimpezaMinutos`, `MaxTitulosPorExportacao`, `MaxJobsAtivosPorUsuario`, `MaxBytesPorExportacao`, `EspacoLivreMinimoMb`, `ValidadeLinkDownloadSegundos`, `WorkerHabilitado` e `FusoHorario` (fuso do nome do ZIP e da hora do LEIA-ME; padrão `America/Sao_Paulo`, aceita também o id do Windows `E. South America Standard Time`; resolvido por `FusoExportacao`, que cai em Brasília e depois em UTC, com aviso no log, se o id não existir), todas ligadas à classe `ExportacaoSettings` (`Application/Configuration`).

Observações:
- **Não existem** mais `GET /api/request` nem `GET /api/rddv` (listagem crua sem escopo de usuário foi removida; use `/api/listing`).
- Quase todas as respostas de negócio (inclusive falhas) voltam com HTTP 200 e `success=false`; HTTP 401 vem do JWT ausente/inválido/expirado e das preview keys inválidas. `GET /{id}/user` usa `BadRequest` para erros.
- `Listing`: `PageSize` padrão 100, `Page` padrão 1; filtros: `DocumentNumber`, `DocumentId`, `RequestStart/End`, `DueStart/End`, `PartnerName`, `RequesterId`, `AssigneeId`, `IsAssigned`, `ManagerStatus`, `FinanceStatus`, `AccountingStatus`, `ViewMode`, `SortField`, `SortOrder`.

---

## Fluxo de Aprovação

```
Criação (Rascunho=true)
  └─► Envio (Rascunho=false) → e-mail para Gestor
        └─► Gestor: Aprovado (2) ou Reprovado (3)
              └─► Financeiro: Autorizado (2) ou Negado (3)
                    └─► Contabilidade: Lançado (2) ou Recusado (3)
```

- `TipoAutorizacao = 1`: no envio (saída do rascunho) o estágio do Gestor é aprovado automaticamente ("Autorização por anexo").
- Rascunhos e registros cancelados **não** aparecem nas visões de fluxo de aprovação (Gestor, Financeiro, Dashboard) nem na exportação; só a visão "Minhas Solicitações" mostra os próprios rascunhos.
- Registro cancelado não pode mais ser alterado. Cancelar não dispara e-mail nem auto-aprovação.
- Se o Financeiro nega, `StatusContabilidade` passa a 3 automaticamente.
- Cada transição dispara e-mail via `EmailService` (ver Serviços).

---

## Segurança

### Autenticação
- **Local:** senha verificada com BCrypt. Hashes SHA256 legados (64 caracteres hex) ainda são aceitos e **re-hasheados com BCrypt no próximo login bem-sucedido**. Observação: `UsersController.SaveUser` ainda grava novas senhas como SHA256, que são migradas no primeiro login.
- **Active Directory:** bind LDAP; o usuário AD é criado automaticamente no primeiro login (`Origem = 1`). Só funciona com `ActiveDirectory:EnableDomainAuthentication=true`.
- **Lockout de login:** `AuthController` conta falhas por IP em `IMemoryCache`; a partir de **10 falhas** (janela de 5 minutos, renovada a cada nova falha) o login retorna `success=false` com "Muitas tentativas de login...". Um login bem-sucedido zera o contador. Usuário inativo recebe "Usuário inativo.".

### JWT
- Algoritmo HS256; `Issuer`/`Audience` `pagueares.local`; expiração `ExpiryMinutes` (720 = 12 h).
- Claims: `sub` (nome), `nameid` (ID), `Roles` (aliases separados por vírgula), `jti`.
- A autenticação/`[Authorize]` é feita pelo esquema `JwtBearer` registrado em `Program.cs` (valida assinatura, issuer, audience e expiração; token adulterado ou expirado → 401). O `JwtMiddleware` também decodifica o token e coloca um `UsuarioDto` em `HttpContext.Items["User"]`; é daí que os controllers lêem o usuário e os papéis (`GetUserFromToken`, `CurrentUserIsInRole`).

### Papéis (RBAC)
| Alias | Papel | Observação |
|-------|-------|-----------|
| ADM | Administrador | Total; único que gerencia usuários (`/api/users` GET lista, `{id}` de terceiros, POST) e grava `/api/config` |
| SOL | Solicitante | Cria e acompanha as próprias solicitações (a constante é `Funcoes.Solicitante = "SOL"`) |
| GES | Gestor | Aprova/reprova como gestor |
| FIN | Financeiro | Autoriza/nega pagamentos; vê todas as aprovadas pelo gestor |
| CON | Contabilidade | Lança/recusa |

Regras aplicadas em `RequestController` (Solicitação):
- ler (`GET /{id}`) ou editar solicitação de terceiros exige ADM, GES, FIN ou CON; senão só o dono;
- mudar `AprovadoGestor` exige ADM ou GES; mudar `AprovadoSetor` exige ADM ou FIN; mudar `StatusContabilidade` exige ADM, FIN ou CON;
- registro cancelado não pode ser editado.

> Atenção: `RddvController.POST` **não** aplica as checagens de papel/propriedade para edição nem para as transições de aprovação (só `GET /{id}` verifica propriedade). `SetUser` (`GET /{id}/user`) em Solicitação e RDDV também não verifica papel. `TravelTypeController.POST`, `ProviderController`, `Users/list` e `Users/directory` não têm checagem de papel além de estar autenticado. Ver pendências abaixo.

### Preview key (links temporários)
`POST /api/preview/generate-preview-key` (autenticado; exige ser dono do registro ou ADM/GES/FIN/CON) devolve uma chave GUID guardada em `IMemoryCache` com validade de **1 minuto**, associada a `(entityId, requestType)`. `RequestType` ∈ `Solicitacao`, `Rddv`, `Exportacao`. As rotas anônimas de download (`/api/preview/...`, `/api/export/...`) só respondem com `?key=` válida; caso contrário 401. Nos previews a chave precisa ser do mesmo tipo **e** do mesmo `entityId`; nos ZIPs de `/api/export/*/attachment/{id}` a comparação usa `&&` (só recusa quando ambos divergem), portanto é mais fraca — ver pendências.

### ViewModes (ListingController)
- `MyView` — próprias solicitações (inclui rascunhos)
- `ManagerView` — onde o usuário é gestor (sem rascunhos/cancelados)
- `FinancialView` — aprovadas pelo gestor (todas se FIN ou ADM, somente atribuídas ao usuário caso contrário, por exemplo CON; sem rascunhos/cancelados)
- `Dashboard` — aprovadas pelo gestor, sem atribuição e não negadas (sem rascunhos/cancelados); só para FIN, CON e ADM, os perfis que veem a tela inicial. Nos demais perfis a fila vem vazia, porque traz títulos de todos os usuários.

A visão é sempre aplicada: `GET /api/listing` sem `viewMode`, ou com `viewMode=0` (`Undefined`), vale como `MyView`, em qualquer perfil, e nunca devolve os títulos de todos os usuários. Um `viewMode` fora do enum (por exemplo `5`, `99`, `-1` ou texto) é recusado pelo model binding com 400; em `ApplyViewModeFilter`, qualquer valor que não seja uma das quatro visões também cai em `MyView`. O rascunho só aparece para o próprio dono, em `MyView`.

Os filtros da tela (sem a visão e sem a ordenação) ficam num lugar só, `ListingController.ApplyFilters`, usado também pelo Excel `Pagamentos.xlsx` (`GET /api/export/payments`), que parte da mesma view: com o mesmo filtro, a planilha traz os títulos que a aba Financeiro/Contábil mostra ao FIN. O documento do parceiro é filtro só da planilha. Na planilha, solicitação e RDDV se distinguem pelo texto `Tipo` da view (o do RDDV começa com `RDDV`), e não pelo `IdTipo`, que no RDDV é o tipo de relatório.

### Tipo de relatório do RDDV (RddvController.POST)
`TipoRelatorio` aceita 1 (Adiantamento), 2 (Prestação de Contas) e 3 (Reembolso); fora disso, `POST /api/rddv` responde 400 com `success=false` e a mensagem `RddvController.MensagemTipoRelatorioInvalido`, sem gravar. Duas exceções, para não quebrar a tela nem travar dados antigos: o rascunho pode ficar com 0 (tipo ainda não escolhido, porque a tela salva o rascunho sem validar), mas não sai do rascunho sem um tipo de 1 a 3; e o RDDV já enviado e gravado com outro valor antes da regra segue o fluxo enquanto o tipo gravado não muda.

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

Definido em `Ares.PagueAres.Domain/GenericResponse.cs`.

---

## Serviços Principais

| Serviço | Localização | Responsabilidade |
|---------|-------------|-----------------|
| `AuthenticationService` | `Application/Authentication` | Login (BCrypt/SHA256 legado/AD), geração de JWT, busca de usuário |
| `EmailService` | `Application/Services` | Envio de e-mails por evento (nova, atribuída, aprovada, negada, paga, pagamento negado, lançada, recusada) |
| `ActiveDirectoryService` | `Application/External/ActiveDirectory` | Busca e autenticação LDAP |

Os serviços são registrados em `Application/Extensions/ApplicationServiceExtensions.cs` (`RegisterApplicationServices`).

---

## Pontos de Atenção para Produção

1. **JWT Secret**: sobrescrever `JwtSettings:Secret` por variável de ambiente (`JwtSettings__Secret`); o valor de exemplo em `appsettings.json` não serve para produção.
2. **CORS**: definir `AllowedOrigins` com as origens do frontend (vazio = qualquer origem). Atrás do Nginx do compose não é necessário.
3. **Ambiente**: `docker compose up` carrega automaticamente `docker-compose.override.yml`, que força `ASPNETCORE_ENVIRONMENT=Development` (expõe OpenAPI/Scalar e publica o SQL na porta 14330). Em produção rode apenas com `docker-compose.yml` (`docker compose -f docker-compose.yml up`).
4. **HTTPS**: `UseHttpsRedirection()` está ativo, exceto no ambiente `Docker`.
5. **RBAC do RDDV e atribuição**: replicar em `RddvController.POST`, `SetUser` e `TravelTypeController.POST` as checagens de papel/propriedade já existentes em `RequestController`.
   **Preview key nos ZIPs**: trocar `&&` por `||` na validação de `ExportController` (`rddv/attachment/{id}` e `request/attachment/{id}`), como já é feito em `PreviewController`.
6. **Arquivos em base64** armazenados direto no banco — avaliar armazenamento externo (blob storage) para escala.
7. **Cache em memória**: lockout de login e preview keys vivem em `IMemoryCache` do processo; com mais de uma instância da API os contadores/chaves não são compartilhados.
