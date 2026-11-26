// Vídeo narrado da tarefa "exportar-titulos-com-anexos" (manual "exportacoes", modo de dados real).
// Roteiro: docs/manual/exportacoes/video/exportar-titulos-com-anexos.narracao.md (um passo por `narrador.passo`, na ordem).
//
// Ambiente: o app de verdade (DOC_URL_BASE), com a massa fictícia da semente, e o Financeiro da configuração
// (DOC_PAGUEARES_USUARIO). O pedido é pequeno (Solicitações de novembro de 2025, cerca de 30 títulos) para concluir em
// segundos. O pedido e o que o vídeo cria são apagados da lista pela API no fim (só esta conta).
//
// Cenas do pacote (p11 e p12): o ZIP não é tela do app. As cenas usam as páginas de g2-fontes/paginas/, montadas de um
// pacote real da mesma API (README em g2-fontes/), abertas por file:// na mesma aba. A volta ao app (p13) é pelo
// histórico do navegador, e a sessão continua.
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";
import { test, expect } from "../../documentacao/narrador.ts";
import type { Locator, Page } from "@playwright/test";
import { ServidorDublado } from "../../documentacao/dublagem.ts";

const aqui = path.dirname(fileURLToPath(import.meta.url));
const proibidos: string[] = JSON.parse(
  fs.readFileSync(path.resolve(aqui, "../../../../docs/manual/exportacoes/fontes/proibidos.json"), "utf8"),
);

function variavel(nomeDaVariavel: string | undefined, padrao: string): string {
  const v = process.env[nomeDaVariavel ?? padrao] ?? process.env[padrao];
  if (!v) throw new Error(`Variável de ambiente ${nomeDaVariavel ?? padrao} não definida (rode por \`doc.ts video\`)`);
  return v;
}
const SENHA = variavel(process.env.DOC_SENHA_ENV, "DOC_PAGUEARES_SENHA");
const FIN = variavel(process.env.DOC_USUARIO_ENV, "DOC_PAGUEARES_USUARIO");
const BASE = (process.env.DOC_URL_BASE ?? "").replace(/\/$/, "");

const paginaDoPacote = (pagina: string): string =>
  pathToFileURL(path.resolve(aqui, "../g2-fontes/paginas", `${pagina}.html`)).href;

// ---------- API: só para limpar o que o vídeo cria ----------
// O que já existia na lista da conta (outras exportações da semente) não é tocado: só os pedidos criados por esta rodada.
const existentes = new Set<number>();
async function jobsDaConta(): Promise<{ cab: Record<string, string>; jobs: { id: number; statusDescricao: string }[] } | null> {
  const r = await fetch(`${BASE}/api/Auth`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ username: FIN, password: SENHA }),
  });
  const token = ((await r.json()) as { data?: { token?: string } }).data?.token;
  if (!token) return null;
  const cab = { Authorization: `Bearer ${token}` };
  const l = await fetch(`${BASE}/api/exportacao`, { headers: cab });
  return { cab, jobs: ((await l.json()) as { data: { id: number; statusDescricao: string }[] }).data };
}
/** Antes: cancela o que estiver em andamento e anota o que já existe. Depois: apaga o que a rodada criou. */
async function prepararConta(): Promise<void> {
  const c = await jobsDaConta();
  if (!c) return;
  for (const j of c.jobs) {
    if (j.statusDescricao === "Pendente" || j.statusDescricao === "Em processamento") {
      await fetch(`${BASE}/api/exportacao/${j.id}`, { method: "DELETE", headers: c.cab });
    }
  }
  const depois = await jobsDaConta();
  for (const j of depois?.jobs ?? []) existentes.add(j.id);
}
async function limparOQueCriou(): Promise<void> {
  const c = await jobsDaConta();
  if (!c) return;
  for (const j of c.jobs) {
    if (!existentes.has(j.id)) await fetch(`${BASE}/api/exportacao/${j.id}`, { method: "DELETE", headers: c.cab });
  }
}

let servidor: ServidorDublado;
test.beforeEach(async ({ page }) => {
  servidor = await ServidorDublado.montar(page, { bases: [], rotas: [], termosProibidos: proibidos });
  // O nome de quem entrou fica oculto em toda página do app (mesma máscara das fotos: `.username`).
  await page.addInitScript(() => {
    const montar = (): void => {
      const s = document.createElement("style");
      s.textContent = ".username{visibility:hidden !important}";
      document.documentElement.appendChild(s);
    };
    if (document.documentElement) montar();
    else addEventListener("DOMContentLoaded", montar);
  });
});
test.afterEach(() => {
  servidor.afirmarSemFalhas();
});
test.afterAll(async () => {
  await limparOQueCriou().catch(() => undefined);
});

const campo = (page: Page, rotulo: string): Locator =>
  page.locator("mat-form-field", { has: page.locator("mat-label", { hasText: rotulo }) });
const aviso = (page: Page): Locator => page.locator(".mat-mdc-snack-bar-container");
const tituloDaLista = (page: Page): Locator => page.locator(".card-header", { hasText: "Minhas exportações" });

/** A rolagem da tela é do painel `.main-panel`: rola até `alvo` ficar `folga` px abaixo do topo. */
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

test("exportar-titulos-com-anexos", async ({ page, narrador }) => {
  test.setTimeout(420_000);
  await prepararConta();

  // ---------- fora de cena: login e a tela inicial pronta ----------
  await page.goto("/");
  await page.locator("#login").fill(FIN);
  await page.locator("#password").fill(SENHA);
  await page.locator("input[type=submit]").click();
  await expect(page.locator(".sidebar-wrapper .nav")).toBeVisible();
  await expect(page.locator(".sidebar-wrapper").getByRole("link", { name: "Exportações" })).toBeVisible();

  const linhas = page.locator("tbody tr");
  const situacao = (i: number): Locator => linhas.nth(i).locator(".status");

  // p01 — Abertura
  await narrador.passo("p01", async () => {
    await narrador.capa("Exportar títulos com anexos");
  });

  // p02 — O menu
  await narrador.passo("p02", async () => {
    await narrador.tirarCapa();
    await expect(page.locator(".sidebar-wrapper li.active")).toHaveText(/Solicitações/);
    const item = page.locator(".sidebar-wrapper").getByRole("link", { name: "Exportações" });
    await narrador.aposFala(0.15);
    await narrador.apontar(item);
    await narrador.aposFala(0.55);
    await narrador.clicar(item);
    await expect(page.locator("#btnGerar")).toBeEnabled();
    await expect(page.getByText("Carregando exportações...")).toBeHidden();
    await expect(page.locator(".sidebar-wrapper li.active")).toHaveText(/Exportações/);
  });

  // p03 — A tela
  await narrador.passo("p03", async () => {
    await expect(page.getByRole("heading", { name: "Exportar títulos com anexos" })).toBeVisible();
    await expect(page.locator("#tipoTitulo")).toContainText("Ambos (Solicitações e RDDVs)");
    await expect(page.locator("#incluirAnexos input")).toBeChecked();
    await narrador.aposFala(0.1);
    await narrador.apontar(page.locator("p.aviso"));
    await narrador.aposFala(0.5);
    await narrador.apontar(page.locator("p.aviso strong", { hasText: "Baixar" }));
    await narrador.aposFala(0.75);
    await narrador.apontar(page.locator("p.aviso strong", { hasText: "Extraia o ZIP" }));
  });

  // p04 — O tipo de título
  await narrador.passo("p04", async () => {
    const tipo = campo(page, "Tipo de título").locator("mat-select");
    await narrador.aposFala(0.05);
    await narrador.apontar(tipo);
    await narrador.aposFala(0.35);
    await narrador.clicar(tipo);
    await expect(page.getByRole("option")).toHaveCount(3);
    await narrador.aposFala(0.55);
    await narrador.clicar(page.getByRole("option", { name: "Solicitação", exact: true }));
    await expect(page.getByRole("option")).toHaveCount(0);
    await expect(page.locator("#tipoTitulo")).toContainText("Solicitação");
    await narrador.aposFala(0.7);
    await narrador.apontar(campo(page, "Tipo de relatório (só RDDV)"));
    await expect(campo(page, "Tipo de relatório (só RDDV)").locator("mat-select")).toHaveAttribute("aria-disabled", "true");
  });

  // p05 — O período
  await narrador.passo("p05", async () => {
    const periodo = campo(page, "Período da Solicitação");
    const inicio = periodo.locator('input[placeholder="Início"]');
    const fim = periodo.locator('input[placeholder="Fim"]');
    // O rótulo flutuante do Material cobre o campo vazio e intercepta o clique: o foco vai direto, sem clicar.
    const digitarNoPeriodo = async (alvo: Locator, texto: string): Promise<void> => {
      await narrador.apontar(alvo);
      await alvo.focus();
      await alvo.pressSequentially(texto, { delay: 70 });
    };
    await narrador.aposFala(0.1);
    await narrador.apontar(periodo);
    await narrador.aposFala(0.3);
    await digitarNoPeriodo(inicio, "01/11/2025");
    await expect(inicio).toHaveValue("01/11/2025");
    await digitarNoPeriodo(fim, "31/03/2026");
    await fim.blur();
    await expect(fim).toHaveValue("31/03/2026");
  });

  // p06 — Os anexos
  await narrador.passo("p06", async () => {
    await narrador.aposFala(0.05);
    await narrador.apontar(campo(page, "Status Gestor"));
    await narrador.apontar(campo(page, "Status Financeiro"));
    await narrador.apontar(campo(page, "Status Contabilidade"));
    await narrador.aposFala(0.4);
    await narrador.apontar(page.locator("#incluirAnexos"));
    await expect(page.locator("#incluirAnexos input")).toBeChecked();
  });

  // p07 — Gerar a exportação
  await narrador.passo("p07", async () => {
    const antes = await linhas.count();
    await narrador.aposFala(0.1);
    await narrador.clicar(page.locator("#btnGerar"));
    await expect(aviso(page)).toContainText("Exportação solicitada.");
    await expect(linhas).toHaveCount(antes + 1);
    await narrador.aposFala(0.45);
    await rolarPara(tituloDaLista(page), 24);
    await expect(situacao(0)).toHaveText(/Pendente|Em processamento/);
    await narrador.apontar(linhas.first());
  });

  // p08 — O andamento
  await narrador.passo("p08", async () => {
    await expect(situacao(0)).toHaveText("Em processamento", { timeout: 30_000 });
    await narrador.apontar(linhas.first().locator(".barra"));
    await narrador.apontar(linhas.first().locator(".andamento small"));
  });

  // p09 — A exportação concluída
  await narrador.passo("p09", async () => {
    await expect(situacao(0)).toHaveText("Concluída", { timeout: 60_000 });
    const linha = linhas.first();
    await expect(linha.getByRole("button", { name: "Baixar" })).toBeVisible();
    await expect(linha.getByRole("button", { name: "Excluir" })).toBeVisible();
    await narrador.aposFala(0.15);
    await narrador.apontar(page.locator("th", { hasText: "Tamanho" }));
    await narrador.apontar(page.locator("th", { hasText: "Expira em" }));
    await narrador.aposFala(0.6);
    await narrador.apontar(linha.getByRole("button", { name: "Baixar" }));
    await narrador.apontar(linha.getByRole("button", { name: "Excluir" }));
  });

  // p10 — Baixar
  await narrador.passo("p10", async () => {
    const baixar = linhas.first().getByRole("button", { name: "Baixar" });
    const url = page.url();
    await narrador.aposFala(0.15);
    const download = page.waitForEvent("download");
    await narrador.clicar(baixar);
    const arquivo = await download;
    expect(arquivo.suggestedFilename()).toMatch(/\.zip$/i);
    await arquivo.cancel();
    expect(page.url()).toBe(url);
    await expect(situacao(0)).toHaveText("Concluída");
  });

  // p11 — Extrair o pacote (cena do pacote já extraído)
  await narrador.passo("p11", async () => {
    await page.goto(paginaDoPacote("arvore"));
    await expect(page.locator("#janela")).toBeVisible();
    for (const raiz of ["titulos.xlsx", "indice.csv", "indice.json", "LEIA-ME.txt"]) {
      await expect(page.locator(".l .n", { hasText: raiz })).toHaveCount(1);
    }
    await expect(page.locator(".l.pasta .n", { hasText: /SOL-\d{6,}/ }).first()).toBeVisible();
    await narrador.aposFala(0.35);
    await narrador.apontar(page.locator(".l .n", { hasText: "titulos.xlsx" }));
    await narrador.apontar(page.locator(".l .n", { hasText: "LEIA-ME.txt" }));
    await narrador.aposFala(0.8);
    await narrador.apontar(page.locator(".l.pasta .n", { hasText: /SOL-\d{6,}/ }).first());
  });

  // p12 — A planilha
  await narrador.passo("p12", async () => {
    await page.goto(paginaDoPacote("planilha-solicitacoes"));
    await expect(page.locator(".guia")).toHaveText(["Solicitações", "RDDVs", "Despesas dos RDDVs", "Anexos"]);
    await expect(page.locator(".guia.ativa")).toHaveText("Solicitações");
    await narrador.aposFala(0.2);
    await narrador.apontar(page.locator(".guia", { hasText: "Solicitações" }));
    await narrador.apontar(page.locator(".guia", { hasText: "RDDVs" }).first());
    await narrador.apontar(page.locator(".guia", { hasText: "Despesas dos RDDVs" }));
    await narrador.apontar(page.locator(".guia", { hasText: "Anexos" }));
    await narrador.aposFala(0.5);
    await expect(page.locator("td a", { hasText: /^SOL-\d{6,}$/ }).first()).toBeVisible();
    await narrador.apontar(page.locator("td a", { hasText: /^SOL-\d{6,}$/ }).first());
    await narrador.aposFala(0.72);
    await page.goto(paginaDoPacote("planilha-anexos"));
    await expect(page.locator(".guia.ativa")).toHaveText("Anexos");
    await expect(page.locator("tr.primeira td").first()).toHaveText("Código do título▾");
    await narrador.apontar(page.locator("td a").first());
  });

  // p13 — Encerramento (a tela Exportações volta, pelo histórico)
  await narrador.passo("p13", async () => {
    await page.goto(`${BASE}/`);
    await expect(page.locator(".sidebar-wrapper .nav")).toBeVisible();
    await page.locator(".sidebar-wrapper").getByRole("link", { name: "Exportações" }).click();
    await expect(page.locator("#btnGerar")).toBeEnabled();
    await expect(page.getByText("Carregando exportações...")).toBeHidden();
    await expect(situacao(0)).toHaveText("Concluída");
    await rolarPara(tituloDaLista(page), 24);
    await narrador.aposFala(0.3);
    await narrador.apontar(situacao(0));
    await narrador.aposFala(0.75);
    await narrador.apontar(linhas.first().getByRole("button", { name: "Excluir" }));
  });
});
