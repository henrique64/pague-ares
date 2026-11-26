// gerado pelo plugin documentacao 0.3.0 — sha256 do modelo: 69072c708aab0701ba94ceffe59ac3982fb4b7fda48115dcc72a84b05a609820
import fs from "node:fs";
import path from "node:path";
import type { Locator, Page } from "@playwright/test";

/**
 * As fotos do manual: a viewport, um recorte em volta de um alvo, um bloco inteiro e o realce.
 *
 * Os PNG caem em `DOC_IMG_DIR`, pasta que `doc.ts capturar` define (uma por grupo); o plugin converte para WebP e
 * leva só o WebP para o git. Rodando o spec sem a CLI, defina `DOC_IMG_DIR` à mão.
 *
 * Variáveis de ambiente lidas aqui:
 *   DOC_IMG_DIR    pasta dos PNG brutos do grupo (obrigatória)
 *   DOC_VIEWPORT   "1360x860": a janela, usada para enquadrar recortes (padrão 1360x860)
 *   DOC_MASCARAS   JSON `string[]` de seletores CSS cobertos por uma tarja em toda foto (dado que não pode aparecer,
 *                  como o nome da pessoa logada no modo de dados real)
 *
 * Toda função tira a foto com o mouse fora da área útil (nenhum hover acidental) e com animações desligadas.
 */

/** A pasta das fotos do grupo; é criada se não existir. */
export function pastaDasFotos(): string {
  const pasta = process.env.DOC_IMG_DIR;
  if (!pasta) {
    throw new Error("DOC_IMG_DIR não está definida: rode o spec por `doc.ts capturar` ou defina DOC_IMG_DIR com a pasta dos PNG.");
  }
  fs.mkdirSync(pasta, { recursive: true });
  return pasta;
}

const arquivo = (id: string): string => path.join(pastaDasFotos(), `${id}.png`);

function viewportPadrao(): { width: number; height: number } {
  const m = /^(\d+)x(\d+)$/.exec(process.env.DOC_VIEWPORT ?? "");
  return m ? { width: Number(m[1]), height: Number(m[2]) } : { width: 1360, height: 860 };
}

const telaDe = (page: Page): { width: number; height: number } => page.viewportSize() ?? viewportPadrao();

/** Os locators mascarados em toda foto (seletores de `DOC_MASCARAS`). */
function mascaras(page: Page): Locator[] {
  const bruto = process.env.DOC_MASCARAS;
  if (!bruto) return [];
  let seletores: unknown;
  try {
    seletores = JSON.parse(bruto);
  } catch {
    throw new Error(`DOC_MASCARAS não é um JSON válido: ${bruto}`);
  }
  if (!Array.isArray(seletores) || seletores.some((s) => typeof s !== "string")) {
    throw new Error("DOC_MASCARAS deve ser um JSON com a lista de seletores (string[])");
  }
  return (seletores as string[]).map((s) => page.locator(s));
}

/** O cursor fora da área útil: nenhum hover acidental na foto. */
export async function soltarMouse(page: Page): Promise<void> {
  const tela = telaDe(page);
  await page.mouse.move(tela.width - 10, tela.height - 10);
}

/**
 * Põe `alvo` logo abaixo da barra fixa do topo — o enquadramento das fotos de tela. `folga` é o espaço, em px, que
 * fica entre o topo da janela e o alvo (a altura da barra). Dois modos:
 *   - `"absoluto"` (padrão): rola para a posição ABSOLUTA do alvo menos a folga e deixa o navegador limitar ao fim da
 *     página. Serve à maioria dos apps: o alvo fica logo abaixo da barra.
 *   - `"recuo"`: `scrollIntoView({ block: "start" })` seguido de `scrollBy(0, -folga)`, isto é, recua a folga a partir
 *     do ponto que o `scrollIntoView` alcança, inclusive no fim da página. Use quando o app tem uma rolagem que a
 *     posição absoluta não alcança (contêiner próprio, alvo no último bloco) e o modo padrão deixa o alvo mal posicionado.
 */
export async function enquadrar(
  alvo: Locator,
  folga = 88,
  opcoes: { modo?: "absoluto" | "recuo" } = {},
): Promise<void> {
  await alvo.evaluate(
    (el, { f, modo }) => {
      if (modo === "recuo") {
        el.scrollIntoView({ block: "start" });
        window.scrollBy(0, -f);
        return;
      }
      const topo = el.getBoundingClientRect().top + window.scrollY;
      window.scrollTo(0, Math.max(0, topo - f));
    },
    { f: folga, modo: opcoes.modo ?? "absoluto" },
  );
}

/** Foto da viewport inteira, como está. */
export async function fotoDaTela(page: Page, id: string): Promise<void> {
  await soltarMouse(page);
  await page.screenshot({ path: arquivo(id), fullPage: false, animations: "disabled", caret: "hide", mask: mascaras(page) });
}

/** Foto do topo da página (rolagem 0). */
export async function fotoDoTopo(page: Page, id: string): Promise<void> {
  await page.evaluate(() => window.scrollTo(0, 0));
  await fotoDaTela(page, id);
}

/**
 * Foto de um recorte em volta de `alvo`, com margem — para blocos menores que a tela.
 *
 * Opções:
 *   - `folga` (padrão 120) e `modo` (padrão `"absoluto"`): como `enquadrar` posiciona o alvo antes da foto.
 *   - `margemDoTopo` (padrão 0): o recorte nunca começa acima deste y, em px da janela. Use para recortar abaixo de uma
 *     barra fixa de N px, que a margem em volta do alvo cobriria. O fundo do recorte não muda: só o topo desce.
 */
export async function fotoDoRecorte(
  page: Page,
  alvo: Locator,
  id: string,
  margem = 16,
  opcoes: { folga?: number; modo?: "absoluto" | "recuo"; margemDoTopo?: number } = {},
): Promise<void> {
  await enquadrar(alvo, opcoes.folga ?? 120, { modo: opcoes.modo });
  await soltarMouse(page);
  const caixa = await alvo.boundingBox();
  if (caixa === null) throw new Error(`Sem caixa para o recorte ${id}: o alvo está invisível ou fora da página`);
  const tela = telaDe(page);
  const x = Math.max(0, Math.floor(caixa.x - margem));
  const y = Math.max(opcoes.margemDoTopo ?? 0, Math.floor(caixa.y - margem));
  const largura = Math.min(tela.width - x, Math.ceil(caixa.width + 2 * margem));
  const altura = Math.min(tela.height - y, Math.ceil(caixa.y + caixa.height + margem - y));
  if (altura <= 0) {
    throw new Error(`Recorte ${id} sem altura: o alvo está abaixo da área visível (y=${y}, janela de ${tela.height}px); ajuste o enquadramento (folga ou modo "recuo").`);
  }
  await page.screenshot({
    path: arquivo(id),
    clip: { x, y, width: largura, height: altura },
    animations: "disabled",
    caret: "hide",
    mask: mascaras(page),
  });
}

/**
 * Foto de um bloco inteiro, mesmo mais alto que a viewport. Uma barra `sticky` ou `fixed` (cabeçalho, menu) é desenhada
 * pelo Chromium por cima do meio do bloco numa captura de elemento maior que a janela: durante a foto, os elementos
 * que casam com `fixosParaEstaticos` e estão `sticky` ou `fixed` viram `static`. Só a apresentação muda, o conteúdo não.
 * Informe os seletores da barra do seu app (padrão: `header`); lista vazia (`[]`) não mexe em barra nenhuma.
 */
export async function fotoDoElemento(
  alvo: Locator,
  id: string,
  opcoes: { fixosParaEstaticos?: string[] } = {},
): Promise<void> {
  const page = alvo.page();
  await soltarMouse(page);
  const seletores = opcoes.fixosParaEstaticos ?? ["header"];
  if (seletores.length === 0) {
    // Sem barra a tratar: `querySelectorAll("")` é rejeitado pelo navegador, então nem a troca nem a restauração rodam.
    await alvo.screenshot({ path: arquivo(id), animations: "disabled", caret: "hide", mask: mascaras(page) });
    return;
  }
  await page.evaluate((sels) => {
    for (const el of Array.from(document.querySelectorAll(sels.join(",")))) {
      const posicao = getComputedStyle(el).position;
      if (posicao === "sticky" || posicao === "fixed") {
        const html = el as HTMLElement;
        html.dataset.docFixoOriginal = html.style.position;
        html.style.setProperty("position", "static", "important");
      }
    }
  }, seletores);
  try {
    await alvo.screenshot({ path: arquivo(id), animations: "disabled", caret: "hide", mask: mascaras(page) });
  } finally {
    await page.evaluate(() => {
      for (const el of Array.from(document.querySelectorAll("[data-doc-fixo-original]"))) {
        const html = el as HTMLElement;
        html.style.position = html.dataset.docFixoOriginal ?? "";
        delete html.dataset.docFixoOriginal;
      }
    });
  }
}

/**
 * Contorno (3 px, folga de 6 px) em volta de cada alvo, numa camada solta no `<body>` — para a foto cuja legenda diz
 * "olhe aqui". Não mexe no DOM da tela. Devolve a função que apaga o realce.
 */
export async function destacar(
  page: Page,
  alvos: Locator | readonly Locator[],
  cor = "#e8590c",
): Promise<() => Promise<void>> {
  const lista: readonly Locator[] = Array.isArray(alvos) ? alvos : [alvos as Locator];
  const caixas = [];
  for (const alvo of lista) {
    const caixa = await alvo.boundingBox();
    if (caixa === null) throw new Error("destacar: alvo sem caixa (invisível?)");
    caixas.push(caixa);
  }
  const marca = `realce-${Date.now()}`;
  await page.evaluate(
    ({ caixas: cs, marca: m, cor: c0 }) => {
      for (const c of cs) {
        const d = document.createElement("div");
        d.dataset.realceDoManual = m;
        Object.assign(d.style, {
          position: "absolute",
          left: `${c.x + window.scrollX - 6}px`,
          top: `${c.y + window.scrollY - 6}px`,
          width: `${c.width + 12}px`,
          height: `${c.height + 12}px`,
          border: `3px solid ${c0}`,
          borderRadius: "8px",
          pointerEvents: "none",
          zIndex: "2147483647",
          boxSizing: "border-box",
        });
        document.body.appendChild(d);
      }
    },
    { caixas, marca, cor },
  );
  return async () => {
    await page.evaluate((m) => document.querySelectorAll(`[data-realce-do-manual="${m}"]`).forEach((el) => el.remove()), marca);
  };
}
