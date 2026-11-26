// Captura as 9 telas de evidência do fluxo de exportação, com os dados fictícios da semente.
//
// Uso (a partir da raiz do repositório, com a stack Docker de pé e a semente aplicada):
//   cd tools/exportacao/capturas && npm install && cd ../../..
//   node tools/exportacao/capturas/capturar.ts
//
// Variáveis de ambiente:
//   PAGUEARES_SEMENTE_SENHA  (obrigatória) senha dos usuários fictícios; nunca é impressa nem gravada.
//   CAPTURAS_URL             URL do front (padrão http://localhost:8080).
//   CAPTURAS_PLAYWRIGHT      módulo do Playwright a carregar (padrão "playwright"; serve também
//                            "playwright-core" ou um caminho de pasta, para quem não instalou o pacote aqui).
//
// Usa o Edge instalado (channel "msedge"); nenhum navegador é baixado. Antes de cada PNG, o script
// confere que o texto visível não traz token, e-mail fora de @exemplo.invalid nem URL com token.
import { createRequire } from "node:module";
import { mkdirSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const require = createRequire(import.meta.url);
const { chromium } = require(process.env["CAPTURAS_PLAYWRIGHT"] ?? "playwright");

const raiz = resolve(dirname(fileURLToPath(import.meta.url)), "..", "..", "..");
const saida = resolve(raiz, "docs", "evidencias", "img");
const previa = resolve(raiz, "docs", "evidencias", "dados", "previa-pacote.html");
const url = (process.env["CAPTURAS_URL"] ?? "http://localhost:8080").replace(/\/$/, "");
const senha = process.env["PAGUEARES_SEMENTE_SENHA"];
if (!senha) {
  console.error("Defina PAGUEARES_SEMENTE_SENHA na sessão (senha dos usuários fictícios da semente).");
  process.exit(2);
}

const log = (m: string) => console.log(`[${new Date().toTimeString().slice(0, 8)}] ${m}`);

async function conferir(page: any, nome: string) {
  const u: string = page.url();
  if (/token=/i.test(u)) throw new Error(`${nome}: a URL contém token`);
  const texto: string = await page.locator("body").innerText();
  if (/Bearer\s|eyJ[A-Za-z0-9_-]{10,}|token=/i.test(texto)) throw new Error(`${nome}: texto com token`);
  const emails = texto.match(/[\w.+-]+@[\w-]+(\.[\w-]+)+/g) ?? [];
  const fora = emails.filter((e) => !e.toLowerCase().endsWith("@exemplo.invalid"));
  if (fora.length > 0) throw new Error(`${nome}: e-mail fora da semente visível (${fora.length})`);
}

async function foto(page: any, nome: string) {
  await conferir(page, nome);
  await page.screenshot({ path: resolve(saida, nome) });
  log(`gravado ${nome}`);
}

const browser = await chromium.launch({ channel: "msedge", headless: true });

async function sessao(usuario: string) {
  const ctx = await browser.newContext({ viewport: { width: 1366, height: 768 }, locale: "pt-BR" });
  const page = await ctx.newPage();
  await page.goto(`${url}/`);
  await page.fill("#login", `${usuario}@exemplo.invalid`);
  await page.fill("#password", senha);
  await page.click("input[type=submit]");
  await page.waitForURL(/#\/(?!login)/, { timeout: 30000 });
  return page;
}

function combo(page: any, rotulo: string) {
  return page.locator("mat-form-field", { has: page.locator("mat-label", { hasText: rotulo }) }).locator("mat-select");
}

async function escolher(page: any, rotulo: string, opcao: string) {
  await combo(page, rotulo).click();
  await page.getByRole("option", { name: opcao, exact: true }).click();
}

async function listaNoTopo(page: any) {
  await page.locator(".card-title", { hasText: "Minhas exportações" }).evaluate((el: HTMLElement) =>
    el.scrollIntoView({ block: "start" }),
  );
  await page.waitForTimeout(400);
}

try {
  mkdirSync(saida, { recursive: true });

  // ---- Financeiro: menu, formulário, andamento e conclusão ----
  const fin = await sessao("fin.semente");
  log(`FIN logado: ${fin.url()}`);
  await fin.goto(`${url}/#/exportacoes`);
  await fin.waitForSelector("#btnGerar", { timeout: 30000 });
  await fin.waitForSelector("tbody tr", { timeout: 30000 });
  await fin.waitForTimeout(800);
  await foto(fin, "01-menu-exportacoes.png");

  await escolher(fin, "Tipo de título", "Solicitação");
  // As datas são digitadas no formato natural dd/MM/aaaa, que o PtBrDateAdapter do app lê como dia/mês/ano.
  await fin.locator('input[placeholder="Início"]').first().fill("01/11/2025");
  await fin.locator('input[placeholder="Fim"]').first().fill("31/12/2025");
  await escolher(fin, "Status Gestor", "Autorizado");
  await escolher(fin, "Status Financeiro", "Autorizado");
  await escolher(fin, "Solicitante", "Usuario Ficticio Solicitante 01");
  await fin.locator("#btnGerar").focus();
  await fin.waitForTimeout(500);
  await foto(fin, "02-formulario-filtros.png");

  // Exportação completa (todos os títulos da semente), para dar tempo de ver o andamento.
  // "Limpar Filtros" devolve o formulário ao estado vazio; confere que nenhum campo de data ficou com texto.
  await fin.getByRole("button", { name: "Limpar Filtros" }).click();
  await fin.waitForTimeout(500);
  const camposData = fin.locator('input[placeholder="Início"], input[placeholder="Fim"]');
  for (let i = 0; i < (await camposData.count()); i++) {
    if ((await camposData.nth(i).inputValue()) !== "") throw new Error("Limpar Filtros deixou texto num campo de data");
  }
  await fin.waitForSelector("tbody tr", { timeout: 30000 });
  await fin.locator("#btnGerar").click();
  log("Exportação completa solicitada.");
  const barra = fin.locator("tbody tr").first().locator("[role=progressbar]");
  let pct = 0;
  const limite = Date.now() + 180000;
  while (Date.now() < limite) {
    pct = Number((await barra.getAttribute("aria-valuenow").catch(() => "0")) ?? "0");
    const situacao = await fin.locator("tbody tr").first().locator(".status").innerText().catch(() => "");
    if (/Em processamento/i.test(situacao) && pct >= 30 && pct <= 85) break;
    await fin.waitForTimeout(500);
  }
  if (!(pct >= 30 && pct <= 85)) throw new Error(`não peguei a exportação em andamento (último percentual ${pct})`);
  await listaNoTopo(fin);
  await foto(fin, "03-exportacao-em-andamento.png");

  await fin.locator("tbody tr").first().locator("button.acao-baixar").waitFor({ timeout: 240000 });
  await fin.waitForTimeout(800);
  await listaNoTopo(fin);
  await foto(fin, "04-exportacao-concluida.png");

  // ---- Contabilidade: sem Exportações no menu e acesso negado ----
  const con = await sessao("con.semente");
  log(`CON logado: ${con.url()}`);
  await con.goto(`${url}/#/home`);
  await con.waitForTimeout(3000);
  if ((await con.locator(".sidebar").getByText("Exportações").count()) !== 0) throw new Error("CON vê o item Exportações");
  await foto(con, "05-menu-contabilidade.png");
  await con.goto(`${url}/#/exportacoes`);
  await con.waitForURL(/#\/payments/, { timeout: 15000 });
  await con.waitForTimeout(2500);
  log(`CON ao abrir /#/exportacoes caiu em: ${con.url()}`);
  await foto(con, "06-acesso-negado-redireciona.png");

  // ---- Prévia do pacote (HTML gerado pelo verificador) ----
  const ctxPrevia = await browser.newContext({ viewport: { width: 1366, height: 768 } });
  const pv = await ctxPrevia.newPage();
  await pv.goto(pathToFileURL(previa).href);
  await pv.locator("h2", { hasText: "Aba Solicitações" }).evaluate((el: HTMLElement) => el.scrollIntoView({ block: "start" }));
  await pv.waitForTimeout(300);
  await foto(pv, "07-previa-planilha.png");
  await pv.evaluate(() => window.scrollTo(0, 0));
  await pv.waitForTimeout(300);
  await foto(pv, "08-arvore-pacote.png");

  // ---- Botão Exportar legado (visão Financeiro/Contábil da tela de pagamentos) ----
  await fin.goto(`${url}/#/payments`);
  await fin.getByRole("tab", { name: "Financeiro/Contábil" }).click();
  await fin.locator("tbody tr").first().waitFor({ timeout: 30000 });
  await fin.getByRole("button", { name: "Exportar", exact: true }).waitFor({ timeout: 10000 });
  await fin.waitForTimeout(1000);
  await foto(fin, "09-botao-exportar-legado.png");

  log("Concluído: 9 capturas gravadas.");
} finally {
  await browser.close();
}
