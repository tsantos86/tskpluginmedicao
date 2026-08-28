net# Excel ao vivo no modelo da casa

O plugin escreve directamente no formato das folhas de medição da casa — o
modelo distribuído por omissão é `modelo-medicoes.xlsx` — em vez de inventar
uma folha própria.

## Como funciona

1. Registas o livro-modelo uma vez (`TSKMODELO`).
2. Ao ligar o Excel ao vivo, o plugin **copia** o modelo para o `%TEMP%` e abre
   a cópia. O original nunca é tocado.
3. As medições entram nas colunas **H a M**. Tudo o resto — fórmulas
   auxiliares, larguras de coluna, cabeçalho do projecto e **as macros** —
   chega intacto.

## Dois estilos de modelo

O plugin reconhece dois formatos de folha de medições, à escolha do que
registares em `TSKMODELO`:

- **Estilo "art / descrição"** (como o modelo da casa): cabeçalho com "art" numa
  coluna e "descrição" noutra, normalmente com macros próprias (Ctrl+M/E/Q).
  O plugin escreve nas colunas H a M, como descrito abaixo.
- **Estilo "Item / Designação"** (como uma folha de medição normal, ex.:
  `Item | Designação | Un | Qt | Comp / Area | Largura | Altura | Unitária |
  Sub total | Totais`): o plugin encontra sozinho o cabeçalho, seja qual for
  a linha ou a ordem das colunas, e escreve nesse layout — preservando
  qualquer bloco de identificação da obra (Obra, Empreitada, Código, Data)
  que esteja por cima.

Não é preciso configurar qual dos dois é: o plugin testa os dois estilos ao
ligar o Excel ao vivo e usa o que encontrar. Se o ficheiro tiver várias
folhas candidatas, escolhe a mais vazia para duplicar — para não arrastar
medições de outro capítulo ou doutra obra para a cópia de trabalho.

## A folha sai limpa e com o aspecto da casa

No estilo "Item / Designação", o plugin não inventa formatação nenhuma:
**copia-a das linhas do próprio modelo**.

Ao abrir o modelo, procura nas primeiras linhas preenchidas um exemplo de cada
tipo de linha e guarda-o como molde:

| Molde | Como é reconhecido | Exemplo típico |
|---|---|---|
| Faixa de capítulo | só texto, com cor de fundo | `01 - ALVENARIAS EXTERIORES E INTERIORES` |
| Subtítulo | só texto, sem cor de fundo | `TORRE 1_Fração CCC + DDD` |
| Artigo | tem unidade e um `SUM` na coluna de totais | `1 - Fornecimento e aplicação de paredes…` `m2` |
| Medição | tem quantidade e dimensões | `1 · 4,168 · 2,980` |

Depois, cada linha escrita recebe o formato do molde correspondente: as cores,
os negritos, o tamanho de letra, as casas decimais e as bandas alternadas são
as do modelo, não as do plugin. Muda-se o modelo, muda o aspecto da folha —
sem tocar em código.

As fórmulas seguem o mesmo princípio. A da coluna *Unitária* é lida do modelo
em notação R1C1 (independente da linha) e reaplicada tal e qual, por isso
funciona com qualquer variante — no exemplo acima é
`=ROUND(((D=0)+D)*((E=0)+E)*((F=0)+F)*((G=0)+G)*(D+E+F+G<>0);3)`. O total de
cada artigo soma o seu próprio bloco de medições.

Antes de escrever, a área de dados é limpa por completo — valores, fórmulas e
formatação — deixando intactos o bloco de identificação da obra, a linha de
cabeçalho, as larguras de coluna e os painéis fixos. É isto que garante que a
folha sai como se fosse nova, sem restos da obra anterior nem bandas herdadas
de quando havia mais linhas.

A folha que serve de molde nunca é escrita: se o capítulo tiver o mesmo nome,
o plugin cria uma folha à parte para não perder a referência.

## O que o plugin escreve (estilo "art / descrição")

| Coluna | Conteúdo | Quem escreve |
|---|---|---|
| A `art` | código do artigo | **tu**, à mão |
| B `art_parciais` | código da linha de total | **tu**, à mão |
| C–G | auxiliares (nível, propagação do código) | fórmulas do modelo |
| H `descrição` | capítulo, artigo, alçado, piso, medição | plugin |
| I `descrition` | unidade (m, m2, m3) | plugin |
| J `=` | quantidade — negativa nas deduções | plugin |
| K `comp` | comprimento | plugin |
| L `larg` | largura (só quando existe) | plugin |
| M `alt` | altura | plugin |
| N `elementares` | `= J×K×L×M` conforme as células preenchidas | fórmula do modelo |
| O `parciais` | `SUMIF` por artigo | fórmula do modelo |

A unidade sai das dimensões que ficam mesmo preenchidas: comp × alt dá `m2`,
comp × larg × alt dá `m3`, só comp dá `m`. É a mesma conta que a coluna N faz.

**A coluna A fica vazia de propósito.** Os códigos vêm do mapa de trabalhos do
projecto e são a única coisa que o plugin não pode adivinhar. Assim que colares
os códigos em A e B, as colunas C–G e O acordam sozinhas e os totais aparecem.

## Estrutura gerada

```
ALVENARIAS                 <- capítulo (nome da folha)

ALVENARIA TIJOLO 15   m2   <- artigo
Alçado Tardoz              <- alçado (só se estiver preenchido no painel)
Piso 0                     <- piso
parede 1        1  4.25       2.80
VE.10          -2  1.20       1.10   <- dedução, a vermelho
parede 2        1  3.10  0.15 2.80
```

Deduções de vãos entram como linha normal com `=` negativo, tal como já fazes
à mão nas folhas antigas.

## Uma folha por capítulo

As alvenarias vão para a folha `ALVENARIAS` e os materiais/fachadas para
`MATERIAIS`. Se a folha ainda não existir, o plugin **duplica a folha de
medições mais vazia do modelo** — herdando formatação e fórmulas — e limpa-a.

### Fazer as macros apanharem as folhas novas

As macros `CriarMD_v1`, `CriarEO_Final` e `CriarQT` trabalham sobre uma lista
fixa de folhas (`"Resumo MD"`, `"1.1"` … `"1.15"`). Para as medições do plugin
entrarem nesses mapas, diz em que folha do modelo cai cada capítulo:

```
%AppData%\TSKTakeOff\folhas.txt

ALVENARIAS=1.1
MATERIAIS=2.1
```

Sem esse ficheiro, o capítulo dá o nome à folha (`ALVENARIAS`, `MATERIAIS`) e
as macros ignoram-nas.

## Comandos

| Comando | Função |
|---|---|
| `TSKMODELO` | Escolher o livro-modelo (fica registado para sempre) |
| `TSKMODELORESET` | Esquecer o modelo e voltar à folha simples |
| `TSKEXCEL` | Ligar/desligar o Excel ao vivo |
| `TSKEXPORT` | Gravar a folha ao lado do DWG, no formato do modelo |
| `TSKMD` | Correr a macro `CriarMD_v1` — mapa de medições |
| `TSKEO` | Correr a macro `CriarEO_Final` — estimativa orçamental |
| `TSKQT` | Correr a macro `CriarQT` — mapa de quantidades |

Os três últimos precisam do Excel ao vivo ligado **e** das macros activadas no
Excel (Ficheiro → Opções → Centro de Fiabilidade → Definições de Macros).

## Quando não funciona

**"O Excel abriu o modelo em Vista Protegida"** — o ficheiro veio da internet
ou do e-mail e o Windows marcou-o. Nesse estado o Excel não deixa ninguém lá
mexer e as macros ficam bloqueadas. Corre `TSKMODELODESBLOQUEAR`, ou abre o
livro no Excel e carrega uma vez em **Activar edição**. O `instalar-modelo.bat`
já faz isto sozinho.

**"Nenhuma folha do modelo tem o cabeçalho de medições"** — a mensagem lista as
folhas encontradas e o que estava em A5 e H5 de cada uma. Serve para perceber
se foi registado o ficheiro errado.

**O ecrã do AutoCAD fica preto ao ligar o Excel** — era o Excel a mostrar uma
caixa de aviso atrás da janela do AutoCAD. O plugin passou a desligar esses
avisos e a abrir o livro sem perguntar por ligações. Se voltar a acontecer,
alt-tab para o Excel e vê o que ele está a pedir.

Em qualquer destes casos o plugin **não pára**: avisa e continua na folha
simples, para não te deixar sem trabalhar.

## Notas práticas

- O ficheiro exportado sai com a mesma extensão do modelo (`.xls` mantém as
  macros; um `.xlsx` perde-as).
- Se as medições passarem da linha 1462, o plugin estende as fórmulas C–G e
  N–O — é o que a macro `Ctrl+F` (`ActualizaFormulas`) faz à mão.
- Sem modelo registado nada quebra: o Excel ao vivo volta à folha simples de
  antes.
- Se o Excel for fechado à mão, basta voltar a carregar em **Excel ao Vivo**.
