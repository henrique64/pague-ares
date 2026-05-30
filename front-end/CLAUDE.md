# CLAUDE.md — PagueAres Frontend

Frontend Angular do sistema de gestão de reembolsos e despesas corporativas da Ares.
O backend C# .NET 8 está em repositório separado.

## Stack

| Tecnologia | Versão |
|-----------|--------|
| Angular | 12.1.2 |
| Angular Material | 12.1.2 |
| Bootstrap | 4.5.2 |
| RxJS | 6.6.3 |
| TypeScript | ~4.x |
| ngx-mask | 13.1.1 |
| ng2-currency-mask | 13.0.3 |
| jwt-decode | 4.0.0 |
| Moment.js | 2.29.1 |

---

## Como Executar

```bash
# Instalar dependências
npm install

# Servidor de desenvolvimento (porta padrão: 4200)
ng serve

# Build de produção
ng build --configuration production
```

O frontend consome a API em `https://localhost:7219` no ambiente de desenvolvimento.
Saída de build em `dist/`.

---

## Configuração

### URL da API

Definida em `src/environments/environment.ts` (dev) e `src/environments/environment.prod.ts` (produção):

```typescript
export const environment = {
  production: false,
  baseUrl: 'https://localhost:7219'
};
```

### Config de Boot

O arquivo `src/assets/config.json` é carregado durante a inicialização via `APP_INITIALIZER` no `AppModule`. Alterações nele não exigem rebuild.

---

## Estrutura de Pastas

```
src/app/
├── core/
│   ├── interceptors/          # ApiAuthenticationInterceptor — injeta Bearer token
│   └── intl/                  # Localização PT-BR do paginador Material
│
├── layouts/admin-layout/      # Layout principal (sidebar + header) para rotas autenticadas
│
├── login/                     # Página de login
├── home/                      # Dashboard com estatísticas
├── payments/                  # Lista de Solicitações de Pagamento
├── rddv/                      # Lista de Relatórios de Viagem (RDDV)
├── refunds/                   # Reembolsos
├── create-request/            # Formulário de criação/edição de Solicitação
├── create-rddv/               # Formulário de criação/edição de RDDV
├── create-user/               # Gestão de usuários
├── payment-report/            # Relatório imprimível de Solicitação (acesso público via token)
├── rddv-report/               # Relatório imprimível de RDDV (acesso público via token)
├── configuration/
│   └── travel-type-list/      # Sub-componente: tipos de viagem
│
├── services/                  # Todos os serviços HTTP
├── models/                    # Interfaces TypeScript
└── utils/                     # Utilitários de data
```

---

## Roteamento

```
/login                    → LoginComponent              (público)
/payment-report/:id       → PaymentReportComponent      (preview token)
/rddv-report/:id          → RddvReportComponent         (preview token)
/                         → AdminLayoutComponent        (autenticado, lazy load)
  /dashboard              → HomeComponent
  /payments               → PaymentsComponent
  /rddv                   → RddvComponent
  /refunds                → RefundsComponent
  /create-request         → CreateRequestComponent
  /create-rddv            → CreateRddvComponent
  /users                  → CreateUserComponent
  /config                 → ConfigurationComponent
```

O `AdminLayoutModule` usa **lazy loading** via `loadChildren` para reduzir o bundle inicial.

---

## Autenticação

O token JWT é armazenado no `localStorage` via `LocalStorageService` e injetado automaticamente em toda requisição HTTP pelo `ApiAuthenticationInterceptor`.

**Exclusões do interceptor** (não recebem o header `Authorization`):
- `POST /api/Auth` (login)
- `GET assets/config.json` (configuração de boot)

---

## Serviços

| Serviço | Arquivo | Responsabilidade |
|---------|---------|-----------------|
| `AuthService` | `auth.service.ts` | Login, token JWT, logout |
| `PaymentService` | `payment.service.ts` | CRUD de Solicitações, listagem, dashboard, atribuição |
| `RddvService` | `rddv.service.ts` | CRUD de RDDVs, listagem, dashboard, atribuição |
| `UsersService` | `users.service.ts` | Gestão de usuários, busca no Active Directory |
| `ProviderService` | `provider.service.ts` | Consulta de fornecedores por CPF/CNPJ |
| `TravelTypeService` | `travel-type.service.ts` | CRUD de tipos de viagem |
| `ExportService` | `export.service.ts` | Download de Excel e ZIP de anexos |
| `PreviewTokenService` | `preview-token.service.ts` | Tokens temporários para links de relatórios públicos |
| `ConfigService` | `config.service.ts` | Leitura e escrita de configurações do sistema |
| `SystemService` | `system.service.ts` | Inicialização via `APP_INITIALIZER`, carrega `config.json` |
| `LocalStorageService` | `local-storage.service.ts` | Wrapper para `localStorage` |
| `BaseUrlService` | `base-url.service.ts` | Provê a URL base da API a partir do `environment` |

---

## Componentes Principais

### CreateRequestComponent (`/create-request`)
Formulário completo de Solicitação de Pagamento.
- Campos: tipo (adiantamento/pagamento/inventário), data, fornecedor, valor, datas (documento/vencimento), banco, descrição
- Upload de múltiplos arquivos (base64)
- Botões de aprovação por estágio: Gestor, Financeiro, Contabilidade
- Modo rascunho: salva sem submeter para aprovação

### CreateRddvComponent (`/create-rddv`)
Formulário de Relatório de Despesas com Viagem.
- Campos: funcionário, CPF, tipo de viagem, datas, destino, finalidade
- Tabela de despesas: data, tipo, moeda, valor, quantidade
- Upload de documentos + workflow de aprovação

### PaymentsComponent e RddvComponent
Listagens com:
- Filtros por período, parceiro, número de documento, status, modo de visualização
- Ordenação e paginação (PT-BR via `mat-paginator-intl.ts`)
- Ações por linha: visualizar, editar, exportar, atribuir responsável

### PaymentReportComponent / RddvReportComponent
- Rotas públicas acessadas via preview token temporário
- Otimizados para impressão
- Exibem status completo de aprovação

---

## Papéis e Controle de Acesso

O perfil do usuário logado é extraído do JWT e determina o que é exibido:

| Alias | Papel |
|-------|-------|
| ADM | Administrador — acesso total |
| GES | Gestor — aprova solicitações da equipe |
| FIN | Financeiro — autoriza pagamentos |
| CON | Contabilidade — lança e finaliza |
| USR | Usuário — cria e acompanha as próprias solicitações |

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

## Localização

- Locale configurado para `pt-BR` no `AppModule`
- Paginador do Angular Material traduzido em `core/intl/mat-paginator-intl.ts`
- Formatação de moeda via `ng2-currency-mask` e datas via `Moment.js`
