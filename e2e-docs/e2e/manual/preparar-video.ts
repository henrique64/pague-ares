// Prepara o ambiente LOCAL para a gravação do vídeo "exportar-titulos-com-anexos" do manual "exportacoes".
//
// Rode ANTES de `doc.ts video --manual exportacoes --tarefa exportar-titulos-com-anexos`, da raiz do repositório, com a
// stack Docker de pé, a semente aplicada e as variáveis PAGUEARES_SEMENTE_CONEXAO e PAGUEARES_SEMENTE_SENHA na sessão
// (nada é impresso):
//
//   node e2e-docs/e2e/manual/preparar-video.ts
//
// O que faz (só no ambiente local; o servidor SQL precisa ser localhost,14330):
//   1. Garante a conta fictícia do vídeo, fin.video.semente@exemplo.invalid, clonada da conta fin.semente (mesma senha,
//      mesmo departamento, mesma função Financeiro). A conta é só do vídeo: nenhum outro trabalho a usa.
//   2. Zera as exportações dessa conta: cancela as em andamento, exclui pela API as Concluídas (para o arquivo sair do
//      disco) e apaga do SQL as linhas restantes de ExportacaoJob (a API não apaga linha de Falhou, Cancelada nem
//      Expirada). Assim a lista de "Minhas exportações" começa vazia e só a exportação feita no vídeo aparece.
//
// Na gravação, informe a conta ao `doc.ts video` pela variável de ambiente (a senha é a mesma da semente):
//   DOC_PAGUEARES_USUARIO=fin.video.semente@exemplo.invalid DOC_PAGUEARES_SENHA="$PAGUEARES_SEMENTE_SENHA"
// A conta some com `--limpar` da semente (apaga as contas @exemplo.invalid) e é refeita por este script.
import { spawnSync } from "node:child_process";

const API = process.env.PAGUEARES_API ?? "http://localhost:5000";
const SENHA = process.env.PAGUEARES_SEMENTE_SENHA ?? process.env.DOC_PAGUEARES_SENHA;
const CONEXAO = process.env.PAGUEARES_SEMENTE_CONEXAO;
if (!SENHA || !CONEXAO) {
  console.error("Defina PAGUEARES_SEMENTE_SENHA e PAGUEARES_SEMENTE_CONEXAO na sessão.");
  process.exit(2);
}

const EMAIL = "fin.video.semente@exemplo.invalid";
const MODELO = "fin.semente@exemplo.invalid";
const NOME = "Usuario Ficticio Financeiro Video";

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

// ---------- 1. a conta do vídeo ----------
sql(
  `if not exists (select 1 from Usuario where Email='${EMAIL}') begin ` +
    `insert into Usuario(Email,Codigo,Nome,Origem,IdExterno,Senha,IdDepartamento,Ativo,DataCadastro) ` +
    `select '${EMAIL}',Codigo,'${NOME}',Origem,IdExterno,Senha,IdDepartamento,Ativo,SYSUTCDATETIME() from Usuario where Email='${MODELO}'; ` +
    `insert into UsuarioFuncao(IdUsuario,IdFuncao) ` +
    `select n.IdUsuario,f.IdFuncao from Usuario n join Usuario m on m.Email='${MODELO}' join UsuarioFuncao f on f.IdUsuario=m.IdUsuario where n.Email='${EMAIL}'; end`,
);
const existe = sql(`set nocount on; select count(*) from Usuario where Email='${EMAIL}'`).trim();
if (existe !== "1") throw new Error(`a conta ${EMAIL} não foi criada (a conta modelo ${MODELO} existe na semente?)`);
log("conta do vídeo garantida");

// ---------- 2. zerar as exportações da conta ----------
const r = await fetch(`${API}/api/Auth`, {
  method: "POST",
  headers: { "Content-Type": "application/json" },
  body: JSON.stringify({ username: EMAIL, password: SENHA }),
});
const token = ((await r.json()) as { data?: { token?: string } }).data?.token;
if (!token) throw new Error(`login da conta do vídeo recusado (${r.status})`);
const cab = { Authorization: `Bearer ${token}` };

interface Job { id: number; statusDescricao: string }
const listar = async (): Promise<Job[]> =>
  ((await (await fetch(`${API}/api/exportacao`, { headers: cab })).json()) as { data: Job[] }).data;
const remover = (id: number) => fetch(`${API}/api/exportacao/${id}`, { method: "DELETE", headers: cab });

for (const j of await listar()) {
  if (j.statusDescricao === "Pendente" || j.statusDescricao === "Em processamento") await remover(j.id);
}
for (let i = 0; i < 60; i++) {
  if (!(await listar()).some((j) => ["Pendente", "Em processamento"].includes(j.statusDescricao))) break;
  await dormir(1500);
}
for (const j of await listar()) if (j.statusDescricao === "Concluída") await remover(j.id);

sql(`delete from ExportacaoJob where IdUsuario in (select IdUsuario from Usuario where Email='${EMAIL}')`);
const sobrou = (await listar()).length;
if (sobrou !== 0) throw new Error(`a conta do vídeo ainda tem ${sobrou} exportação(ões) na lista`);
log("pronto: a conta do vídeo está sem exportações");
