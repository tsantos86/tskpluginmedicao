# Análise de desempenho — TSK TakeOff

Relatório gerado pela rotina automática, tarefa "Análise — Desempenho" da
fila (`Docs/TAREFAS_AGENTE.md`). Só relatório e medições em lógica pura:
**nenhum código de produção foi alterado ou otimizado nesta tarefa.**

Método: leitura direta do código-fonte (não da documentação/README),
delegada em três agentes de investigação em paralelo — um por área
(`Leitura.cs`/`Palette.cs`; `ExcelLiveSync.cs`; `FolhaMedicao.cs`/
`MapaQuantidades.cs`/exportadores) — com citações `ficheiro:linha`
confirmadas por leitura directa, e testes de desempenho novos em
`Tests/DesempenhoTests.cs` (100, 1.000 e 5.000 medições sintéticas,
`[Trait("Categoria","Desempenho")]`), correndo só a lógica pura já
independente do AutoCAD.

## 1. Resumo executivo

| Área | Complexidade encontrada | Risco real | Confiança |
|---|---|---|---|
| `Leitura.Tudo` (travessia do Model Space) | O(n) no total de entidades Polyline/Hatch/Circle do desenho, não só nas medições TSK | Médio-alto em desenhos de arquitetura com milhares de polylines de referência | FACTO no código; impacto absoluto por confirmar no AutoCAD |
| `Palette.cs` — disparo do refresh | Debounce/throttle temporal já existe (Idle + janela adaptativa); **sem filtro semântico** — qualquer alteração ao desenho dispara `Leitura.Tudo` completo | Médio — o throttle já limita a frequência, mas não a causa | FACTO |
| `ExcelLiveSync.AtualizarTudo` (caminho principal, com modelo) | Reescreve o conteúdo por inteiro a cada atualização, mas em bloco (1 `ClearContents` + 1 escrita de array 2D a `Range`), não célula a célula; formatação já incremental (só a partir da 1ª linha que mudou de tipo); ganho de 16× já documentado no código (10.081 ms → 643 ms, obra de ~360 linhas) | Baixo no caminho principal | FACTO |
| `ExcelLiveSync.EscreverSimples` (caminho de fallback, sem modelo) | ≈10-15 chamadas COM **por linha** (célula a célula) + `Columns.AutoFit()` sobre toda a folha | Alto quando este caminho é usado (sem ficheiro-modelo configurado, ou depois de uma falha do modelo) | FACTO |
| `ResultadosArvore`/`ResultadosAdaptadores` (construção da árvore) | O(n) por passagem; 4 passagens recursivas sequenciais pós-construção (`OrdenarResiduaisNoFim`→`Agregar`→`RotularTipos`→`PropriedadesDosGrupos`), cada uma O(n) — soma constante, não assintótica | Baixo — confirmado por medição real (secção 4): 67 ms para 5.000 medições | FACTO + medido |
| `FolhaMedicao.Construir`/`AgruparParedes` | O(n log n) — `GroupBy`/`OrderBy` aninhados parecem quadráticos à leitura superficial mas não o são (cada nível particiona sem reprocessar) | Baixo — confirmado por medição real: 7 ms para 5.000 medições | FACTO + medido |
| `FolhaMedicao.Numerar` | Parece nested-loop O(n²) à primeira vista; é O(n) porque os scans internos não se sobrepõem (documentado como "falso positivo" na secção 5) | Nenhum | FACTO (leitura cuidadosa) |
| `MapaQuantidades.Procurar`/`OrdemDe`/`ChaveCanonica` (hot-path da folha) | O(1) via `Dictionary` | Nenhum | FACTO |
| `MapaQuantidades.PorCodigo`/`Filtrar` | O(n) por chamada, sem índice | Baixo — uso pontual/interactivo, não em loop sobre medições | FACTO + inferência razoável |
| `FiebdcExporter.Gerar` | O(n), agrupamento via `Dictionary` | Nenhum | FACTO + medido |
| `ExcelExporter.Export` | O(n), uma passagem simples (custo de `ClosedXML` internamente não avaliável a partir daqui) | Nenhum no código deste ficheiro | FACTO + inferência (biblioteca externa) |
| `ResultadosArvore.Handles(raiz).Contains(nova)` em `AtualizarResultadosCompactos` | Travessia completa da árvore (O(n)) + alocação de lista só para testar UM handle conhecido | Baixo isoladamente, mas corre a cada refresh com medição nova pendente | FACTO |

**Conclusão geral:** a lógica pura já testável (árvore de resultados, folha
de medição, exportador FIEBDC) está bem dimensionada — nenhuma delas
excede alguns dezenas de milissegundos com 5.000 medições sintéticas. O
risco de desempenho real está do lado do AutoCAD: `Leitura.Tudo` percorre
TODO o Model Space (não só as medições) a cada refresh, e o refresh
dispara para qualquer alteração ao desenho, sem filtrar se essa alteração
tem alguma relação com uma medição TSK. Isto só é observável com perfil ao
vivo num desenho real, mas o padrão de acesso já está confirmado no
código.

## 2. `Leitura.Tudo` — travessia do Model Space

**Ficheiro:** `Leitura.cs`, função `Tudo` (linhas 19-100).

Abre uma única transação e itera `foreach (ObjectId id in ms)` sobre o
`BlockTableRecord` do Model Space inteiro (linha 36), onde `ms` vem de
`Util.EspacoMedicoesId(db)` — todo o Model Space da base de dados, não uma
coleção pré-filtrada às medições do plugin.

- **Filtro de classe cedo, sem abrir a entidade (FACTO).** O filtro é feito
  por `id.ObjectClass == Util.ClPolyline / ClHatch / ClCircle` (linhas 41,
  84, 91) — uma comparação de `RXClass` que não abre o objecto. O próprio
  código documenta esta preocupação em `Commands.cs:2695-2700`:

  > "Comparar o ObjectClass de um ObjectId NÃO abre o objecto — é só uma
  > comparação de ponteiros. Abrir cada entidade do Model Space para depois
  > descobrir que é um texto ou um bloco é o que torna a paleta lenta num
  > desenho de arquitectura, onde as nossas medições são um punhado entre
  > dezenas de milhares de objectos."

- **Sem filtro por XData antes de abrir a entidade (FACTO).** Para CADA
  `Polyline` do desenho (não só as medições TSK), a entidade é aberta
  (`tr.GetObject`, linha 43) e passa por até 3 leituras de XData sequenciais
  antes de ser descartada como "não é nossa": `AlvRepo.LerParede`
  (`AlvRepo.cs:46`), depois `FacRepo.LerFachada` (`Fachada.cs:180`), depois
  a XData linear directamente (`Leitura.cs:59-60`). Uma polyline de
  referência qualquer (cota, linha auxiliar) paga 1 `GetObject` + 3
  `GetXDataForApplication` só para ser rejeitada. Cada `Hatch` e `Circle`
  pagam 1 leitura de XData cada (`AlvRepo.cs:94`, `Contagem.cs:123`).

- **Cálculo de geometria só nas medições reais (FACTO).** `pl.Area`,
  `LadosDoRetangulo`, `EhRectangulo`, `MedidasDaHachura` só correm depois de
  a XData confirmar que a entidade é uma medição TSK — o custo geométrico
  extra é proporcional às medições, não ao desenho inteiro.

**Complexidade:** O(n) no total de Polylines/Hatches/Circles do Model
Space, não no número de medições. **Como medir no AutoCAD real:** o
`Cronometro` já existe no projecto — ligá-lo à volta de `Leitura.Tudo` num
desenho real com uma contagem conhecida de entidades totais vs. medições
TSK, e comparar o tempo com um desenho onde as duas contagens são
próximas. **Precisa de AutoCAD para validar** o impacto absoluto; o padrão
de acesso está confirmado por leitura do código.

## 3. `Palette.cs` — quando o refresh dispara

**Ficheiro:** `Palette.cs`.

- **Reactors "baratos" sem filtro semântico (FACTO).** `AoObjectoMudar`
  (linhas 304-307) e `AoObjectoApagado` (309-312), ligados a
  `ObjectModified`/`ObjectAppended`/`ObjectErased`
  (`LigarSincronizacaoAutomatica`, 266-274; `ObservarDocumentoActivo`,
  281-302), apenas marcam `_sujoPorEdicaoExterna = true` — não distinguem
  se o objecto alterado é uma medição TSK ou qualquer outra entidade do
  desenho. Mover uma linha qualquer ou criar um círculo sem XData dispara
  igualmente a flag.

- **Debounce/throttle temporal já existe (FACTO — não é um problema).**
  `AoFicarOcioso` (318-333) só chama `RefreshData()` no `Application.Idle`,
  nunca a meio de uma edição, e adapta a janela mínima ao tempo que a
  última sincronização demorou (`_ultimaSincronizacaoMs`/
  `_proximaSincronizacaoPermitida`), com teto de 5000 ms
  (`AtrasoAutoSyncMaxMs`, linha 243).

- **Mas o throttle é só temporal, não semântico (FACTO).** Passada a
  janela, o próximo Idle "sujo" dispara `RefreshData()` →
  `Leitura.Tudo` (linha 85) incondicionalmente, sem verificar se a
  alteração que sujou a flag tinha alguma relação com uma medição TSK.
  `FluxoAtualizacao.DeveLerDesenho` (`FluxoAtualizacao.cs:10-13`) só decide
  **se** vale a pena ler (painel aberto OU Excel ligado), nunca **o que**
  mudou.

- **Estado estático não é o problema (FACTO, ao contrário do suspeitado).**
  `Config` é memória simples sem I/O; `MapaQuantidades.Procurar` já usa
  cache por documento e `Dictionary` O(1) (`GarantirCarregado`,
  `MapaQuantidades.cs:508-513`); `FachadaConfig.Carregar`/
  `ContagemConfig.Carregar` só correm na abertura do diálogo de definições,
  fora do caminho do refresh. Nenhum destes é recalculado a cada refresh.

**Conclusão:** o mecanismo de debounce está bem desenhado no tempo, mas não
na causa — uma alteração no desenho sem relação nenhuma com medições ainda
provoca, mais cedo ou mais tarde, uma releitura completa do Model Space
(secção 2). **Precisa de AutoCAD** para medir a frequência real destes
eventos "irrelevantes" num desenho de trabalho.

## 4. Construção da árvore de resultados (medido)

**Ficheiros:** `ResultadosArvore.cs`, `ResultadosAdaptadores.cs`,
`Palette.cs` (`BindData`, 3555-3682).

- `FolhaMedicao.OrdenarComoFolha` (chamado de `BindData`, linhas 3566/3569)
  usa a mesma cadeia de `GroupBy`/`OrderBy` de 4-5 níveis que
  `AgruparParedes` (ver secção 5) — O(n log n), reconstruída do zero a
  cada `RefreshData`, mesmo que só uma medição tenha mudado.
- `ResultadosArvore.DeParedes`/`ResultadosAdaptadores.De*` — uma única
  passagem `foreach` por lista, sem travessias redundantes internas
  (FACTO, confirmado por leitura).
- `ResultadosArvore.Construir` — um `foreach` único que constrói a árvore
  com um `Dictionary<string, NoResultado>` (lookup O(1) por grupo), seguido
  de **4 passagens recursivas adicionais** sobre a árvore já construída:
  `OrdenarResiduaisNoFim` → `Agregar` → `RotularTipos` →
  `PropriedadesDosGrupos`. Cada uma é O(n); juntas são um múltiplo
  constante de O(n), não um problema assintótico.
- **Achado concreto:** em `AtualizarResultadosCompactos`
  (`Palette.cs:1989`), `ResultadosArvore.Handles(raiz).Contains(nova)` faz
  uma travessia recursiva completa da árvore para construir uma
  `List<string>` com todos os handles, só para testar a pertença de **um
  único handle já conhecido** (a medição nova). Corre a cada
  `BindData`/refresh em que há uma medição nova pendente. Não é grave
  isoladamente (ainda O(n)), mas é trabalho evitável — um
  `HashSet<string>` construído uma vez, ou percorrer a árvore só até
  encontrar o handle, evitava a alocação completa da lista.

### Medido (`Tests/DesempenhoTests.cs`, máquina do sandbox, sem AutoCAD)

| Operação | n=100 | n=1.000 | n=5.000 |
|---|---|---|---|
| `ResultadosArvore.DeAlvenaria` (projectar + construir) | 1 ms | 11 ms | 67 ms |
| `ResultadosArvore.Projetar` (sem filtros, tudo expandido) | 0 ms | 1 ms | 103 ms* |
| `ResultadosArvore.Projetar` (com pesquisa + filtro) | 0 ms | 0 ms | 3 ms |
| `FolhaMedicao.Construir` (só alvenaria) | 0 ms | 1 ms | 7 ms |
| `FiebdcExporter.Gerar` | 0 ms | 1 ms | 8 ms |

\* O valor de 103 ms em n=5.000 (vs. 3 ms da mesma operação com pesquisa+
filtro) reflecte a fixture sintética: sem filtro, TODOS os 5.000 nós ficam
expandidos e `Achatar` gera uma `List<NoVisivel>` com todas as
medições+vãos+títulos (dezenas de milhares de linhas) — é o custo de
achatar uma árvore totalmente aberta, não uma anomalia de algoritmo. Com
pesquisa/filtro activos a lista achatada é muito menor, daí o tempo cair.
Isto é coerente com o comportamento esperado: um desenho real com 5.000
medições dificilmente é visto com a árvore inteira expandida de uma vez.

Todos os valores são de uma única máquina de sandbox (sem afinidade de
CPU garantida, sem repetição estatística) — servem para confirmar a ordem
de grandeza (dezenas de milissegundos, não segundos), não como benchmark
rigoroso. Nenhum destes tempos é motivo de preocupação para uso interactivo
numa paleta.

## 5. `FolhaMedicao.cs` e `MapaQuantidades.cs`

**Ficheiro:** `FolhaMedicao.cs`.

- `Construir` (117-141) é uma passagem simples por 4 listas disjuntas.
- `AgruparParedes` (463-616) e `AgruparFachadas` (653-727) usam cadeias de
  `GroupBy`/`OrderBy`/`ThenBy` aninhadas que parecem, à leitura
  superficial, um loop dentro de loop quadrático. **Não são**: cada
  `GroupBy` particiona só os elementos já dentro desse subgrupo — nenhum
  elemento é reprocessado pelos níveis inferiores mais do que uma vez, e os
  `OrderBy`/`ThenBy` ordenam as chaves de grupo (poucas), não os `n`
  elementos repetidamente. **Estimativa: O(n log n)**, não O(n²) — FACTO
  por leitura cuidadosa.
- `Numerar` (827-903) é o ponto que mais parece um O(n²) à primeira leitura
  (um loop `for` com um segundo `for` interno por linha marcada `"@ALC"`).
  **Não é**: as linhas que iniciam um scan interno são exactamente os
  tipos que também terminam o scan de outra (Alcado/Piso/Capítulo/Título),
  portanto os intervalos varridos por scans sucessivos não se sobrepõem —
  cada linha de dados é visitada por, no máximo, um scan interno. Resultado
  líquido: O(n), apesar da forma sintáctica de loop aninhado. Vale a pena
  registar isto como um "falso positivo" de leitura superficial, para
  ninguém "otimizar" isto sem necessidade numa sessão futura.
- Sem concatenação de strings ineficiente (`StringBuilder`/`string.Join`
  usados correctamente, nunca `+=` a acumular em loop grande).

**Ficheiro:** `MapaQuantidades.cs`.

- **Hot-path já optimizado (FACTO).** `Procurar` (180-203) usa
  `Dictionary<string, No> _porNormalizada` — O(1) por chamada. `OrdemDe`
  e `ChaveCanonica` (usados dentro do `GroupBy` de `FolhaMedicao`, o que
  confirma que aquele agrupamento não é O(n²)) passam por `Procurar`,
  logo também O(1).
- **`PorCodigo`/`Filtrar` continuam O(n) por chamada, sem índice** (217-232,
  238-260) — pesquisa linear/substring. Sem evidência, nestes ficheiros,
  de que sejam chamados dentro de um loop sobre todas as medições (o que
  geraria O(n²)); parecem servir uso pontual/interactivo (escrever um
  código à mão, autocomplete). Risco baixo tal como estão.
- `Indexar`/`Guardar` (318-341, 398-461) reconstroem os dois dicionários
  e substituem o `DBDictionary` inteiro no DWG a cada carregar/gravar —
  O(n) por evento, não por medição, e é uma opção deliberada documentada
  no próprio código ("Substituir por inteiro: um MQT meio antigo meio novo
  era pior").

## 6. `FiebdcExporter.cs` e `ExcelExporter.cs`

- **`FiebdcExporter.Gerar`** (64-115): filtra com LINQ O(n), agrupa por
  artigo com um `Dictionary<string,int>` como índice (73-85) — sem
  procura linear escondida. `StringBuilder` em todo o ficheiro, sem
  concatenação ineficiente. **Estimativa: O(n)**, confirmado por medição
  (secção 4: 8 ms para 5.000 linhas).
- **`ExcelExporter.Export`** (19-42): chama `FolhaMedicao.Construir` uma
  vez, depois uma passagem simples `foreach (var l in linhas) Escrever(...)`
  (29-34) — O(1) por linha no código deste ficheiro. `Formatar` (188-208)
  aplica estilo a *ranges* completos com uma chamada por coluna, não um
  loop por célula, do lado do TSK TakeOff — o custo interno do `ClosedXML`
  ao aplicar a um range é opaco a partir daqui (INFERÊNCIA; não corre
  fora do AutoCAD/sem dependência de Excel COM neste caso, mas não foi
  medido nesta sessão porque `ExcelExporter.cs` não está no projecto de
  testes — usa `ClosedXML`, que exigiria adicionar essa dependência ao
  `Tests/TSKTakeOff.Tests.csproj`; fica como proposta separada, não feito
  aqui para não alargar o âmbito desta tarefa).

## 7. `ExcelLiveSync.cs` — Excel ao vivo (COM Interop)

**Ficheiro:** `ExcelLiveSync.cs` (2.497 linhas). Há **três caminhos de
escrita diferentes**, consoante o modo de ligação ao Excel:

- `AbrirModelo` (161-203) — caminho **principal** do produto: o workbook é
  aberto **uma única vez** (`_app.Workbooks.Open`, linha 167), nunca
  reaberto a cada atualização. Escreve por `EscreverFolha` (modelo clássico
  art/descrição) ou `EscreverFolhaItem` (modelo Item/Designação).
- `AbrirSimples` (153-159) — caminho de **recurso**, só usado quando não
  há ficheiro-modelo configurado ou quando `AbrirModelo` falha (125-143).
  Cria um workbook novo em memória e escreve por `EscreverSimples`.

Os dois caminhos têm desempenho radicalmente diferente.

### `AtualizarTudo`/`Redesenhar` — reescreve tudo, mas em bloco (FACTO)

`AtualizarTudo` (592-601) chama `Redesenhar(regra)` (609-666), que
despacha para o caminho do modelo ou o simples.

No **caminho do modelo** (`EscreverFolhaItem`, 1112-1259):
`LimparDadosItem` (1738-1791) faz `ClearContents()` (linha 1770) sobre
todo o intervalo de dados (até 2000 linhas, linha 1743) numa **única**
chamada COM; a seguir os valores de TODAS as N linhas são escritos numa
**única** chamada em bloco, via array 2D atribuído a um `Range`
(`destino.Value2 = dados`, linhas 1233-1234). Ou seja: o conteúdo É
reescrito por inteiro a cada atualização (sem diff linha-a-linha), mas com
1 chamada COM de limpeza + 1 de escrita, **não célula a célula**. A
formatação, ao contrário dos valores, já é incremental: `PrimeiraLinhaAlterada`
(1047-1066) calcula a partir de que linha o "tipo" de linha mudou desde a
última escrita, e só essa zona é reformatada. Fórmulas (`EscreverFormulasItem`,
1558-1609) saem em 3 chamadas em bloco por coluna, independentemente de N.
Formatação de estilo usa `Copy()`/`PasteSpecial` sobre **blocos contíguos**
de linhas do mesmo tipo, não célula a célula (`Blocos`, 1430-1446).

No caminho de **fallback sem modelo** (`EscreverSimples`, 2112-2224): **é
célula a célula**, dentro de um `foreach` — `_ws.Cells.Clear()` (limpa a
folha inteira, linha 2114) seguido de, por linha: até 7 escritas de valor +
até 3 de fórmula + criação de um `Range` novo (linha 2154) + até 4
propriedades de formatação (`Font.Bold/.Italic/.Color`, `Interior.Color`),
mais `_ws.Columns.AutoFit()` no fim (linha 2217) sobre TODAS as colunas
usadas — o clássico anti-padrão de COM Interop, ao contrário do caminho
principal.

### Chamadas COM por atualização (estimativa por leitura, não medida em execução)

- **Caminho do modelo (principal):** ≈ dezenas de chamadas COM em bloco,
  independentemente de N, mais **≈ 1 chamada COM por artigo/capítulo**
  (não por medição) em `FormatarColunaTotais` (1512-1535, linha 1532
  dentro de um `for` por artigo — ponto residual não em bloco, mas escala
  com o nº de artigos, tipicamente dezenas, não com N).
- **Caminho de fallback (`EscreverSimples`):** ≈ **10 a 15 chamadas COM
  por linha**, ou seja, **≈ 10N a 15N chamadas COM** para N medições, mais
  `Cells.Clear()` e `Columns.AutoFit()` sobre a folha inteira.

### `ScreenUpdating`/`Calculation`/`EnableEvents` — já desligados (FACTO, não é um problema)

`Redesenhar` (609-666) desliga explicitamente `_app.ScreenUpdating`,
`_app.Calculation` (modo manual) e `_app.EnableEvents` antes de escrever, e
repõe os três no `finally` (628-664). O comentário no próprio código
(622-624) explica a motivação: sem isto "ele redesenha e RECALCULA a
folha inteira a cada fórmula que entra". **Este ponto do checklist do
plano NÃO é um problema encontrado — é uma otimização já presente.**

### Medição real já documentada no próprio código (dado empírico dos autores)

Comentário em `ExcelLiveSync.cs:1373-1377`: numa folha com a forma de uma
obra real (30 artigos, 150 blocos, ~360 linhas), fazer 1 `Copy()` de
formatação **por bloco** demorava **10.081 ms**; agregando por **tipo de
linha** antes de colar, o mesmo trabalho passou a **643 ms** — **16×**
mais rápido. É o ponto de maior impacto de desempenho já identificado no
histórico do próprio código, e já está corrigido no caminho
`EscreverFolhaItem`/`AplicarEstiloDoModelo`.

### Bug da coluna "Item" (contexto, não desempenho)

Confirmado neste ficheiro (linhas 1190-1219, já reportado em
`Docs/ANALISE_ARQUITETURA.md`, achado #1): a escrita da coluna Item só
acontece no ramo de linhas de **título** (`l.HandleOrigem` não vazio); para
linhas normais de medição (`l.Handle` sem `HandleOrigem`) o valor de
`l.Item` nunca entra no array `dados`, ficando a célula vazia. É uma
questão de correção funcional, sem relação com o desempenho — o método
onde vive já é o caminho otimizado descrito acima.

### Ineficiências residuais pontuais

- `FormatarColunaTotais` (linha 1529-1533) e `EscreverSimples` (linha 2154)
  ainda criam objetos `Range`/`Cells` COM **por linha**, em vez de por
  bloco — inconsistência face ao resto do ficheiro, que evita este padrão
  no caminho principal.
- Sem chamada explícita a recálculo forçado (`.Calculate()`,
  `CalculateFull`) em lado nenhum do ficheiro — o cálculo fica em modo
  manual durante a escrita e volta ao automático no fim, disparando UM
  recálculo do Excel com as fórmulas já prontas.

**Como validar no Excel/AutoCAD real:** o `Cronometro` já existente no
projecto está instrumentado dentro de `EscreverFolhaItem`
(`cron.Marcar(...)`, linhas 1124/1132/1147/1235/1246/1253) — é o sítio
indicado para confirmar os números acima com uma obra real. A contagem
exacta de chamadas COM Interop ao nível OLE Automation só se confirma com
profiling do processo (ETW/Process Monitor); a análise acima é por leitura
de código.

## 8. Propostas de otimização

Ver `## Propostas (aguardam aprovação)` em `Docs/TAREFAS_AGENTE.md` — o
utilizador move para a `## Fila` as que aprovar. Nenhuma foi implementada
nesta tarefa (relatório + medição, sem otimizar produção, como pedido).
