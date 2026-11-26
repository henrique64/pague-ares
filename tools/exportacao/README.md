# Ferramentas de verificação da exportação de títulos com anexos

Três ferramentas independentes, fora do código de produção, usadas para semear dados fictícios, medir uma exportação e conferir o pacote ZIP gerado. Cada uma fica na própria pasta para que nenhum `.csproj` ou `Directory.Build.props` vizinho interfira. Nenhuma contém segredo: tudo o que é sensível entra por variável de ambiente.

Todos os comandos abaixo rodam a partir da raiz do repositório. As ferramentas em C# são aplicativos de arquivo único (`dotnet run arquivo.cs`, .NET 10) e restauram os próprios pacotes NuGet; a de medição é TypeScript executado direto pelo Node 22.18 ou mais novo (`node arquivo.ts`).

## Variáveis de ambiente

| Variável | Quem usa | Conteúdo |
|---|---|---|
| `PAGUEARES_SEMENTE_CONEXAO` | `semente.cs` | String de conexão do SQL Server **local** do Docker. Só `localhost` ou `127.0.0.1` na porta `14330` (formato `localhost,14330`). |
| `PAGUEARES_SEMENTE_SENHA` | `semente.cs`, `medir.ts` | Senha dos usuários fictícios criados pela semente. É gravada como hash BCrypt na semente e usada no login pela medição. |

Defina as duas só na sessão do terminal, nunca em arquivo versionado. Nenhuma ferramenta imprime esses valores.

## 1. Semente: `semente/semente.cs`

Insere dados fictícios e determinísticos (mesma `--semente`, mesmos dados) no SQL Server local.

```
dotnet run tools/exportacao/semente/semente.cs -- --titulos 500 --mb-por-titulo 5 --semente 42
dotnet run tools/exportacao/semente/semente.cs -- --limpar
dotnet run tools/exportacao/semente/semente.cs -- --autoteste
dotnet run tools/exportacao/semente/semente.cs -- --ajuda
```

| Opção | Padrão | Efeito |
|---|---|---|
| `--titulos <n>` | 500 | Títulos **exportáveis**: cerca de 60% solicitações e 40% RDDVs (com 2 a 8 despesas cada). |
| `--mb-por-titulo <n>` | 5 | Total de anexos por título, repartido em 1 a 3 anexos (aceita decimal, por exemplo `0.1`). |
| `--semente <n>` | 42 | Semente do gerador pseudoaleatório. Datas partem de uma data fixa, nunca do relógio. |
| `--autoteste` | | Verifica os CPFs e CNPJs fictícios (ver abaixo) e sai. Não acessa banco nem exige variável de ambiente. |
| `--limpar` | | Remove tudo o que a semente criou e sai. Não semeia. |

O que é criado:

- Usuários `@exemplo.invalid`, todos com a senha de `PAGUEARES_SEMENTE_SENHA`: `adm.semente`, `fin.semente`, `con.semente`, `ges.semente` e `sol01` a `sol05.semente` (papéis ADM, FIN, CON, GES e SOL).
- Os títulos exportáveis têm `Rascunho = 0`, `Cancelado = 0`, `AprovadoGestor = 1` e datas de aprovação do gestor; o financeiro varia (autorizado, aguardando, negado) e o contábil só aparece depois de autorizado. Os títulos atribuídos (aba Financeiro/Contábil) vão para `adm.semente`, `fin.semente` e, em cerca de um terço, para `con.semente`, que só vê o que lhe é atribuído.
- Além deles entram cerca de 5% (mínimo 2) de **rascunhos e cancelados**, também aprovados pelo gestor, para provar que ficam de fora da exportação. Eles têm um anexo pequeno cada.
- Anexos em base64 na coluna `Arquivo`, conteúdo binário pseudoaleatório com cabeçalho de PDF ou JPEG, metade com o prefixo `data:...;base64,`. A coluna `CaminhoArquivo` guarda o tipo de mídia do conteúdo (`application/pdf` ou `image/jpeg`), como o front grava `file.type` no upload e como o `PreviewController` espera ao servir o anexo. Cada anexo vai num comando próprio (nunca se monta o total em memória), em transações de 20 títulos.
- Nomes problemáticos de anexo (`../x.pdf`, `..\..\windows\system.ini`, `C:\Windows\x.exe`, `CON.pdf`, `nul`, `a/b\c.pdf`, nome com espaço final, caracteres inválidos, nome de 245 caracteres, nome vazio), nomes duplicados no mesmo título (inclusive em outra caixa) e tipos de documento variados, para exercitar a sanitização e a deduplicação do pacote.
- CPF e CNPJ **obviamente fictícios**: o número-base é sorteado pela `--semente`, mas os dois dígitos verificadores são propositalmente errados (o gerador calcula o dígito correto e o troca por outro valor). Nenhum passa na validação oficial, então não podem coincidir com documento real, o que importa porque os dados aparecem em capturas de tela. Saem sempre formatados (`000.000.000-00` e `00.000.000/0000-00`), são únicos dentro de uma execução e continuam determinísticos pela `--semente`. Usados em `Solicitacao.NumDocParceiro` e em `Rddv.CPF`.
- Marcação para o `--limpar`: `[SEMENTE]` no início de `Solicitacao.Descricao` e de `Rddv.Finalidade`, e e-mails `@exemplo.invalid` nos usuários. O `--limpar` também apaga os jobs de exportação desses usuários, quando a tabela `ExportacaoJob` já existe.

`--autoteste` gera 10.000 CPFs e 10.000 CNPJs com a `--semente` informada e confirma que nenhum passa no algoritmo de dígito verificador (módulo 11, implementado à parte do gerador e conferido contra um CPF e um CNPJ válidos conhecidos), que todos têm o formato esperado, que são únicos e que repetir a semente repete a lista. Sai com `0` se tudo passar e `1` se algo falhar.

Se já existir qualquer dado da semente, o comando recusa (código 3): rode `--limpar` antes.

Segurança: antes de abrir qualquer conexão a ferramenta valida o servidor e recusa (código 2) tudo o que não for `localhost`/`127.0.0.1` na porta `14330` (também recusa instância nomeada, `Failover Partner` e `AttachDbFilename`). O servidor local usa certificado autoassinado, então a ferramenta liga `TrustServerCertificate` se a string não o definir. Para 500 títulos de 5 MB espere alguns GB no banco e vários minutos: rode em segundo plano com a saída num log.

Códigos de saída: `0` ok; `1` erro durante a execução; `2` uso incorreto ou servidor recusado; `3` já existe semente.

## 2. Verificador: `verificar/verificar.cs`

Confere um pacote ZIP contra os contratos de nomes, estrutura, planilha e índices.

```
dotnet run tools/exportacao/verificar/verificar.cs -- <zip> [--relatorio <json>] [--previa-html <html>]
```

Falha (saída `1`, cada falha listada com a regra e o detalhe) quando:

- falta alguma das quatro entradas da raiz (`LEIA-ME.txt`, `titulos.xlsx`, `indice.csv`, `indice.json`);
- uma entrada fora da raiz não casa `^(SOL|RDDV)-\d{6,}/[^/]+$`;
- um nome tem `..`, `\`, `:`, caractere inválido no Windows, nome reservado, fim com espaço ou ponto, mais de 120 caracteres ou repetição (sem diferenciar caixa) na pasta;
- um item do índice não tem entrada no ZIP, ou tem tamanho ou SHA-256 diferente (recalculado em streaming, um anexo por vez); ou uma entrada de anexo não tem item no índice;
- o `indice.csv` (UTF-8 com BOM, separador `;`) e o `indice.json` (UTF-8 sem BOM, camelCase, indentado) divergem entre si, em conteúdo ou ordem, ou os totais e os títulos do JSON não batem com os arquivos;
- as abas, os nomes ou os cabeçalhos de `titulos.xlsx` diferem do contrato; uma célula de coluna data, data-hora, valor ou inteiro não é `DateTime`/`Number`, ou uma de texto ou sim/não não é texto;
- um hiperlink (códigos nas abas Solicitações e RDDVs, caminho na aba Anexos) não aponta para uma pasta ou entrada existente, ou um título com anexos não tem hiperlink;
- as linhas de Solicitações + RDDVs (e as de Despesas e Anexos) divergem dos totais do JSON;
- o `LEIA-ME.txt` está vazio ou não descreve os arquivos nem orienta a extrair o ZIP.

Avisos (não reprovam): formato numérico ou de data diferente do contrato, cabeçalho sem negrito ou sem filtro automático.

`--relatorio` grava o resultado em JSON (regras, falhas, avisos, resumo). `--previa-html` grava uma página autocontida com as 10 primeiras linhas de cada aba e a árvore de pastas, para screenshot.

O cabeçalho "Parceiro (nome)" também aceita "Parceiro", porque o contrato não deixa claro se o parêntese faz parte do título.

Códigos de saída: `0` aprovado; `1` reprovado (inclusive ZIP ilegível); `2` uso incorreto ou arquivo inexistente.

## 3. Medição: `medir/medir.ts`

Faz login, cria a exportação pela API, acompanha o status, amostra a memória do contêiner da API, baixa o ZIP em streaming para o disco e grava um JSON de medição.

```
node tools/exportacao/medir/medir.ts --login fin.semente@exemplo.invalid --api http://localhost:5000 \
     --filtro '{"tipoTitulo":0}' --saida medicao.json --zip exportacao.zip
node tools/exportacao/medir/medir.ts --ajuda
```

| Opção | Padrão | Efeito |
|---|---|---|
| `--api <url>` | `http://localhost:5000` | Base da API. |
| `--login <e-mail>` | | Usuário FIN ou ADM. A senha vem de `PAGUEARES_SEMENTE_SENHA`. |
| `--filtro <json>` | `{}` | `FiltroExportacaoDto` em camelCase; aceita `@caminho` ou o caminho de um arquivo JSON (útil no PowerShell, onde as aspas atrapalham). |
| `--saida <json>` | `medicao.json` | Arquivo da medição. |
| `--zip <caminho>` | `exportacao.zip` | Destino do ZIP baixado. |
| `--tempo-limite-min <n>` | 120 | Desiste de esperar a conclusão depois de n minutos. |

Saída (`medicao.json`): `titulos`, `anexos`, `bytesAnexos`, `tamanhoZipBytes`, `segundosAteConcluir`, `segundosDownload`, `memoriaApiMiBAntes`, `memoriaApiMiBPico` e `amostras` (`[{ "t": segundos, "mib": n }]`). `titulos`, `anexos` e `bytesAnexos` vêm do `indice.json` dentro do ZIP baixado. A memória vem de `docker stats --no-stream --format "{{.MemUsage}}" pagueares-api`, convertida para MiB, a cada 5 s desde antes da criação do job até o fim do download; sem Docker ou com o contêiner parado, os campos de memória saem `null` e a ferramenta avisa.

Códigos de saída: `0` ok; `1` falha na exportação ou na rede; `2` uso incorreto.

## Como foram testadas

- O verificador roda contra um ZIP montado à mão: pacote correto sai `0`; um byte trocado num anexo, uma entrada `../x`, CSV diferente do JSON, célula de valor como texto, hiperlink quebrado, entrada da raiz ausente, totais errados e entrada solta na raiz saem `1`.
- A semente roda `--autoteste` (10.000 CPFs e 10.000 CNPJs sem nenhum dígito verificador válido, todos únicos).
- A semente recusa servidor não local sem conectar (código 2). Seus comandos SQL só foram exercitados contra um banco de verdade na verificação integrada (E14).
- A medição roda contra uma API simulada em memória (login, criação, status, link de download e download) e o leitor de `indice.json` lê o ZIP com mais de 65 mil entradas (Zip64).
