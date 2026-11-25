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
- A migration `AdicionaExportacaoJob` aplicada no banco do ambiente (a API a aplica na subida quando `RunMigrations` está ligado; em produção, aplique o script gerado pela equipe de banco antes de liberar a versão).

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

Ao subir, o worker faz sozinho a recuperação: jobs que estavam `Processando` viram `Falhou` com a mensagem "A exportação foi interrompida por um reinício do servidor. Gere a exportação de novo.", os arquivos `*.parcial` da pasta são apagados e os jobs `Pendente` voltam para a fila por ordem de criação.

## 3. Pasta dos pacotes

- Crie a pasta **fora do diretório do site** (nunca dentro de `wwwroot` ou da pasta publicada), de preferência em um volume com espaço próprio. Exemplo: `D:\PagueAres\Exportacao`.
- Conceda permissão de **Modificar** (leitura, escrita e exclusão) à identidade do pool, `IIS AppPool\<nome do pool>`, e a nenhuma outra conta de usuário comum:

  ```powershell
  New-Item -ItemType Directory -Force -Path 'D:\PagueAres\Exportacao' | Out-Null
  icacls 'D:\PagueAres\Exportacao' /grant 'IIS AppPool\PagueAres:(OI)(CI)M'
  ```

- Informe o caminho na configuração `Exportacao:Pasta` (variável de ambiente `Exportacao__Pasta`, seção 6). Com `Pasta` vazio a API usa `%TEMP%\PagueAres\Exportacao` do pool, o que **não é recomendado em produção** (a pasta temporária do pool costuma ficar no disco do sistema).
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

- `EspacoLivreMinimoMb` (padrão 2048): antes de gerar, o worker confere o espaço livre da unidade da pasta; se estiver abaixo disso, o job falha com mensagem amigável.
- `MaxBytesPorExportacao` (padrão 20 GiB): uma exportação cujos anexos somem mais que isso é recusada com pedido de filtros menores.
- `MaxTitulosPorExportacao` (padrão 5000) e `MaxJobsAtivosPorUsuario` (padrão 2) limitam o tamanho e a quantidade de pedidos simultâneos.

Monitore o espaço livre da unidade e dimensione o disco com folga: o pior caso é o disco encher durante a geração, o que faz o job falhar (o `.parcial` é apagado), sem afetar o banco.

## 5. Antivírus

Se a política de segurança permitir, **exclua a pasta de exportação da varredura em tempo real** (e o processo `w3wp.exe` do pool, se for esse o padrão do cliente). Cada pacote é escrito em fluxo, com milhares de arquivos de alguns MB; a varredura em tempo real de cada escrita pode multiplicar o tempo de geração e travar o arquivo no momento de renomear `.parcial` para `.zip`. Se a exclusão não for permitida, programe a varredura da pasta fora do horário de uso e considere aumentar `requestTimeout` e o tempo esperado de geração.

## 6. Configuração da aplicação

A seção `Exportacao` do `appsettings.json` traz os valores padrão. Em produção, sobrescreva apenas o que for diferente por **variável de ambiente** (no pool ou no `web.config`), usando `__` como separador de nível:

| Variável de ambiente | Padrão | O que faz |
|---|---|---|
| `Exportacao__Pasta` | vazio (`%TEMP%\PagueAres\Exportacao`) | Pasta dos pacotes. **Defina sempre em produção** (seção 3). |
| `Exportacao__RetencaoHoras` | `48` | Horas que um pacote concluído fica disponível para download antes de ser apagado. |
| `Exportacao__IntervaloLimpezaMinutos` | `30` | Intervalo da varredura que apaga pacotes vencidos. |
| `Exportacao__MaxTitulosPorExportacao` | `5000` | Máximo de títulos em uma exportação; acima disso o pedido é recusado (400). |
| `Exportacao__MaxJobsAtivosPorUsuario` | `2` | Máximo de exportações Pendente + Processando por usuário; acima disso, 429. |
| `Exportacao__MaxBytesPorExportacao` | `21474836480` (20 GiB) | Teto da soma dos anexos de uma exportação. |
| `Exportacao__EspacoLivreMinimoMb` | `2048` | Espaço livre mínimo, em MB, da unidade da pasta para iniciar uma geração. |
| `Exportacao__ValidadeLinkDownloadSegundos` | `120` | Validade do link de download (token de uso único). |
| `Exportacao__WorkerHabilitado` | `true` | Com `false`, nem o worker nem a limpeza são registrados: os pedidos ficam Pendente. |

O mesmo vale para o Docker: o `docker-compose.yml` já define `Exportacao__Pasta=/var/pagueares/exportacao` e o volume nomeado `pagueares-exportacao`.

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
2. **Permissão da pasta.** `icacls 'D:\PagueAres\Exportacao'` deve listar `IIS AppPool\PagueAres` com `(OI)(CI)(M)`.
3. **Exportação de ponta a ponta.** Entre no sistema com um usuário FIN ou ADM, abra "Exportações", gere uma exportação com um filtro pequeno e acompanhe o status até "Concluída". Durante a geração aparece `exportacao-<id>.zip.parcial` na pasta; ao terminar, vira `exportacao-<id>.zip`.
4. **Download.** Baixe o pacote pelo botão da tela, extraia o ZIP e confira: `LEIA-ME.txt`, `titulos.xlsx`, `indice.csv`, `indice.json` e as pastas `SOL-xxxxxx/` e `RDDV-xxxxxx/`. Se a ferramenta `tools/exportacao/verificar/verificar.cs` estiver disponível, rode `dotnet run tools/exportacao/verificar/verificar.cs -- <caminho do zip>`: o resultado esperado é aprovado (código de saída 0).
5. **Retomada após reciclagem.** Com a exportação de um filtro maior em andamento ou Pendente, recicle o pool (`& $appcmd recycle apppool "PagueAres"`): o job em geração deve passar a `Falhou` com a mensagem de reinício e um job Pendente deve ser retomado sozinho, sem abrir o sistema.
6. **Usuário sem permissão.** Um usuário SOL, GES ou CON não vê o item "Exportações" no menu e recebe 403 nos endpoints `api/exportacao`.
7. **Expiração.** Com `Exportacao__RetencaoHoras` reduzido temporariamente em um ambiente de teste (por exemplo `0.05`, três minutos), confirme que, passado o prazo e o intervalo de limpeza, o `.zip` some da pasta e o job aparece como "Expirada".
8. **Espaço em disco.** Confirme que o monitoramento do servidor alerta antes de a unidade chegar perto de `EspacoLivreMinimoMb`.

Em caso de falha, verifique nesta ordem: o log da aplicação (a exceção completa é registrada; o usuário só vê uma mensagem amigável), o Visualizador de Eventos do Windows (ANCM) e as permissões da pasta.
