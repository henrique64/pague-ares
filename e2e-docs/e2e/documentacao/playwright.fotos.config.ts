// gerado pelo plugin documentacao 0.3.0 — sha256 do modelo: 0c22e9f8e85a8aba9a4f412a040d58062b302df81939eb9388e77514c4486bcb
import { defineConfig } from "@playwright/test";

/**
 * Configuração do Playwright para as FOTOS do manual. Quem a roda é `doc.ts capturar`, que define as variáveis abaixo
 * (um grupo de fotos por vez, cada grupo na sua porta). Rodar à mão: defina `DOC_PORTA`, `DOC_SERVIDOR_COMANDO`,
 * `DOC_SERVIDOR_DIR` e `DOC_IMG_DIR` e chame `playwright test -c <este arquivo>`.
 *
 * Variáveis lidas:
 *   DOC_PORTA                porta do grupo (obrigatória)
 *   DOC_SERVIDOR_COMANDO     comando que sobe o app, com a porta já resolvida (obrigatória no modo mock)
 *   DOC_SERVIDOR_DIR         pasta onde o comando roda
 *   DOC_SERVIDOR_ENV         JSON { "VARIAVEL": "valor" } acrescentado ao ambiente do servidor
 *   DOC_SERVIDOR_URL         URL em que o servidor responde (padrão http://127.0.0.1:<porta>)
 *   DOC_VIEWPORT             "1360x860"
 *   DOC_LOCALE, DOC_FUSO     locale e fuso horário do navegador
 *   DOC_DADOS                "mock" (padrão) ou "real"; no modo real não há servidor e a base é DOC_URL_BASE
 *   DOC_URL_BASE             modo real: URL do ambiente
 *
 * Uso por grupo: cada grupo de `fotos.grupos` do documentacao.json aponta para um arquivo de configuração próprio, que
 * escolhe o spec do grupo e reaproveita esta função:
 *
 *     import { criarConfigDeFotos } from "./playwright.fotos.config.ts";
 *     export default criarConfigDeFotos({ testMatch: "g1-cadastros.fotos.spec.ts", outputDir: "test-results/g1" });
 *
 * Sem argumentos (o `export default` deste arquivo), roda todo `*.fotos.spec.ts` da pasta. `reuseExistingServer` é
 * sempre falso: uma foto nunca pode ser tirada de um servidor que sobrou de outra rodada.
 */

export interface OpcoesDeFotos {
  /** Pasta dos specs, relativa a este arquivo (padrão: a própria pasta). */
  testDir?: string;
  testMatch?: string | RegExp | Array<string | RegExp>;
  outputDir?: string;
}

function exigir(nome: string): string {
  const v = process.env[nome];
  if (!v) throw new Error(`${nome} não está definida: rode por \`doc.ts capturar\` ou defina a variável (ver o cabeçalho de playwright.fotos.config.ts)`);
  return v;
}

function viewport(): { width: number; height: number } {
  const m = /^(\d+)x(\d+)$/.exec(process.env.DOC_VIEWPORT ?? "1360x860");
  if (!m) throw new Error(`DOC_VIEWPORT inválida (${process.env.DOC_VIEWPORT}): use o formato 1360x860`);
  return { width: Number(m[1]), height: Number(m[2]) };
}

function ambienteDoServidor(): Record<string, string> {
  const bruto = process.env.DOC_SERVIDOR_ENV;
  if (!bruto) return {};
  try {
    return JSON.parse(bruto) as Record<string, string>;
  } catch {
    throw new Error(`DOC_SERVIDOR_ENV não é um JSON válido: ${bruto}`);
  }
}

export function criarConfigDeFotos(opcoes: OpcoesDeFotos = {}) {
  const real = process.env.DOC_DADOS === "real";
  const porta = exigir("DOC_PORTA");
  const baseURL = real ? exigir("DOC_URL_BASE") : (process.env.DOC_SERVIDOR_URL ?? `http://127.0.0.1:${porta}`);
  return defineConfig({
    testDir: opcoes.testDir ?? ".",
    testMatch: opcoes.testMatch ?? "**/*.fotos.spec.ts",
    outputDir: opcoes.outputDir ?? "test-results/fotos",
    timeout: 120_000,
    expect: { timeout: 15_000 },
    fullyParallel: false,
    workers: 1,
    reporter: [["list"]],
    use: {
      baseURL,
      viewport: viewport(),
      deviceScaleFactor: 1,
      locale: process.env.DOC_LOCALE ?? "pt-BR",
      timezoneId: process.env.DOC_FUSO ?? "America/Sao_Paulo",
      trace: "retain-on-failure",
    },
    webServer: real
      ? undefined
      : [
          {
            command: exigir("DOC_SERVIDOR_COMANDO"),
            cwd: process.env.DOC_SERVIDOR_DIR,
            url: baseURL,
            timeout: 120_000,
            reuseExistingServer: false,
            env: ambienteDoServidor(),
          },
        ],
  });
}

export default criarConfigDeFotos();
