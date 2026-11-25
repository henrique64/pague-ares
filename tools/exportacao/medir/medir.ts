// Medição da exportação de títulos: login, criação do job, acompanhamento, amostragem de memória do contêiner
// da API, download do ZIP em streaming para o disco e gravação de um JSON de medição.
//
// Uso:  node tools/exportacao/medir/medir.ts --login <e-mail> [--api <url>] [--filtro <json|@arquivo>]
//                                            [--saida <json>] [--zip <caminho>] [--tempo-limite-min <n>]
//       node tools/exportacao/medir/medir.ts --ajuda
//
// A senha vem só da variável de ambiente PAGUEARES_SEMENTE_SENHA (nunca de argumento nem de arquivo).
// Requer Node 22.18 ou mais novo (executa TypeScript sem build).

import { spawn } from "node:child_process";
import { createWriteStream, existsSync, readFileSync, statSync, mkdirSync, writeFileSync } from "node:fs";
import { open } from "node:fs/promises";
import { dirname, resolve } from "node:path";
import { Readable } from "node:stream";
import { pipeline } from "node:stream/promises";
import { fileURLToPath } from "node:url";
import { inflateRawSync } from "node:zlib";

const AJUDA = `medir.ts - mede a exportação de títulos pela API (tempo, memória e tamanho do pacote).

Uso:
  node tools/exportacao/medir/medir.ts --login <e-mail> [opções]

Opções:
  --api <url>              base da API; padrão http://localhost:5000
  --login <e-mail>         usuário FIN ou ADM (por exemplo o fin.semente@exemplo.invalid criado pela semente)
  --filtro <json|@arq>     filtro da exportação (FiltroExportacaoDto em camelCase) ou @caminho de um arquivo JSON; padrão {}
  --saida <json>           arquivo da medição; padrão medicao.json
  --zip <caminho>          onde gravar o ZIP baixado; padrão exportacao.zip
  --tempo-limite-min <n>   desiste de esperar a conclusão após n minutos; padrão 120
  --ajuda                  mostra este texto

Ambiente:
  PAGUEARES_SEMENTE_SENHA  senha do usuário informado em --login (obrigatória)

Saída (JSON): titulos, anexos, bytesAnexos, tamanhoZipBytes, segundosAteConcluir, segundosDownload,
  memoriaApiMiBAntes, memoriaApiMiBPico, amostras[{t, mib}].
  A memória vem de "docker stats --no-stream" do contêiner pagueares-api, a cada 5 s; sem Docker os campos de memória saem null.

Códigos de saída: 0 ok; 1 falha na exportação ou na rede; 2 uso incorreto.`;

const NOME_CONTEINER = "pagueares-api";
const INTERVALO_MEMORIA_MS = 5000;
const INTERVALO_STATUS_MS = 2000;

// Valores de StatusExportacao (C5).
const STATUS = { Pendente: 0, Processando: 1, Concluido: 2, Falhou: 3, Expirado: 4, Cancelado: 5 } as const;

interface Opcoes {
  api: string;
  login: string;
  filtro: string;
  saida: string;
  zip: string;
  tempoLimiteMin: number;
}

function lerOpcoes(argv: string[]): Opcoes | null {
  const op: Opcoes = { api: "http://localhost:5000", login: "", filtro: "{}", saida: "medicao.json", zip: "exportacao.zip", tempoLimiteMin: 120 };
  for (let i = 0; i < argv.length; i++) {
    const a = argv[i];
    const proximo = (): string => {
      if (i + 1 >= argv.length) throw new Error(`Falta o valor de ${a}.`);
      return argv[++i];
    };
    switch (a) {
      case "--ajuda":
      case "-h":
      case "--help":
        return null;
      case "--api": op.api = proximo().replace(/\/+$/, ""); break;
      case "--login": op.login = proximo(); break;
      case "--filtro": op.filtro = proximo(); break;
      case "--saida": op.saida = proximo(); break;
      case "--zip": op.zip = proximo(); break;
      case "--tempo-limite-min": op.tempoLimiteMin = Number(proximo()); break;
      default: throw new Error(`Argumento desconhecido: ${a}.`);
    }
  }
  return op;
}

function lerFiltro(texto: string): unknown {
  let conteudo = texto.trim();
  if (conteudo.startsWith("@")) conteudo = readFileSync(conteudo.slice(1), "utf8");
  else if (!conteudo.startsWith("{") && existsSync(conteudo)) conteudo = readFileSync(conteudo, "utf8");
  try {
    return JSON.parse(conteudo.replace(/^﻿/, ""));
  } catch {
    throw new Error("--filtro não é um JSON válido.");
  }
}

// ---- Memória do contêiner ---------------------------------------------------------------------------

// Converte a primeira parte de "123.4MiB / 7.6GiB" em MiB.
export function paraMiB(memUsage: string): number | null {
  const m = /^\s*([\d.,]+)\s*([A-Za-z]+)/.exec(memUsage);
  if (!m) return null;
  const n = Number(m[1].replace(",", "."));
  const unidade = m[2].toLowerCase();
  const fatores: Record<string, number> = {
    b: 1 / (1024 * 1024),
    kib: 1 / 1024, kb: 1000 / (1024 * 1024),
    mib: 1, mb: 1000 * 1000 / (1024 * 1024),
    gib: 1024, gb: 1000 * 1000 * 1000 / (1024 * 1024),
    tib: 1024 * 1024, tb: 1000 * 1000 * 1000 * 1000 / (1024 * 1024),
  };
  const f = fatores[unidade];
  return f === undefined || Number.isNaN(n) ? null : Math.round(n * f * 10) / 10;
}

function amostrarMemoria(): Promise<number | null> {
  return new Promise((resolver) => {
    let saida = "";
    let terminou = false;
    const fim = (v: number | null) => { if (!terminou) { terminou = true; resolver(v); } };
    try {
      const p = spawn("docker", ["stats", "--no-stream", "--format", "{{.MemUsage}}", NOME_CONTEINER], { windowsHide: true });
      p.stdout.on("data", (d) => { saida += d.toString(); });
      p.on("error", () => fim(null));
      p.on("close", (codigo) => fim(codigo === 0 ? paraMiB(saida.trim()) : null));
      setTimeout(() => { p.kill(); fim(null); }, 30000).unref();
    } catch {
      fim(null);
    }
  });
}

// ---- Leitura do indice.json dentro do ZIP (sem biblioteca, só o diretório central) -------------------

// Devolve o conteúdo de uma entrada do ZIP, lendo apenas o diretório central e a própria entrada.
export async function lerEntradaZip(caminho: string, nomeEntrada: string): Promise<Buffer | null> {
  const arquivo = await open(caminho, "r");
  try {
    const tamanho = (await arquivo.stat()).size;
    const leituraFim = Math.min(tamanho, 65535 + 22 + 20 + 64);
    const cauda = Buffer.alloc(leituraFim);
    await arquivo.read(cauda, 0, leituraFim, tamanho - leituraFim);

    let eocd = -1;
    for (let i = cauda.length - 22; i >= 0; i--) {
      if (cauda.readUInt32LE(i) === 0x06054b50) { eocd = i; break; }
    }
    if (eocd < 0) return null;

    let totalEntradas = cauda.readUInt16LE(eocd + 10);
    let tamanhoCd = cauda.readUInt32LE(eocd + 12);
    let deslocamentoCd = cauda.readUInt32LE(eocd + 16);

    if (totalEntradas === 0xffff || tamanhoCd === 0xffffffff || deslocamentoCd === 0xffffffff) {
      const loc = eocd - 20;
      if (loc < 0 || cauda.readUInt32LE(loc) !== 0x07064b50) return null;
      const posZip64 = Number(cauda.readBigUInt64LE(loc + 8));
      const reg = Buffer.alloc(56);
      await arquivo.read(reg, 0, 56, posZip64);
      if (reg.readUInt32LE(0) !== 0x06064b50) return null;
      totalEntradas = Number(reg.readBigUInt64LE(32));
      tamanhoCd = Number(reg.readBigUInt64LE(40));
      deslocamentoCd = Number(reg.readBigUInt64LE(48));
    }

    const cd = Buffer.alloc(tamanhoCd);
    await arquivo.read(cd, 0, tamanhoCd, deslocamentoCd);

    let pos = 0;
    for (let n = 0; n < totalEntradas && pos + 46 <= cd.length; n++) {
      if (cd.readUInt32LE(pos) !== 0x02014b50) return null;
      const metodo = cd.readUInt16LE(pos + 10);
      let tamComprimido = cd.readUInt32LE(pos + 20);
      let tamOriginal = cd.readUInt32LE(pos + 24);
      const lenNome = cd.readUInt16LE(pos + 28);
      const lenExtra = cd.readUInt16LE(pos + 30);
      const lenComentario = cd.readUInt16LE(pos + 32);
      let deslocamentoLocal = cd.readUInt32LE(pos + 42);
      const nome = cd.toString("utf8", pos + 46, pos + 46 + lenNome);

      if (nome === nomeEntrada) {
        // Campos Zip64 do extra: só aparecem os que estavam em 0xFFFFFFFF, nesta ordem.
        const extra = cd.subarray(pos + 46 + lenNome, pos + 46 + lenNome + lenExtra);
        for (let e = 0; e + 4 <= extra.length;) {
          const id = extra.readUInt16LE(e);
          const len = extra.readUInt16LE(e + 2);
          if (id === 0x0001) {
            let o = e + 4;
            if (tamOriginal === 0xffffffff) { tamOriginal = Number(extra.readBigUInt64LE(o)); o += 8; }
            if (tamComprimido === 0xffffffff) { tamComprimido = Number(extra.readBigUInt64LE(o)); o += 8; }
            if (deslocamentoLocal === 0xffffffff) { deslocamentoLocal = Number(extra.readBigUInt64LE(o)); o += 8; }
          }
          e += 4 + len;
        }
        const cab = Buffer.alloc(30);
        await arquivo.read(cab, 0, 30, deslocamentoLocal);
        if (cab.readUInt32LE(0) !== 0x04034b50) return null;
        const inicioDados = deslocamentoLocal + 30 + cab.readUInt16LE(26) + cab.readUInt16LE(28);
        const comprimido = Buffer.alloc(tamComprimido);
        await arquivo.read(comprimido, 0, tamComprimido, inicioDados);
        if (metodo === 0) return comprimido;
        if (metodo === 8) return inflateRawSync(comprimido);
        return null;
      }
      pos += 46 + lenNome + lenExtra + lenComentario;
    }
    return null;
  } finally {
    await arquivo.close();
  }
}

// ---- API --------------------------------------------------------------------------------------------

interface Envelope<T> { success?: boolean; message?: string; data?: T }

async function chamar<T>(api: string, rota: string, token: string | null, metodo: string, corpo?: unknown): Promise<{ status: number; corpo: Envelope<T> | null }> {
  const cabecalhos: Record<string, string> = { Accept: "application/json" };
  if (token) cabecalhos["Authorization"] = `Bearer ${token}`;
  if (corpo !== undefined) cabecalhos["Content-Type"] = "application/json";
  const resposta = await fetch(`${api}${rota}`, { method: metodo, headers: cabecalhos, body: corpo === undefined ? undefined : JSON.stringify(corpo) });
  let json: Envelope<T> | null = null;
  try { json = (await resposta.json()) as Envelope<T>; } catch { json = null; }
  return { status: resposta.status, corpo: json };
}

function dormir(ms: number): Promise<void> {
  return new Promise((r) => setTimeout(r, ms));
}

interface JobDto {
  id: number; status: number; statusDescricao: string; totalTitulos: number; titulosProcessados: number;
  percentual: number; totalAnexos: number; tamanhoBytes?: number | null; mensagemErro?: string | null;
}

async function principal(): Promise<number> {
  let op: Opcoes | null;
  try {
    op = lerOpcoes(process.argv.slice(2));
  } catch (e) {
    console.error((e as Error).message + " Use --ajuda.");
    return 2;
  }
  if (op === null) {
    console.log(AJUDA);
    return 0;
  }
  if (!op.login) { console.error("Informe --login <e-mail>. Use --ajuda."); return 2; }
  if (!Number.isFinite(op.tempoLimiteMin) || op.tempoLimiteMin <= 0) { console.error("--tempo-limite-min inválido."); return 2; }
  const senha = process.env["PAGUEARES_SEMENTE_SENHA"];
  if (!senha) { console.error("Defina a variável PAGUEARES_SEMENTE_SENHA."); return 2; }

  let filtro: unknown;
  try { filtro = lerFiltro(op.filtro); } catch (e) { console.error((e as Error).message); return 2; }

  // 1. Login
  const login = await chamar<{ token: string }>(op.api, "/api/Auth", null, "POST", { username: op.login, password: senha });
  const token = login.corpo?.data?.token;
  if (login.status !== 200 || !login.corpo?.success || !token) {
    console.error(`Login recusado (HTTP ${login.status}): ${login.corpo?.message ?? "sem mensagem"}`);
    return 1;
  }
  console.log("Login ok.");

  // 2. Amostragem de memória em segundo plano, a cada 5 s, desde antes da criação do job.
  const amostras: { t: number; mib: number }[] = [];
  const inicioRelogio = Date.now();
  let amostrando = true;
  let memoriaAntes: number | null = await amostrarMemoria();
  if (memoriaAntes === null) console.warn("Aviso: não foi possível ler a memória do contêiner (Docker ausente ou contêiner parado); campos de memória ficarão null.");
  const lacoMemoria = (async () => {
    while (amostrando) {
      await dormir(INTERVALO_MEMORIA_MS);
      if (!amostrando) break;
      const mib = await amostrarMemoria();
      if (mib !== null) amostras.push({ t: Math.round((Date.now() - inicioRelogio) / 100) / 10, mib });
    }
  })();

  try {
    // 3. Criar o job
    const t0 = Date.now();
    const criar = await chamar<JobDto>(op.api, "/api/exportacao", token, "POST", filtro);
    if (criar.status !== 202 || !criar.corpo?.data) {
      console.error(`Criação recusada (HTTP ${criar.status}): ${criar.corpo?.message ?? "sem mensagem"}`);
      return 1;
    }
    const id = criar.corpo.data.id;
    console.log(`Exportação ${id} criada (${criar.corpo.data.totalTitulos} títulos informados).`);

    // 4. Acompanhar até concluir
    const limite = t0 + op.tempoLimiteMin * 60_000;
    let job: JobDto = criar.corpo.data;
    let ultimoPercentual = -1;
    while (job.status === STATUS.Pendente || job.status === STATUS.Processando) {
      if (Date.now() > limite) { console.error("Tempo limite excedido esperando a exportação."); return 1; }
      await dormir(INTERVALO_STATUS_MS);
      const r = await chamar<JobDto>(op.api, `/api/exportacao/${id}`, token, "GET");
      if (r.status !== 200 || !r.corpo?.data) { console.error(`Falha ao consultar o job (HTTP ${r.status}).`); return 1; }
      job = r.corpo.data;
      if (job.percentual !== ultimoPercentual && job.percentual % 10 === 0) {
        console.log(`  ${job.statusDescricao}: ${job.percentual}% (${job.titulosProcessados}/${job.totalTitulos})`);
        ultimoPercentual = job.percentual;
      }
    }
    const segundosAteConcluir = Math.round((Date.now() - t0) / 100) / 10;
    if (job.status !== STATUS.Concluido) {
      console.error(`A exportação terminou como "${job.statusDescricao}"${job.mensagemErro ? `: ${job.mensagemErro}` : ""}.`);
      return 1;
    }
    console.log(`Concluída em ${segundosAteConcluir} s; ${job.totalAnexos} anexos; ${job.tamanhoBytes ?? "?"} bytes.`);

    // 5. Link de download e download em streaming direto para o disco
    const link = await chamar<{ token: string }>(op.api, `/api/exportacao/${id}/link-download`, token, "POST");
    const tokenDownload = link.corpo?.data?.token;
    if (link.status !== 200 || !tokenDownload) { console.error(`Link de download recusado (HTTP ${link.status}).`); return 1; }

    const destino = resolve(op.zip);
    mkdirSync(dirname(destino), { recursive: true });
    const tDownload = Date.now();
    const resposta = await fetch(`${op.api}/api/exportacao/download?token=${encodeURIComponent(tokenDownload)}`);
    if (resposta.status !== 200 || !resposta.body) { console.error(`Download recusado (HTTP ${resposta.status}).`); return 1; }
    await pipeline(Readable.fromWeb(resposta.body as import("node:stream/web").ReadableStream), createWriteStream(destino));
    const segundosDownload = Math.round((Date.now() - tDownload) / 100) / 10;
    const tamanhoZipBytes = statSync(destino).size;
    console.log(`Download em ${segundosDownload} s: ${tamanhoZipBytes} bytes em ${destino}.`);

    // 6. Totais do próprio pacote (indice.json); se não der para ler, usa os do job.
    let titulos = job.totalTitulos;
    let anexos = job.totalAnexos;
    let bytesAnexos: number | null = null;
    try {
      const bruto = await lerEntradaZip(destino, "indice.json");
      if (bruto) {
        const indice = JSON.parse(bruto.toString("utf8").replace(/^﻿/, ""));
        titulos = (indice.totais?.solicitacoes ?? 0) + (indice.totais?.rddvs ?? 0);
        anexos = indice.totais?.anexos ?? anexos;
        bytesAnexos = indice.totais?.bytesAnexos ?? null;
      } else {
        console.warn("Aviso: indice.json não encontrado no ZIP; bytesAnexos ficará null.");
      }
    } catch (e) {
      console.warn(`Aviso: não foi possível ler indice.json (${(e as Error).message}); bytesAnexos ficará null.`);
    }

    amostrando = false;
    await lacoMemoria;
    const todas = amostras.map((a) => a.mib);
    if (memoriaAntes !== null) todas.push(memoriaAntes);
    const pico = todas.length > 0 ? Math.max(...todas) : null;

    const medicao = {
      titulos,
      anexos,
      bytesAnexos,
      tamanhoZipBytes,
      segundosAteConcluir,
      segundosDownload,
      memoriaApiMiBAntes: memoriaAntes,
      memoriaApiMiBPico: pico,
      amostras,
    };
    const arquivoSaida = resolve(op.saida);
    mkdirSync(dirname(arquivoSaida), { recursive: true });
    writeFileSync(arquivoSaida, JSON.stringify(medicao, null, 2) + "\n", "utf8");
    console.log(`Medição gravada em ${arquivoSaida}.`);
    return 0;
  } finally {
    amostrando = false;
  }
}

// Só executa quando chamado diretamente (permite importar paraMiB e lerEntradaZip em testes).
const executadoDiretamente = process.argv[1] !== undefined && resolve(process.argv[1]) === resolve(fileURLToPath(import.meta.url));
if (executadoDiretamente) {
  principal().then(
    (codigo) => { process.exitCode = codigo; },
    (erro) => { console.error(`Falha: ${(erro as Error).message}`); process.exitCode = 1; },
  );
}
