# Verificação integrada ponta a ponta (E14)

Data: 25/11/2026. Branch `feature/exportacao-anexos`. Stack local do Docker (`docker compose up -d --build`), SQL Server local em `localhost,14330`, API em `http://localhost:5000`, front (Nginx) em `http://localhost:8080`. Todos os dados são fictícios, vindos da semente (`tools/exportacao/semente`). Nenhum segredo aparece neste arquivo: a string de conexão e a senha dos usuários fictícios foram montadas só em variáveis de ambiente da sessão. Os horários abaixo são os do relógio local da máquina; o relógio dos contêineres está em UTC (3 horas à frente).

## 1. Subida da stack

| Passo | Comando | Resultado |
|---|---|---|
| Build e subida | `docker compose up -d --build` | Três contêineres no ar: `pagueares-db` (saudável), `pagueares-api`, `pagueares-frontend` |
| Migration | log do `pagueares-api` | `Applying migration '20261125214019_AdicionaExportacaoJob'`: `CREATE TABLE [ExportacaoJob]` com a FK para `Usuario` (sem cascata) e os índices `IX_ExportacaoJob_IdUsuario_DataCriacao` e `IX_ExportacaoJob_Status`, registrada em `__EFMigrationsHistory` |
| Respostas | `GET http://localhost:8080/` e `GET http://localhost:5000/scalar/v1` | HTTP 200 nos dois |

## 2. Semente (500 títulos, cerca de 5 MB cada)

Comando: `dotnet run tools/exportacao/semente/semente.cs -- --titulos 500 --mb-por-titulo 5 --semente 42` (19:40:22 a 19:41:29, 63 s de inserção).

Resultado: 300 solicitações e 200 RDDVs exportáveis, mais 13 rascunhos e 12 cancelados (ficam de fora), 1038 despesas de RDDV, 1076 anexos, 2.622.750.237 bytes gravados em base64. A semente funcionou na primeira execução contra o SQL real, sem ajuste de código.

## 3. Medição pequena (cerca de 100 títulos)

Comando: `node tools/exportacao/medir/medir.ts --login fin.semente@exemplo.invalid --api http://localhost:5000 --filtro <json> --saida docs/evidencias/dados/medicao-100.json --zip <zip>`, com filtro `{"tipoTitulo":0,"dataSolicitacaoInicio":"2025-11-01","dataSolicitacaoFim":"2026-01-10"}`. Início 19:41:52.

Resultado (`medicao-100.json`): 113 títulos, 239 anexos, 592.445.339 bytes de anexos, ZIP de 625.009.408 bytes; concluída em 24,4 s; download em 4,6 s; memória da API de 84,4 MiB antes para 229,8 MiB no pico.

## 4. Medição cheia (todos os 500 títulos)

Comando: o mesmo, com filtro `{"tipoTitulo":0}`, saída `docs/evidencias/dados/medicao-500.json`. Início 19:42:30.

Resultado: 500 títulos, 1023 anexos, 2.621.439.561 bytes de anexos, ZIP de 2.765.438.530 bytes; concluída em 96,6 s (meta de 20 min cumprida com folga); download em 12,6 s; memória da API de 226,4 MiB antes (a API ainda carregava a medição anterior, sem reinício) para 338,4 MiB no pico, estável entre 300 e 338 MiB nos últimos 60 s.

Critérios de memória:

| Critério | Meta | Medido | Resultado |
|---|---|---|---|
| `memoriaApiMiBPico` (500) | ≤ 700 MiB | 338,4 MiB | atendido |
| `memoriaApiMiBPico(500) - memoriaApiMiBPico(100)` | ≤ 150 MiB | 108,6 MiB (338,4 - 229,8) | atendido |

Ressalva: o pico de 500 títulos fica 108,6 MiB acima do de 113. O volume de dados cresceu 4,4 vezes (de 625 MB para 2,77 GB) e a memória não acompanhou, o que é o comportamento esperado de streaming. A amostragem é a cada 5 s por `docker stats`, então picos mais curtos podem não aparecer.

## 5. Verificação do pacote de 500 títulos

Comando: `dotnet run tools/exportacao/verificar/verificar.cs -- <zip-500> --relatorio docs/evidencias/dados/verificacao-500.json --previa-html docs/evidencias/dados/previa-pacote.html`.

Resultado: `RESULTADO: APROVADO`, código de saída 0, as 16 regras ok (ZIP legível, quatro entradas da raiz, estrutura de pastas, nomes seguros e únicos, índice JSON, tamanho e SHA-256 de 1023 anexos recalculados em streaming, CSV igual ao JSON, abas, cabeçalhos, tipos das células, hiperlinks, totais e LEIA-ME). 1027 entradas (1023 anexos mais as 4 da raiz), 500 pastas de título, 300 linhas de Solicitações, 200 de RDDVs, 980 de despesas. Os nomes maliciosos da semente (`../x.pdf`, `C:\Windows\x.exe`, `CON.pdf`, duplicados em caixas diferentes) saíram sanitizados e deduplicados. Amostra em `amostra-pacote.txt`.

## 6. Recuperação depois de reinício

Script próprio (login como FIN, cria a exportação cheia, espera o status Processando com progresso, executa `docker restart pagueares-api`, consulta o job e a pasta do volume, cria nova exportação). Execução às 19:45:43.

| Hora | Evento |
|---|---|
| 19:45:43 | Exportação 3 criada (HTTP 202) |
| 19:45:45 | Status Processando, 10/500 títulos; `docker restart pagueares-api` |
| 19:45:54 | Job 3: status 3 (Falhou), mensagem "A exportação foi interrompida por um reinício do servidor. Gere a exportação de novo." |
| 19:45:54 | `docker exec pagueares-api ls -la /var/pagueares/exportacao`: só `exportacao-1.zip` e `exportacao-2.zip` (os pacotes concluídos); nenhum arquivo `.parcial` |
| 19:45:54 | Nova exportação 4 criada (HTTP 202) |
| 19:46:18 | Exportação 4 concluída (status 2): 113 títulos, 625.009.410 bytes |

Resultado esperado atendido: job Falhou com a mensagem de C9, nenhum `.parcial` no volume, nova exportação conclui. Os pacotes concluídos antes do reinício sobreviveram, pois a pasta é um volume nomeado.

## 7. Segurança na stack real

Todas as chamadas com os usuários fictícios da semente. Os de papel FIN, ADM, CON, GES e SOL foram usados; CON, GES e SOL não têm permissão de exportar.

| Chamada | Esperado | Obtido |
|---|---|---|
| `POST api/exportacao` sem token | 401 | 401 |
| `POST api/exportacao` como CON | 403 | 403 |
| `POST api/exportacao` como SOL | 403 | 403 |
| `GET api/exportacao` como CON | 403 | 403 |
| `GET api/exportacao/4` como CON | 403 | 403 |
| `GET api/exportacao/4` como ADM (job do FIN) | 404 | 404 |
| `POST api/exportacao/4/link-download` como ADM (job do FIN) | 404 | 404 |
| `POST api/Preview/generate-preview-key?requestType=2` (Exportacao) como CON, SOL e GES | 403 sem chave | 403, `success=false`, sem chave |
| A mesma chamada como FIN e como ADM | 200 com chave | 200, chave emitida |
| Link de download do FIN, 1º `GET api/exportacao/download?token=...` pelo front (`:8080`, passando pelo Nginx) | 200 `application/zip` | 200, `content-type: application/zip`, `content-disposition: attachment; filename=exportacao-titulos-4-20261125-2246.zip`, `content-length: 625009410`; 625 MB baixados em 3,1 s |
| 2º download com o mesmo token (pelo front) | 401 | 401 |
| 3º download com o mesmo token (direto na API) | 401 | 401 |
| Download sem token | 401 | 401 |

O download passou pela `location = /api/exportacao/download` do Nginx do compose (sem buffer): arquivo de 625 MB entregue com tamanho correto e `Content-Disposition` preservado.

Observação: o nome sugerido do download usa a hora local do servidor (`20261125-2246`), que no contêiner é UTC. No IIS do cliente será a hora do Windows.

## 8. Estado final

Stack no ar (`pagueares-db`, `pagueares-api`, `pagueares-frontend`), com os dados da semente (500 títulos exportáveis, mais rascunhos e cancelados) e os jobs 1 a 4 no banco. Usuários fictícios `@exemplo.invalid` (`adm.semente`, `fin.semente`, `con.semente`, `ges.semente`, `sol01` a `sol05.semente`).
