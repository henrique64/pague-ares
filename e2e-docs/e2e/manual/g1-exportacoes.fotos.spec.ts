// Fotos do grupo g1 do manual "exportacoes" (modo de dados real): menu de Financeiro, Administrador e Contabilidade,
// tela Exportações em vários estados, e a aba Financeiro/Contábil de Solicitações com e sem o botão Exportar.
//
// Ambiente: o app de verdade (DOC_URL_BASE), com a massa fictícia da semente. ANTES de rodar, execute
// `node e2e-docs/e2e/manual/preparar-g1.ts` (ver o cabeçalho dele): fin02 fica com uma exportação Falhou, uma Cancelada
// e uma Expirada; fin03 e adm02 ficam sem nenhuma. Este spec confere isso no início e reprova com a instrução se o
// ambiente não estiver assim (cada rodada deixa exportações novas nessas contas, então o preparo vale para uma rodada).
// Concluída, Em processamento, Pendente, o aviso de sucesso e as recusas são feitos aqui, ao vivo, pela própria tela, e
// os pedidos grandes (500 títulos) são cancelados no fim.
//
// Contas (todas fictícias, @exemplo.invalid, mesma senha da semente, lida da variável de ambiente que o capturar indica):
//   Financeiro da configuração (DOC_USUARIO_ENV) — aba Financeiro/Contábil com o botão Exportar;
//   con.semente   — Contabilidade: menu sem Exportações e aba sem o botão Exportar;
//   adm02.semente — Administrador: menu e lista vazia;
//   fin02.semente — Financeiro: formulário, recusas e a lista com Falhou, Cancelada, Expirada, Concluída e as em andamento;
//   fin03.semente — Financeiro: a tela inteira com uma Em processamento e uma Concluída.
//
// Relógio: no modo real as datas das linhas vêm do servidor (a data de hoje), então o relógio da página não é fixado
// (um relógio diferente do servidor poderia invalidar o token de sessão). O calendário abre no mês de hoje.
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { test, expect, type Locator, type Page } from "@playwright/test";
import { ServidorDublado } from "../documentacao/dublagem.ts";
import { destacar, fotoDaTela, fotoDoRecorte, pastaDasFotos, soltarMouse } from "../documentacao/fotos.ts";

const aqui = path.dirname(fileURLToPath(import.meta.url));
const proibidos: string[] = JSON.parse(
  fs.readFileSync(path.resolve(aqui, "../../../docs/manual/exportacoes/fontes/proibidos.json"), "utf8"),
);

function variavel(nomeDaVariavel: string | undefined, padrao: string): string {
  const v = process.env[nomeDaVariavel ?? padrao] ?? process.env[padrao];
  if (!v) throw new Error(`Variável de ambiente ${nomeDaVariavel ?? padrao} não definida (rode por \`doc.ts capturar\`)`);
  return v;
}
const SENHA = variavel(process.env.DOC_SENHA_ENV, "DOC_PAGUEARES_SENHA");
const FIN = variavel(process.env.DOC_USUARIO_ENV, "DOC_PAGUEARES_USUARIO");
const CON = "con.semente@exemplo.invalid";
const ADM2 = "adm02.semente@exemplo.invalid";
const FIN2 = "fin02.semente@exemplo.invalid";
const FIN3 = "fin03.semente@exemplo.invalid";
const BASE = (process.env.DOC_URL_BASE ?? "").replace(/\/$/, "");

// ---------- API (só para conferir o ambiente e limpar no fim; as fotos vêm da tela) ----------
interface Job { id: number; statusDescricao: string }
async function tokenDe(email: string): Promise<string> {
  const r = await fetch(`${BASE}/api/Auth`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ username: email, password: SENHA }),
  });
  const token = ((await r.json()) as { data?: { token?: string } }).data?.token;
  if (!token) throw new Error(`login de ${email} recusado`);
  return token;
}
async function listarPelaApi(email: string): Promise<Job[]> {
  const l = await fetch(`${BASE}/api/exportacao`, { headers: { Authorization: `Bearer ${await tokenDe(email)}` } });
  return ((await l.json()) as { data: Job[] }).data;
}
async function cancelarAtivasPelaApi(email: string): Promise<void> {
  const token = await tokenDe(email);
  for (const j of await listarPelaApi(email)) {
    if (j.statusDescricao === "Pendente" || j.statusDescricao === "Em processamento") {
      await fetch(`${BASE}/api/exportacao/${j.id}`, { method: "DELETE", headers: { Authorization: `Bearer ${token}` } });
    }
  }
}

test.describe.configure({ mode: "serial" });

test.beforeAll(async () => {
  const instrucao = "Rode `node e2e-docs/e2e/manual/preparar-g1.ts` e tente de novo.";
  const situacoes = (await listarPelaApi(FIN2)).map((j) => j.statusDescricao).sort();
  if (situacoes.join(",") !== ["Cancelada", "Expirada", "Falhou"].join(",")) {
    throw new Error(`fin02 deveria ter exatamente Cancelada, Expirada e Falhou (tem: ${situacoes.join(", ") || "nada"}). ${instrucao}`);
  }
  for (const email of [FIN3, ADM2]) {
    if ((await listarPelaApi(email)).length > 0) throw new Error(`${email} deveria estar sem exportações. ${instrucao}`);
  }
});

test.afterAll(async () => {
  // Se um teste parou no meio, não deixa pedido grande rodando no servidor.
  for (const email of [FIN2, FIN3]) await cancelarAtivasPelaApi(email).catch(() => undefined);
});

let servidor: ServidorDublado;
test.beforeEach(async ({ page }) => {
  servidor = await ServidorDublado.montar(page, { bases: [], rotas: [], termosProibidos: proibidos });
});
test.afterEach(() => {
  servidor.afirmarSemFalhas();
});

// ---------- ajudantes de tela ----------
async function entrar(page: Page, email: string): Promise<void> {
  await page.goto("/");
  await page.locator("#login").fill(email);
  await page.locator("#password").fill(SENHA);
  await page.locator("input[type=submit]").click();
  await expect(page.locator(".sidebar-wrapper .nav")).toBeVisible();
  await expect(page.locator(".username")).toBeVisible();
}

async function abrirExportacoes(page: Page): Promise<void> {
  await page.locator(".sidebar-wrapper").getByRole("link", { name: "Exportações" }).click();
  await expect(page.locator("#btnGerar")).toBeVisible();
  await expect(page.locator("#btnGerar")).toBeEnabled();
  await expect(page.getByText("Carregando exportações...")).toBeHidden();
}

/** A rolagem da tela é do painel `.main-panel`, não da janela: rola o painel até `alvo` ficar `folga` px abaixo do topo. */
async function rolarPara(alvo: Locator, folga = 20): Promise<void> {
  await alvo.evaluate((el, f) => {
    const painel = document.querySelector(".main-panel") as HTMLElement;
    const topo = el.getBoundingClientRect().top - painel.getBoundingClientRect().top + painel.scrollTop;
    painel.scrollTop = Math.max(0, topo - f);
  }, folga);
  await expect(alvo).toBeInViewport();
}

async function noTopo(page: Page): Promise<void> {
  await page.locator(".main-panel").evaluate((p) => {
    p.scrollTop = 0;
  });
}

/** Confere que a tela não mostra token, e-mail fora da semente nem termo proibido, e que a URL não leva token. */
async function conferir(page: Page, id: string): Promise<void> {
  if (/token|chave=/i.test(page.url())) throw new Error(`${id}: a URL tem token`);
  const texto = await page.locator("body").innerText();
  if (/Bearer\s|eyJ[A-Za-z0-9_-]{10,}/.test(texto)) throw new Error(`${id}: a tela mostra token`);
  const fora = (texto.match(/[\w.+-]+@[\w-]+(\.[\w-]+)+/g) ?? []).filter((e) => !e.toLowerCase().endsWith("@exemplo.invalid"));
  if (fora.length > 0) throw new Error(`${id}: e-mail fora da semente na tela`);
  const proibido = proibidos.find((t) => texto.toLowerCase().includes(t.toLowerCase()));
  if (proibido) throw new Error(`${id}: termo proibido na tela`);
}

async function tela(page: Page, id: string): Promise<void> {
  await conferir(page, id);
  await fotoDaTela(page, id);
}

const campo = (page: Page, rotulo: string): Locator =>
  page.locator("mat-form-field", { has: page.locator("mat-label", { hasText: rotulo }) });
/** A caixa do campo, sem o espaço de mensagem de erro que o Material reserva embaixo (o realce fica justo). */
const caixa = (page: Page, rotulo: string): Locator => campo(page, rotulo).locator(".mat-mdc-text-field-wrapper");

async function escolher(page: Page, rotulo: string, opcao: string): Promise<void> {
  await campo(page, rotulo).locator("mat-select").click();
  await page.getByRole("option", { name: opcao, exact: true }).click();
  await expect(page.getByRole("option")).toHaveCount(0);
}

const aviso = (page: Page): Locator => page.locator(".mat-mdc-snack-bar-container");
const tituloDaLista = (page: Page): Locator => page.locator(".card-header", { hasText: "Minhas exportações" });

/** Recorte do menu lateral: só a lista de itens (o nome de quem entrou fica fora, e a máscara cobre o que sobrar). */
async function fotoDoMenu(page: Page, id: string): Promise<void> {
  await conferir(page, id);
  await fotoDoRecorte(page, page.locator(".sidebar-wrapper ul.nav"), id, 14, { folga: 0 });
}

/** Faixa da lista "Minhas exportações": do título do cartão até o fim da linha `ate` (o cartão inteiro, na largura). */
async function fotoDaFaixa(page: Page, id: string, ate: Locator): Promise<void> {
  await rolarPara(tituloDaLista(page), 24);
  await conferir(page, id);
  await soltarMouse(page);
  const cartao = await page.locator(".card", { has: tituloDaLista(page) }).boundingBox();
  const cabecalho = await tituloDaLista(page).boundingBox();
  const fim = await ate.boundingBox();
  if (!cartao || !cabecalho || !fim) throw new Error(`${id}: faixa da lista sem caixa`);
  const x = Math.max(0, Math.floor(cartao.x - 8));
  const y = Math.max(0, Math.floor(cabecalho.y - 14));
  const largura = Math.ceil(cartao.width + 16);
  const altura = Math.ceil(fim.y + fim.height + 12 - y);
  const bruto = process.env.DOC_MASCARAS;
  const mascaras = bruto ? (JSON.parse(bruto) as string[]).map((s) => page.locator(s)) : [];
  await page.screenshot({
    path: path.join(pastaDasFotos(), `${id}.png`),
    clip: { x, y, width: Math.min(largura, 1360 - x), height: Math.min(altura, 860 - y) },
    animations: "disabled",
    caret: "hide",
    mask: mascaras,
  });
}

/** Digita o período da Solicitação, no formato dd/MM/aaaa que o app lê como dia/mês/ano. */
async function digitarPeriodo(page: Page, inicio: string, fim: string): Promise<void> {
  const periodo = campo(page, "Período da Solicitação");
  await periodo.locator('input[placeholder="Início"]').fill(inicio);
  await periodo.locator('input[placeholder="Fim"]').fill(fim);
  await periodo.locator('input[placeholder="Fim"]').blur();
  await expect(periodo.locator('input[placeholder="Fim"]')).toHaveValue(fim);
}

// ---------- Financeiro (conta da configuração): Solicitações com Exportar ----------
test("aba Financeiro/Contábil com o botão Exportar", async ({ page }) => {
  await entrar(page, FIN);
  await page.locator(".sidebar-wrapper").getByRole("link", { name: "Solicitações" }).click();
  await page.getByRole("tab", { name: "Financeiro/Contábil" }).click();
  await expect(page.getByRole("tab", { name: "Financeiro/Contábil" })).toHaveAttribute("aria-selected", "true");
  await expect(page.locator(".loader-text")).toHaveCount(0);
  const exportar = page.getByRole("button", { name: "Exportar", exact: true });
  await expect(exportar).toBeVisible();
  await expect(page.getByRole("button", { name: "Limpar Filtros" })).toBeVisible();
  await expect(page.getByRole("button", { name: "Filtrar", exact: true })).toBeVisible();
  await expect(page.getByRole("tabpanel", { name: "Financeiro/Contábil" }).locator("tbody tr").first()).toBeVisible();
  const apagar = await destacar(page, exportar);
  await tela(page, "pagamentos-botao-exportar");
  await apagar();
});

// ---------- Contabilidade: menu sem Exportações e aba sem o botão ----------
test("menu da Contabilidade e aba Financeiro/Contábil sem o botão Exportar", async ({ page }) => {
  await entrar(page, CON);
  await expect(page.locator(".sidebar-wrapper ul.nav li")).toHaveText([/Home/, /Solicitações/]);
  await expect(page.locator(".sidebar-wrapper").getByRole("link", { name: "Exportações" })).toHaveCount(0);
  await fotoDoMenu(page, "menu-exportacoes-contabilidade");

  await page.getByRole("tab", { name: "Financeiro/Contábil" }).click();
  await expect(page.getByRole("tab", { name: "Financeiro/Contábil" })).toHaveAttribute("aria-selected", "true");
  await expect(page.locator(".loader-text")).toHaveCount(0);
  await expect(page.getByRole("button", { name: "Filtrar", exact: true })).toBeVisible();
  await expect(page.getByRole("button", { name: "Exportar", exact: true })).toHaveCount(0);
  await expect(page.getByRole("tabpanel", { name: "Financeiro/Contábil" }).getByRole("heading", { name: "Solicitações de Pagamento" })).toBeVisible();
  await tela(page, "pagamentos-sem-botao-exportar");
});

// ---------- Administrador: menu e lista vazia ----------
test("menu do Administrador e lista de exportações vazia", async ({ page }) => {
  await entrar(page, ADM2);
  await abrirExportacoes(page);
  await expect(page.locator(".sidebar-wrapper ul.nav li")).toHaveText([/Home/, /Solicitações/, /Exportações/, /Configurações/]);
  await fotoDoMenu(page, "menu-exportacoes-administrador");

  await expect(page.getByText("Você ainda não gerou nenhuma exportação nos últimos 30 dias.")).toBeVisible();
  await rolarPara(tituloDaLista(page), 20);
  await tela(page, "exportacoes-lista-vazia");
});

// ---------- Financeiro (fin03): a tela inteira, com uma Em processamento e uma Concluída ----------
test("tela Exportações com uma exportação concluída e uma em processamento", async ({ page }) => {
  test.setTimeout(240_000);
  page.on("dialog", (d) => void d.accept());
  await entrar(page, FIN3);
  await abrirExportacoes(page);
  const linhas = page.locator("tbody tr");

  // uma exportação pequena que termina sozinha: Concluída
  await escolher(page, "Tipo de título", "Solicitação");
  await digitarPeriodo(page, "01/11/2025", "30/11/2025");
  await page.locator("#btnGerar").click();
  await expect(aviso(page)).toContainText("Exportação solicitada.");
  await expect(linhas.first().locator(".status")).toHaveText("Concluída", { timeout: 60_000 });
  await page.getByRole("button", { name: "Limpar Filtros" }).click();
  await expect(page.locator("#tipoTitulo")).toContainText("Ambos (Solicitações e RDDVs)");
  await expect(aviso(page)).toBeHidden({ timeout: 15_000 });

  // e um pedido grande (todos os títulos, com anexos) que fica Em processamento
  await page.locator("#btnGerar").click();
  await expect(linhas).toHaveCount(2);
  await expect(linhas.first().locator(".status")).toHaveText("Em processamento", { timeout: 30_000 });
  await expect(linhas.first()).toContainText(/\(([1-9]\d*) de 500 títulos\)/, { timeout: 30_000 });
  await expect(linhas.nth(1).locator(".status")).toHaveText("Concluída");
  await expect(aviso(page)).toBeHidden({ timeout: 15_000 });

  // o fim do formulário (formulário no padrão) e a lista, com o menu Exportações em destaque
  await expect(page.locator(".sidebar-wrapper li.active")).toHaveText(/Exportações/);
  await expect(page.locator("#incluirAnexos input")).toBeChecked();
  await rolarPara(page.locator("#btnGerar"), 340);
  await tela(page, "exportacoes-tela-com-lista");

  // limpeza
  await rolarPara(tituloDaLista(page), 24);
  await linhas.first().getByRole("button", { name: "Cancelar" }).click();
  await expect(linhas.first().locator(".status")).toHaveText("Cancelada", { timeout: 60_000 });
});

// ---------- Financeiro (fin02): formulário, recusa e lista ----------
test("formulário: padrão, tipos, período e recusa sem títulos", async ({ page }) => {
  await entrar(page, FIN2);
  await abrirExportacoes(page);

  // padrão ao abrir
  await expect(page.locator("#tipoTitulo")).toContainText("Ambos (Solicitações e RDDVs)");
  await expect(page.locator("#incluirAnexos input")).toBeChecked();
  await expect(page.getByText("Extraia o ZIP antes de abrir o Excel")).toBeVisible();
  await noTopo(page);
  await tela(page, "exportacoes-formulario-padrao");

  // seletor de período aberto
  await campo(page, "Período de Vencimento").locator("mat-datepicker-toggle button").click();
  await expect(page.locator("mat-calendar")).toBeVisible();
  await tela(page, "exportacoes-seletor-de-periodo");
  await page.keyboard.press("Escape");
  await expect(page.locator("mat-calendar")).toHaveCount(0);

  // Tipo de título = RDDV
  await escolher(page, "Tipo de título", "RDDV");
  const soSolicitacao = [
    caixa(page, "Número do Documento (só Solicitação)"),
    caixa(page, "Tipo de solicitação (só Solicitação)"),
    caixa(page, "Tipo de pagamento (só Solicitação)"),
    caixa(page, "Forma de pagamento (só Solicitação)"),
  ];
  await expect(campo(page, "Tipo de solicitação (só Solicitação)").locator("mat-select")).toHaveAttribute("aria-disabled", "true");
  await expect(campo(page, "Tipo de pagamento (só Solicitação)").locator("mat-select")).toHaveAttribute("aria-disabled", "true");
  await expect(campo(page, "Forma de pagamento (só Solicitação)").locator("mat-select")).toHaveAttribute("aria-disabled", "true");
  await expect(campo(page, "Número do Documento (só Solicitação)").locator("input")).toBeDisabled();
  await expect(campo(page, "Tipo de relatório (só RDDV)").locator("mat-select")).toHaveAttribute("aria-disabled", "false");
  let apagar = await destacar(page, soSolicitacao);
  await tela(page, "exportacoes-tipo-rddv");
  await apagar();

  // formulário preenchido: Solicitação, período da solicitação e Status Gestor
  await escolher(page, "Tipo de título", "Solicitação");
  await digitarPeriodo(page, "01/11/2025", "31/12/2025");
  await escolher(page, "Status Gestor", "Autorizado");
  await expect(campo(page, "Tipo de relatório (só RDDV)").locator("mat-select")).toHaveAttribute("aria-disabled", "true");
  await expect(campo(page, "Tipo de pagamento (só Solicitação)").locator("mat-select")).toHaveAttribute("aria-disabled", "false");
  apagar = await destacar(page, caixa(page, "Tipo de relatório (só RDDV)"));
  await tela(page, "exportacoes-formulario-preenchido");
  await apagar();

  // recusa: nenhum título com esses filtros (um código que não existe)
  await page.locator("#codigo").fill("999999");
  await page.locator("#btnGerar").click();
  await expect(aviso(page)).toContainText("Nenhum título encontrado com os filtros informados.");
  await tela(page, "exportacoes-aviso-sem-titulos");
  await page.getByRole("button", { name: "Limpar Filtros" }).click();
  await expect(page.locator("#codigo")).toHaveValue("");
  await expect(page.locator("#tipoTitulo")).toContainText("Ambos (Solicitações e RDDVs)");
  await expect(aviso(page)).toBeHidden({ timeout: 15_000 });

  // a lista com Falhou, Cancelada e Expirada, sem ações
  const linhas = page.locator("tbody tr");
  await expect(linhas).toHaveCount(3);
  for (const [i, situacao] of ["Falhou", "Cancelada", "Expirada"].entries()) {
    await expect(linhas.nth(i).locator(".status")).toHaveText(situacao);
    await expect(linhas.nth(i).locator("button")).toHaveCount(0);
  }
  await expect(linhas.first()).toContainText("A exportação foi interrompida por um reinício do servidor. Gere a exportação de novo.");
  await fotoDaFaixa(page, "exportacoes-lista-falhou-cancelada-expirada", linhas.nth(2));
});

// ---------- Financeiro (fin02): ao vivo, Concluída, Em processamento, Pendente e a recusa 429 ----------
test("aviso de solicitada, lista em processamento e recusa por excesso de pedidos", async ({ page }) => {
  test.setTimeout(240_000);
  const perguntas: string[] = [];
  page.on("dialog", (d) => {
    perguntas.push(d.message());
    void d.accept();
  });
  await entrar(page, FIN2);
  await abrirExportacoes(page);
  const linhas = page.locator("tbody tr");
  await expect(linhas).toHaveCount(3);

  // uma exportação pequena que termina sozinha: Concluída
  await escolher(page, "Tipo de título", "Solicitação");
  await digitarPeriodo(page, "01/11/2025", "30/11/2025");
  await page.locator("#btnGerar").click();
  await expect(aviso(page)).toContainText("Exportação solicitada.");
  await expect(linhas.first().locator(".status")).toHaveText("Concluída", { timeout: 60_000 });
  await page.getByRole("button", { name: "Limpar Filtros" }).click();
  await expect(page.locator("#tipoTitulo")).toContainText("Ambos (Solicitações e RDDVs)");
  await expect(aviso(page)).toBeHidden({ timeout: 15_000 });

  // pedido grande: aviso de sucesso e a nova linha no topo da lista
  await page.locator("#btnGerar").click();
  await expect(aviso(page)).toContainText("Exportação solicitada. Ela aparece na lista abaixo e você pode baixar quando ficar pronta.");
  await expect(linhas).toHaveCount(5);
  await rolarPara(tituloDaLista(page), 24);
  await expect(linhas.first().locator(".status")).toHaveText(/Pendente|Em processamento/);
  await tela(page, "exportacoes-aviso-solicitada");
  await expect(aviso(page)).toBeHidden({ timeout: 15_000 });

  // a linha Em processamento, com barra e "N de 500 títulos", ao lado da Concluída (com Tamanho, Expira em, Baixar, Excluir)
  await expect(linhas.first().locator(".status")).toHaveText("Em processamento", { timeout: 30_000 });
  await expect(linhas.first()).toContainText(/\(([1-9]\d*) de 500 títulos\)/, { timeout: 30_000 });
  await expect(linhas.first().getByRole("button", { name: "Cancelar" })).toBeVisible();
  await expect(linhas.first().getByRole("button", { name: "Baixar" })).toHaveCount(0);
  await expect(linhas.nth(1).locator(".status")).toHaveText("Concluída");
  await expect(linhas.nth(1).getByRole("button", { name: "Baixar" })).toBeVisible();
  await expect(linhas.nth(1).getByRole("button", { name: "Excluir" })).toBeVisible();
  await fotoDaFaixa(page, "exportacoes-lista-concluida", linhas.nth(1));

  // segundo pedido grande: espera atrás do primeiro, Pendente
  await noTopo(page);
  await page.locator("#btnGerar").click();
  await expect(aviso(page)).toContainText("Exportação solicitada.");
  await expect(linhas).toHaveCount(6);
  await expect(linhas.first().locator(".status")).toHaveText("Pendente");
  await expect(linhas.first().getByRole("button", { name: "Cancelar" })).toBeVisible();
  await expect(linhas.nth(1).locator(".status")).toHaveText("Em processamento");
  await expect(aviso(page)).toBeHidden({ timeout: 15_000 });
  await expect(linhas.nth(1)).toContainText(/\(([1-9]\d*) de 500 títulos\)/);
  await fotoDaFaixa(page, "exportacoes-lista-em-processamento", linhas.nth(1));

  // recusa: terceiro pedido seguido, com duas exportações em andamento
  await noTopo(page);
  await page.locator("#btnGerar").click();
  await expect(aviso(page)).toContainText(
    "Você já tem 2 exportações em andamento, o máximo permitido. Aguarde uma terminar ou cancele uma delas.",
  );
  await expect(linhas).toHaveCount(6);
  await tela(page, "exportacoes-recusa-429");
  await expect(aviso(page)).toBeHidden({ timeout: 15_000 });

  // limpeza: cancela o que está em andamento, pela própria tela
  await rolarPara(tituloDaLista(page), 24);
  await linhas.first().getByRole("button", { name: "Cancelar" }).click();
  await expect(aviso(page)).toContainText("Cancelamento solicitado.");
  expect(perguntas).toEqual(["Cancelar esta exportação?"]);
  await expect(linhas.first().locator(".status")).toHaveText("Cancelada", { timeout: 20_000 });
  await expect(aviso(page)).toBeHidden({ timeout: 15_000 });
  await linhas.nth(1).getByRole("button", { name: "Cancelar" }).click();
  await expect(linhas.nth(1).locator(".status")).toHaveText("Cancelada", { timeout: 60_000 });
});
