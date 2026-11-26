// Monta as páginas HTML de que as fotos do grupo g2 (o pacote gerado) são tiradas.
//
// O pacote da exportação não é uma tela do app: o usuário o extrai e abre no Explorador, no Excel e no Bloco de Notas.
// Estas páginas mostram esse conteúdo, lido do pacote REAL extraído (árvore, LEIA-ME.txt, indice.csv, indice.json) e dos
// JSON que `xlsx-para-json.cs` gera de titulos.xlsx e de Pagamentos.xlsx. Nada é inventado: cada texto, valor e nome
// vem do arquivo; só a moldura (barra de título, letras de coluna, abas) é desenhada.
//
// Uso:  node montar-paginas.ts --pacote <pasta do ZIP extraído> --dados <pasta com titulos.json e pagamentos.json> --saida <pasta>
// Receita completa (gerar o pacote, extrair, converter e montar): README.md desta pasta.
import { readdirSync, readFileSync, statSync, mkdirSync, writeFileSync } from "node:fs";
import { join } from "node:path";

const args = process.argv.slice(2);
const opcao = (nome: string): string => {
  const i = args.indexOf(nome);
  if (i < 0 || i + 1 >= args.length) throw new Error(`Falta ${nome}. Uso: node montar-paginas.ts --pacote <pasta> --dados <pasta> --saida <pasta>`);
  return args[i + 1];
};
const pastaPacote = opcao("--pacote");
const pastaDados = opcao("--dados");
const pastaSaida = opcao("--saida");
mkdirSync(pastaSaida, { recursive: true });

const esc = (s: string): string => s.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;").replace(/"/g, "&quot;");
const semBom = (s: string): string => s.replace(/^﻿/, "");
const lerTexto = (nome: string): string => semBom(readFileSync(join(pastaPacote, nome), "utf8"));
const milhar = (n: number): string => n.toLocaleString("pt-BR");

const CSS = `
*{box-sizing:border-box}
html,body{margin:0;background:#dfe3e8;font-family:"Segoe UI",Arial,sans-serif;color:#1f2328}
body{padding:20px}
.janela{width:1320px;background:#fff;border:1px solid #8a8f98;box-shadow:0 2px 8px rgba(0,0,0,.25)}
.titulo{height:32px;background:#f3f4f6;border-bottom:1px solid #c9ccd1;display:flex;align-items:center;padding:0 12px;font-size:13px;color:#333}
.titulo b{font-weight:600}
.mono{font-family:Consolas,"Cascadia Mono","Courier New",monospace}
`;

function pagina(titulo: string, corpo: string, css = ""): string {
  return `<!DOCTYPE html>\n<html lang="pt-BR"><head><meta charset="utf-8"><title>${esc(titulo)}</title><style>${CSS}${css}</style></head><body>\n${corpo}\n</body></html>\n`;
}

// ---------- Árvore do pacote extraído ----------

interface No {
  nome: string;
  pasta: boolean;
  bytes: number;
  filhos: No[];
}

function lerArvore(dir: string): No[] {
  const itens = readdirSync(dir, { withFileTypes: true }).map((e): No => {
    const caminho = join(dir, e.name);
    return e.isDirectory()
      ? { nome: e.name, pasta: true, bytes: 0, filhos: lerArvore(caminho) }
      : { nome: e.name, pasta: false, bytes: statSync(caminho).size, filhos: [] };
  });
  // Como o Explorador do Windows: pastas primeiro, depois arquivos, cada grupo em ordem alfabética sem diferenciar caixa.
  return itens.sort((a, b) => Number(b.pasta) - Number(a.pasta) || a.nome.localeCompare(b.nome, "pt-BR", { sensitivity: "base", numeric: true }));
}

const tamanhoExplorador = (bytes: number): string => `${milhar(Math.max(1, Math.ceil(bytes / 1024)))} KB`;

function linhasDaArvore(nos: No[], saida: string[]): void {
  for (const no of nos) {
    saida.push(
      `<div class="l${no.pasta ? " pasta" : ""}"><span class="n">${no.pasta ? "[+] " : ""}${esc(no.nome)}</span><span class="t">${no.pasta ? "" : tamanhoExplorador(no.bytes)}</span></div>`,
    );
    if (no.pasta) {
      saida.push(`<div class="filhos">`);
      linhasDaArvore(no.filhos, saida);
      saida.push("</div>");
    }
  }
}

const indice = JSON.parse(lerTexto("indice.json")) as { exportacaoId: number; geradoEm: string };
const aaaammdd = indice.geradoEm.slice(0, 10).replace(/-/g, "");
const hhmm = indice.geradoEm.slice(11, 16).replace(":", "");
const nomeDaPasta = `exportacao-titulos-${indice.exportacaoId}-${aaaammdd}-${hhmm}`;

{
  const linhas: string[] = [];
  linhasDaArvore(lerArvore(pastaPacote), linhas);
  const css = `.corpo{padding:14px 18px 18px;font-size:14px;line-height:24px;min-height:200px}
.cab{display:flex;justify-content:space-between;color:#555;border-bottom:1px solid #ddd;padding-bottom:4px;margin-bottom:6px;font-size:12px}
.l{display:flex;justify-content:space-between;white-space:pre;font-family:Consolas,"Cascadia Mono","Courier New",monospace}
.l .t{color:#555;width:110px;text-align:right}
.l.pasta .n{font-weight:700}
.filhos{margin-left:10px;padding-left:20px;border-left:1px solid #b8bcc2}`;
  const corpo = `<div class="janela" id="janela"><div class="titulo"><b>${esc(nomeDaPasta)}</b>&nbsp;&nbsp;(pasta do ZIP extraído)</div>
<div class="corpo"><div class="cab"><span>Nome</span><span style="width:110px;text-align:right">Tamanho</span></div>
${linhas.join("\n")}
</div></div>`;
  writeFileSync(join(pastaSaida, "arvore.html"), pagina("Árvore do pacote", corpo, css));
}

// ---------- Planilhas (titulos.xlsx e Pagamentos.xlsx) ----------

interface Celula {
  t: string;
  num: boolean;
  neg: boolean;
  fundo: string | null;
  lig: string | null;
}
interface Aba {
  nome: string;
  larguras: number[];
  filtro: boolean;
  bordas: boolean;
  linhas: Celula[][];
}

const letra = (i: number): string => {
  let n = i + 1;
  let s = "";
  while (n > 0) {
    const r = (n - 1) % 26;
    s = String.fromCharCode(65 + r) + s;
    n = Math.floor((n - 1) / 26);
  }
  return s;
};

function paginaDePlanilha(arquivo: string, abas: Aba[], ativa: number, minimoDeLinhas = 16, zoom = 1): string {
  const aba = abas[ativa];
  const px = aba.larguras.map((w) => Math.round(w * 7 + 5));
  const colgroup = `<col style="width:46px">${px.map((p) => `<col style="width:${p}px">`).join("")}`;
  const larguraTotal = 46 + px.reduce((a, b) => a + b, 0);
  const cabLetras = `<tr class="letras"><th class="canto"></th>${px.map((_, i) => `<th>${letra(i)}</th>`).join("")}</tr>`;
  const celulaDe = (c: Celula, cabecalho: boolean): string => {
    const classes = [c.num ? "d" : "", c.neg ? "neg" : "", aba.bordas ? "borda" : "", cabecalho ? "cab" : ""].filter(Boolean).join(" ");
    const estilo = c.fundo ? ` style="background:#${c.fundo}"` : "";
    const texto = c.lig ? `<a>${esc(c.t)}</a>` : esc(c.t);
    const filtro = cabecalho && aba.filtro ? `<i class="f">▾</i>` : "";
    return `<td class="${classes}"${estilo}>${texto}${filtro}</td>`;
  };
  const corpoLinhas = aba.linhas.map((cs, r) => `<tr class="${r === 0 ? "primeira" : ""}"><th class="num">${r + 1}</th>${cs.map((c) => celulaDe(c, r === 0)).join("")}</tr>`);
  for (let r = aba.linhas.length; r < minimoDeLinhas; r++) {
    corpoLinhas.push(`<tr><th class="num">${r + 1}</th>${px.map(() => "<td></td>").join("")}</tr>`);
  }
  const guias = abas.map((a, i) => `<span class="guia${i === ativa ? " ativa" : ""}">${esc(a.nome)}</span>`).join("");
  const css = `.janela{width:1320px}
.grade{overflow:hidden;width:${Math.round(1318 / zoom)}px;zoom:${zoom};font-family:Calibri,"Segoe UI",Arial,sans-serif;font-size:13.5px}
table{border-collapse:collapse;table-layout:fixed;width:${larguraTotal}px}
td,th{height:20px;line-height:19px;padding:0 4px;border:1px solid #d4d4d4;white-space:nowrap;overflow:hidden;text-overflow:clip;text-align:left}
th{background:#f0f0f0;color:#444;font-weight:400;text-align:center;padding:0}
th.num{background:#f0f0f0}
td.d{text-align:right}
td.neg{font-weight:700}
td.cab{position:relative;padding-right:18px}
td.borda{border:1px solid #000}
td a{color:#0563c1;text-decoration:underline}
i.f{position:absolute;right:1px;top:2px;width:15px;height:15px;border:1px solid #999;background:#fff;color:#333;font-style:normal;font-size:10px;line-height:13px;text-align:center}
tr.primeira td{border-bottom:2px solid #217346}
.guias{display:flex;background:#e6e6e6;border-top:1px solid #b5b5b5;font-family:"Segoe UI",Arial,sans-serif;font-size:12.5px;height:28px}
.guia{padding:0 18px;line-height:27px;color:#333}
.guia.ativa{background:#fff;color:#217346;font-weight:600;border-bottom:3px solid #217346}`;
  const corpo = `<div class="janela" id="janela"><div class="titulo"><b>${esc(arquivo)}</b></div>
<div class="grade"><table><colgroup>${colgroup}</colgroup>${cabLetras}${corpoLinhas.join("\n")}</table></div>
<div class="guias">${guias}</div></div>`;
  return pagina(`${arquivo} - ${aba.nome}`, corpo, css);
}

{
  const titulos = JSON.parse(readFileSync(join(pastaDados, "titulos.json"), "utf8")) as { abas: Aba[] };
  const nomes = ["planilha-solicitacoes", "planilha-rddvs", "planilha-despesas", "planilha-anexos"];
  if (titulos.abas.length !== nomes.length) throw new Error(`titulos.xlsx deveria ter ${nomes.length} abas e tem ${titulos.abas.length}`);
  nomes.forEach((nome, i) => writeFileSync(join(pastaSaida, `${nome}.html`), paginaDePlanilha("titulos.xlsx", titulos.abas, i, 16, nome === "planilha-anexos" ? 0.77 : 1)));

  const pagamentos = JSON.parse(readFileSync(join(pastaDados, "pagamentos.json"), "utf8")) as { abas: Aba[] };
  writeFileSync(join(pastaSaida, "pagamentos.html"), paginaDePlanilha("Pagamentos.xlsx", pagamentos.abas, 0, 12, 0.92));
}

// ---------- Arquivos de texto (indice.csv, indice.json, LEIA-ME.txt) ----------

function paginaDeTexto(arquivo: string, linhas: string[], primeiraLinha: number | null, tamanhoDaFonte: number): string {
  const css = `.texto{padding:10px 0 14px;font-size:${tamanhoDaFonte}px;line-height:${Math.round(tamanhoDaFonte * 1.45)}px}
.linha{display:flex;white-space:pre;min-height:${Math.round(tamanhoDaFonte * 1.45)}px}
.linha .g{flex:none;width:52px;padding-right:12px;text-align:right;color:#8a8f98;user-select:none}
.linha .c{padding-left:${primeiraLinha === null ? 16 : 0}px}`;
  const corpoLinhas = linhas
    .map((l, i) => `<div class="linha mono">${primeiraLinha === null ? "" : `<span class="g">${primeiraLinha + i}</span>`}<span class="c">${esc(l)}</span></div>`)
    .join("\n");
  const corpo = `<div class="janela" id="janela"><div class="titulo"><b>${esc(arquivo)}</b></div><div class="texto">${corpoLinhas}</div></div>`;
  return pagina(arquivo, corpo, css);
}

const linhasDe = (texto: string): string[] => texto.replace(/\r\n/g, "\n").replace(/\n$/, "").split("\n");

writeFileSync(join(pastaSaida, "indice-csv.html"), paginaDeTexto("indice.csv", linhasDe(lerTexto("indice.csv")), null, 11.5));
writeFileSync(join(pastaSaida, "leia-me.html"), paginaDeTexto("LEIA-ME.txt", linhasDe(lerTexto("LEIA-ME.txt")), null, 14));

{
  const json = linhasDe(lerTexto("indice.json"));
  const iTitulos = json.findIndex((l) => l.startsWith('  "titulos"'));
  const iArquivos = json.findIndex((l) => l.startsWith('  "arquivos"'));
  if (iTitulos < 0 || iArquivos < 0) throw new Error("indice.json sem as seções titulos e arquivos");
  // Começo do arquivo: versão, exportação, filtros, totais e as primeiras linhas de titulos.
  const fimInicio = iTitulos + 1 + 2 * 7;
  writeFileSync(join(pastaSaida, "indice-json-inicio.html"), paginaDeTexto("indice.json", json.slice(0, fimInicio), 1, 13));
  // Seção arquivos: o cabeçalho da seção e os três primeiros anexos.
  const fimArquivos = iArquivos + 1 + 3 * 10;
  writeFileSync(join(pastaSaida, "indice-json-arquivos.html"), paginaDeTexto("indice.json", json.slice(iArquivos, fimArquivos), iArquivos + 1, 13));
}

console.log(`Páginas gravadas em ${pastaSaida}`);
