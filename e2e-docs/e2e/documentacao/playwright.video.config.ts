// gerado pelo plugin documentacao 0.3.0 — sha256 do modelo: be278cb2ecd423a629af24c17662733ca86d98bb385e68f662bb53d868f1367a
import { defineConfig } from "@playwright/test";

/**
 * Configuração do Playwright para os VÍDEOS narrados. Igual à das fotos, com três diferenças: `video: on` na resolução
 * do vídeo (e a janela do mesmo tamanho, para não haver reescala), um único worker e timeout de 7 minutos (um vídeo
 * tem até 4 minutos de cena, mais o login fora de cena).
 *
 * Quem a roda é `doc.ts video`, que define as variáveis abaixo e também `DOC_ROTEIROS`, `DOC_VIDEO_TEMPOS`,
 * `DOC_VIDEO_SAIDA`, `DOC_CPS`, `DOC_MARCA` e `DOC_CAPA_MODO` (lidas por `narrador.ts`). Sem `DOC_VIDEO_TEMPOS` o spec
 * roda MUDO, com tempos estimados: serve para escrever o spec sem gastar síntese de voz.
 *
 * Variáveis lidas aqui:
 *   DOC_PORTA                porta do vídeo (obrigatória)
 *   DOC_SERVIDOR_COMANDO     comando que sobe o app, com a porta já resolvida (obrigatória no modo mock)
 *   DOC_SERVIDOR_DIR         pasta onde o comando roda
 *   DOC_SERVIDOR_ENV         JSON { "VARIAVEL": "valor" } acrescentado ao ambiente do servidor
 *   DOC_SERVIDOR_URL         URL em que o servidor responde (padrão http://127.0.0.1:<porta>)
 *   DOC_VIEWPORT             resolução do vídeo e da janela, "1280x720" (padrão); a CLI a define a partir de
 *                            `video.resolucao` do documentacao.json
 *   DOC_LOCALE, DOC_FUSO     locale e fuso horário do navegador
 *   DOC_DADOS, DOC_URL_BASE  "mock" (padrão) ou "real" e, no modo real, a URL do ambiente
 *
 * Os specs narráveis chamam-se `<tarefa>.video.spec.ts` e o título do teste é o id da tarefa (é ele que acha o roteiro
 * `<tarefa>.narracao.md`). Para rodar uma tarefa só, o `doc.ts video` passa o arquivo na linha de comando; um arquivo de
 * configuração próprio pode reaproveitar `criarConfigDeVideo({ testMatch: ... })`.
 */

export interface OpcoesDeVideo {
  testDir?: string;
  testMatch?: string | RegExp | Array<string | RegExp>;
  outputDir?: string;
}

function exigir(nome: string): string {
  const v = process.env[nome];
  if (!v) throw new Error(`${nome} não está definida: rode por \`doc.ts video\` ou defina a variável (ver o cabeçalho de playwright.video.config.ts)`);
  return v;
}

function resolucao(): { width: number; height: number } {
  const m = /^(\d+)x(\d+)$/.exec(process.env.DOC_VIEWPORT ?? "1280x720");
  if (!m) throw new Error(`DOC_VIEWPORT inválida (${process.env.DOC_VIEWPORT}): use o formato 1280x720`);
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

export function criarConfigDeVideo(opcoes: OpcoesDeVideo = {}) {
  const real = process.env.DOC_DADOS === "real";
  const porta = exigir("DOC_PORTA");
  const baseURL = real ? exigir("DOC_URL_BASE") : (process.env.DOC_SERVIDOR_URL ?? `http://127.0.0.1:${porta}`);
  const tamanho = resolucao();
  return defineConfig({
    testDir: opcoes.testDir ?? ".",
    testMatch: opcoes.testMatch ?? "**/*.video.spec.ts",
    outputDir: opcoes.outputDir ?? "test-results/video",
    timeout: 420_000,
    expect: { timeout: 15_000 },
    fullyParallel: false,
    workers: 1,
    reporter: [["list"]],
    use: {
      baseURL,
      viewport: tamanho,
      deviceScaleFactor: 1,
      locale: process.env.DOC_LOCALE ?? "pt-BR",
      timezoneId: process.env.DOC_FUSO ?? "America/Sao_Paulo",
      video: { mode: "on", size: tamanho },
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

export default criarConfigDeVideo();
