# Implantação da exportação de títulos com anexos no IIS

Este guia descreve como preparar o servidor Windows (IIS) do cliente para a exportação de títulos com anexos do PagueAres. A exportação gera, **em segundo plano**, um pacote ZIP (planilha `titulos.xlsx`, índices `indice.csv` e `indice.json` e uma pasta por título com os anexos), que o colaborador do Financeiro (papel FIN) ou o administrador (papel ADM) baixa manualmente depois.

O recurso tem três peças que moram **dentro do processo da API**: a fila de exportações, o serviço em segundo plano que gera os pacotes (`ExportacaoWorker`) e o serviço que apaga os pacotes vencidos (`ExportacaoLimpeza`). A fila e os tokens de download ficam em memória; os pacotes ficam em uma pasta em disco; o estado de cada exportação fica na tabela `ExportacaoJob` do banco. Por isso a configuração do IIS abaixo não é opcional: ela evita que o worker pare, que a fila se perca ou que o download seja cortado.

> Valores de exemplo neste guia (`PagueAres` como nome do site e do pool, `D:\PagueAres\Exportacao` como pasta) devem ser trocados pelos do ambiente. Nenhum segredo (senha, token, connection string) deve ser gravado neste guia, em `web.config` versionado ou na pasta de exportação.

## 1. Pré-requisitos

- Windows Server com IIS e o **ASP.NET Core Hosting Bundle** do .NET 10 instalado (traz o ASP.NET Core Module V2, ANCM).
- Recurso do Windows **Application Initialization** (`IIS-ApplicationInit`), usado para pré-carregar a aplicação. Instalação em PowerShell elevado:

  ```powershell
  Install-WindowsFeature Web-AppInit
  # em estação com Windows cliente: Enable-WindowsOptionalFeature -Online -FeatureName IIS-ApplicationInit
  ```

- Um **único servidor** para a API. A fila e os tokens de download vivem na memória do processo e os pacotes ficam em disco local: balanceador com várias instâncias da API não é suportado por esta versão.
- As migrations `AdicionaExportacaoJob` e `AdicionaRemocaoDaListaExportacao` (esta acrescenta a coluna `DataRemocaoDaLista` à tabela `ExportacaoJob`, usada por "Remover da lista") aplicadas no banco do ambiente (a API as aplica na subida quando `RunMigrations` está ligado; em produção, aplique o script gerado pela equipe de banco antes de liberar a versão).

## 2. Application pool

Crie (ou ajuste) o pool exclusivo da API, sem código gerenciado (`.NET CLR Version = No Managed Code`). Ajustes obrigatórios para o worker:

| Configuração | Valor | Por quê |
|---|---|---|
| Start Mode (`startMode`) | `AlwaysRunning` | O pool sobe junto com o IIS, sem esperar a primeira requisição; o worker só roda enquanto o processo existe. |
| Idle Time-out (`processModel.idleTimeout`) | `0` (desligado) | Sem isso o IIS encerra o processo ocioso e uma exportação em andamento é interrompida. |
| Reciclagem periódica (`recycling.periodicRestart.time`) | `0` (desligada) | Cada reciclagem derruba o processo e a exportação em curso. Se a política do cliente exigir reciclagem, use somente horário fixo sem uso (`periodicRestart.schedule`, por exemplo `03:00`). |
| `recycling.disallowOverlappingRotation` | `true` | Na reciclagem o IIS não deixa dois processos da API vivos ao mesmo tempo (dois workers disputariam a mesma fila e a mesma pasta). |
| **Maximum Worker Processes** (`processModel.maxProcesses`) | **`1`** | **Obrigatório.** Fila e tokens de download vivem em memória: um segundo processo (web garden) não enxerga a fila nem os tokens do primeiro, e o download falharia de forma intermitente. |
| Limites de memória privada e de memória virtual (`recycling.periodicRestart.privateMemory`/`memory`) | sem limite, ou folgados | A geração mantém na memória só metadados e um anexo por vez, mas um limite apertado causaria reciclagem no meio do job. |

Comandos equivalentes com `appcmd` (PowerShell elevado; troque `PagueAres` pelo nome real do pool):

```powershell
$appcmd = "$env:windir\System32\inetsrv\appcmd.exe"
& $appcmd set apppool "PagueAres" /startMode:AlwaysRunning
& $appcmd set apppool "PagueAres" /processModel.idleTimeout:00:00:00
& $appcmd set apppool "PagueAres" /recycling.periodicRestart.time:00:00:00
& $appcmd set apppool "PagueAres" /recycling.disallowOverlappingRotation:true
& $appcmd set apppool "PagueAres" /processModel.maxProcesses:1
```

### 2.1 Pré-carregamento do site (`preloadEnabled`)

Com `AlwaysRunning` o pool sobe, mas a aplicação ASP.NET Core só inicia na primeira requisição. Para o worker **retomar os jobs Pendente depois de uma reciclagem ou de um reinício do servidor** sem esperar alguém abrir o sistema, habilite o pré-carregamento do site (exige o módulo Application Initialization):

```powershell
& $appcmd set app "PagueAres/" /preloadEnabled:true
```

No `web.config` publicado, o módulo pode receber uma página de aquecimento (qualquer rota que responda, mesmo com 401 ou 404, já dispara a subida da aplicação):

```xml
<system.webServer>
  <applicationInitialization doAppInitAfterRestart="true">
    <add initializationPage="/" />
  </applicationInitialization>
</system.webServer>
```

Ao subir, o worker faz sozinho a recuperação: jobs que estavam `Processando` viram `Falhou` com a mensagem "A exportação foi interrompida por um reinício do servidor. Gere a exportação de novo." (e o pacote `.zip` que algum deles tenha chegado a publicar é apagado), os arquivos `*.parcial` da pasta são apagados e os jobs `Pendente` voltam para a fila por ordem de criação.

Com o processo no ar, uma **reconciliação periódica** (a cada `IntervaloReconciliacaoSegundos`, padrão 60 s) cobre o que a recuperação do início não alcança, como uma queda passageira do banco:

- a gravação do estado final de um job (Concluída, Falhou ou Cancelada) é repetida até 5 vezes, com espera crescente (1, 2, 4 e 8 s); se ainda assim falhar, o job fica para a reconciliação. Na conclusão, o pacote já publicado não é apagado nesse caso, porque a gravação pode ter chegado ao banco sem que a resposta voltasse: se o job ficou mesmo `Processando`, a reconciliação o encerra e apaga o pacote;
- um job `Processando` que não está em execução neste processo e que passa `LimiteSemSinalDeVidaSegundos` (padrão 300 s) sem nenhuma gravação do executor vira `Falhou` com a mesma mensagem de interrupção (ou `Cancelado`, se o usuário tinha pedido o cancelamento), e o pacote incompleto dele é apagado. Enquanto gera, o executor grava o progresso e um sinal de vida a cada décimo desse limite, então um job em execução nunca é tomado por abandonado;
- um job `Pendente` que ficou fora da fila (por exemplo, porque a reivindicação falhou com o banco fora do ar) volta para ela.

Assim, um job nunca fica preso em `Processando` até o próximo reinício, e o limite de exportações em andamento por usuário (`MaxJobsAtivosPorUsuario`) é liberado sozinho.

## 3. Pasta dos pacotes

- Crie a pasta **fora do diretório do site** (nunca dentro de `wwwroot` ou da pasta publicada), de preferência em um volume com espaço próprio. Exemplo: `D:\PagueAres\Exportacao`.
- Conceda permissão de **Modificar** (leitura, escrita e exclusão) à identidade do pool, `IIS AppPool\<nome do pool>`, e a nenhuma outra conta de usuário comum:

  ```powershell
  New-Item -ItemType Directory -Force -Path 'D:\PagueAres\Exportacao' | Out-Null
  icacls 'D:\PagueAres\Exportacao' /grant 'IIS AppPool\PagueAres:(OI)(CI)M'
  ```

- **Uma pasta por instância, obrigatória em produção.** Informe o caminho na configuração `Exportacao:Pasta` (variável de ambiente `Exportacao__Pasta`, seção 6). Cada instância da API no servidor (por exemplo, homologação e produção) precisa da **sua própria** pasta: os pacotes se chamam `exportacao-<id>.zip` e os ids se repetem entre bancos, então duas instâncias na mesma pasta sobrescreveriam e apagariam os pacotes uma da outra.
- Fora do ambiente `Development` (ou seja, em homologação, produção e qualquer outro ambiente), a API **recusa subir a exportação com `Pasta` vazio**: o worker e a limpeza não são iniciados e o log registra o erro "O serviço de exportação não foi iniciado: Exportacao:Pasta não está configurada no ambiente ...". O resto da API continua funcionando, mas um pedido de nova exportação (`POST api/exportacao`) responde **503** com a mensagem "A exportação está indisponível neste servidor por falta de configuração. Acione o suporte.". Configure a pasta e reinicie o pool para liberar o recurso. Só em `Development` o `Pasta` vazio é aceito, e aí a API usa `%TEMP%\PagueAres\Exportacao`.
- Dentro da pasta ficam somente `exportacao-<id>.zip` (pacotes concluídos) e `exportacao-<id>.zip.parcial` (pacote em geração). Não guarde outros arquivos ali: a limpeza apaga `exportacao-*.zip` sem job correspondente e mais velhos que a retenção.

## 4. Espaço em disco

Referência de volume: cerca de **25 títulos por dia útil e 5 MB de anexos por título**, ou seja, cerca de **2,5 GB de anexos por mês**. Anexos PDF e imagem quase não comprimem, então o ZIP tem praticamente o tamanho dos anexos.

Cálculo de uma estimativa conservadora:

```
disco ≥ (maior exportação prevista, em GB)
        × (exportações grandes que podem coexistir dentro da retenção + 1 em geração)
        + espaço livre mínimo (EspacoLivreMinimoMb, padrão 2 GB)
        + margem de 20%
```

Exemplo: uma exportação mensal completa de 2,5 GB, até 3 delas dentro das 48 horas de retenção padrão e mais 1 em geração: `2,5 × 4 = 10 GB`, mais 2 GB de espaço mínimo, mais 20%, dá cerca de **15 GB**. Se a retenção (`RetencaoHoras`) for aumentada, ou se o Financeiro exportar períodos longos com frequência, refaça a conta com o número de pacotes que ficarão retidos ao mesmo tempo.

Proteções da própria aplicação (todas ajustáveis na seção 6):

- `EspacoLivreMinimoMb` (padrão 2048): o worker confere o espaço livre da unidade da pasta antes de gerar e de novo a cada 2 segundos durante a geração; se ficar abaixo disso, a geração é interrompida, o `.parcial` é apagado e o job falha com a mensagem "O servidor está sem espaço em disco suficiente para gerar a exportação agora...". Se o disco encher entre duas conferências, o erro de disco cheio também resulta nessa mensagem. Em pasta de rede (caminho UNC) o espaço não pode ser medido: a geração segue sem a conferência e o log registra um aviso.
- `MaxBytesPorExportacao` (padrão 20 GiB): uma exportação cujos anexos somem mais que isso é recusada com pedido de filtros menores.
- `MaxTitulosPorExportacao` (padrão 5000) e `MaxJobsAtivosPorUsuario` (padrão 2) limitam o tamanho e a quantidade de pedidos simultâneos.

Monitore o espaço livre da unidade e dimensione o disco com folga: o pior caso é o disco encher durante a geração, o que faz o job falhar (o `.parcial` é apagado), sem afetar o banco.

## 5. Antivírus

Se a política de segurança permitir, **exclua a pasta de exportação da varredura em tempo real** (e o processo `w3wp.exe` do pool, se for esse o padrão do cliente). Cada pacote é escrito em fluxo, com milhares de arquivos de alguns MB; a varredura em tempo real de cada escrita pode multiplicar o tempo de geração e travar o arquivo no momento de renomear `.parcial` para `.zip`. Se a exclusão não for permitida, programe a varredura da pasta fora do horário de uso e considere aumentar `requestTimeout` e o tempo esperado de geração.

## 6. Configuração da aplicação

A seção `Exportacao` do `appsettings.json` traz os valores padrão. Em produção, sobrescreva apenas o que for diferente por **variável de ambiente** (no pool ou no `web.config`), usando `__` como separador de nível:

| Variável de ambiente | Padrão | O que faz |
|---|---|---|
| `Exportacao__Pasta` | vazio (`%TEMP%\PagueAres\Exportacao`, só em `Development`) | Pasta dos pacotes, **uma por instância, obrigatória em produção** (seção 3). Vazia fora de `Development`, a exportação não sobe e o pedido de nova exportação responde 503. |
| `Exportacao__RetencaoHoras` | `48` | Horas que um pacote concluído fica disponível para download antes de ser apagado. |
| `Exportacao__IntervaloLimpezaMinutos` | `30` | Intervalo da varredura que apaga pacotes vencidos. |
| `Exportacao__MaxTitulosPorExportacao` | `5000` | Máximo de títulos em uma exportação; acima disso o pedido é recusado (400). |
| `Exportacao__MaxJobsAtivosPorUsuario` | `2` | Máximo de exportações Pendente + Processando por usuário; acima disso, 429. |
| `Exportacao__MaxBytesPorExportacao` | `21474836480` (20 GiB) | Teto da soma dos anexos de uma exportação. |
| `Exportacao__EspacoLivreMinimoMb` | `2048` | Espaço livre mínimo, em MB, da unidade da pasta para iniciar e para continuar uma geração. |
| `Exportacao__ValidadeLinkDownloadSegundos` | `120` | Validade do link de download (token de uso único). |
| `Exportacao__WorkerHabilitado` | `true` | Com `false`, nem o worker nem a limpeza são registrados: os pedidos ficam Pendente. |
| `Exportacao__IntervaloReconciliacaoSegundos` | `60` | Intervalo da reconciliação que encerra jobs `Processando` sem executor e devolve à fila os `Pendente` que ficaram fora dela (seção 2.1). Aceita de 1 a 3600; um valor fora da faixa é trocado pelo extremo mais próximo (um valor que não é número, pelo padrão) e o log registra um aviso. |
| `Exportacao__LimiteSemSinalDeVidaSegundos` | `300` | Tempo sem nenhuma gravação do executor depois do qual um job `Processando` que não roda neste processo é dado como interrompido. Aceita de 10 a 86400 (1 dia), com o mesmo ajuste e aviso no log. |
| `Exportacao__FusoHorario` | `America/Sao_Paulo` | Fuso do nome do ZIP baixado e da hora do `LEIA-ME.txt` (e do `geradoEm` do `indice.json`). Ver a seção 6.1. |

O mesmo vale para o Docker: o `docker-compose.yml` já define `Exportacao__Pasta=/var/pagueares/exportacao` e o volume nomeado `pagueares-exportacao`.

### 6.1 Fuso horário do nome do ZIP e do LEIA-ME (`Exportacao:FusoHorario`)

Sem esta chave, a hora do pacote dependeria de onde a API roda (UTC no contêiner, a hora do Windows no IIS) e a mesma exportação teria nomes e horas diferentes conforme o servidor. Com ela, o nome do arquivo baixado (`exportacao-titulos-<id>-<aaaaMMdd-HHmm>.zip`) e a linha de geração do `LEIA-ME.txt` (por exemplo, "Exportação nº 42, gerada em 26/11/2026 12:30 (horário de Brasília, UTC-03:00).") usam o fuso configurado; o `geradoEm` do `indice.json` sai com o mesmo deslocamento (`-03:00`).

- **Valor padrão:** `America/Sao_Paulo` (horário de Brasília). Em geral não é preciso configurar nada.
- **Formatos aceitos:** o identificador IANA (`America/Sao_Paulo`) ou o do Windows (`E. South America Standard Time`); a API converte de um para o outro conforme o sistema. Para outro fuso, informe o identificador dele (por exemplo `America/Manaus`); o LEIA-ME passa a dizer "(fuso America/Manaus, UTC-04:00)" em vez de "horário de Brasília".
- **Windows (IIS):** o fuso padrão é resolvido pelo identificador do Windows `E. South America Standard Time` quando o IANA não estiver disponível; basta o Windows ter a atualização de fusos em dia. Não é preciso instalar nada.
- **Linux e Docker:** a imagem `mcr.microsoft.com/dotnet/aspnet:10.0` já traz a base de fusos (`/usr/share/zoneinfo`); em outra imagem enxuta (por exemplo Alpine), instale o pacote `tzdata`.
- **Fuso que não existe:** a API **não falha**. Se o valor configurado não for encontrado, ela tenta o padrão de Brasília (nos dois formatos) e registra um aviso no log; se nenhum existir no sistema, usa **UTC** e registra o aviso "... o nome do ZIP e o LEIA-ME usarão UTC". Procure esse aviso no log da subida se o horário do pacote parecer 3 horas adiantado.
- **Só isso muda de fuso:** as datas gravadas no banco, as da planilha e as da lista da tela ficam como antes (a tela mostra no fuso do navegador).

```xml
<environmentVariable name="Exportacao__FusoHorario" value="America/Sao_Paulo" />
```

## 7. Timeouts de download e compressão

O download do pacote é uma resposta `application/zip` em fluxo, que pode ter vários GB e durar muitos minutos em links lentos. Os limites padrão do IIS (2 minutos) cortariam o download no meio.

- **Hospedagem fora de processo (out-of-process):** aumente o `requestTimeout` do `aspNetCore` (padrão `00:02:00`) para um valor maior que o pior download esperado, por exemplo `01:00:00`.
- **Hospedagem em processo (in-process, o padrão do `dotnet publish` para IIS):** o ANCM não impõe `requestTimeout`; a regra do item seguinte continua valendo.
- **`connectionTimeout` do site** (tempo ocioso da conexão HTTP, padrão `00:02:00`): aumente para cobrir clientes lentos, por exemplo `01:00:00`:

  ```powershell
  & $appcmd set site "PagueAres" /limits.connectionTimeout:01:00:00
  ```

- **Compressão dinâmica:** não comprima `application/zip` (o conteúdo já está compactado e a compressão obrigaria o IIS a bufferizar a resposta). Por padrão o IIS não comprime esse tipo; confirme que ninguém acrescentou `*/*` ou `application/zip` aos `dynamicTypes` habilitados. Duas formas de garantir:
  - simples: desligar a compressão dinâmica do aplicativo no `web.config` (`<urlCompression doDynamicCompression="false" />`, como no exemplo abaixo); as respostas JSON da API deixam de ser compactadas, o que é aceitável em API interna;
  - ou manter a compressão e excluir o tipo (a seção `httpCompression` só pode ser alterada no `applicationHost.config`):

    ```powershell
    & $appcmd set config -section:system.webServer/httpCompression "/+dynamicTypes.[mimeType='application/zip',enabled='False']" /commit:apphost
    ```

- Se houver proxy reverso ou firewall de aplicação na frente do IIS, aplique neles os mesmos limites de tempo e desligue o armazenamento temporário de respostas (buffering) para o caminho `/api/exportacao/download`.

### 7.1 Exemplo de `web.config`

Trecho de exemplo para o **diretório publicado** (o repositório não traz `web.config`; o `dotnet publish` gera um e este trecho mostra o que ajustar nele). Use `hostingModel="inprocess"` ou `"outofprocess"` conforme o ambiente; `requestTimeout` só vale fora de processo.

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <location path="." inheritInChildApplications="false">
    <system.webServer>
      <handlers>
        <add name="aspNetCore" path="*" verb="*" modules="AspNetCoreModuleV2" resourceType="Unspecified" />
      </handlers>

      <aspNetCore processPath="dotnet"
                  arguments=".\Ares.PagueAres.API.dll"
                  hostingModel="outofprocess"
                  requestTimeout="01:00:00"
                  stdoutLogEnabled="false">
        <environmentVariables>
          <environmentVariable name="ASPNETCORE_ENVIRONMENT" value="Production" />
          <environmentVariable name="Exportacao__Pasta" value="D:\PagueAres\Exportacao" />
          <environmentVariable name="Exportacao__RetencaoHoras" value="48" />
          <environmentVariable name="Exportacao__EspacoLivreMinimoMb" value="2048" />
          <!-- A connection string e o segredo do JWT NÃO entram aqui: configure-os por
               variável de ambiente do pool ou por outro mecanismo de segredos do servidor. -->
        </environmentVariables>
      </aspNetCore>

      <applicationInitialization doAppInitAfterRestart="true">
        <add initializationPage="/" />
      </applicationInitialization>

      <urlCompression doStaticCompression="true" doDynamicCompression="false" />
    </system.webServer>
  </location>
</configuration>
```

## 8. Como conferir que funciona

1. **Pool e site.** Liste a configuração e confira os valores da seção 2:

   ```powershell
   & $appcmd list apppool "PagueAres" /text:startMode
   & $appcmd list apppool "PagueAres" /text:processModel.idleTimeout
   & $appcmd list apppool "PagueAres" /text:processModel.maxProcesses
   & $appcmd list apppool "PagueAres" /text:recycling.disallowOverlappingRotation
   & $appcmd list app "PagueAres/" /text:preloadEnabled
   ```

   Esperado: `AlwaysRunning`, `00:00:00`, `1`, `true` e `true`.
2. **Permissão da pasta.** `icacls 'D:\PagueAres\Exportacao'` deve listar `IIS AppPool\PagueAres` com `(OI)(CI)(M)`. Confira também que nenhuma outra instância da API no servidor usa a mesma pasta e que o log da subida não traz o erro "O serviço de exportação não foi iniciado".
3. **Exportação de ponta a ponta.** Entre no sistema com um usuário FIN ou ADM, abra "Exportações", gere uma exportação com um filtro pequeno e acompanhe o status até "Concluída". Durante a geração aparece `exportacao-<id>.zip.parcial` na pasta; ao terminar, vira `exportacao-<id>.zip`.
4. **Download.** Baixe o pacote pelo botão da tela, extraia o ZIP e confira: `LEIA-ME.txt`, `titulos.xlsx`, `indice.csv`, `indice.json` e as pastas `SOL-xxxxxx/` e `RDDV-xxxxxx/`. Se a ferramenta `tools/exportacao/verificar/verificar.cs` estiver disponível, rode `dotnet run tools/exportacao/verificar/verificar.cs -- <caminho do zip>`: o resultado esperado é aprovado (código de saída 0).
5. **Retomada após reciclagem.** Com a exportação de um filtro maior em andamento ou Pendente, recicle o pool (`& $appcmd recycle apppool "PagueAres"`): o job em geração deve passar a `Falhou` com a mensagem de reinício e um job Pendente deve ser retomado sozinho, sem abrir o sistema.
6. **Usuário sem permissão.** Um usuário SOL, GES ou CON não vê o item "Exportações" no menu e recebe 403 nos endpoints `api/exportacao`.
7. **Expiração.** Com `Exportacao__RetencaoHoras` reduzido temporariamente em um ambiente de teste (por exemplo `0.05`, três minutos), confirme que, passado o prazo e o intervalo de limpeza, o `.zip` some da pasta e o job aparece como "Expirada".
8. **Espaço em disco.** Confirme que o monitoramento do servidor alerta antes de a unidade chegar perto de `EspacoLivreMinimoMb`.

9. **Fuso do pacote.** Gere uma exportação, baixe o ZIP e confira que o nome do arquivo (`...-aaaaMMdd-HHmm.zip`) e a linha de geração do `LEIA-ME.txt` estão no horário de Brasília (`(horário de Brasília, UTC-03:00)`). Se aparecer UTC, procure o aviso sobre o fuso no log da subida (seção 6.1).

Em caso de falha, verifique nesta ordem: o log da aplicação (a exceção completa é registrada; o usuário só vê uma mensagem amigável), o Visualizador de Eventos do Windows (ANCM) e as permissões da pasta.

## 9. Front e API na mesma origem (mensagem de erro do download)

A tela Exportações baixa o pacote por um iframe oculto: o arquivo vai direto para o navegador, em streaming, sem passar pela memória do aplicativo. Quando o servidor recusa o download (link já usado, link vencido, exportação excluída, arquivo que sumiu), a resposta de erro cai nesse iframe e a tela **lê a mensagem do servidor** do conteúdo dele para mostrá-la ao usuário ("O arquivo desta exportação foi excluído. Gere a exportação de novo.", por exemplo).

O navegador só deixa a tela ler o conteúdo do iframe se o **front-end e a API estiverem na mesma origem** (mesmo protocolo, host e porta). Por isso:

- Configure o front para chamar a API pelo **mesmo host**: `BaseUrl` vazio (o padrão do front, chamadas em `/api/...`) com o IIS (ou o proxy reverso) publicando o front e encaminhando `/api/` para a API sob o mesmo endereço, como o Nginx do `docker-compose.yml` faz.
- Se a API ficar em **outro host ou porta** (origem diferente), o download continua funcionando, mas a leitura da mensagem é bloqueada pelo navegador e o usuário vê só o aviso genérico: "Não foi possível baixar o arquivo. O link expira rápido e só vale uma vez: gere o link de novo clicando em Baixar." Para mensagens específicas, publique front e API sob a mesma origem.
- A resposta da API não pode proibir o uso em iframe da própria origem: não configure `X-Frame-Options: DENY` nem `Content-Security-Policy: frame-ancestors 'none'` para o caminho `/api/exportacao/download` (o Nginx do repositório envia `X-Frame-Options: SAMEORIGIN`, que serve). Se houver proxy reverso ou firewall de aplicação na frente do IIS, aplique a mesma regra a eles.

## 10. Correções dos achados do manual: o que muda na implantação

Resumo das 22 decisões do dono sobre os achados do manual de exportações (detalhe e testes em `docs/evidencias/exportacao-tecnica.md`, seção 12). Só as três primeiras linhas pedem ação de quem implanta; as demais mudam o comportamento da tela e do pacote sem nova configuração.

| Decisões | O que muda | Ação na implantação |
|---|---|---|
| 3 | Nova situação "Excluída" (valor 6 em `ExportacaoJob.Status`) e nova coluna `DataRemocaoDaLista` | Aplicar a migration `AdicionaRemocaoDaListaExportacao` (seção 1). A coluna é aditiva e anulável. |
| 18 | Nome do ZIP e hora do LEIA-ME no fuso configurável, padrão Brasília | Conferir a chave `Exportacao__FusoHorario` e, no Linux, a base de fusos (seção 6.1). |
| 1 e 2 | Aviso de download com "gere" em minúsculo; a mensagem do servidor aparece quando o download falha | Front e API na mesma origem (seção 9); sem isso, aparece o aviso genérico. |
| 3 e 15 | "Excluir" numa Concluída vira "Excluída" (o "Expira em" original é preservado); "Remover da lista" nas linhas Falhou, Cancelada, Expirada e Excluída | Nenhuma. A limpeza (`ExportacaoLimpeza`) continua como antes e apaga o arquivo que sobrar de uma exportação excluída. |
| 4 | A lista mostra o número da exportação (o mesmo do nome do ZIP) | Nenhuma. |
| 5 e 14 | O filtro Código aceita 123, SOL-000123 e RDDV-45 e recusa zero e decimal | Nenhuma. |
| 6, 17 | Rótulos em caixa de frase e coluna "Filtros" com os rótulos do formulário | Nenhuma. |
| 7 e 8 | Botão "Exportar planilha" com dica; setas de ordenação de "Data de inclusão" no campo certo | Nenhuma. |
| 9, 10 e 21 | Pagamentos.xlsx com cabeçalhos novos, "Nº do documento" vazio no RDDV e os mesmos filtros da lista | Nenhuma. Quem usa a planilha percebe: período com um lado só e "atribuído" passam a filtrar; número do documento, nome e documento do parceiro excluem os RDDVs. |
| 11 | `indice.csv` e `indice.json` com o tipo "Solicitação" e "RDDV" | Nenhuma. Sistemas que leem o índice devem aceitar os nomes novos (antes "Solicitacao" e "Rddv"). |
| 12 | Mantido: "Valor líquido do RDDV" é o saldo líquido | Nenhuma. |
| 13 | Rótulos do formulário legíveis a 1360 px | Nenhuma. |
| 16 | A tela avisa, com o tipo "Ambos", quando um campo exclusivo de um tipo tira os títulos do outro | Nenhuma. |
| 19 | A semente de dados fictícios atribui títulos ao usuário `con.semente` | Só ambientes de teste. |
| 20 | O Administrador vê na aba Financeiro/Contábil o mesmo conjunto que o Financeiro | Nenhuma de configuração; avisar a quem administra os perfis, porque o Administrador passa a ver todos os títulos aprovados pelo gestor nessa aba. |
| 22 | Mantido: o Parceiro do RDDV é o solicitante | Nenhuma. |
