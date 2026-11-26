# CLAUDE.md — PagueAres Frontend

Frontend Angular do sistema de gestão de reembolsos e despesas corporativas da Ares.
Este diretório (`front-end/`) faz parte do monorepo `pague-ares`; o backend ASP.NET Core 10 está em `../back-end/` e o `docker-compose.yml` na raiz sobe os dois.

## Stack

Versões conforme `package.json` (Angular em modo NgModule, `standalone: false`; build com o builder `application`/esbuild):

| Tecnologia | Versão |
|-----------|--------|
| Angular (core, common, router, forms, ...) | ~20.2.0 |
| Angular CLI / `@angular-devkit/build-angular` | ~20.2.0 (instalado: 20.2.2) |
| Angular Material / CDK | ~20.2.14 |
| TypeScript | ~5.9.0 |
| RxJS | 6.6.3 |
| zone.js | 0.15.1 |
| Bootstrap | 4.5.2 (com `bootstrap-material-design` 4.1.3, jQuery 3.6.0, Popper 1.16.1) |
| ngx-mask | ^20.0.3 |
| ng2-currency-mask | ^13.0.3 (`ngx-currency-mask` ^4.3.2 também está nas dependências) |
| jwt-decode | ^4.0.0 |
| Moment.js | ^2.29.1 |
| Chartist | 0.11.4 |
| Testes unitários | Karma 6.3.4 + Jasmine (`jasmine-core` 3.8.0) |
| E2E | Cypress ^13.17.0 |

Requisito de Node: `engines` pede `>=18.19.0`, mas o Angular 20 exige Node 20.19+ ou 22.12+ (o `Dockerfile` usa `node:20-alpine`).

---

## Como Executar

```bash
# Instalar dependências (usa o package-lock.json)
npm ci

# Servidor de desenvolvimento (http://localhost:4200)
npx ng serve            # ou: npm start

# Testes unitários (Karma/Jasmine), uma execução, sem janela de navegador
npx ng test --watch=false --browsers=ChromeHeadless

# Build de produção (saída em dist/browser/)
npx ng build --configuration production

# E2E (Cypress)
npm run e2e             # cypress run
npm run e2e:open        # modo interativo
```

> **Use `npx ng ...` (ou `npm start` / `npm test`), nunca o `ng` global.** O projeto depende do Angular CLI local (v20); um `ng` global de outra versão falha ou se comporta de forma diferente.

Rotas usam **hash** (`useHash: true`): o app abre em `http://localhost:4200/#/...`.

---

## Configuração

### URL da API

A URL base da API **não vem de `src/environments/*.ts`** (esses arquivos existem, mas só `main.ts` lê `environment.production`). Quem define a URL é `src/assets/config.json`, carregado antes de o app subir por `APP_INITIALIZER` (`SystemService.loadConfig()`), que grava o valor em `BaseUrlService.BaseUrl`:

```json
{
    "BaseUrl": "https://localhost:7219"
}
```

- Em desenvolvimento sem Docker, `https://localhost:7219` é a URL HTTPS do perfil do backend (`dotnet run --project Ares.PagueAres.API`).
- Alterar `config.json` não exige rebuild do app já publicado.
- No contêiner, `docker-entrypoint.sh` reescreve `assets/config.json` a cada start com `BaseUrl` = variável `FRONTEND_API_BASE_URL`. Vazio (padrão) significa URL relativa: as chamadas `/api/*` passam pelo proxy do Nginx (`nginx.conf`, `location /api/` → `http://api:8080`), sem CORS.

### Docker

`Dockerfile`: estágio Node (`npm ci` + `npx ng build --configuration production --output-hashing=all`) e estágio Nginx servindo `dist/browser/`. Porta do host configurável por `FRONTEND_PORT` (padrão 80).

---

## Estrutura de Pastas

```
src/app/
├── core/
│   ├── interceptors/          # ApiAuthenticationInterceptor.ts (classe AuthInterceptor) — injeta Bearer token
│   └── intl/                  # Localização PT-BR do paginador Material
│
├── components/                # dialog (confirmação), footer, loading, navbar, sidebar
├── layouts/admin-layout/      # Layout principal (sidebar + header) para rotas autenticadas
│
├── login/                     # Página de login
├── home/                      # Dashboard com estatísticas (atualiza a cada 30 s)
├── payments/                  # Lista de Solicitações/RDDVs por visão (abas)
├── exportacoes/               # ExportacoesComponent: tela de exportações de títulos com anexos (só FIN e ADM)
├── guards/                    # papel.guard.ts (papelGuard): libera a rota só para os papéis informados
├── rddv/                      # RddvComponent (declarado no módulo, sem rota própria)
├── refunds/                   # RefundsComponent (declarado no módulo, sem rota própria)
├── icons/                     # Página de ícones (template Material Dashboard)
├── create-request/            # Formulário de criação/edição de Solicitação
├── create-rddv/               # Formulário de criação/edição de RDDV
├── create-user/               # Cadastro/edição de usuário
├── payment-report/            # Relatório imprimível de Solicitação
├── rddv-report/               # Relatório imprimível de RDDV
├── configuration/
│   └── travel-type-list/      # Sub-componente: tipos de viagem
│
├── services/                  # Serviços HTTP e utilitários
├── models/                    # Interfaces/enums TypeScript
└── utils/                     # Utilitários de data
```

Outros diretórios: `cypress/` (specs E2E), `src/assets/` (config.json, scss, imagens, fontes), `documentation/` e `e2e/` (restos do template original/Protractor, não usados pelos scripts atuais).

---

## Roteamento

Definido em `app.routing.ts` e `layouts/admin-layout/admin-layout.routing.ts` (rotas com hash, ex.: `/#/payments`):

```
/login                    → LoginComponent
/payment-report/:id       → PaymentReportComponent   (fora do layout; exige JWT no localStorage para carregar os dados)
/rddv-report/:id          → RddvReportComponent      (idem)
/                         → AdminLayoutComponent     (redireciona para /login se não autenticado)
  /home                   → HomeComponent
  /payments               → PaymentsComponent
  /exportacoes            → ExportacoesComponent     (só FIN e ADM, via papelGuard)
  /config                 → ConfigurationComponent
  /rddv                   → RddvComponent
  /icons                  → IconsComponent
  /create-request[/:id]   → CreateRequestComponent
  /create-rddv[/:id]      → CreateRddvComponent
  /create-user[/:id]      → CreateUserComponent
```

O `AdminLayoutModule` é carregado com **lazy loading** (`loadChildren`). A única rota com `canActivate` é `/exportacoes`, protegida pelo `papelGuard(EnumFuncao.Financeiro, EnumFuncao.Administrador)` (`guards/papel.guard.ts`), que redireciona para `/payments` quando o usuário não tem nenhum dos papéis; as demais rotas não têm guarda, e o redirecionamento ao login está em `AdminLayoutComponent`. Após login com sucesso o app navega para `/payments`; se já autenticado ao abrir `/login`, vai para `/home`.

O menu lateral (`sidebar.component.ts`) depende do papel: ADM vê Home, Solicitações, Exportações e Configurações; FIN vê Home, Solicitações e Exportações; CON vê Home e Solicitações (não exporta); GES e SOL veem apenas Solicitações. Os relatórios são abertos em nova aba a partir das listagens (`/#/payment-report/{codigo}`).

---

## Autenticação

O JWT é guardado no `localStorage` (chave `Token`) via `LocalStorageService` e injetado em toda requisição HTTP pelo `AuthInterceptor` (`core/interceptors/ApiAuthenticationInterceptor.ts`, registrado em `app.module.ts` com `HTTP_INTERCEPTORS`).

**Exclusões do interceptor** (URLs que contêm estes trechos não recebem `Authorization`):
- `api/Auth` (login)
- `assets/config.json` (configuração de boot)

O papel vem do JWT (claim `Roles`, aliases separados por vírgula) e é consultado por `AuthService.IsInRole(EnumFuncao)`. Falhas de login chegam como HTTP 200 com `success=false`; o `LoginComponent` diferencia credenciais inválidas (mensagem da API, inclusive o bloqueio após 10 tentativas) de erro de conexão.

---

## Serviços (`src/app/services`)

| Serviço | Arquivo | Responsabilidade |
|---------|---------|-----------------|
| `AuthService` | `auth.service.ts` | Login, token JWT, papéis, logout |
| `PaymentService` | `payment.service.ts` | Solicitações (`/api/Request`), listagem unificada (`/api/Listing`), dashboard, atribuição |
| `RddvService` | `rddv.service.ts` | RDDVs (`/api/Rddv`), dashboard, atribuição |
| `UsersService` | `users.service.ts` | Gestão de usuários, busca no Active Directory |
| `ProviderService` | `provider.service.ts` | Consulta de fornecedores por ID e CPF/CNPJ |
| `TravelTypeService` | `travel-type.service.ts` | Tipos de viagem |
| `DepartmentService` | `department.service.ts` | Departamentos (`/api/Department`) — substituiu as listas fixas |
| `ExportService` | `export.service.ts` | URLs de Excel e ZIP de anexos |
| `ExportacaoService` | `exportacao.service.ts` | Exportações de títulos com anexos: criar, listar, obter, gerar link de download e cancelar, em `/api/exportacao` (o download usa `/api/exportacao/download?token=`) |
| `PreviewTokenService` | `preview-token.service.ts` | Gera a *preview key* (válida por 1 min) para anexos/exportação |
| `ConfigService` | `config.service.ts` | Leitura e escrita de configurações do sistema |
| `SystemService` | `system.service.ts` | `APP_INITIALIZER`: carrega `assets/config.json` |
| `LocalStorageService` | `local-storage.service.ts` | Wrapper para `localStorage` |
| `BaseUrlService` | `base-url.service.ts` | Monta as URLs de cada endpoint a partir de `BaseUrl` (preenchido pelo `SystemService`) |
| `UtilsService` | `utils.service.ts` | Utilitários estáticos (ex.: validação de CPF) |

---

## Componentes Principais

### CreateRequestComponent (`/create-request`)
Formulário de Solicitação de Pagamento.
- Tipo de solicitação (1 Adiantamento, 2 Pagamento, 3 Movimento Almoxarifado), tipo/forma de pagamento, fornecedor, valor, datas (documento/vencimento), dados bancários, descrição.
- Upload de múltiplos arquivos (base64); download de anexos e ZIP usa a preview key.
- Botões de aprovação por estágio (Gestor, Financeiro, Contabilidade); etapa já decidida (2/3) não pode ser editada.
- **Rascunho:** salva sem validar; ao reabrir os campos são editáveis; cancelar não inicia o fluxo de aprovação.

### CreateRddvComponent (`/create-rddv`)
Relatório de Despesas com Viagem: funcionário, CPF, tipo de viagem, datas, destino, finalidade, tabela de despesas (data, tipo, moeda, valor, quantidade), upload de documentos e workflow de aprovação, com as mesmas regras de rascunho e de etapas.

### PaymentsComponent (`/payments`)
Listagem em abas conforme o papel: "Minhas Solicitações" (MyView), "Gestor" (GES/ADM) e "Financeiro/Contábil" (FIN/CON/ADM). Mostra Solicitações e RDDVs juntos (`/api/Listing`).
- Filtros por período, parceiro, nº do documento, status etc.; o filtro é persistido por usuário no `localStorage` e o `viewMode` é validado pelo papel.
- Badge "Rascunho", spinner de carregamento, resposta "último vence" ao alternar abas/contas.
- Botões "Solicitar pagamento", "Solicitar RDDV", "Exportar planilha" (Excel `Pagamentos.xlsx`, com dica apontando o menu Exportações para os anexos; só na visão financeira e só para FIN e ADM; CON não vê o botão, `allowExport`) e "Filtrar"/"Limpar filtros". Nas linhas de RDDV, "#" que a view grava em Nº do documento, Parceiro e Descrição não aparece: Nº do documento e Descrição ficam vazios e Parceiro mostra o solicitante.

### HomeComponent (`/home`)
Cards Pendentes/Aprovados/Reprovados (soma de Solicitações e RDDVs), atualização a cada 30 s, lista de itens para atribuição de responsável (ADM/FIN).

### PaymentReportComponent / RddvReportComponent
Páginas de relatório otimizadas para impressão, abertas em nova aba; exibem o status completo de aprovação.

### ConfigurationComponent (`/config`)
Parâmetros do sistema, lista de usuários (link para `/create-user`) e `TravelTypeListComponent` para tipos de viagem/valor da diária.

### CreateUserComponent (`/create-user`)
Cadastro/edição de usuários (restrito a ADM no backend), busca no Active Directory, papéis e departamento.

---

## Papéis e Controle de Acesso

O perfil do usuário logado é extraído do JWT e determina o que é exibido (a autorização real é sempre do backend):

| Alias | Papel |
|-------|-------|
| ADM | Administrador — acesso total |
| SOL | Solicitante — cria e acompanha as próprias solicitações |
| GES | Gestor — aprova solicitações da equipe |
| FIN | Financeiro — autoriza pagamentos |
| CON | Contabilidade — lança e finaliza |

---

## Padrão de Resposta da API

Todos os endpoints retornam `GenericResponse<T>` (`src/app/models/GenericResponse.ts`):

```typescript
interface GenericResponse<T> {
  success: boolean;
  message: string;
  data: T;
  records: number;  // total de registros para paginação
  pages: number;
  page: number;
}
```

---

## Testes

- **Unitários (Karma + Jasmine):** specs `*.spec.ts` ao lado dos componentes/serviços (`app`, `footer`, `icons`, `home`, `login`, `payments`, `rddv`, `refunds`, `create-request`, `create-rddv`, `create-user`, `AuthService`, `LocalStorageService`, `UtilsService`). Config em `karma.conf.js` e `src/test.ts`. Comando: `npx ng test --watch=false --browsers=ChromeHeadless`.
- **E2E (Cypress):** specs em `cypress/e2e/{auth,payments,rddv,security,workflow}/*.cy.ts`; `cypress.config.ts` usa `baseUrl: http://localhost:4200`. Exigem frontend e API rodando e usuários de teste existentes no banco (os specs usam credenciais fixas como `adm@test.com`); o comando `cy.login` (`cypress/support/commands.ts`) autentica direto na API.
- Resultados e contagens atuais: ver `../RELATORIO_TESTES.md`.

---

## Localização

- Locale `pt-BR` registrado no `AppModule` (`LOCALE_ID`).
- Paginador do Angular Material traduzido em `core/intl/mat-paginator-intl.ts`.
- Máscaras com `ngx-mask`, moeda com `ng2-currency-mask`, datas com Moment.js.
