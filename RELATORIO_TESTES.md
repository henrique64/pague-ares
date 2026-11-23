# Relatório de Execução de Testes — PagueAres
**Data:** 23/11/2026 (última execução; o detalhamento por teste abaixo é da execução de 09/06/2026)  
**Branch:** `main` (após o merge de `feature/general-improvements`, commit `4a0c32a`)  
**Responsável:** Alexandre Henrique Moreira

---

## Resumo Executivo

| Suite | Total | Aprovados | Reprovados | Duração |
|-------|-------|-----------|------------|---------|
| Backend — Integração | 47 | **47** | 0 | 5s |
| Backend — Unitários | 20 | **20** | 0 | 2s |
| Frontend — Unitários (Karma, Edge headless) | 73 | **73** | 0 | ~0,7s |
| E2E (Cypress) | — | — | — | *não executado* |
| **TOTAL** | **140** | **140** | **0** | |

Smoke test em Docker (`docker compose up -d --build`, 23/11/2026): banco saudável com migrations aplicadas (16 tabelas), OpenAPI e Scalar respondendo 200, frontend em Nginx respondendo 200, login `admin@local` via proxy `/api/Auth` devolvendo JWT, endpoints protegidos com 200 com token e 401 sem token.

> **Cypress:** o binário Electron do Cypress requer ambiente gráfico (desktop). Os testes E2E foram escritos e estão prontos para execução em ambiente com browser disponível (`npx ng serve` + `npx cypress open` ou pipeline CI).

---

> As tabelas por teste abaixo são da execução de 09/06/2026 e não listam os testes adicionados depois (por exemplo `DepartmentControllerTests` e `EndpointExposureTests`). Os totais por suíte são os da execução de 23/11/2026.

## Backend — Testes de Integração (47/47)

**Projeto:** `Ares.PagueAres.Tests.Integration`  
**Resultado:** `Aprovados: 47 | Com falha: 0`  
**Runtime:** .NET 10, xUnit 2.9.2, EF Core In-Memory, FluentAssertions

### ApprovalWorkflowTests (16 testes)

Cobrem o fluxo completo de aprovação em 3 estágios (Gestor → Financeiro → Contabilidade) para Solicitações e RDDVs.

| Teste | Resultado |
|-------|-----------|
| Post_SolicitacaoRascunho_NaoDispara… | ✓ Aprovado |
| Post_SolicitacaoEnviada_DisparaEmail… | ✓ Aprovado |
| Post_GestorAprova_StatusGestorVira2 | ✓ Aprovado |
| Post_GestorRejeita_StatusGestorVira3 | ✓ Aprovado |
| Post_FinanceiroAprova_StatusFinanceiro… | ✓ Aprovado |
| Post_FinanceiroRejeita_StatusFinanceiro… | ✓ Aprovado |
| Post_ContabilidadeLanca_StatusContabilidade… | ✓ Aprovado |
| Post_ContabilidadeRecusa_StatusContabilidade… | ✓ Aprovado |
| Post_SolicitacaoCancelada_RetornaErroDeNegocio | ✓ Aprovado |
| PostRddv_RascunhoNaoDispara… | ✓ Aprovado |
| PostRddv_EnviadoDisparaEmail… | ✓ Aprovado |
| PostRddv_GestorAprova_… | ✓ Aprovado |
| PostRddv_FinanceiroAprova_… | ✓ Aprovado |
| PostRddv_ContabilidadeLanca_… | ✓ Aprovado |
| PostRddv_ContabilidadeRecusa_… | ✓ Aprovado |
| PostRddv_CanceladoRetornaErro | ✓ Aprovado |

### AuthControllerTests (4 testes)

| Teste | Resultado |
|-------|-----------|
| Login_CredenciaisValidas_RetornaToken | ✓ Aprovado |
| Login_SenhaErrada_RetornaFalha | ✓ Aprovado |
| Login_UsuarioInativo_RetornaFalha | ✓ Aprovado |
| Login_AposDezFalhas_RetornaMensagemDeLockout | ✓ Aprovado |

### DataSegregationTests (3 testes)

| Teste | Resultado |
|-------|-----------|
| Get_SolicitacaoDoProprioUsuario_Retorna200 | ✓ Aprovado |
| Get_SolicitacaoDeOutroUsuario_USR_Retorna403 | ✓ Aprovado |
| Get_SolicitacaoDeOutroUsuario_ADM_Retorna200 | ✓ Aprovado |

### SecurityTests (20 testes)

Testes de segurança cobrindo autenticação, escalada de privilégios vertical e horizontal, e controles de acesso por papel.

**Grupo: Authentication (5 testes)**
| Teste | Resultado |
|-------|-----------|
| Unauth_GetUsers_Returns401 | ✓ Aprovado |
| Unauth_GetConfig_Returns401 | ✓ Aprovado |
| Unauth_PostRequest_Returns401 | ✓ Aprovado |
| TamperedJwt_Returns401 | ✓ Aprovado |
| ExpiredJwt_Returns401 | ✓ Aprovado |

**Grupo: UserManagementGap (4 testes)**
| Teste | Resultado |
|-------|-----------|
| NonAdmin_GetAllUsers_ReturnsAccessDenied | ✓ Aprovado |
| NonAdmin_GetUserById_OtherUser_ReturnsAccessDenied | ✓ Aprovado |
| NonAdmin_SaveUser_ReturnsAccessDenied | ✓ Aprovado |
| NonAdmin_CanGetOwnProfile | ✓ Aprovado |

**Grupo: ConfigGap (2 testes)**
| Teste | Resultado |
|-------|-----------|
| NonAdmin_SetConfig_ReturnsAccessDenied | ✓ Aprovado |
| AnyRole_GetConfig_Succeeds | ✓ Aprovado |

**Grupo: HorizontalEscalation (1 teste)**
| Teste | Resultado |
|-------|-----------|
| USR_EditOtherUserRequest_ReturnsAccessDenied | ✓ Aprovado |

**Grupo: ApprovalBypass (2 testes)**
| Teste | Resultado |
|-------|-----------|
| USR_ApproveAsGestor_ReturnsAccessDenied | ✓ Aprovado |
| USR_ApproveAsFinanceiro_ReturnsAccessDenied | ✓ Aprovado |

**Grupo: ExistingControls (6 testes)**
| Teste | Resultado |
|-------|-----------|
| ADM_GetAllUsers_Succeeds | ✓ Aprovado |
| ADM_GetOtherUserById_Succeeds | ✓ Aprovado |
| GES_ApproveSolicitacao_Succeeds | ✓ Aprovado |
| FIN_ApproveSolicitacao_Succeeds | ✓ Aprovado |
| CON_ApproveSolicitacao_Succeeds | ✓ Aprovado |
| UserList_AllRoles_Succeeds | ✓ Aprovado |

---

## Backend — Testes Unitários (20/20)

**Projeto:** `Ares.PagueAres.Tests.Unit`  
**Resultado:** `Aprovados: 20 | Com falha: 0`

### AuthenticationServiceTests (6 testes)

| Teste | Resultado |
|-------|-----------|
| TryAuthenticate_ComSenhaBcryptValida_RetornaUsuario | ✓ Aprovado |
| TryAuthenticate_ComSenhaSha256Valida_RetornaUsuario | ✓ Aprovado |
| TryAuthenticate_ComSenhaErrada_LancaExcecao | ✓ Aprovado |
| TryAuthenticate_ComSenhaSha256_MigraParaBcrypt | ✓ Aprovado |
| TryAuthenticate_UsuarioNaoEncontrado_LancaExcecao | ✓ Aprovado |
| GenerateAuthenticationToken_UsuarioValido_TokenContemClaims | ✓ Aprovado |

### AuthControllerUnitTests (4 testes)

| Teste | Resultado |
|-------|-----------|
| Authenticate_CredenciaisValidas_RetornaToken | ✓ Aprovado |
| Authenticate_UsuarioInativo_RetornaFalha | ✓ Aprovado |
| Authenticate_FalhaAbaixoDoLimite_NaoBloqueiaProximaTentativa | ✓ Aprovado |
| Authenticate_FalhaAcimaDeLimite_RetornaMensagemDeBloqueio | ✓ Aprovado |
| Authenticate_LoginBemSucedidoAposFalhas_ZeraContador | ✓ Aprovado |

### PreviewTokenValidationTests (5 testes)

| Teste | Resultado |
|-------|-----------|
| PreviewPayment_TokenInexistente_RetornaUnauthorized | ✓ Aprovado |
| PreviewPayment_TokenValido_EntityIdErrado_RetornaUnauthorized | ✓ Aprovado |
| PreviewRddv_TipoErrado_RetornaUnauthorized | ✓ Aprovado |
| PreviewPayment_TokenValido_EntityIdCorreto_ServeArquivo | ✓ Aprovado |

---

## Frontend — Testes Unitários (73/73)

**Framework:** Karma 6.3.4 + Jasmine 3.8 + Angular 20  
**Browser:** Microsoft Edge 148 (Headless)  
**Resultado:** `TOTAL: 73 SUCCESS`

Componentes cobertos: AppComponent, FooterComponent, IconsComponent, HomeComponent, LoginComponent, CreateRequestComponent, CreateRddvComponent, CreateUserComponent, RddvComponent, RefundsComponent, AuthService, LocalStorageService, UtilsService.

---

## E2E — Cypress (scaffolding disponível)

**Versão:** Cypress 13.17.0  
**Status:** Testes escritos e prontos. Execução requer ambiente gráfico.

**Specs criados:**
- `cypress/e2e/auth/login.cy.ts` — Login
- `cypress/e2e/payments/create-request.cy.ts` — Criação de solicitações
- `cypress/e2e/rddv/create-rddv.cy.ts` — Criação de RDDVs
- `cypress/e2e/workflow/approval-flow.cy.ts` — Fluxo de aprovação
- `cypress/e2e/security/data-segregation.cy.ts` — Segregação de dados por papel

**Como executar:**
```bash
# Iniciar backend
dotnet run --project back-end/Ares.PagueAres.API

# Iniciar frontend (usar o CLI local, não o ng global)
cd front-end && npx ng serve

# Executar testes E2E (em outro terminal, também em front-end/)
npx cypress run
```

---

## Avisos (não bloqueantes)

Os avisos abaixo não causam falhas de teste e não representam defeitos funcionais:

- **Backend:** 8 avisos CS8602/CS8604 (possível referência nula) — melhorias de nullable handling para iterações futuras
- **Frontend:** Avisos de `mat-form-field`/`mat-label` em `RefundsComponent` — componente usa módulo compartilhado não declarado explicitamente no TestBed de spec (isolado nos testes)
