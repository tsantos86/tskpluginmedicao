# TSK TakeOff — manual

Medir alvenarias e materiais em cima do desenho, e ter a folha de medição
feita ao mesmo tempo.

---

## O essencial em cinco minutos

1. Abra o AutoCAD. O separador **TSK TakeOff** aparece sozinho na ribbon.
2. Carregue em **Painel de Medições**. É daqui que se controla tudo.
3. No painel, escolha o **piso** e a **altura** (ex.: `PISO 0`, `2.80`).
4. Carregue em **Parede Retângulo** e dê dois cliques em planta, nos cantos da
   parede. O comprimento e a espessura saem do que desenhou; a altura vem do
   painel.
5. Carregue em **Excel ao Vivo**. A folha abre e vai-se preenchendo à medida
   que mede.

A partir daqui é repetir o passo 4. Cada medição fica desenhada, com a área
escrita por cima, e aparece logo na folha.

---

## O painel

O painel tem quatro separadores, organizados por tipo de trabalho: **Arquitetura**, **Materiais**, **Lineares** e **Contagens**.

### Arquitetura

Para paredes. Preencha antes de medir:

| Campo | Para que serve |
|---|---|
| **Serviço** | O que está a medir (ex.: `ALVENARIA TIJOLO 15`). Agrupa as medições na folha. |
| **Piso** | `PISO 0`, `PISO 1`… Cada piso ganha uma cor no desenho. |
| **Alçado** | Opcional. Zona ou fachada (ex.: `Alçado Tardoz`). |
| **Bloco / etiqueta** | Opcional. Identificação da zona, compartimento, torre ou fracção. A informação aparece na designação da medição. |
| **Altura** | Pé-direito da parede, em metros. |
| **Espessura** | Espessura da parede. Pondo `0`, usa a medida do rectângulo desenhado. |
| **Regra de vãos** | Como descontar portas e janelas — ver abaixo. |

A secção **CONFIGURAÇÃO** abre recolhida e, fechada, mostra na própria barra o
resumo do que vai sair na próxima medição — `PISO 0 · ALVENARIA · 11.2.1 ·
h 2,80 m · e 0,15 m`. O mesmo resumo está no cabeçalho do painel, ao lado do
nome do DWG activo. São dois sítios de propósito: é a única coisa do painel que
muda o que acontece a seguir sem ninguém estar a olhar para ela, e medir dez
paredes para o artigo errado descobre-se tarde de mais.

O **Texto do título** e a **Layer** manual estão em **Mais opções**, dentro da
configuração. Não são campos do dia a dia: a layer é calculada a partir do
artigo e do bloco em quase todos os casos.

### Ler os resultados

Os resultados aparecem numa **árvore**, organizada por
`Pavimentos > Piso > Serviço > Artigo > Medição > Vão/Título`.

A árvore tem **duas colunas**: o nome do elemento e a **quantidade**, na unidade
do artigo. Nos níveis que juntam unidades diferentes, elas aparecem lado a lado
— `62,93 m² · 36,90 m · 2 un.` — e **nunca somadas**: são grandezas diferentes.

O comprimento, a altura, a espessura e as contas derivadas vivem no painel
**PROPRIEDADES**, por baixo. O botão `Essenciais` / `Tudo` alterna entre o
resumo e a lista completa. **É aí que se edita**: os campos com fundo amarelo
escrevem no desenho quando sai da célula. Com várias medições escolhidas na
árvore (`Ctrl` para soltas, `Shift` para um intervalo), o que se escreve numa
vale para todas.

Os vãos aparecem por baixo da parede a que pertencem, com a dedução em negativo
— e não são somados outra vez: a quantidade da parede já é líquida.

- **Pesquisa** (`Ctrl+F`) — procura em serviço, artigo, piso, bloco, alçado,
  designação, handle e nome dos vãos. Ignora acentos e maiúsculas. Vários
  termos são um **E**: `piso 1 tijolo` procura o que tenha as duas coisas.
- **Filtros** — Pavimento, Serviço, Artigo, Tipo/unidade e Estado. Escolhe-se
  tudo e carrega-se em `Aplicar`: a árvore muda uma vez, no fim. O `Repor`
  devolve as escolhas ao filtro em vigor; o `Limpar` da barra apaga pesquisa e
  filtros de uma vez.
- Filtrar **não altera nada** no desenho, no Excel nem na próxima medição — é
  só uma lente. O contador diz sempre `n visíveis de N`.

### Medir aqui

Selecionar uma linha **serve para consultar e editar**, e mais nada. Para mandar
as próximas medições para um artigo, escolha o nó desse **artigo** e carregue em
**Medir aqui**: copia o piso, o serviço e o artigo para a configuração,
mantendo as dimensões. O nó fixado passa a mostrar a etiqueta `PRÓXIMA`.

É a única operação que altera a próxima medição.

### Materiais

Para fachadas, ETICS, revestimentos. Mesma lógica, mas o campo principal é o
**material** em vez do serviço.

---

## Como medir

| Botão | Quando usar | Como |
|---|---|---|
| **Parede Retângulo** | Parede vista em planta | Dois cliques, nos cantos opostos |
| **Parede Polyline** | Parede com desenvolvimento irregular | Clique o eixo, `Enter` para fechar |
| **Área** | Camadas medidas por área: betonilhas, enchimentos, impermeabilizações | Clique o contorno em planta, `Enter` fecha. A medição é **área × Altura** e sai em **m³** — a *Altura* do painel é aqui a espessura da camada |
| **Área da Selecção** | O mesmo, quando a área **já está desenhada** no projeto | Selecione hachuras ou polylines fechadas. Mede sobre **cópias** na layer das medições — o desenho do arquiteto fica intacto |
| **Retângulo** (Materiais) | Pano de fachada visto em alçado | Dois cliques — comprimento × altura saem da geometria |
| **Polyline × Altura** | Fachada em planta, a multiplicar pela altura do piso | Clique o contorno, `Enter` |
| **Medição Linear** | Rodapés, tubagens, remates | Clique o percurso, `Enter` |

### Contagens

Na aba **Contagens**, informe o nome do elemento e, opcionalmente, a categoria
e o piso. Use **Contar Blocos** para selecionar blocos existentes no desenho.
Cada bloco é marcado e a quantidade é agrupada na folha em unidades (`un`).

### Organização da folha

| Botão | Quando usar | Função |
|---|---|---|
| **Artigo** | Inserir ou editar um título associado à medição | Mantém a estrutura da folha organizada por artigo. |
| **Linha branca** | Separar medições | Insere uma ou mais linhas em branco no ponto escolhido. |
| **Reclassificar** | Passar medições já feitas para outro artigo | Escolha o artigo no painel, selecione as linhas (`Ctrl`/`Shift`) e carregue. |

Quando existe um mapa de quantidades, os capítulos e artigos são obtidos a
partir dele. A folha é organizada pela ordem do articulado e só inclui artigos
que possuem medições. Uma linha em branco é criada quando muda o artigo; outros
separadores podem ser acrescentados manualmente com **Linha branca**.

### Onde ficam as somas

| Coluna | Onde aparece |
|---|---|
| **Totais** | sempre na linha do **serviço** (`PAR.01`, `RV.06`) |
| **Sub total** | na linha de cada **piso** ou **alçado**, quando os há; **na linha do serviço** quando o serviço não tem divisão nenhuma |

O Sub total nunca fica vazio de alto a baixo: um serviço medido sem piso —
um `RV.06` de uma ponta à outra — soma-se a si próprio. E nunca aparece nos
dois sítios ao mesmo tempo, para não haver dúvida sobre qual dos dois manda.

Cada medição fica guardada **dentro do ficheiro DWG**. Se fechar o AutoCAD e
voltar amanhã, está lá tudo. Se enviar o DWG a um colega com o plugin, ele vê
as suas medições.

---

## Vãos (portas e janelas)

O plugin procura sozinho os vãos dentro da parede que acabou de medir: lê
textos, atributos e blocos, e percebe se as medidas estão em milímetros,
centímetros ou metros.

Aparece uma janela com o que encontrou. Confirme, corrija, ou apague o que não
interessa. Se não encontrar nada, use o botão **Adicionar Vão** (também disponível como
**Vãos à mão** na ribbon): escolha a medição e depois selecione no desenho os
vãos.

Nessa selecção entram duas coisas diferentes:

| O que selecionar | O que o plugin lê |
|---|---|
| **Textos, MText e blocos** | As cotas escritas — `VE.02 1190x2350`, `PC.04 200X160`, `2,67x2,62 m`. A unidade é inferida por plausibilidade |
| **Rectângulos, polylines, hachuras e linhas** | As medidas da **própria geometria**, para desenhos onde as aberturas estão desenhadas mas não cotadas |

Na geometria, o que é lido depende do tipo de medição a que o vão pertence:

- **Materiais / fachada (alçado)** — o rectângulo tem as duas cotas: lê largura
  e altura do desenho.
- **Arquitetura (planta)** — em planta o rectângulo é largura × espessura da
  parede, e a altura não está lá. Lê-se a largura e a altura entra a `2.10 m`,
  para afinar na tabela.

O comando `TSKVAODIAG` mostra o que o detector leria numa selecção — nas duas
leituras, planta e alçado — sem tocar em medição nenhuma. Serve para conferir
antes de aplicar.

### Regra de vãos

| Regra | O que faz |
|---|---|
| **Descontar todos os vãos** | Área da parede menos a área de todos os vãos |
| **SINAPI (2 m²)** | Vãos até 2 m² não descontam; acima disso desconta só o excedente |
| **Não descontar** | Área bruta, sem descontos |

Escolha antes de exportar — a regra aplica-se a toda a folha.

---

## Excel

### Excel ao Vivo

Abre o Excel e mantém a folha sincronizada enquanto mede. É a maneira mais
rápida de ver se as contas estão a bater certo.

### Modelo da casa

Por omissão o plugin usa uma folha simples. Para usar o modelo da sua empresa:

1. Comando `TSKMODELO`.
2. Escolha o ficheiro Excel que usa nas medições.

O plugin reconhece dois formatos automaticamente:

- folhas com cabeçalho `art` / `descrição` (as que têm as macros Ctrl+M, Ctrl+E,
  Ctrl+Q);
- folhas com cabeçalho `Item / Designação / Un / Qt / Comp / Largura / Altura`.

Encontra sozinho em que linha e em que colunas está o cabeçalho, e preserva o
bloco de identificação da obra que esteja por cima.

**O ficheiro original nunca é alterado.** O plugin trabalha sempre numa cópia.

### Exportar

Grava a folha ao lado do DWG, no formato do modelo. Se não houver modelo
registado, sai um `.xlsx` simples.

---

## Mapa de quantidades do cliente

Quem mede não escolhe os artigos: recebe-os. O comando `TSKMQT` importa o
articulado que o cliente enviou — capítulos, subcapítulos e artigos — e a
partir daí escolhe-se o artigo de uma lista em vez de o escrever.

**Entra o articulado, não entram números.** Quantidades, dimensões, fórmulas e
totais ficam de fora. Os números da folha nascem sempre da medição.

1. Comando `TSKMQT` e escolha o ficheiro do cliente.
2. Aparece a lista das folhas do livro com o que foi encontrado em cada uma.
   Vêm marcadas as que têm artigos; as de resumo e as vazias ficam de fora.
3. Confira a pré-visualização da árvore e carregue em **Importar**.

O plugin reconhece os dois feitios de mapa que circulam:

| Feitio | Como se reconhece |
|---|---|
| `Item / Designação / Un / Qt / Comp…` | cabeçalho procurado na folha; hierarquia pelos pontos do código; artigo = tem unidade |
| `art / auxiliar / … / descrição / =` | o das macros MD/EO/QT; traz o nível numa coluna e uma coluna que marca os artigos |

### Medir para um artigo

No painel, escolha directamente na lista **Artigo do mapa**. A lista inclui
apenas artigos e pode ser percorrida com o scroll ou pelo início do código
digitado no teclado. As medições que fizer a partir daí vão todas para esse artigo, até trocar.

O campo **Serviço** continua a existir e a fazer o que sempre fez: manda na
layer e na cor no desenho. Uma coisa é onde a medição aparece na folha do
cliente, outra é como se vê na planta.

Na folha, os artigos saem pela ordem do articulado, não pela ordem por que
mediu. Só aparecem os artigos que tiverem medições.

### Mudar o artigo do que já está medido

O caso normal de uma obra: mediu-se antes de o mapa do cliente chegar, ou o
mapa mudou de versão a meio. Nesse caso as medições antigas ficam **sem
artigo** — a árvore junta-as num grupo **Por classificar**, marca-as com `⚑` e
o rodapé diz quantas são. Na folha saem **no fim**, depois de tudo o que já
está classificado; é por isso que uma medição nova, essa com artigo, parece
saltar-lhes à frente. Não é a folha que se baralhou: é o que já tem lugar no
articulado a ocupá-lo.

Para as arrumar:

1. Selecione as medições na árvore — `Ctrl` para escolher soltas, `Shift` para
   um intervalo.
2. Abra **Mais ▸ Reclassificar…** e escolha o artigo de destino.

A geometria não se toca: muda só o artigo a que as medições pertencem, e com
ele o sítio onde saem na folha.

Para uma medição só, também pode escrever o código no campo **Artigo** do painel
PROPRIEDADES — o plugin vai buscar a designação ao mapa.

### Limpar em bloco (Piso e Artigo)

Selecione as medições na árvore com `Ctrl` ou `Shift` e **deixe o campo vazio**
em PROPRIEDADES: limpa a selecção toda de uma vez. Funciona no **Piso** e no
**Artigo**.

O mesmo vale para escrever por cima: com várias medições seleccionadas, o que
escrever numa vale para todas.

Isto existe porque a árvore **reagrupa-se a cada alteração** (agrupa por piso,
serviço e artigo): editar uma a uma faz a seguinte mudar de sítio debaixo do
rato, e acaba-se a alterar duas vezes umas e nenhuma vez outras.

### Teclado

| Tecla | Faz |
|---|---|
| `Ctrl+F` | Vai para a pesquisa, esteja o foco onde estiver |
| `↑` `↓` | Percorre os nós visíveis |
| `←` | Recolhe o nó; já recolhido, sobe ao nível de cima |
| `→` | Expande o nó; já expandido, desce ao primeiro filho |
| `Home` / `End` | Primeiro / último nó da lista |
| `Enter` / `Espaço` | Abre ou fecha o grupo; num nó de artigo, faz `Medir aqui` |
| `Ctrl` / `Shift` | Selecção múltipla, para as operações em lote |
| `Esc` | Fecha os filtros e devolve o foco ao botão que os abriu |

### Quando alguma coisa não bate certo

- **Unidade diferente** — medir uma parede (m²) para um artigo de `un` dá um
  aviso na linha de comandos, uma vez por artigo. Não trava: há mapas que vêm
  com a unidade em branco.
- **Artigo que o mapa não conhece** — acontece com medições feitas antes da
  importação, ou com artigos escritos à mão. O `TSKMQT` lista-os no fim, e na
  folha saem no fim do bloco do seu serviço.
- **Mapa novo do cliente** — corra o `TSKMQT` outra vez. As medições não se
  perdem: continuam ligadas ao artigo pelo código e designação. No fim, o
  comando diz quantas ficaram sem artigo e como as reclassificar.
- `TSKMQTLIMPAR` apaga o mapa deste desenho. As medições ficam como estão.

### Mapas MD, EO e QT

Se o seu modelo tiver as macros da casa, os botões **MD**, **EO** e **QT**
correm-nas a partir do AutoCAD, sem ir ao Excel.

---

## Comandos

Quem prefere escrever a carregar em botões:

| Comando | O que faz |
|---|---|
| `TSKPAINEL` | Abre o painel |
| `TSKPAREDERET` | Parede por rectângulo |
| `TSKPAREDE` | Parede por polyline |
| `TSKAREA` | Área em planta × altura (m³) |
| `TSKAREASEL` | Área × altura sobre hachuras/polylines já existentes |
| `TSKRET` | Material por rectângulo |
| `TSKPOLF` | Material por polyline × altura |
| `TSKLINEAR` | Medição linear |
| `TSKMEDSEL` | Mede elementos já desenhados no projeto |
| `TSKVAO` | Adiciona vãos manualmente |
| `TSKCONTAR` | Conta blocos selecionados |
| `TSKEXCEL` | Liga/desliga o Excel ao vivo |
| `TSKEXPORT` | Exporta a folha |
| `TSKMODELO` | Escolhe o modelo de medições |
| `TSKIMPORTAR` | Recupera medições de uma folha guardada |
| `TSKMQT` | Importa o mapa de quantidades do cliente |
| `TSKMQTLIMPAR` | Apaga o mapa de quantidades deste desenho |
| `TSKMD` / `TSKEO` / `TSKQT` | Corre as macros do modelo |
| `TSKTEXTO` | Altura dos rótulos no desenho |
| `TSKLICENCA` | Ver ou activar a licença |
| `TSKSOBRE` | Versão, build e estado da licença |
| `TSKONDE` | Diagnóstico: onde estão as medições |
| `TSKVAODIAG` | Diagnóstico: o que o detector lê numa selecção de vãos |

---

## Licença

Ao instalar, tem **15 dias de avaliação**. No primeiro comando de medição,
informe um e-mail de contacto. A avaliação fica associada à instalação e começa
sem necessidade de inserir um código.

Depois disso, use `TSKLICENCA` e cole o código que recebeu. O código fica ligado a
este computador e renova-se sozinho enquanto houver internet de vez em quando.

Sem licença, os comandos de **medir** param. **Ver o painel, consultar medições
antigas e exportar continuam a funcionar** — nunca fica com trabalho retido.

Sem internet, vale a validade guardada localmente. Em obra continua a
trabalhar.

---

## Quando alguma coisa corre mal

**As medições não aparecem no desenho.**
Comando `TSKONDE`. Diz em que espaço estão, se a layer está desligada ou
congelada, e leva-o até lá.

**Os rótulos saem enormes ou minúsculos.**
Comando `TSKTEXTO` e escreva a altura que quer. Fica guardada para todos os
desenhos.

**O Excel abriu o modelo em Vista Protegida.**
Comando `TSKMODELODESBLOQUEAR`. Acontece quando o ficheiro veio por email ou
download.

**O ecrã do AutoCAD fica preto ao ligar o Excel.**
É uma caixa do Excel escondida atrás da janela. Alt+Tab para o Excel e veja o
que ele está a pedir.

**Fechei o Excel sem querer.**
Carregue outra vez em **Excel ao Vivo**.

**O plugin não carrega depois de instalar.**
Quase sempre são ficheiros bloqueados pelo Windows. No PowerShell:

```powershell
Get-ChildItem "$env:APPDATA\Autodesk\ApplicationPlugins\TSKTakeOff.bundle" -Recurse | Unblock-File
```

Depois reabra o AutoCAD.

---

## Onde ficam as coisas

| O quê | Onde |
|---|---|
| As medições | dentro do próprio DWG |
| Licença | `%APPDATA%\TSKTakeOff\licenca.dat` |
| Modelo de medições | `%APPDATA%\TSKTakeOff\modelo.xlsx` |
| Materiais e cores | `%APPDATA%\TSKTakeOff\materiais.txt` |

Ao mudar de computador, copie a pasta `%APPDATA%\TSKTakeOff` para levar as
preferências. A licença não vai — introduz-se o código outra vez.
