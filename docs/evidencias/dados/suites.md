# Resultado das suítes automatizadas da exportação (E13)

Data da execução: 2026-11-25. Branch `feature/exportacao-anexos`, partindo do commit `cd21a73` (ponta com todas as etapas de código integradas). Durante a execução entrou o commit `5233945`, que altera só documentação em Markdown (`DOCUMENTACAO.md`, `back-end/CLAUDE.md`, `front-end/CLAUDE.md`); nenhum arquivo de código mudou.

## 1. Estado do repositório

- `git status`: árvore limpa antes de rodar qualquer comando.
- `git log --oneline main..HEAD` em `cd21a73`: 29 commits (etapas E1 a E12 e correções, com os merges de cada etapa).

## 2. Build Release

- Comando: `dotnet build back-end/Ares.PagueAres.sln -c Release`
- Resultado: 0 erros (35 avisos, todos de nulabilidade e de código legado já existente).

## 3. Testes do back-end

- Comando: `dotnet test back-end/Ares.PagueAres.sln --logger "trx;LogFileName=resultado.trx"` (configuração Debug, a padrão do comando)
- Resultado: 0 falhas.

| Projeto | Aprovados | Falhos | Ignorados | Total |
|---|---|---|---|---|
| Ares.PagueAres.Tests.Integration | 220 | 0 | 0 | 220 |
| Ares.PagueAres.Tests.Unit | 131 | 0 | 0 | 131 |
| Total | 351 | 0 | 0 | 351 |

## 4. Testes do front-end (Karma com Edge)

- Comando: `npx ng test --watch=false --browsers=ChromeHeadless` em `front-end/`, com `CHROME_BIN` apontando para o Edge.
- Resultado: 115 de 115 com sucesso, 0 falhas (`TOTAL: 115 SUCCESS`).
- Observação: o log traz mensagens `NG0304` de elementos `mat-form-field` e `mat-label` desconhecidos no `RefundsComponent`. São avisos de console de um componente que já existia, não falhas de teste.

## 5. Build do front-end

- Comando: `npx ng build` em `front-end/`
- Resultado: sem erros; bundle gerado (inicial 4,85 MB, mais o chunk lazy `admin-layout-module`).

## 6. Buscas no código

- `rg -n "ex\.Message" back-end/Ares.PagueAres.API/Controllers`: 24 ocorrências, todas em controllers legados que não fazem parte da exportação (`UsersController`, `TravelTypeController`, `AuthController`, `ConfigController`, `ListingController`, `ProviderController`, `DepartmentController`, `RddvController`, `RequestController`). Nenhuma em `ExportController`, `PreviewController` nem `ExportacaoController`, que são os controllers do escopo da E1 e da E7.
- `rg -n "Spire" back-end --glob "*.cs*"`: vazio (nenhuma ocorrência), como esperado.

## Resultado geral

Build Release sem erros, 351 testes do back-end e 115 do front-end sem falhas, build do front sem erros. A única pendência registrada é a lista de `ex.Message` em controllers legados fora do escopo da exportação.
