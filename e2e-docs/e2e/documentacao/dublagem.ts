// gerado pelo plugin documentacao 0.3.0 — sha256 do modelo: a839c9f81072ab9ca33e3bdecbcf3f33739ab76b62acfe4d271ec401ec7f87e9
import type { Page, Request, Route } from "@playwright/test";

/**
 * O SERVIDOR DUBLADO do manual: o app roda de verdade (servidor do projeto), mas as APIs que ele chama ficam em PORTAS
 * MORTAS e toda requisição é atendida por `page.route` com o universo fictício. Nenhuma foto nem vídeo toca um banco.
 *
 * Quatro garantias:
 * - toda rota declarada diz de onde copia a regra (`origem`: o arquivo da API que decide aquele comportamento). O dublê
 *   não é um mock solto: reproduz a regra do servidor real (criar dá o número seguinte, aprovar exige o papel certo,
 *   escrita sem versão é 400...). `origem` vazia reprova a montagem;
 * - com `contrato` (qualquer objeto com `parse(x)` ou `safeParse(x)`, como um esquema zod), a resposta é validada antes
 *   de chegar à tela e o corpo de escrita RECEBIDO é validado por `contratoDaRequisicao`: mock no formato errado
 *   reprova a foto, não a embeleza;
 * - corpo que contenha um termo proibido (dado real que sobrou de uma fixture) não é servido (`termosProibidos`);
 * - chamada sem atendente responde 599 e entra em `semResposta`, que o `afterEach` de cada spec afirma vazio
 *   (`afirmarSemFalhas`).
 *
 * No modo de dados real (`DOC_DADOS=real`) o dublê NÃO intercepta nada: o app fala com o ambiente de verdade, e só
 * `errosDaPagina` continua sendo coletado.
 *
 * Este arquivo não importa nada do projeto nem do plugin; é copiado para o projeto por `doc.ts instalar`.
 */

/** Um objeto que valida: `parse` lança, `safeParse` devolve `{ success }` (zod, valibot, qualquer um dos dois). */
export type Contrato =
  | { parse(valor: unknown): unknown }
  | { safeParse(valor: unknown): { success: boolean; data?: unknown; error?: unknown } };

/** O que o atendente recebe. */
export interface Chamada {
  readonly req: Request;
  readonly url: URL;
  /** Os `{parâmetros}` do padrão da rota, já extraídos do caminho. */
  readonly partes: Readonly<Record<string, string>>;
  /** O corpo recebido, já lido como JSON (ou texto, se não for JSON; `null` sem corpo). */
  readonly corpo: unknown;
}

/** O que um atendente devolve. */
export interface Resposta {
  readonly status: number;
  readonly corpo?: unknown;
  /** Sobrescreve `contratoDaResposta` da rota para esta resposta (`null` = não validar, por exemplo erros 4xx). */
  readonly contrato?: Contrato | null;
}

export type Atendente = (chamada: Chamada) => Resposta;

export interface RotaDublada {
  readonly metodo: string;
  /** Caminho com `{parametros}` (`/v1/pedidos/{id}`) ou expressão regular com grupos nomeados, casado com o `pathname`. */
  readonly padrao: string | RegExp;
  /** O arquivo da API cuja regra esta rota copia (obrigatório). */
  readonly origem: string;
  readonly responder: Atendente;
  /** Valida a resposta (exceto quando a `Resposta` traz o seu `contrato`). */
  readonly contratoDaResposta?: Contrato;
  /** Valida o corpo de escrita recebido. */
  readonly contratoDaRequisicao?: Contrato;
}

export interface Escrita {
  readonly metodo: string;
  readonly caminho: string;
  readonly corpo: unknown;
}

export interface OpcoesDoServidor {
  /** As bases em porta morta que o app chama (`http://127.0.0.1:59000`); tudo abaixo delas é do dublê. */
  readonly bases: readonly string[];
  readonly rotas: readonly RotaDublada[];
  /** Termos de dado real que nunca podem chegar à tela (a lista `proibidos` do projeto). */
  readonly termosProibidos?: readonly string[];
}

const CORS = {
  "access-control-allow-origin": "*",
  "access-control-allow-methods": "GET,POST,PUT,PATCH,DELETE,OPTIONS",
  "access-control-allow-headers": "authorization,content-type",
};

/** Os termos proibidos que `texto` contém, com fronteira de palavra Unicode (`\p{L}\p{N}`), sem diferenciar caixa. */
export function acharProibidos(texto: string, termos: readonly string[]): string[] {
  return termos.filter((termo) =>
    new RegExp(`(?<![\\p{L}\\p{N}])${termo.replace(/[.*+?^${}()|[\]\\]/g, "\\$&")}(?![\\p{L}\\p{N}])`, "iu").test(texto),
  );
}

function aplicar(contrato: Contrato, valor: unknown): { ok: true; valor: unknown } | { ok: false; motivo: string } {
  if ("safeParse" in contrato) {
    const r = contrato.safeParse(valor);
    return r.success ? { ok: true, valor: r.data ?? valor } : { ok: false, motivo: descrever(r.error) };
  }
  try {
    return { ok: true, valor: contrato.parse(valor) };
  } catch (erro) {
    return { ok: false, motivo: descrever(erro) };
  }
}

function descrever(erro: unknown): string {
  if (erro instanceof Error) return erro.message.slice(0, 400);
  if (typeof erro === "object" && erro !== null && "issues" in erro && Array.isArray((erro as { issues: unknown[] }).issues)) {
    return (erro as { issues: { path?: unknown[]; message?: string }[] }).issues
      .map((i) => `${(i.path ?? []).join(".")}: ${i.message ?? "inválido"}`)
      .join("; ");
  }
  return String(erro).slice(0, 400);
}

function lerCorpo(req: Request): unknown {
  const texto = req.postData();
  if (texto === null) return null;
  try {
    return JSON.parse(texto) as unknown;
  } catch {
    return texto;
  }
}

/** Compila o padrão `/v1/itens/{id}` em expressão regular com grupos nomeados. */
function compilar(padrao: string | RegExp): RegExp {
  if (padrao instanceof RegExp) return padrao;
  const fonte = padrao
    .split(/(\{[A-Za-z_][A-Za-z0-9_]*\})/)
    .map((trecho) => {
      const m = /^\{([A-Za-z_][A-Za-z0-9_]*)\}$/.exec(trecho);
      return m ? `(?<${m[1]}>[^/]+)` : trecho.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
    })
    .join("");
  return new RegExp(`^${fonte}/?$`);
}

interface RotaCompilada {
  readonly rota: RotaDublada;
  readonly regex: RegExp;
}

export class ServidorDublado {
  /** Os corpos de escrita RECEBIDOS, na ordem — o que a foto afirma que a tela ENVIA. */
  readonly escritas: Escrita[] = [];
  /** Cada chamada que o dublê recusou (rota não declarada, formato fora do contrato, dado real). */
  readonly semResposta: string[] = [];
  readonly errosDaPagina: string[] = [];
  readonly modoReal: boolean;
  private readonly rotas: RotaCompilada[] = [];
  private readonly termos: readonly string[];

  private constructor(termos: readonly string[], modoReal: boolean) {
    this.termos = termos;
    this.modoReal = modoReal;
  }

  /** Monta o dublê na página: registra as rotas e as interceptações. No modo real só coleta `errosDaPagina`. */
  static async montar(page: Page, opcoes: OpcoesDoServidor): Promise<ServidorDublado> {
    const real = process.env.DOC_DADOS === "real";
    const s = new ServidorDublado(opcoes.termosProibidos ?? [], real);
    page.on("pageerror", (erro) => s.errosDaPagina.push(erro.message));
    if (real) return s;
    for (const r of opcoes.rotas) s.declarar(r);
    for (const base of opcoes.bases) {
      const origem = new URL(base);
      const prefixo = origem.pathname.replace(/\/$/, "");
      await page.route(`${origem.origin}${prefixo}/**`, (route) => s.atender(route, prefixo));
    }
    return s;
  }

  /** Declara (ou, para o mesmo método e padrão, TROCA) uma rota — é como cada foto monta o estado de que precisa. */
  declarar(rota: RotaDublada): void {
    if (rota.origem.trim() === "") {
      throw new Error(
        `rota ${rota.metodo} ${String(rota.padrao)} sem \`origem\`: diga o arquivo da API cuja regra o dublê copia, ou a regra vira palpite`,
      );
    }
    const metodo = rota.metodo.toUpperCase();
    const existente = this.rotas.findIndex((c) => c.rota.metodo.toUpperCase() === metodo && String(c.rota.padrao) === String(rota.padrao));
    const compilada = { rota: { ...rota, metodo }, regex: compilar(rota.padrao) };
    if (existente >= 0) this.rotas[existente] = compilada;
    else this.rotas.push(compilada);
  }

  /** Atalho: troca só o atendente de uma rota já declarada, mantendo a origem e os contratos. */
  responder(metodo: string, padrao: string | RegExp, responder: Atendente): void {
    const atual = this.rotas.find((c) => c.rota.metodo === metodo.toUpperCase() && String(c.rota.padrao) === String(padrao));
    if (atual === undefined) throw new Error(`rota ${metodo} ${String(padrao)} não foi declarada: declare-a com \`declarar\` antes de trocar o atendente`);
    this.declarar({ ...atual.rota, responder });
  }

  /** As escritas de uma rota, na ordem: `escritasDe("POST", "/v1/itens")`. */
  escritasDe(metodo: string, caminho: string): unknown[] {
    return this.escritas.filter((e) => e.metodo === metodo.toUpperCase() && e.caminho === caminho).map((e) => e.corpo);
  }

  /** Para o `afterEach` do spec: reprova se o dublê recusou alguma chamada ou a página lançou erro. */
  afirmarSemFalhas(): void {
    const problemas = [
      ...this.semResposta.map((s) => `sem resposta: ${s}`),
      ...this.errosDaPagina.map((e) => `erro na página: ${e}`),
    ];
    if (problemas.length > 0) throw new Error(`o servidor dublado registrou ${problemas.length} falha(s):\n${problemas.join("\n")}`);
  }

  private json(route: Route, status: number, corpo: unknown): Promise<void> {
    return route.fulfill({
      status,
      headers: CORS,
      contentType: status >= 400 ? "application/problem+json" : "application/json",
      body: JSON.stringify(corpo),
    });
  }

  private async atender(route: Route, prefixo: string): Promise<void> {
    const req = route.request();
    if (req.method() === "OPTIONS") return route.fulfill({ status: 204, headers: CORS });
    const url = new URL(req.url());
    const caminho = prefixo !== "" && url.pathname.startsWith(prefixo) ? url.pathname.slice(prefixo.length) || "/" : url.pathname;

    const recusar = (motivo: string): Promise<void> => {
      this.semResposta.push(`${req.method()} ${url.pathname}${url.search} — ${motivo}`);
      return route.fulfill({ status: 599, headers: CORS, contentType: "text/plain", body: `sem resposta no servidor dublado: ${motivo}` });
    };

    const metodo = req.method().toUpperCase();
    let achada: RotaCompilada | undefined;
    let partes: Record<string, string> = {};
    for (const c of this.rotas) {
      if (c.rota.metodo !== metodo) continue;
      const m = c.regex.exec(caminho);
      if (m) {
        achada = c;
        partes = { ...(m.groups ?? {}) };
        break;
      }
    }
    if (achada === undefined) return recusar("rota não declarada no universo do manual");

    const corpo = lerCorpo(req);
    if (metodo !== "GET" && metodo !== "HEAD") {
      const contrato = achada.rota.contratoDaRequisicao;
      if (contrato !== undefined && typeof corpo === "object" && corpo !== null) {
        const lido = aplicar(contrato, corpo);
        if (!lido.ok) return recusar(`corpo de requisição fora do contrato: ${lido.motivo}`);
      }
      this.escritas.push({ metodo, caminho, corpo });
    }

    let resposta: Resposta;
    try {
      resposta = achada.rota.responder({ req, url, partes, corpo });
    } catch (erro) {
      return recusar(erro instanceof Error ? erro.message : String(erro));
    }

    let servido = resposta.corpo;
    const contrato = resposta.contrato === undefined ? achada.rota.contratoDaResposta : resposta.contrato ?? undefined;
    if (contrato !== undefined) {
      const lido = aplicar(contrato, resposta.corpo);
      if (!lido.ok) return recusar(`resposta fora do contrato: ${lido.motivo}`);
      servido = lido.valor;
    }

    const vazou = acharProibidos(JSON.stringify(servido ?? null), this.termos);
    if (vazou.length > 0) return recusar(`serviria dado real: ${vazou.join(", ")}`);

    if (resposta.status === 204) return route.fulfill({ status: 204, headers: CORS });
    return this.json(route, resposta.status, servido);
  }
}
