// gerado pelo plugin documentacao 0.3.0 — sha256 do modelo: eaf288fd25003b30aa43d2332a89aa3cf5ea540346e992a858453167ec4d7a5c
/**
 * O UNIVERSO FICTÍCIO do manual — esqueleto. Copie para `universo.ts` (não edite este arquivo: `doc.ts instalar
 * --conferir` o compara com o modelo do plugin) e troque o conteúdo pelo do seu app.
 *
 * O que o universo é: uma empresa e umas pessoas que não existem, com os registros que cada foto precisa mostrar
 * (um em cada estado importante) e um relógio fixo. Nada daqui vem de cliente: nomes, documentos, referências e
 * valores são inventados, e o dublê (`dublagem.ts`) recusa servir qualquer corpo que contenha um termo da lista
 * `proibidos` do projeto.
 *
 * Regras de ouro:
 *  - Relógio fixo. Toda foto e todo vídeo mostram o mesmo "hoje" (`page.clock.setFixedTime(AGORA_DO_MANUAL)`), senão
 *    a data da tela muda a cada rodada e o conferente do texto perde a referência.
 *  - Portas mortas. A base da API fica numa porta em que ninguém escuta; o servidor do app recebe a base por variável
 *    de ambiente (`servidor.env` do documentacao.json) e todo request é atendido por `page.route`.
 *  - Os registros nascem do CONTRATO da API. Se o projeto tem esquema (zod, OpenAPI, JSON Schema), gere cada resposta
 *    a partir de uma fixture do contrato e passe-a pelo esquema (`contratoDaResposta` da rota): um registro no formato
 *    errado reprova a foto em vez de enfeitá-la. Exemplo, com zod:
 *
 *        import { z } from "zod";
 *        export const EsquemaDoPedido = z.object({ id: z.string(), numero: z.string(), status: z.enum(["pendente", "aprovado"]) });
 *        // na rota:  contratoDaResposta: EsquemaDoPedido
 *
 *  - Um registro por estado. A foto "registro aprovado" e a foto "registro pendente" usam registros diferentes, escritos
 *    aqui, em vez de editar um registro dentro do spec.
 */

// ── Relógio e bases ──────────────────────────────────────────────────────────────────────────────
/** O "hoje" de todas as fotos e vídeos (instante fixo, em UTC). */
export const AGORA_DO_MANUAL = new Date("2026-01-14T13:30:00Z");
/** O mesmo dia como data civil, para os registros. */
export const HOJE = "2026-01-14";

/** A base da API em porta MORTA (confira que nada escuta nela). O app a recebe pela variável do `servidor.env`. */
export const BASES = {
  api: "http://127.0.0.1:59999",
} as const;

// ── Pessoas ──────────────────────────────────────────────────────────────────────────────────────
export type Papel = "leitor" | "operador" | "administrador";

export interface PessoaDoManual {
  readonly email: string;
  readonly nome: string;
  readonly papel: Papel;
}

/** A empresa fictícia. */
export const EMPRESA = { id: "00000000-0000-4000-8000-000000000001", nome: "Empresa Exemplo Ltda" } as const;

/** Uma pessoa por papel que muda o que a tela mostra. */
export const ANA: PessoaDoManual = { email: "ana@exemplo.invalid", nome: "Ana Souza", papel: "operador" };
export const BRUNO: PessoaDoManual = { email: "bruno@exemplo.invalid", nome: "Bruno Lima", papel: "administrador" };
export const CARLA: PessoaDoManual = { email: "carla@exemplo.invalid", nome: "Carla Dias", papel: "leitor" };

// ── Registros ────────────────────────────────────────────────────────────────────────────────────
export type Situacao = "pendente" | "aprovado" | "cancelado";

export interface Registro {
  readonly id: string;
  readonly numero: string;
  readonly titulo: string;
  readonly situacao: Situacao;
  readonly criadoEm: string;
}

/** Um registro em cada situação: as fotos escolhem o que mostrar, sem editar o registro dentro do spec. */
export const REGISTROS: readonly Registro[] = [
  { id: "r-001", numero: "2026-001", titulo: "Primeiro registro de exemplo", situacao: "aprovado", criadoEm: "2026-01-05" },
  { id: "r-002", numero: "2026-002", titulo: "Segundo registro de exemplo", situacao: "pendente", criadoEm: "2026-01-12" },
  { id: "r-003", numero: "2026-003", titulo: "Terceiro registro de exemplo", situacao: "cancelado", criadoEm: "2026-01-13" },
];

/** O número que o próximo registro criado recebe (a regra do servidor real, copiada no dublê). */
export const PROXIMO_NUMERO = "2026-004";

/** Soma `dias` a uma data civil `aaaa-mm-dd` (sempre em UTC, sem depender do fuso da máquina). */
export function somarDias(data: string, dias: number): string {
  const d = new Date(`${data}T00:00:00Z`);
  d.setUTCDate(d.getUTCDate() + dias);
  return d.toISOString().slice(0, 10);
}
