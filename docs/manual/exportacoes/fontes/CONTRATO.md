# Contrato de escrita das páginas do manual

> Caminhos relativos à pasta `fontes` do manual (a do `documentacao.json`). Como gerar e publicar: a skill
> `documentacao` do plugin de mesmo nome. Este arquivo foi instalado por `doc.ts instalar`; ajuste a lista de
> públicos e os exemplos ao projeto, mas mantenha os componentes: o shell e os gates dependem deles.

Cada página é UM arquivo `paginas/<id>.html` contendo exatamente um `<article>`. O esqueleto (`shell.html`) monta o
cabeçalho (seção, título, selos de público), o índice lateral, o "Nesta página", os botões anterior e próxima, a busca
e a ampliação de imagens. A página traz só o miolo.

```html
<article class="pagina" data-id="criar-registro" data-secao="Cadastros"
         data-titulo="Criar um registro" data-publico="operador">
  <p class="resumo">Uma ou duas frases: para que serve esta página e quem a usa.</p>
  <h2>…</h2>
  …
</article>
```

- `data-id` é igual ao nome do arquivo, sem `.html`. A página só entra no manual se o id estiver em `ordem.json`, que
  também é a ordem do índice.
- `data-secao` agrupa o índice; `data-titulo` é o título que o shell mostra. Os dois são obrigatórios.
- `data-publico`: um ou mais valores de `publicos` do `documentacao.json`, separados por espaço (o padrão é `todos`),
  por exemplo `data-publico="admin operador"`. O gate confere cada valor: atributo vazio ou valor fora da lista reprova,
  e a mensagem nomeia o valor.
- Sem `<h1>` (o esqueleto põe). Use `<h2>` para seções e `<h3>`/`<h4>` dentro delas.
- Sem `style=` inline, sem `<script>`, sem classes inventadas. Só os componentes abaixo.

## Componentes

**Texto de tela** (botão, campo, aba, título, situação, mensagem): SEMPRE assim, exato como no fonte, letra por
letra, inclusive caixa, acento e pontuação. O gate procura cada um no corpus de telas (`corpusDeTelas` da
configuração). Texto que mora fora do corpus (mensagem que a API manda, tela de outro módulo) entra em
`fontes/rotulos-de-fora.json`, com o caminho relativo (à pasta do `documentacao.json`) do arquivo que o emite; caminho
absoluto ou fora da raiz reprova.

```html
<span class="tela">Novo registro</span>
```

**Captura de tela** (o arquivo final é `img/<id>.webp`, e o `id` é o de `fontes/capturas.json`):

```html
<figure class="captura"><img src="img/cadastro-lista.webp" alt="Descrição objetiva da tela" loading="lazy">
  <figcaption>Legenda que diz o que olhar na foto.</figcaption></figure>
```

Duas lado a lado: envolva em `<div class="galeria">…</div>`. Toda foto citada precisa existir, ser de um grupo
declarado e ter legenda de pelo menos 30 caracteres em `capturas.json`; toda foto legendada precisa aparecer em alguma
página.

**Vídeo da tarefa** (só em tarefa com vídeo declarado em `video.tarefas`; o mp4 é levado ao pacote pelo `doc.ts
montar`; a capa é gerada pelo `doc.ts video`):

```html
<figure class="video"><video controls preload="none" src="video/criar-registro.mp4" poster="img/video-criar-registro-capa.webp"></video>
  <figcaption>Vídeo de 3 minutos: o registro criado do começo ao fim.</figcaption></figure>
```

Página sem vídeo gerado não promete vídeo. Montado com `--sem-videos`, cada `figure.video` vira a nota "Vídeo em
produção" (o rótulo do idioma).

**Passos de procedimento** (só quando a ordem importa): `<ol class="passos"><li>…</li></ol>`

**Campos de uma tela**: `<dl class="campos"><dt>Rótulo do campo</dt><dd>O que faz e regras.</dd></dl>`

**Tabelas**: `<div class="tabela"><table><thead>…</thead><tbody>…</tbody></table></div>`

**Avisos**: `<div class="nota">`, `<div class="dica">`, `<div class="atencao">`, `<div class="perigo">`, cada um
começando com `<p><strong>Título curto.</strong> texto…</p>`.

**Situação** (pílula curta no texto ou numa célula de tabela): `<span class="chip sucesso">em aberto</span>`.
As variantes recomendadas são `sucesso` (verde), `alerta` (âmbar), `perigo` (vermelho), `info` (azul-acinzentado)
e `neutro`; sem variante, a pílula é neutra. Os nomes antigos `ok`, `venc`, `baixa` e `pago` seguem aceitos por
compatibilidade, com as mesmas cores. Só `span.tela` é conferido contra o corpus, então o rótulo da pílula não é.

**Conversa** (use SÓ mensagens reais ou ensaiadas da fonte, com o texto exato; quebras de linha viram `<br>`; o que o
cliente digita vai em `msg cli`):

```html
<div class="conversa">
  <p class="conversa-titulo">Título da conversa</p>
  <div class="msg cli">texto do cliente</div>
  <div class="msg bot">texto do atendimento<br>com quebra de linha</div>
  <div class="msg bot anexo"><span class="anexo-nome" data-tipo="PDF">arquivo.pdf</span>texto que acompanha o anexo</div>
  <div class="marca-tempo">14 minutos depois</div>
</div>
<p class="conversa-fonte">De onde veio a conversa.</p>
```

O miolo de `.conversa` é fala citada: o gate de texto não varre segunda pessoa nele, mas continua conferindo os termos
proibidos. Todo `.msg` fica dentro de um `.conversa`, e todo `.conversa` tem ao menos um `.msg`. O quadradinho do anexo
mostra o `data-tipo` (sem o atributo, "PDF"). O `p.conversa-fonte` é prosa e segue as regras de escrita.

**Teclas e código**: `<kbd>Enter</kbd>`, `<code>REG-0014</code>`, blocos `<pre><code>…</code></pre>`.

**Links entre páginas**: `<a href="#criar-registro">Criar um registro</a>`; para uma seção,
`<a href="#criar-registro--os-campos">`. O id da seção é `<pagina>--<slug do h2>` (minúsculas, sem acento, espaços e
pontuação viram `-`, até 60 caracteres). Renomear um `<h2>` quebra os links para ele; o `doc.ts montar` acusa.

## Escrita

- Direto, voz ativa, frases curtas. Nomeie as coisas pelo que a pessoa vê na tela.
- Impessoal: o texto diz o que a tela faz, nunca quem a construiu, e fala de quem opera sem segunda pessoa (a lista
  `segundaPessoa` das regras do idioma): imperativo ("confira o código") ou terceira pessoa ("quem aprova", "a
  empresa"). O texto de tela citado fica como a tela o escreve, mesmo que use segunda pessoa.
- Nenhum nome de pessoa real, de empresa cliente ou de fornecedor real. Os exemplos são os do universo fictício das
  fotos. A lista `proibidos` do projeto é conferida pelo gate, sem diferenciar caixa e como palavra inteira.
- Valor interno (código de enumeração, chave de API, nome de campo do banco) nunca vai para a página: use o rótulo
  que a tela mostra.
- Nada inventado: toda afirmação vem do inventário das telas (`fontes/inventario.json`), do manifesto das capturas
  (`fontes/capturas.json`), das especificações do projeto ou do código. Se não souber, não escreva.
- Sem travessão como aparte, sem "não X, mas Y", sem "vale notar". Sem emoji.
- Numeração só em procedimento. Tabela quando houver comparação de verdade.

## Achados

Quem escreve o manual não corrige a tela. O que parecer defeito (texto cortado, rótulo incoerente, ação sem
explicação, valor interno visível) vai para `fontes/achados.md`:

```markdown
| tela | o que se viu | arquivo:linha | dono provável |
|---|---|---|---|
| Lista de registros | A coluna Situação fica atrás da rolagem em 1360 px | src/telas/Registros.tsx:41 | equipe do cadastro |
```

O manual descreve a tela como ela é hoje. Corrigida a tela (em outra tarefa), as fotos são refeitas e o texto é
revisto.
