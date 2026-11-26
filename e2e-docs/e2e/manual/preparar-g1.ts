// Prepara o ambiente LOCAL para as fotos do grupo g1 do manual "exportacoes" (modo de dados real).
//
// Rode ANTES de `doc.ts capturar --manual exportacoes --grupos g1`, da raiz do repositório, com a stack Docker de pé,
// a semente aplicada e as variáveis PAGUEARES_SEMENTE_CONEXAO e PAGUEARES_SEMENTE_SENHA na sessão (nada é impresso):
//
//   node e2e-docs/e2e/manual/preparar-g1.ts
//
// O que faz (só no ambiente local; o servidor SQL precisa ser localhost,14330):
//   1. Garante três contas fictícias extras, só para as fotos: fin02.semente e fin03.semente (Financeiro) e
//      adm02.semente (Administrador), clonadas das contas da semente (mesma senha, mesmo departamento). Isso isola as
//      fotos das contas que outros trabalhos usam e dá a cada foto uma lista de exportações previsível.
//   2. Zera as exportações dessas três contas: antes, cancela as em andamento e exclui pela API as Concluídas (para o
//      arquivo sair do disco), depois apaga as linhas restantes da tabela ExportacaoJob (SQL local; a API não tem como
//      apagar linha de Falhou, Cancelada ou Expirada).
//   3. Monta para fin02, pela API de verdade, as linhas que não dá para fotografar "ao vivo", da mais antiga à mais nova:
//        Expirada   — exportação pequena, depois "Excluir" (a API a marca como Expirada);
//        Cancelada  — pedido grande (500 títulos) em andamento, depois cancelado;
//        Falhou     — outro pedido grande em andamento quando a API é reiniciada (`docker restart pagueares-api`): a
//                     recuperação da subida o encerra como Falhou, com a mensagem de interrupção.
//   fin03 e adm02 ficam sem exportações. Em processamento, Pendente, Concluída (da foto da lista), o aviso de sucesso e as
//   recusas são feitos pelo spec, ao vivo, pela própria tela.
import { spawnSync } from "node:child_process";

const API = process.env.PAGUEARES_API ?? "http://localhost:5000";
const SENHA = process.env.PAGUEARES_SEMENTE_SENHA ?? process.env.DOC_PAGUEARES_SENHA;
const CONEXAO = process.env.PAGUEARES_SEMENTE_CONEXAO;
if (!SENHA || !CONEXAO) {
  console.error("Defina PAGUEARES_SEMENTE_SENHA e PAGUEARES_SEMENTE_CONEXAO na sessão.");
  process.exit(2);
}

const log = (m: string) => console.log(`[${new Date().toTimeString().slice(0, 8)}] ${m}`);
const dormir = (ms: number) => new Promise((r) => setTimeout(r, ms));

// ---------- SQL local (sqlcmd), com a senha só no ambiente do processo filho ----------
const cs = Object.fromEntries(
  CONEXAO.split(";").filter(Boolean).map((p) => {
    const i = p.indexOf("=");
    return [p.slice(0, i).trim().toLowerCase(), p.slice(i + 1).trim()];
  }),
) as Record<string, string>;
const servidor = (cs["server"] ?? cs["data source"] ?? "").replace(/^tcp:/, "");
if (!/^(localhost|127\.0\.0\.1)[,:]14330$/.test(servidor)) {
  console.error("Servidor SQL recusado: só localhost,14330.");
  process.exit(2);
}
function sql(consulta: string): string {
  const r = spawnSync(
    "sqlcmd",
    ["-C", "-S", servidor, "-U", cs["user id"] ?? cs["uid"], "-d", cs["database"] ?? cs["initial catalog"], "-W", "-h", "-1", "-b", "-Q", consulta],
    { env: { ...process.env, SQLCMDPASSWORD: cs["password"] ?? cs["pwd"] }, encoding: "utf8" },
  );
  if (r.status !== 0) throw new Error(`sqlcmd falhou: ${(r.stdout + r.stderr).replaceAll(cs["password"] ?? "§§", "***").slice(0, 400)}`);
  return r.stdout;
}

// ---------- API ----------
async function entrar(usuario: string): Promise<string> {
  const r = await fetch(`${API}/api/Auth`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ username: `${usuario}@exemplo.invalid`, password: SENHA }),
  });
  const corpo = (await r.json()) as { data?: { token?: string } };
  if (!corpo.data?.token) throw new Error(`login de ${usuario} recusado (${r.status})`);
  return corpo.data.token;
}

interface Job { id: number; status: number; statusDescricao: string; percentual: number; titulosProcessados: number; totalTitulos: number; mensagemErro: string | null }

class Sessao {
  readonly usuario: string;
  private token: string;
  constructor(usuario: string, token: string) { this.usuario = usuario; this.token = token; }
  static async de(usuario: string) { return new Sessao(usuario, await entrar(usuario)); }
  async chamar(metodo: string, caminho: string, corpo?: unknown): Promise<{ status: number; corpo: any }> {
    const r = await fetch(`${API}/${caminho}`, {
      method: metodo,
      headers: { Authorization: `Bearer ${this.token}`, "Content-Type": "application/json" },
      body: corpo === undefined ? undefined : JSON.stringify(corpo),
    });
    const texto = await r.text();
    return { status: r.status, corpo: texto ? JSON.parse(texto) : null };
  }
  async listar(): Promise<Job[]> { return (await this.chamar("GET", "api/exportacao")).corpo.data as Job[]; }
  async pedir(filtro: object): Promise<Job> {
    const r = await this.chamar("POST", "api/exportacao", filtro);
    if (r.status !== 202) throw new Error(`pedido recusado (${r.status}): ${r.corpo?.message}`);
    return r.corpo.data as Job;
  }
  async remover(id: number) { return this.chamar("DELETE", `api/exportacao/${id}`); }
  async esperar(id: number, situacao: string, ate = 240_000): Promise<Job> {
    const limite = Date.now() + ate;
    while (Date.now() < limite) {
      const j = (await this.listar()).find((x) => x.id === id);
      if (j?.statusDescricao === situacao) return j;
      await dormir(1500);
    }
    throw new Error(`exportação ${id} não chegou a "${situacao}" a tempo`);
  }
}

async function esperarApi() {
  for (let i = 0; i < 90; i++) {
    try {
      const r = await fetch(`${API}/api/Auth`, { method: "POST", headers: { "Content-Type": "application/json" }, body: "{}" });
      if (r.status > 0) return;
    } catch { /* ainda subindo */ }
    await dormir(2000);
  }
  throw new Error("a API não voltou a responder");
}

// ---------- 1. contas extras ----------
function garantirConta(email: string, nome: string, modelo: number, funcao: number) {
  sql(
    `if not exists (select 1 from Usuario where Email='${email}') begin ` +
      `insert into Usuario(Email,Codigo,Nome,Origem,IdExterno,Senha,IdDepartamento,Ativo,DataCadastro) ` +
      `select '${email}',Codigo,'${nome}',Origem,IdExterno,Senha,IdDepartamento,Ativo,SYSUTCDATETIME() from Usuario where Email=(select Email from Usuario where IdUsuario=${modelo}); ` +
      `insert into UsuarioFuncao(IdUsuario,IdFuncao) select IdUsuario,${funcao} from Usuario where Email='${email}'; end`,
  );
}
garantirConta("fin02.semente@exemplo.invalid", "Usuario Ficticio Financeiro 02", 1020, 4);
garantirConta("fin03.semente@exemplo.invalid", "Usuario Ficticio Financeiro 03", 1020, 4);
garantirConta("adm02.semente@exemplo.invalid", "Usuario Ficticio Administrador 02", 1019, 1);
log("contas fin02, fin03 e adm02 garantidas");

// ---------- 2. zerar as exportações das duas contas ----------
for (const u of ["fin02.semente", "fin03.semente", "adm02.semente"]) {
  const s = await Sessao.de(u);
  for (const j of await s.listar()) {
    if (j.statusDescricao === "Pendente" || j.statusDescricao === "Em processamento") await s.remover(j.id);
  }
  for (let i = 0; i < 60; i++) {
    if (!(await s.listar()).some((j) => ["Pendente", "Em processamento"].includes(j.statusDescricao))) break;
    await dormir(1500);
  }
  for (const j of await s.listar()) if (j.statusDescricao === "Concluída") await s.remover(j.id);
}
sql(
  "delete from ExportacaoJob where IdUsuario in (select IdUsuario from Usuario where Email in ('fin02.semente@exemplo.invalid','fin03.semente@exemplo.invalid','adm02.semente@exemplo.invalid'))",
);
log("exportações de fin02, fin03 e adm02 zeradas");

// ---------- 3. as linhas de cada situação, para fin02 ----------
const fin = await Sessao.de("fin02.semente");

const pequena = await fin.pedir({ tipoTitulo: 0, dataSolicitacaoInicio: "2025-12-01", dataSolicitacaoFim: "2025-12-20", incluirAnexos: false });
await fin.esperar(pequena.id, "Concluída");
await fin.remover(pequena.id);
await fin.esperar(pequena.id, "Expirada");
log(`Expirada: exportação ${pequena.id}`);

async function pedidoGrandeEmProcessamento(): Promise<Job> {
  const g = await fin.pedir({ tipoTitulo: 0, incluirAnexos: true });
  for (let i = 0; i < 120; i++) {
    const j = (await fin.listar()).find((x) => x.id === g.id);
    if (j?.statusDescricao === "Em processamento" && j.titulosProcessados > 0) return j;
    await dormir(1000);
  }
  throw new Error("o pedido grande não começou a andar");
}

// Cancelada: um pedido grande já em andamento é cancelado (a linha mostra o progresso até onde chegou)
const aCancelar = await pedidoGrandeEmProcessamento();
const pedido = await fin.remover(aCancelar.id);
if (pedido.status >= 300) throw new Error(`cancelamento recusado (${pedido.status})`);
await fin.esperar(aCancelar.id, "Cancelada");
log(`Cancelada: exportação ${aCancelar.id}`);

// Falhou: a API é reiniciada com outro pedido grande em andamento
const aInterromper = await pedidoGrandeEmProcessamento();
const reinicio = spawnSync("docker", ["restart", "pagueares-api"], { encoding: "utf8" });
if (reinicio.status !== 0) throw new Error(`docker restart falhou: ${reinicio.stderr}`);
await esperarApi();
const reconectado = await Sessao.de("fin02.semente");
const falhou = await reconectado.esperar(aInterromper.id, "Falhou", 120_000);
log(`Falhou: exportação ${aInterromper.id} (${falhou.mensagemErro})`);

log("pronto: fin02 com Falhou, Cancelada e Expirada; fin03 e adm02 sem exportações");
