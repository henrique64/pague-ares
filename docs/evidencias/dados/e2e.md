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

## 9. Reverificação pós-revisão

Data: 25/11/2026, entre 20:14 e 20:23 (relógio local; os contêineres estão em UTC, 3 horas à frente). Commit verificado: `c786890` (correções F1, F2 e F3 da revisão). Mesma stack e mesma semente da E14 (a semente não foi rodada de novo). Sem segredos: a senha dos usuários fictícios foi derivada do `.env` só na sessão. Resultado geral: **parcial**. Os passos 1, 3 (com filtro alternativo), 4 e 5 passaram; o passo 2 (medição de 113 títulos) **falhou por um dado da semente corrompido no banco local**, descrito abaixo.

### 9.1 Reconstrução da stack

`docker compose up -d --build` (20:14). Os três contêineres subiram; `pagueares-api` em `Development`, com `Exportacao__Pasta=/var/pagueares/exportacao`, e os serviços em segundo plano ativos (o log mostra o worker consultando `ExportacaoJob` Pendente e a limpeza consultando os Concluídos). `GET http://localhost:8080/` e `GET http://localhost:5000/scalar/v1` responderam 200.

### 9.2 Medição de 100 títulos: falhou por dado corrompido

Comando: o mesmo da E14 (`node tools/exportacao/medir/medir.ts --login fin.semente@exemplo.invalid --api http://localhost:5000 --filtro <json> --saida docs/evidencias/dados/medicao-100-pos-revisao.json --zip <zip>`, filtro `{"tipoTitulo":0,"dataSolicitacaoInicio":"2025-11-01","dataSolicitacaoFim":"2026-01-10"}`, 20:14:38).

Resultado: a exportação 5 terminou como Falhou com a mensagem "O anexo boleto março.pdf do título SOL-000172 está corrompido e não pôde ser exportado." O arquivo `medicao-100-pos-revisao.json` não foi gerado.

Causa investigada (consultas só de leitura no SQL local do Docker): o anexo 333 de SOL-000172 tem base64 inválido no banco, com dois caracteres diferentes do original na posição 2.645.845 e 2.645.849 (`a` virou `á` e `3` virou `³`, ou seja, o bit 0x80 ligado em dois bytes). O original é conhecido: o pacote de 500 títulos da E14 (`SOL-000172/Comprovantes - boleto março.pdf`, 2.592.330 bytes) foi gerado com esse anexo, então o dado estava íntegro na E14 e mudou depois, por causa desconhecida (nenhum código do repositório grava essa coluna; a semente não produz esse padrão). É o único anexo inválido entre os 629 de solicitações e os 447 de RDDVs. O código de exportação se comportou como projetado: recusou o anexo corrompido com a mensagem amigável e apagou o `.parcial`. O reparo (UPDATETEXT de dois caracteres no banco local) não foi feito por esta etapa por falta de permissão; fica como decisão do orquestrador.

Medição alternativa, só para exercitar a stack reconstruída: filtro `{"tipoTitulo":0,"dataSolicitacaoInicio":"2025-11-01","dataSolicitacaoFim":"2025-12-11"}` (exclui SOL-000172, de 12/12/2025), saída `medicao-parcial-pos-revisao.json`, início 20:19:37. 69 títulos, 147 anexos, 361.758.657 bytes de anexos, ZIP de 381.649.468 bytes, concluída em 14,1 s, download em 2 s, memória da API de 198 MiB antes para 203,5 MiB no pico. `verificar.cs` sobre esse ZIP: `RESULTADO: APROVADO`, código 0, as 16 regras ok. Esta medição não é a de 113 títulos pedida e não substitui `medicao-100-pos-revisao.json`.

### 9.3 Reinício com job em Processando

Script da E14 (`recuperacao.ts`), com a nova exportação limitada ao filtro sem SOL-000172 (mesma razão acima).

| Hora | Evento |
|---|---|
| 20:20:10 | Exportação 7 (cheia, 500 títulos) criada, HTTP 202 |
| 20:20:12 | Status Processando, 10/500 títulos; `docker restart pagueares-api` |
| 20:20:20 | Job 7: status 3 (Falhou), mensagem "A exportação foi interrompida por um reinício do servidor. Gere a exportação de novo." |
| 20:20:21 | `ls /var/pagueares/exportacao`: só `exportacao-1.zip`, `-2.zip`, `-4.zip` e `-6.zip` (pacotes concluídos); nenhum `.parcial` |
| 20:20:21 | Nova exportação 8 criada, HTTP 202 |
| 20:20:35 | Exportação 8 concluída (status 2): 69 títulos, 381.649.480 bytes |

Resultado esperado atendido. A exportação 5, que falhou no passo 2, também não deixou `.parcial`.

### 9.4 Segurança na stack real

Mesmas chamadas do passo 7 da E14 (job 6 do FIN no lugar do 4), às 20:21: `POST api/exportacao` sem token 401, como CON 403, como SOL 403; `GET api/exportacao` e `GET api/exportacao/6` como CON 403; `GET api/exportacao/6` e `POST api/exportacao/6/link-download` como ADM (job do FIN) 404; chave de preview de Exportação como CON, SOL e GES 403 sem chave, como FIN e ADM 200 com chave; download pelo front (Nginx) 200, `application/zip`, `Content-Disposition: attachment`, 381.649.468 bytes em 2,1 s; segundo download com o mesmo token pelo front 401, terceiro direto na API 401; download sem token 401.

Casos novos da F1 contra a API real (20:21:25):

| Chamada | Esperado | Obtido |
|---|---|---|
| `GET api/Preview/payment/173/332?key=<chave da SOL-000173>` (anexo 332 é da SOL-000172) | 404 | 404 |
| `GET api/Preview/rddv/1/5?key=<chave do RDDV-000001>` (anexo 5 é do RDDV-000002) | 404 | 404 |
| ZIP legado `api/Export/request/attachment/3` (anexo `../x.pdf`) | nenhuma entrada com `..` | 1 entrada, `.x.pdf`; 0 entradas com `..`, barra ou `:` |
| ZIP legado `api/Export/request/attachment/10` (anexo `../../etc/passwd`) | idem | `fatura janeiro.pdf`, `FATURA JANEIRO (2).PDF`, `.etcpasswd`; 0 com `..` |
| ZIP legado `api/Export/rddv/attachment/77` (anexo `../../etc/passwd`) | idem | `.etcpasswd`; 0 com `..` |
| ZIP legado `api/Export/rddv/attachment/1` (anexo `C:\Windows\x.exe`) | idem | `ordem de compra.jpg`, `CWindowsx.exe`, `contrato assinado.jpg`; 0 com `..`, barra ou `:` |

Observação sobre o controle positivo do preview: a chamada com o anexo do próprio título (`payment/173/335` e `rddv/1/3`) devolve 500, não 200. A consulta com `IdSolicitacao = @id` encontra o registro (aparece no log SQL), e o erro vem depois, em `File(...)`: a semente grava em `CaminhoArquivo` um valor que não é tipo de mídia (`semente/DocumentoSolicitacao/173/0`) e o ASP.NET recusa o cabeçalho. É um defeito dos dados da semente (E12), não da correção da F1; no sistema real essa coluna guarda o tipo de mídia enviado pelo navegador.

### 9.5 Download pelo front em navegador real

Playwright (`playwright-core`, instalado só numa pasta temporária fora do repositório) com o Edge do sistema (`channel: 'msedge'`, headless), às 20:22. Login como `fin.semente@exemplo.invalid` em `http://localhost:8080` (redirecionou para `#/payments`), abertura de `#/exportacoes`, filtro Tipo Solicitação e Código 173, clique em Gerar exportação.

- A exportação (job 9) concluiu e o clique em Baixar disparou o evento `download` do navegador: arquivo `exportacao-titulos-9-20261125-2322.zip`, 5.544.277 bytes, salvo. O verificador aprovou esse ZIP (código 0).
- A URL antes e depois do clique é a mesma (`http://localhost:8080/#/exportacoes`), o título "Exportar títulos com anexos" continua visível e a URL do quadro principal não mudou (o contador de navegações do quadro, só de hash do login e do roteador, ficou em 6 depois do download e continuou em 6 depois do segundo clique). Há um iframe oculto na página durante o download.
- Token inválido: no segundo clique em Baixar a resposta de `link-download` foi trocada pelo token já consumido do primeiro clique. Resultado: nenhum download, a URL e a tela permanecem, nenhum JSON no corpo da página, o iframe é removido, e aparece o aviso "Não foi possível baixar o arquivo. O link expira rápido e só vale uma vez: Gere o link de novo clicando em Baixar."

### 9.6 Estado final

Stack no ar (`pagueares-db`, `pagueares-api`, `pagueares-frontend`) com a semente intacta, exceto o anexo 333 de SOL-000172, que continua com os dois caracteres corrompidos. Jobs 1 a 9 no banco (5 e 7 em Falhou, os demais Concluídos).

### 9.7 Semente corrigida e recriada, preview e medição de 100 títulos

Data: 25/11/2026, a partir das 20:25 (relógio local). Resultado: **aprovado**.

- **Defeito da semente corrigido.** O sistema real grava em `CaminhoArquivo` o tipo de mídia do arquivo enviado (o front usa `file.type` no upload, em `create-request.component.ts` e `create-rddv.component.ts`) e o `PreviewController` repassa o campo a `File(...)`. A semente (`tools/exportacao/semente/semente.cs`) passou a gravar `application/pdf` ou `image/jpeg`, conforme o conteúdo gerado (e a extensão `.pdf` ou `.jpg` dos nomes comuns), em vez de `semente/<tabela>/<id>/<índice>`. Nenhum código de produção mudou.
- **Massa recriada com a própria ferramenta.** `semente --limpar` (removeu 629 anexos de solicitações, 447 de RDDVs, 1038 despesas, 315 solicitações, 210 RDDVs, 9 jobs de exportação e os 9 usuários fictícios) e `semente --titulos 500 --mb-por-titulo 5 --semente 42` (300 solicitações e 200 RDDVs exportáveis, 13 rascunhos e 12 cancelados, 1038 despesas, 1076 anexos, 2.622.750.237 bytes). Os anexos têm os mesmos conteúdo e nomes de antes, mas ids novos (a identidade não volta a 1), e o anexo corrompido da E14 deixou de existir.
- **Conferência do alfabeto base64** (consulta só de leitura no SQL local, depois do prefixo `data:...;base64,` quando existe): 0 anexos inválidos em 629 de solicitações e 0 em 447 de RDDVs. `CaminhoArquivo`: 740 `application/pdf` e 336 `image/jpeg`.
- **Preview real com a chave do próprio título** (login FIN, `POST api/Preview/generate-preview-key` e depois `GET api/Preview/{payment|rddv}/{id}/{anexo}?key=`): `payment/630/1258` 200 `application/pdf` (5.242.880 bytes); `payment/629/1255` 200 `image/jpeg` (833.481 bytes); `rddv/420/894` 200 `application/pdf` (5.242.880 bytes); `rddv/418/888` 200 `image/jpeg` (2.008.881 bytes). O 500 da seção 9.4 não ocorre mais.
- **Medição de 100 títulos**, mesmo comando e mesmo filtro da E14 (`{"tipoTitulo":0,"dataSolicitacaoInicio":"2025-11-01","dataSolicitacaoFim":"2026-01-10"}`, login FIN, 20:30:52), saída `medicao-100-pos-revisao.json`: exportação 10 concluída, 113 títulos, 239 anexos, 592.445.339 bytes de anexos, ZIP de 625.009.461 bytes, concluída em 20,1 s, download em 3,1 s, memória da API de 280 MiB antes (a API já carregava as atividades anteriores) para 284,7 MiB no pico. Mesmos números de títulos, anexos e bytes de anexos da E14.
- **Verificador** sobre esse ZIP: `RESULTADO: APROVADO`, código 0, as 16 regras ok.

O ZIP não foi versionado. A seção 9.6 descreve o estado antes desta recriação; o estado final agora é: stack no ar com a semente nova e íntegra, e o job 10 (Concluído) como único job de exportação.

## 10. Verificação final (código 93d3989)

Data: 25/11/2026, das 20:59 às 21:11 (relógio local), com o código integrado da branch `feature/exportacao-anexos` no commit `93d3989` (F4 do worker, F5 de datas pt-BR e F6 da semente com CPF/CNPJ de dígito verificador inválido). Resultado: **aprovado**. Nenhum código de produção foi alterado.

### 10.1 Reconstrução da stack

`docker compose up -d --build`: imagens da API e do front reconstruídas, `pagueares-api` e `pagueares-frontend` recriados, `pagueares-db` mantido (saudável). Depois da subida, `GET http://localhost:8080/` respondeu 200 e `GET http://localhost:5000/api/exportacao` respondeu 401 (sem login), e o log da API mostra a consulta de reconciliação de `ExportacaoJob` do worker em segundo plano (que roda dentro do processo da API).

### 10.2 Semente recriada

`semente --limpar` e depois `semente --titulos 500 --mb-por-titulo 5 --semente 42`: 300 solicitações e 200 RDDVs exportáveis, 13 rascunhos e 12 cancelados (ficam fora da exportação), 1.092 despesas de RDDV, 1.049 anexos, 2.622.750.255 bytes de anexos, 9 usuários fictícios. A limpeza removeu também 3 jobs de exportação antigos dos usuários fictícios.

Conferências em consultas só de leitura no SQL local do Docker:

- Alfabeto base64 (depois do prefixo `data:...;base64,` quando existe): 0 anexos inválidos em 630 de solicitações e 0 em 419 de RDDVs. `CaminhoArquivo`: 745 `application/pdf` e 304 `image/jpeg`.
- CPF/CNPJ (`Solicitacao.NumDocParceiro` e `Rddv.CPF` dos registros `[SEMENTE]`): 525 documentos. Numa amostra de 50 (1 a cada 10), 0 passam no dígito verificador; no conjunto todo, também 0 de 525. O validador usado foi conferido contra um CPF e um CNPJ válidos conhecidos (ambos aceitos).

### 10.3 Datas no navegador real

Playwright (`playwright-core`, fora do repositório) com o Edge do sistema, fuso `America/Sao_Paulo`, locale `pt-BR`, login como `fin.semente@exemplo.invalid`. Datas digitadas no formato natural; as requisições foram interceptadas (a criação de exportação foi abortada de propósito, sem gerar job).

| Tela e campo | Digitado | Valor exibido | Enviado à API |
|---|---|---|---|
| `#/exportacoes`, Período da Solicitação | `01/11/2025` a `31/12/2025` | `01/11/2025` e `31/12/2025` | `POST api/exportacao`: `dataSolicitacaoInicio=2025-11-01`, `dataSolicitacaoFim=2025-12-31` |
| `#/exportacoes`, Período de Vencimento | `05/12/2025` a `12/01/2026` | `05/12/2025` e `12/01/2026` | não enviado (conferido só na tela) |
| `#/payments`, Período da Solicitação | `01/11/2025` a `31/12/2025` | `01/11/2025` e `31/12/2025` | `GET api/Listing`: `requestStart=2025-11-01T03:00:00.000Z`, `requestEnd=2025-12-31T03:00:00.000Z` (meia-noite de 01/11 e de 31/12 em São Paulo, UTC-3) |
| `#/payments`, Período de Vencimento | `05/12/2025` a `31/12/2025` | `05/12/2025` | `dueStart=2025-12-05T03:00:00.000Z` (05/12/2025 em São Paulo) |
| `#/create-request`, Data do Vencimento | `05/12/2025`, depois perde o foco | `05/12/2025` | o calendário abre em dez. de 2025 com o dia 5 selecionado (não salvou) |

"Limpar Filtros" em `#/exportacoes`: com datas preenchidas, os 6 campos de data ficaram vazios; com o texto inválido `31/02/2025` num campo, ele também sumiu depois do botão. Observação, fora do escopo desta etapa: a tela `#/payments` envia a data final como meia-noite do dia escolhido (início do dia), comportamento que já existia; a tela Exportações envia `yyyy-MM-dd`.

### 10.4 Medições com o código final

Mesmo login FIN e mesmo filtro da E14 para 100 títulos (`{"tipoTitulo":0,"dataSolicitacaoInicio":"2025-11-01","dataSolicitacaoFim":"2026-01-10"}`); `medicao-100-pos-revisao.json` foi regravado e **é a medição do código final**.

| | títulos | anexos | bytes de anexos | ZIP (bytes) | concluir | download | memória da API (MiB) antes / pico |
|---|---|---|---|---|---|---|---|
| 100 (`medicao-100-pos-revisao.json`) | 98 | 194 | 513.802.155 | 542.037.256 | 20,3 s | 2,5 s | 108,1 / 251,8 |
| 500 (`medicao-500-codigo-final.json`) | 500 | 995 | 2.621.439.581 | 2.765.428.931 | 92,4 s | 11,7 s | 244,2 / 374,3 |

Pico de memória: 374,3 MiB (limite 700) e diferença entre as duas medições de 122,5 MiB (limite 150). Os totais de títulos e anexos diferem dos da E14 porque a semente foi recriada com a F6 (os CPFs/CNPJs inválidos mudaram o gerador pseudoaleatório). O verificador aprovou os dois ZIPs (código 0, as 16 regras ok); o relatório do de 500 está em `verificacao-500-codigo-final.json`. A medição de 500 foi refeita só para regenerar `previa-pacote.html` com a semente nova (os CPFs da prévia antiga tinham dígito verificador válido); as medições e a verificação da E14 (`medicao-500.json`, `verificacao-500.json`, `amostra-pacote.txt`) ficam como registro histórico.

### 10.5 Capturas regeneradas

`tools/exportacao/capturas/capturar.ts` passou a digitar `01/11/2025` e `31/12/2025` (removido o contorno `11/01/2025` e `12/31/2025`) e a usar o botão "Limpar Filtros" em vez de recarregar a tela, conferindo que nenhum campo de data ficou com texto. O script saiu com 0 e regravou as 9 capturas em `docs/evidencias/img/` (mesmos nomes); as de menu, formulário e acesso negado (01, 02 e 06) saíram idênticas às anteriores, as outras seis mudaram. Cada PNG foi aberto e conferido: só dados da semente (usuários e nomes fictícios, `@exemplo.invalid`, parceiros inventados), nenhum token, senha ou URL com token. A captura 02 mostra o período `01/11/2025 – 31/12/2025`.

### 10.6 Estado final

Stack no ar (`pagueares-db`, `pagueares-api`, `pagueares-frontend`) com a semente nova e íntegra e 3 jobs de exportação do usuário FIN fictício, todos Concluídos (as duas exportações de 500 títulos e a de 98).
