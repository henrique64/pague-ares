import { EnumTipoTituloExportacao } from "app/models/exportacao/enum-tipo-titulo-exportacao";

// Maior valor que o back-end aceita no filtro de código (inteiro de 32 bits com sinal).
export const CODIGO_EXPORTACAO_MAXIMO = 2147483647;

// Resultado da leitura do texto digitado no filtro Código do formulário de exportação.
//  - vazio: nada digitado (sem filtro);
//  - invalido: texto fora dos formatos aceitos, zero, decimal, negativo ou acima do máximo;
//  - ok: número do título e, quando o texto trouxe o prefixo SOL ou RDDV, o tipo que o prefixo define.
export type ResultadoCodigoExportacao =
  | { situacao: "vazio" }
  | { situacao: "invalido" }
  | { situacao: "ok"; numero: number; tipo: EnumTipoTituloExportacao | null };

// Formatos aceitos (sem diferenciar maiúsculas, espaços nas pontas ignorados): 123, 000123, SOL-000123, SOL123,
// RDDV-45 e RDDV45. O hífen depois do prefixo é opcional.
const FORMATO_CODIGO = /^(?:(SOL|RDDV)-?)?(\d+)$/i;

export function interpretarCodigoExportacao(texto: string | number | null | undefined): ResultadoCodigoExportacao {
  const t = String(texto ?? "").trim();
  if (t.length === 0) return { situacao: "vazio" };

  const m = FORMATO_CODIGO.exec(t);
  if (!m) return { situacao: "invalido" };

  const numero = Number(m[2]);
  if (!Number.isInteger(numero) || numero < 1 || numero > CODIGO_EXPORTACAO_MAXIMO) return { situacao: "invalido" };

  const prefixo = m[1]?.toUpperCase();
  const tipo =
    prefixo === "SOL" ? EnumTipoTituloExportacao.Solicitacao
    : prefixo === "RDDV" ? EnumTipoTituloExportacao.Rddv
    : null;

  return { situacao: "ok", numero, tipo };
}

// Código como aparece nas telas do sistema: SOL-000123 e RDDV-000045 (seis dígitos, no mínimo).
export function formatarCodigoExportacao(tipo: EnumTipoTituloExportacao, numero: number): string {
  const digitos = String(numero).padStart(6, "0");
  return tipo === EnumTipoTituloExportacao.Rddv ? `RDDV-${digitos}` : `SOL-${digitos}`;
}
