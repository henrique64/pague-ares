# Exportar títulos com anexos

- Tarefa: exportar-titulos-com-anexos
- Spec: e2e-docs/e2e/manual/video/exportar-titulos-com-anexos.video.spec.ts
- Página do manual: gerar-exportacao
- Telas: front-end/src/app/exportacoes/exportacoes.component.html, front-end/src/app/components/sidebar/sidebar.component.ts, back-end/Ares.PagueAres.Application/Exportacao/PlanilhaExportacao.cs

## p01 — Abertura

Este vídeo mostra como exportar títulos com anexos no PagueAres, do pedido até o pacote aberto no computador.

## p02 — O menu

> A tela inicial do PagueAres aparece com o usuário Financeiro; o cursor vai até o item Exportações do menu lateral e clica.

A exportação fica no menu lateral, no item **Exportações**. O item aparece só para os perfis Financeiro e Administrador.

## p03 — A tela

> A tela Exportações abre com o formulário no padrão; o cursor percorre o aviso fixo do topo.

No alto da tela, um aviso explica que o sistema prepara um arquivo ZIP em segundo plano. Quando o arquivo ficar pronto, o botão **Baixar** aparece na lista. O aviso lembra ainda de extrair o ZIP antes de abrir o Excel.

## p04 — O tipo de título

> O cursor aponta para o campo Tipo de título, abre a lista com as três opções e escolhe Solicitação; o campo Tipo de relatório (só RDDV) fica desabilitado.

O campo **Tipo de título** define o que entra no pacote: solicitações, RDDVs ou os dois. Neste exemplo, a escolha é **Solicitação**. Com ela, o campo **Tipo de relatório (só RDDV)** fica desabilitado, porque só vale para RDDV.

## p05 — O período

> O cursor aponta para o período da solicitação; as datas de início e de fim são digitadas nos dois campos.

O período limita a exportação. Em **Período da Solicitação (RDDV: cadastro)**, as datas de **Início** e de **Fim** são digitadas no formato dia, mês e ano. Um período mais curto reduz o número de títulos e o tamanho do pacote.

## p06 — Os anexos

> O cursor passa pelos campos de status e para na caixa Incluir anexos, que está marcada.

Os demais filtros são opcionais. A caixa **Incluir anexos** vem marcada. Com ela marcada, o pacote leva os arquivos anexados a cada título.

## p07 — Gerar a exportação

> O cursor clica em Gerar exportação; o aviso de sucesso aparece e uma linha nova surge no topo da lista Minhas exportações.

O botão **Gerar exportação** envia o pedido. Um aviso confirma a solicitação, e uma linha nova aparece na lista **Minhas exportações**.

## p08 — O andamento

> A linha aparece como Pendente e passa a Em processamento; o cursor aponta para a barra de andamento e para o texto com os títulos já tratados.

A situação começa em **Pendente** e passa para **Em processamento**. A barra mostra o percentual, e o texto ao lado diz quantos títulos já foram tratados. A lista se atualiza sozinha a cada cinco segundos, sem recarregar a página.

## p09 — A exportação concluída

> A situação muda para Concluída; o cursor aponta para as colunas Tamanho e Expira em e para os botões Baixar e Excluir.

Quando a situação muda para **Concluída**, a linha mostra o **Tamanho** do arquivo e a data de **Expira em**. Os botões **Baixar** e **Excluir** aparecem na coluna **Ações**. Pelo valor padrão do sistema, o arquivo fica disponível por 48 horas.

## p10 — Baixar

> O cursor clica em Baixar; o download do ZIP começa sem que a tela mude.

O botão **Baixar** gera um link de uso único, que vale por 120 segundos. O navegador baixa o ZIP sem sair da tela. O nome do arquivo leva o número da exportação e a data da conclusão.

## p11 — Extrair o pacote

> Cena do pacote já extraído numa pasta do computador: os quatro arquivos da raiz e as pastas de alguns títulos. O capturador escolhe como mostrar essa pasta dentro do navegador.

Antes de abrir o Excel, o ZIP precisa ser extraído por inteiro numa pasta do computador. Os links da planilha só funcionam depois da extração. Na raiz ficam a planilha, dois índices e um arquivo de instruções, o LEIA-ME. Cada título com anexos ganha uma pasta com o seu código.

## p12 — A planilha

> Prévia da planilha de títulos com as quatro abas; o cursor passa pelas abas e para na coluna de links da aba Anexos.

A planilha tem quatro abas: **Solicitações**, **RDDVs**, **Despesas dos RDDVs** e **Anexos**. Na aba **Solicitações**, a coluna **Código** é um link para a pasta do título. Na aba **Anexos**, cada arquivo tem o seu link e o seu código de conferência.

## p13 — Encerramento

> A tela Exportações volta a aparecer, com a lista e a linha Concluída.

Assim termina a exportação: pedida, acompanhada, baixada e aberta. Para apagar o arquivo antes do prazo, o botão **Excluir** apaga o arquivo do servidor. A página do manual traz os limites e as mensagens de cada recusa.
