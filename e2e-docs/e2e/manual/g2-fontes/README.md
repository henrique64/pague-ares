# g2-fontes: páginas do pacote gerado (fotos do grupo g2 e passos do vídeo)

O pacote da exportação (ZIP) não é uma tela do app. As fotos do grupo `g2` e as cenas do vídeo que mostram o pacote saem
de páginas HTML montadas a partir de um pacote **real**, gerado pela API local com o usuário FIN fictício da semente.

- `paginas/`: as páginas prontas (versionadas, ~90 KB no total, só texto). Nenhum anexo, nenhum ZIP, nenhuma chamada de
  rede. Abrem por `file://`; o elemento a fotografar é `#janela`. Dados 100% fictícios (semente), conferidos contra
  `fontes/proibidos.json`.
- `montar-paginas.ts`: gera `paginas/` de um pacote extraído (árvore, `LEIA-ME.txt`, `indice.csv`, `indice.json`) e dos JSON das planilhas.
- `xlsx-para-json.cs`: lê `titulos.xlsx` e `Pagamentos.xlsx` (ClosedXML) e grava o conteúdo como o Excel o mostra (texto formatado, negrito, link).
- `baixar-pagamentos.ts`: baixa o `Pagamentos.xlsx` pela rota legada `GET /api/Export/payments` com a chave de exportação.

Por que versionar as páginas: o pacote depende do estado do banco e da hora da geração; com as páginas no repositório a recaptura
(M9) não precisa de banco nem de semente. Só é preciso refazer tudo se a estrutura do pacote mudar.

## Páginas

| Página | Mostra | Foto |
|---|---|---|
| `arvore.html` | pasta do ZIP extraído (pastas SOL-/RDDV-, raiz) | `pacote-arvore-extraida` |
| `planilha-solicitacoes.html` | `titulos.xlsx`, aba Solicitações (quatro abas à vista) | `pacote-planilha-abas` |
| `planilha-rddvs.html`, `planilha-despesas.html` | abas RDDVs e Despesas dos RDDVs | só vídeo |
| `planilha-anexos.html` | aba Anexos, oito colunas | `pacote-planilha-anexos` |
| `indice-csv.html` | `indice.csv` | `pacote-indice-csv` |
| `indice-json-inicio.html`, `indice-json-arquivos.html` | começo e seção `arquivos` do `indice.json` | só vídeo |
| `leia-me.html` | `LEIA-ME.txt` inteiro | `pacote-leia-me` |
| `pagamentos.html` | `Pagamentos.xlsx`, aba Pagamentos | `pagamentos-excel-previa` |

## Abrir uma página num spec (foto ou vídeo)

```ts
import { pathToFileURL } from "node:url";
const url = (pagina: string) =>
  pathToFileURL(new URL(`./g2-fontes/paginas/${pagina}.html`, import.meta.url).pathname.replace(/^\/([A-Za-z]:)/, "$1")).href;
await page.goto(url("planilha-anexos"));   // spec em e2e/manual/; num spec em e2e/manual/video/ use "../g2-fontes/..."
await expect(page.locator("#janela")).toBeVisible();
```

## Refazer as páginas

Da raiz do repositório, com a stack local de pé e `PAGUEARES_SEMENTE_SENHA` na sessão (nada é impresso):

1. Gerar o pacote: `node tools/exportacao/medir/medir.ts --login fin.semente@exemplo.invalid --filtro '{"tipoTitulo":0,"dataSolicitacaoInicio":"2025-11-09","dataSolicitacaoFim":"2025-11-13"}' --zip pacote.zip --saida medicao.json`
   (a janela de datas escolhida traz 5 solicitações e 1 RDDV com despesas, todos com anexos, e um nome de anexo repetido que ganha o sufixo "(2)").
2. Extrair o ZIP numa pasta vazia (fora do repositório).
3. Planilhas: `dotnet run e2e-docs/e2e/manual/g2-fontes/xlsx-para-json.cs -- <pasta>/titulos.xlsx dados/titulos.json`; e
   `node e2e-docs/e2e/manual/g2-fontes/baixar-pagamentos.ts Pagamentos.xlsx 2025-11-09 2025-11-13` seguido do mesmo `xlsx-para-json.cs` para `dados/pagamentos.json`.
4. `node e2e-docs/e2e/manual/g2-fontes/montar-paginas.ts --pacote <pasta extraída> --dados dados --saida e2e-docs/e2e/manual/g2-fontes/paginas`.
5. Conferir o pacote com `dotnet run tools/exportacao/verificar/verificar.cs -- pacote.zip` (deve sair APROVADO).
