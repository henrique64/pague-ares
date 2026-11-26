// gerado pelo plugin documentacao 0.3.0 — sha256 do modelo: e1e2a355ea6409fd15e1387e86d28f552db68c3c6daa1e404652a5f043d1cacb
import { test as base, expect, type Locator, type Page } from "@playwright/test";
import fs from "node:fs";
import path from "node:path";

/**
 * O spec NARRÁVEL dos vídeos do manual.
 *
 * O fixture se chama `narrador` (e não `video`, que é a opção nativa do Playwright, de escopo worker: sobrescrevê-la é
 * erro de carga). Um spec o recebe como `{ page, narrador }`.
 *
 * Um passo do roteiro (`## pNN — título` em `<DOC_ROTEIROS>/<tarefa>.narracao.md`) é um `narrador.passo("pNN", ...)`.
 * A fala do passo começa no instante em que ele começa (o pipeline do plugin põe o áudio ali), e o passo só termina
 * quando a fala acabou e passou a FOLGA. Assim a fala de um passo nunca invade o seguinte: é propriedade da construção,
 * e o manifesto prova.
 *
 * **O único `waitForTimeout` permitido nos specs de manual é o daqui**, e ele é CADÊNCIA, não sincronização: a tela já
 * chegou ao estado do passo (os `expect` da ação garantiram) e fica parada enquanto a voz fala. `aposFala` faz o mesmo
 * dentro do passo, para o clique sair depois de a voz dizer onde clicar.
 *
 * O cursor e o realce são desenhados por um script de teste (`addInitScript`), com `aria-hidden` e sem receber clique:
 * a tela do app não muda. O vídeo de headless não mostra o cursor do sistema, e quem aprende precisa ver onde clicar.
 *
 * Variáveis de ambiente que o plugin define (a CLI `doc.ts video`; sem elas o spec roda MUDO, para escrever o spec sem
 * gastar síntese de voz):
 *   DOC_ROTEIROS      pasta dos roteiros (obrigatória)
 *   DOC_VIDEO_TEMPOS  caminho do tempos.json { "p01": segundos de áudio }; ausente = estimativa por DOC_CPS
 *   DOC_VIDEO_SAIDA   pasta onde gravar marcas.json e <tarefa>.webm
 *   DOC_CPS           caracteres por segundo da estimativa (padrão 13)
 *   DOC_MARCA         texto pequeno acima do título na capa
 *   DOC_CAPA_MODO     "escura" (padrão) ou "clara"
 *
 * Este arquivo roda no projeto e não pode importar o plugin: o leitor de roteiro abaixo é o mesmo algoritmo de
 * `scripts/lib/roteiro.ts` do plugin, copiado. Mexer num sem mexer no outro é erro.
 */
export { expect };

/** Segundos entre o fim da fala de um passo e o começo do seguinte. */
export const FOLGA_S = 0.7;
/** Nenhum passo fica menos que isto na tela, mesmo com fala curta. */
export const MINIMO_S = 2.0;

const CARACTERES_POR_SEGUNDO = Number(process.env.DOC_CPS ?? "13");

export interface PassoDoRoteiro {
  readonly id: string;
  readonly texto: string;
}

function pastaDosRoteiros(): string {
  const pasta = process.env.DOC_ROTEIROS;
  if (!pasta) {
    throw new Error(
      "DOC_ROTEIROS não está definida: rode o spec por `doc.ts video` ou defina DOC_ROTEIROS com a pasta dos roteiros (*.narracao.md).",
    );
  }
  return pasta;
}

/**
 * Lê `<tarefa>.narracao.md`: `## pNN — título` abre um passo, as linhas seguintes são a fala, `**negrito**` perde os
 * asteriscos (é texto de tela, que entra na fala) e linhas iniciadas por `>` são direção de cena (não narradas).
 */
export function lerRoteiro(tarefa: string): PassoDoRoteiro[] {
  const arquivo = path.join(pastaDosRoteiros(), `${tarefa}.narracao.md`);
  const bruto = fs.readFileSync(arquivo, "utf8").replace(/\r\n/g, "\n");
  const passos: PassoDoRoteiro[] = [];
  let atual: { id: string; linhas: string[] } | null = null;
  const fechar = (p: { id: string; linhas: string[] }): PassoDoRoteiro => ({
    id: p.id,
    texto: p.linhas.join(" ").replace(/\*\*/g, "").replace(/\s+/g, " ").trim(),
  });
  for (const linha of bruto.split("\n")) {
    const m = /^## (p\d{2}) — /.exec(linha);
    if (m) {
      if (atual) passos.push(fechar(atual));
      atual = { id: m[1], linhas: [] };
      continue;
    }
    if (atual && linha.trim() !== "" && !linha.startsWith(">")) atual.linhas.push(linha.trim());
  }
  if (atual) passos.push(fechar(atual));
  if (passos.length === 0) throw new Error(`o roteiro ${arquivo} não tem nenhum passo (\`## p01 — título\`)`);
  const vistos = new Set<string>();
  passos.forEach((p, i) => {
    if (p.texto === "") throw new Error(`roteiro ${arquivo}: o passo ${p.id} está sem fala`);
    if (vistos.has(p.id)) throw new Error(`roteiro ${arquivo}: passo ${p.id} repetido`);
    vistos.add(p.id);
    const esperado = `p${String(i + 1).padStart(2, "0")}`;
    if (p.id !== esperado) throw new Error(`roteiro ${arquivo}: ${p.id} fora de sequência (esperado ${esperado})`);
  });
  return passos;
}

function lerTempos(roteiro: readonly PassoDoRoteiro[]): Record<string, number> {
  const arquivo = process.env.DOC_VIDEO_TEMPOS;
  if (arquivo) return JSON.parse(fs.readFileSync(arquivo, "utf8")) as Record<string, number>;
  return Object.fromEntries(roteiro.map((p) => [p.id, p.texto.length / CARACTERES_POR_SEGUNDO]));
}

const CURSOR_E_REALCE = `(() => {
  if (window.__cursorDoVideo) return; window.__cursorDoVideo = true;
  const montar = () => {
    const c = document.createElement("div");
    c.setAttribute("aria-hidden", "true");
    c.dataset.video = "cursor";
    Object.assign(c.style, { position: "fixed", left: "-40px", top: "-40px", width: "22px", height: "22px",
      margin: "-11px 0 0 -11px", borderRadius: "50%", border: "3px solid #e8590c", background: "rgba(232,89,12,.18)",
      zIndex: "2147483647", pointerEvents: "none", transition: "transform .12s ease" });
    document.documentElement.appendChild(c);
    addEventListener("mousemove", (e) => { c.style.left = e.clientX + "px"; c.style.top = e.clientY + "px"; }, true);
    addEventListener("mousedown", () => { c.style.transform = "scale(.7)"; }, true);
    addEventListener("mouseup", () => { c.style.transform = "scale(1)"; }, true);
  };
  if (document.documentElement) montar(); else addEventListener("DOMContentLoaded", montar);
})();`;

export interface Narrador {
  /** Executa `acao` e segura o passo na tela até a fala acabar (mais a folga). */
  passo(id: string, acao: () => Promise<void>): Promise<void>;
  /** Dentro de um passo: espera a fração dada da fala antes de continuar (o clique sai depois de a voz dizer onde clicar). */
  aposFala(fracao: number): Promise<void>;
  /** Move o cursor até o alvo e o realça. */
  apontar(alvo: Locator): Promise<void>;
  /** Aponta, espera o olho acompanhar e clica. */
  clicar(alvo: Locator): Promise<void>;
  /** Aponta, limpa o campo e digita devagar. */
  digitar(alvo: Locator, texto: string): Promise<void>;
  /** Cobre a tela com a capa (marca e título). O passo da capa é o primeiro do roteiro. */
  capa(titulo: string): Promise<void>;
  /** Retira a capa; o pipeline sincroniza áudio e imagem pelo instante em que ela some. */
  tirarCapa(): Promise<void>;
}

async function realcar(page: Page, caixa: { x: number; y: number; width: number; height: number }): Promise<void> {
  await page.evaluate((c) => {
    const r = document.createElement("div");
    r.setAttribute("aria-hidden", "true");
    Object.assign(r.style, { position: "fixed", left: `${c.x - 6}px`, top: `${c.y - 6}px`, width: `${c.width + 12}px`,
      height: `${c.height + 12}px`, border: "3px solid #e8590c", borderRadius: "8px", zIndex: "2147483646", pointerEvents: "none" });
    document.documentElement.appendChild(r);
    setTimeout(() => r.remove(), 900);
  }, caixa);
}

// `narrador`, nunca `video`: `video` já é fixture do Playwright (opção de worker que o `context` usa para gravar).
export const test = base.extend<{ narrador: Narrador }>({
  narrador: async ({ page }, use, testInfo) => {
    const tarefa = testInfo.title;
    const roteiro = lerRoteiro(tarefa);
    const tempos = lerTempos(roteiro);
    const t0 = performance.now(); // o vídeo começa com a página, alguns ms antes: a FOLGA cobre
    const agora = (): number => (performance.now() - t0) / 1000;
    const marcas: { id: string; inicio_s: number }[] = [];
    let atual: { id: string; inicio: number } | null = null;
    await page.addInitScript(CURSOR_E_REALCE);

    const segurar = async (ate: number): Promise<void> => {
      const resta = ate - agora();
      if (resta > 0) await page.waitForTimeout(resta * 1000); // CADÊNCIA (ver o cabeçalho), nunca sincronização
    };
    const fala = (id: string): number => {
      const s = tempos[id];
      if (s === undefined) throw new Error(`o roteiro de ${tarefa} não tem o passo ${id}`);
      return s;
    };

    const narrador: Narrador = {
      async passo(id, acao) {
        if (marcas.some((m) => m.id === id)) throw new Error(`passo repetido: ${id}`);
        const inicio = agora(); // const: o `let atual` perde o estreitamento depois do await
        atual = { id, inicio };
        marcas.push({ id, inicio_s: inicio });
        await acao();
        await segurar(inicio + Math.max(fala(id) + FOLGA_S, MINIMO_S));
        atual = null;
      },
      async aposFala(fracao) {
        if (atual === null) throw new Error("aposFala fora de um passo");
        await segurar(atual.inicio + fala(atual.id) * fracao);
      },
      async apontar(alvo) {
        await alvo.scrollIntoViewIfNeeded();
        const caixa = await alvo.boundingBox();
        if (caixa === null) throw new Error("alvo sem caixa para apontar");
        await page.mouse.move(caixa.x + caixa.width / 2, caixa.y + caixa.height / 2, { steps: 30 });
        await realcar(page, caixa);
      },
      async clicar(alvo) {
        await narrador.apontar(alvo);
        await page.waitForTimeout(250); // CADÊNCIA: o olho acompanha o realce antes do clique
        await alvo.click();
      },
      async digitar(alvo, texto) {
        await narrador.apontar(alvo);
        await alvo.click();
        await alvo.fill("");
        await alvo.pressSequentially(texto, { delay: 70 });
      },
      async capa(titulo) {
        const claro = (process.env.DOC_CAPA_MODO ?? "escura") === "clara";
        await page.evaluate(
          ({ t, marca, clara }) => {
            const c = document.createElement("div");
            c.dataset.video = "capa";
            c.setAttribute("aria-hidden", "true");
            // Fundo opaco: o brilho médio da capa é o que o pipeline mede para achar o instante em que ela some.
            Object.assign(c.style, { position: "fixed", inset: "0", zIndex: "2147483645", display: "grid", placeItems: "center",
              background: clara ? "#ffffff" : "#111827", color: clara ? "#111827" : "#ffffff",
              font: "600 44px/1.2 system-ui, sans-serif", textAlign: "center", padding: "0 64px", boxSizing: "border-box" });
            const bloco = document.createElement("div");
            if (marca) {
              const m = document.createElement("div");
              m.style.cssText = "font-size:20px;font-weight:500;opacity:.8;margin-bottom:12px";
              m.textContent = marca;
              bloco.appendChild(m);
            }
            const h = document.createElement("div");
            h.textContent = t;
            bloco.appendChild(h);
            c.appendChild(bloco);
            document.documentElement.appendChild(c);
          },
          { t: titulo, marca: process.env.DOC_MARCA ?? "", clara: claro },
        );
        await expect(page.locator('[data-video="capa"]')).toBeVisible();
      },
      async tirarCapa() {
        await page.evaluate(() => document.querySelector('[data-video="capa"]')?.remove());
        await expect(page.locator('[data-video="capa"]')).toHaveCount(0);
      },
    };

    await use(narrador);

    if (testInfo.status !== testInfo.expectedStatus) return;
    expect(marcas.map((m) => m.id), "os passos do spec são os do roteiro, na ordem").toEqual(roteiro.map((p) => p.id));
    const fim_s = agora();
    const saida = process.env.DOC_VIDEO_SAIDA ?? path.join(path.dirname(testInfo.config.configFile ?? "."), "test-results", "video", tarefa);
    fs.mkdirSync(saida, { recursive: true });
    fs.writeFileSync(path.join(saida, "marcas.json"), JSON.stringify({ tarefa, passos: marcas, fim_s }, null, 2) + "\n", "utf8");
    await page.close();
    await page.video()?.saveAs(path.join(saida, `${tarefa}.webm`));
  },
});
