// Baixa o Pagamentos.xlsx pela rota legada do botão Exportar (GET /api/Export/payments), com a chave de exportação.
//
// Uso:  node baixar-pagamentos.ts <saida.xlsx> <inicio aaaa-mm-dd> <fim aaaa-mm-dd> [--api http://localhost:5000] [--login <e-mail>]
// A senha vem só de PAGUEARES_SEMENTE_SENHA (nunca de argumento nem de arquivo) e nada dela é impresso.
import { writeFileSync } from "node:fs";

const args = process.argv.slice(2);
const opcao = (nome: string, padrao: string): string => {
  const i = args.indexOf(nome);
  return i >= 0 ? args.splice(i, 2)[1] : padrao;
};
const api = opcao("--api", "http://localhost:5000").replace(/\/+$/, "");
const login = opcao("--login", "fin.semente@exemplo.invalid");
const [saida, inicio, fim] = args;
if (!saida || !inicio || !fim) throw new Error("Uso: node baixar-pagamentos.ts <saida.xlsx> <inicio> <fim> [--api <url>] [--login <e-mail>]");
const senha = process.env["PAGUEARES_SEMENTE_SENHA"];
if (!senha) throw new Error("PAGUEARES_SEMENTE_SENHA não está definida.");

const auth = await fetch(`${api}/api/Auth`, {
  method: "POST",
  headers: { "Content-Type": "application/json" },
  body: JSON.stringify({ username: login, password: senha }),
});
const dadosAuth: any = await auth.json();
const token: string = dadosAuth?.data?.token;
if (!token) throw new Error(`Login recusado (HTTP ${auth.status}).`);

// requestType=2 é EnumRequestType.Exportacao: a chave de uso único que o front pede ao clicar em Exportar.
const resChave = await fetch(`${api}/api/Preview/generate-preview-key?entityId=0&requestType=2`, {
  method: "POST",
  headers: { Authorization: `Bearer ${token}` },
});
const chave: string | undefined = ((await resChave.json()) as any)?.data?.previewKey;
if (!chave) throw new Error(`Chave de exportação não emitida (HTTP ${resChave.status}).`);

const filtro = JSON.stringify({ RequestStart: `${inicio}T00:00:00`, RequestEnd: `${fim}T23:59:59` });
const url = `${api}/api/Export/payments?sFilter=${encodeURIComponent(filtro)}&userId=0&key=${encodeURIComponent(chave)}`;
const res = await fetch(url);
if (!res.ok) throw new Error(`Exportação recusada (HTTP ${res.status}).`);
const bytes = Buffer.from(await res.arrayBuffer());
writeFileSync(saida, bytes);
console.log(`Pagamentos.xlsx gravado (${bytes.length} bytes) em ${saida}`);
