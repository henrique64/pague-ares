// Grupo de fotos g2 do manual "exportacoes": o pacote gerado (árvore extraída, planilhas, índice e LEIA-ME).
// Roda por `doc.ts capturar --manual exportacoes --grupos g2` (config: playwright.g2.config.ts).
//
// O pacote não é tela do app: as fotos saem das páginas de `g2-fontes/paginas/`, montadas de um pacote REAL gerado pela
// API local (dados fictícios da semente) por `g2-fontes/montar-paginas.ts` (receita no README da pasta). As páginas são
// abertas por `file://`; nenhuma chama a API, então o spec não depende do app nem do banco. Cada foto vem depois de um
// `expect` do conteúdo que ela mostra.
import { expect, test, type Page } from "@playwright/test";
import { pathToFileURL } from "node:url";
import { ServidorDublado } from "../documentacao/dublagem.ts";
import { fotoDoElemento } from "../documentacao/fotos.ts";

const AGORA_DO_MANUAL = new Date("2026-11-26T12:00:00-03:00");
const PROIBIDOS = ["Grant Thornton", "Leonardo Luiz da Silva", "Leonardo Silva", "Raphael Barbosa de Lima", "Raphael Barbosa", "Rafael Campos Cabral", "Rafael Cabral"];

let servidor: ServidorDublado;

const abrir = (page: Page, pagina: string): Promise<unknown> =>
  page.goto(pathToFileURL(new URL(`./g2-fontes/paginas/${pagina}.html`, import.meta.url).pathname.replace(/^\/([A-Za-z]:)/, "$1")).href);

test.beforeEach(async ({ page }) => {
  await page.clock.setFixedTime(AGORA_DO_MANUAL);
  // Modo real e páginas locais: o dublê não intercepta nada e só coleta erros de página (afirmarSemFalhas no afterEach).
  servidor = await ServidorDublado.montar(page, { bases: [], rotas: [], termosProibidos: PROIBIDOS });
});

test.afterEach(() => {
  servidor.afirmarSemFalhas();
});

const janela = (page: Page) => page.locator("#janela");

async function semTermoProibido(page: Page): Promise<void> {
  const texto = (await page.locator("body").innerText()).toLowerCase();
  for (const termo of PROIBIDOS) expect(texto).not.toContain(termo.toLowerCase());
}

test("árvore do pacote extraído", async ({ page }) => {
  await abrir(page, "arvore");
  for (const raiz of ["titulos.xlsx", "indice.csv", "indice.json", "LEIA-ME.txt"]) {
    await expect(page.locator(".l .n", { hasText: raiz })).toHaveCount(1);
  }
  await expect(page.locator(".l.pasta .n", { hasText: /SOL-\d{6,}/ }).first()).toBeVisible();
  await expect(page.locator(".l.pasta .n", { hasText: /RDDV-\d{6,}/ }).first()).toBeVisible();
  await expect(page.locator(".l .n", { hasText: "(2).PDF" })).toHaveCount(1);
  await expect(page.locator(".titulo b")).toHaveText(/^exportacao-titulos-\d+-\d{8}-\d{4}$/);
  await semTermoProibido(page);
  await fotoDoElemento(janela(page), "pacote-arvore-extraida", { fixosParaEstaticos: [] });
});

test("planilha: aba Solicitações com as quatro abas", async ({ page }) => {
  await abrir(page, "planilha-solicitacoes");
  await expect(page.locator(".guia")).toHaveText(["Solicitações", "RDDVs", "Despesas dos RDDVs", "Anexos"]);
  await expect(page.locator(".guia.ativa")).toHaveText("Solicitações");
  await expect(page.locator("tr.primeira td").first()).toHaveText("Código▾");
  await expect(page.locator("td a", { hasText: /^SOL-\d{6,}$/ }).first()).toBeVisible();
  await semTermoProibido(page);
  await fotoDoElemento(janela(page), "pacote-planilha-abas", { fixosParaEstaticos: [] });
});

test("planilha: aba Anexos", async ({ page }) => {
  await abrir(page, "planilha-anexos");
  await expect(page.locator(".guia.ativa")).toHaveText("Anexos");
  await expect(page.locator("tr.primeira td")).toHaveText([
    "Código do título▾", "Tipo do título▾", "Tipo de documento▾", "Nome original▾", "Arquivo no pacote▾", "Tamanho (bytes)▾", "SHA-256▾", "Data de envio▾",
  ]);
  await expect(page.locator("td", { hasText: /\(2\)\.PDF$/ })).toHaveCount(1);
  await semTermoProibido(page);
  await fotoDoElemento(janela(page), "pacote-planilha-anexos", { fixosParaEstaticos: [] });
});

test("índice em CSV", async ({ page }) => {
  await abrir(page, "indice-csv");
  await expect(page.locator(".linha").first()).toHaveText("titulo;tipo_titulo;tipo_documento;nome_original;arquivo;caminho_relativo;tamanho_bytes;sha256");
  await expect(page.locator(".linha").nth(1)).toContainText(/^(SOL|RDDV)-\d{6,};(Solicitação|RDDV);/);
  await expect(page.locator(".linha", { hasText: /^SOL-\d{6,};Solicitação;/ }).first()).toBeVisible();
  await expect(page.locator(".linha", { hasText: /^RDDV-\d{6,};RDDV;/ }).first()).toBeVisible();
  await semTermoProibido(page);
  await fotoDoElemento(janela(page), "pacote-indice-csv", { fixosParaEstaticos: [] });
});

test("LEIA-ME.txt", async ({ page }) => {
  await abrir(page, "leia-me");
  await expect(page.locator(".linha").first()).toHaveText("PagueAres - Exportação de títulos com anexos");
  await expect(page.getByText("IMPORTANTE: extraia o arquivo ZIP inteiro para uma pasta antes de abrir o Excel.")).toBeVisible();
  await expect(page.locator(".linha").nth(1)).toHaveText(/^Exportação nº \d+, gerada em \d{2}\/\d{2}\/\d{4} \d{2}:\d{2} \(horário de Brasília, UTC-03:00\)\.$/);
  await expect(page.getByText("Filtros usados:")).toBeVisible();
  await expect(page.getByText("- Anexos incluídos: sim")).toBeVisible();
  await semTermoProibido(page);
  await fotoDoElemento(janela(page), "pacote-leia-me", { fixosParaEstaticos: [] });
});

test("prévia do Pagamentos.xlsx", async ({ page }) => {
  await abrir(page, "pagamentos");
  await expect(page.locator(".guia.ativa")).toHaveText("Pagamentos");
  await expect(page.locator("tr.primeira td")).toHaveText([
    "Código▾", "Tipo▾", "Data de inclusão▾", "Data de vencimento▾", "Nº do documento▾", "Solicitante▾", "Parceiro▾", "Valor▾", "Status gestor▾", "Status contábil▾", "Status financeiro▾",
  ].map((s) => s.replace("▾", "")));
  await expect(page.locator("td", { hasText: /^RDDV - / }).first()).toBeVisible();
  await expect(page.locator("td", { hasText: /^Pagamento$/ }).first()).toBeVisible();
  // Nas linhas de RDDV o "Nº do documento" sai vazio (antes saía "#").
  const linhaRddv = page.locator("tr", { has: page.locator("td", { hasText: /^RDDV - / }) }).first();
  await expect(linhaRddv.locator("td").nth(4)).toHaveText("");
  await semTermoProibido(page);
  await fotoDoElemento(janela(page), "pagamentos-excel-previa", { fixosParaEstaticos: [] });
});
