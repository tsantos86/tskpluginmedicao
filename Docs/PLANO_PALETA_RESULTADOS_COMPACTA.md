# Plano de implementação — Paleta de resultados compacta

Referência visual aprovada: `Deploy/MockupPalette/index-resultados-compacto.html`

Última atualização: 2026-09-18

Estado geral: **Painel único em grafite, com `DefinicoesTipoDialog` a fechar os campos de Materiais e Contagens; falta só a matriz manual da Fase 8**

## Como acompanhar

- `[ ]` pendente
- `[~]` em curso
- `[x]` concluído
- `[!]` bloqueado ou a aguardar decisão

| Fase | Entrega | Estado |
|---|---|---|
| 0 | Decisões e âmbito | [x] |
| 1 | Modelo de resultados e testes | [x] |
| 2 | Estrutura visual compacta de Arquitetura | [x] |
| 3 | Árvore de resultados e propriedades | [~] |
| 4 | Pesquisa, filtros e totais visíveis | [~] |
| 5 | Edição, ações e `Medir aqui` | [~] |
| 6 | Unificação num painel só (superou "Aplicação às restantes abas") | [x] |
| 7 | Teclado, acessibilidade, DPI e desempenho | [~] |
| 8 | Regressão, documentação e build final | [ ] |

## Fase 0 — Decisões e âmbito [x]

### Direção aprovada

- O HTML compacto é a referência para densidade, cores, barras, pesquisa, filtros, árvore, propriedades e feedback.
- A revisão mais recente de `Deploy/MockupPalette/brief.md` prevalece onde diverge do HTML:
  - a árvore tem `Estrutura / elemento`, `Comp.`, `Altura` e `Quantidade`, com a unidade dentro da célula e `—` onde não se aplica;
  - as dimensões repetem-se em `PROPRIEDADES`, como mosaicos de métrica sempre visíveis;
  - selecionar serve para consultar/editar; só `Medir aqui` altera a próxima medição;
  - `Medir aqui` aparece apenas em nós de artigo;
  - os totais dos grupos são sempre calculados a partir dos filhos.
- ~~Manter as quatro abas nativas do `PaletteSet`~~ → **UM PAINEL SÓ** (decidido a 2026-09-05). Não há especialidades: mede-se arquitetura, e o que varia é o tipo de medida, que passou a ser um nível da árvore. Com abas, um piso nunca mostrava o seu total nas quatro unidades.
- Implementar primeiro Arquitetura e estabilizar o padrão antes de migrar as restantes abas.
- Hierarquia: `Pavimentos > Piso > Tipo de medida > Artigo > Medição > Vão/Título`.
- A ordem visual não altera a ordem da folha Excel, que continua a ser definida por `FolhaMedicao.cs`.
- ~~`Especialidade` será representada pelo `Serviço`~~ → não existe especialidade. O `Serviço` continua a identificar a medição na folha e vive nas propriedades; o nível da árvore é o **tipo de medida**, que é o que fixa a unidade.
- `Sem piso` e `Por classificar` serão grupos explícitos.
- Filtros afetam apenas a vista. Não alteram o DWG, a exportação nem os dados enviados para o Excel.
- A árvore e os resumos filtrados mostram os totais visíveis e a indicação `n visíveis de N`.
- Manter `Rows.AddRange`, `SuspendLayout` e duplo buffer. `VirtualMode` só será ativado se a medição real justificar a complexidade.
- Não eliminar campos, comandos, edição em lote, seleção Excel/paleta, seguimento da medição nova ou restauração de seleção/scroll.

### Fora do âmbito desta passagem

- Alterar o formato persistido no DWG ou a ordem do Excel.
- Criar uma árvore única que misture as quatro abas.
- Reescrever os comandos de medição ou os repositórios AutoCAD sem necessidade funcional.
- ~~Introduzir um tema escuro novo~~ → o **grafite** passou a ser a variante principal (`index-premium.html`, aprovado a 2026-09-05). O painel vive ancorado dentro do AutoCAD, e um painel branco naquela moldura lê-se como uma janela estranha. A variante clara mantém-se nos tokens e o alto contraste continua a mandar sobre ambas.

## Fase 1 — Modelo de resultados e testes [x]

Objetivo: retirar da interface a lógica de hierarquia, identidade, filtros e agregação, para poder testá-la sem AutoCAD nem WinForms.

### Trabalho

- [x] Criar `ResultadosModelo.cs` com modelos C# 7.3 para:
  - identidade estável do nó;
  - tipo, profundidade, pai e estado expandido;
  - origem por `handle` e índice de vão/título;
  - quantidade por unidade;
  - propriedades essenciais e completas;
  - estado de pesquisa e filtros.
- [x] Criar `ResultadosArvore.cs` com:
  - projeção de `Parede` e `Vao` para a hierarquia aprovada;
  - achatamento dos nós visíveis;
  - agregação separada de `m²`, `m³`, `m` e `un.`;
  - grupos `Sem piso` e `Por classificar`;
  - pesquisa normalizada em serviço, artigo, piso, bloco, alçado, nota, handle e designação dos vãos;
  - propagação dos antepassados de cada resultado encontrado.
- [x] Adicionar os ficheiros puros a `Tests/TSKTakeOff.Tests.csproj` por `Compile Include`.
- [x] Criar `Tests/ResultadosArvoreTests.cs`, `Tests/ResultadosFiltroTests.cs` e `Tests/ResultadosTotaisTests.cs`.

### Critérios de conclusão

- [x] Uma fixture com `PISO 0` e `PISO 1` produz ambos os pisos a partir de dados reais.
- [x] O total do topo é a soma dos filhos por unidade; unidades incompatíveis nunca são somadas.
- [x] O comprimento de uma parede não entra no total em metros de artigos lineares.
- [x] Pesquisa e filtros mantêm visíveis todos os antepassados necessários.
- [x] IDs permanecem estáveis após recolher, filtrar e reconstruir a árvore.
- [x] Os novos ficheiros não usam `Autodesk.*` nem `System.Windows.Forms`.
- [x] `dotnet test Tests/TSKTakeOff.Tests.csproj` passa.

## Fase 2 — Estrutura visual compacta de Arquitetura [x]

Objetivo: aplicar a composição aprovada sem mudar ainda as regras operacionais.

### Ficheiros previstos

- Novo: `PalettePanelShell.cs`
- Novo: `PaletteTheme.cs`
- Alterar: `Palette.cs`
- Alterar apenas se faltarem símbolos: `IconFactory.cs`

### Trabalho

- [x] Reordenar as abas nativas para `Arquitetura`, `Materiais`, `Lineares`, `Contagens`.
- [x] Criar o cabeçalho compacto com DWG ativo e resumo da próxima medição.
- [x] Fazer `CONFIGURAÇÃO` iniciar recolhida e manter o resumo sincronizado com `Config`.
- [x] Manter no corpo principal Artigo, Serviço, Bloco/etiqueta, Piso/Cor, Alçado/zona, Altura, Espessura e Regra de vãos.
- [x] Colocar `Texto do título`, `Layer` manual e layer efetiva em `Mais opções`.
- [x] Criar a barra `MEDIR` com as seis ações existentes:
  - Retângulo;
  - Polyline;
  - Área;
  - Área da seleção;
  - Medir seleção;
  - Adicionar vão.
- [x] Criar a barra de Resultados com Excel ao Vivo, Exportar XLSX, Atualizar e `Mais`.
- [x] Eliminar a duplicação visual de `Atualizar`; haverá uma ação inequívoca.
- [x] Centralizar cores, fontes, margens, alturas e estados de foco em `PaletteTheme.cs`.

### Critérios de conclusão

- [ ] Todos os campos e seis comandos atuais continuam acessíveis.
- [ ] Recolher/expandir Configuração não perde valores nem altera `Config`.
- [ ] O painel permanece legível entre 480 e 760 px.
- [ ] A alteração de layout não muda XData, comandos ou resultados do Excel.

## Fase 3 — Árvore de resultados e propriedades [~]

Objetivo: substituir a grelha larga por uma árvore achatada compacta sem perder identidade, seleção ou edição futura.

### Ficheiros previstos

- Novo: `ResultadosTreeControl.cs`
- Novo: `PropriedadesControl.cs`
- Alterar: `Palette.cs`
- Usar: `ResultadosModelo.cs`, `ResultadosArvore.cs`

### Trabalho

- [x] Criar um `DataGridView` compacto com `Estrutura / elemento`, `Comp.`, `Altura` e `Quantidade`, alimentado por `ResultadosArvore.Projetar`.

  > **Conflito resolvido a 2026-09-05.** As colunas de dimensão voltaram, mas
  > sem a armadilha que o brief nomeava. A objecção era o cabeçalho MENTIR
  > quando o artigo muda de grandeza — «uma contagem de 2 portas debaixo de
  > Comp.». A regra que a desarma: **a unidade viaja dentro da célula** e a
  > coluna que não se aplica mostra `—`, nunca um zero. Numa camada, o «Comp.»
  > traz `48,50 m²` — área em planta — e é a unidade que impede a leitura
  > errada; num linear a altura é `—`, porque não existe.
  >
  > Ajuda ainda o nível novo: dentro de um grupo de tipo de medida, os
  > factores significam sempre a mesma coisa.
- [x] Criar painel de `PROPRIEDADES` ligado ao nó selecionado.
- [x] Desenhar indentação, guias, ícones e comandos de expandir/recolher por tipo de nó.
- [x] Mostrar as quantidades com unidade na própria célula.
- [x] Nos grupos com unidades diferentes, mostrar valores lado a lado, por exemplo `62,93 m² · 36,90 m · 2 un.`.
- [x] Mostrar vãos como deduções negativas conforme a regra em vigor.
- [x] Manter títulos/separadores como nós identificáveis, sem lhes atribuir um total falso.
- [x] Restaurar seleção e scroll por ID estável, não por índice visual da linha.
- [x] Seguir a medição acabada de criar pelo respetivo handle.
- [x] Nós de grupo não transportam handle e não podem ser removidos, editados ou reclassificados.
- [~] Criar `PROPRIEDADES` recolhível com modos `Essenciais` e `Tudo`.

  > **Nota 2026-09-17.** Os modos `Essenciais`/`Tudo` já existiam e foram
  > confirmados correctos por leitura do modelo: cada `Propriedade` traz o
  > seu próprio `Essencial` (não há um `if` por tipo de nó), e medição, vão,
  > título e grupo mostram o conjunto certo dos dois lados — a mesma
  > verificação que o mockup documenta (`data-props` com `*`). O que
  > faltava era o "recolhível": o painel real (`MedPanelControl` em
  > `Palette.cs`, com `PalettePanelShell.MosaicoMetricas`) não tinha
  > nenhum botão para fechar PROPRIEDADES — só o gémeo morto em
  > `ResultadosPainel.cs` (ver nota abaixo) o tinha. Acrescentado um botão
  > `▼/▶ PROPRIEDADES` igual ao de CONFIGURAÇÃO/Mais opções, que recolhe o
  > mosaico e reduz `propriedades.Height` à altura do cabeçalho, mantendo
  > `Essenciais/Tudo` e `Medir aqui` sempre visíveis no cabeçalho. A perda
  > de valor em edição ao trocar de modo/recolher não é um risco novo: o
  > `MosaicoMetricas._editor.Leave` já chama `Confirmar()`, e isso dispara
  > sempre ANTES do `Click` do botão que muda de modo, pela ordem normal de
  > foco do WinForms — confirmado por leitura, não há aqui nada para
  > compilar de novo além do botão. **Implementado, aguarda build manual**
  > (não há AutoCAD/WinForms neste sandbox para compilar `Palette.cs`).
- [x] Mostrar propriedades de grupo, medição, vão e título de forma coerente; a edição entra na Fase 5.

### Critérios de conclusão

- [~] Expandir/recolher e atualizar preservam seleção e posição sempre que o nó ainda existe.

  > **Nota 2026-09-18.** Confirmado por leitura cuidadosa, sem código novo.
  > `AtualizarResultadosCompactos` é o ÚNICO caminho de reconstrução (duplo-
  > clique, `Enter/Espaço/Left/Right`, filtros e `BindData`/`Atualizar`
  > convergem nele) e captura o Id seleccionado e o Id do nó no topo do
  > scroll ANTES de limpar as linhas, repondo os dois por Id depois. Os Ids
  > são determinísticos (handle para medições; caminho de rótulos para
  > grupos), como os testes `Os_Ids_sao_estaveis_ao_reconstruir_a_arvore` e
  > `Os_Ids_sobrevivem_a_recolher_e_filtrar` já garantem no modelo. Fica
  > `[~]` e não `[x]` só porque `Palette.cs` não compila neste sandbox — não
  > há aqui nenhuma alteração de código, é confirmação de comportamento já
  > existente.
- [x] Uma seleção escondida por filtros não fica como alvo invisível de ações; a seleção visual e as ações são limpas/desativadas.
- [~] A nova medição recebe foco sem mudar a configuração da próxima medição.

  > **Nota 2026-09-18.** Confirmado por leitura cuidadosa: `MedicaoNova` é
  > totalmente independente de `Config`, e `MedirAqui()` é a única operação
  > da árvore que escreve em `Config.Piso/Servico/Artigo` (comentário no
  > próprio código). `[~]` pela mesma razão do critério acima.
- [x] Alertas de artigo desconhecido, por classificar e vãos excessivos continuam visíveis sem depender só da cor.
- [x] A árvore apresenta apenas quantidades; todas as dimensões vivem em Propriedades.

## Fase 4 — Pesquisa, filtros e totais visíveis [~]

Objetivo: permitir localizar e conferir resultados sem modificar os dados do desenho.

### Trabalho

- [x] Adicionar pesquisa incremental na barra de resultados.
- [x] Adicionar filtros com estado de rascunho e botão `Aplicar`:
  - Pavimento;
  - Serviço;
  - Artigo;
  - Tipo/unidade;
  - Estado (`Por classificar`, `Com vãos`, `Com alerta`).
- [x] Mostrar badge com o número de filtros aplicados, até dois chips e ação `Limpar`.
- [x] Implementar `Repor` apenas para o rascunho e `Limpar` para pesquisa + filtros aplicados.
- [x] Durante pesquisa/filtro, mostrar os antepassados dos resultados e ignorar temporariamente recolhas que os esconderiam.
- [x] Calcular contagem e totais sobre os resultados visíveis, mostrando também o total global (`n visíveis de N`).
- [x] Mostrar um estado vazio claro quando não há correspondências.

### Critérios de conclusão

- [x] Combinações dos cinco filtros são determinísticas e cobertas por testes.
- [x] Limpar repõe exatamente a vista anterior, sem alterar expansão persistida em memória.
- [x] Filtrar nunca muda `Config`, o DWG, o Excel ou a seleção do Excel.
- [x] A contagem apresentada é calculada; não existe texto fixo herdado do mockup.

## Fase 5 — Edição, ações e `Medir aqui` [~]

Objetivo: recuperar no novo painel compacto toda a capacidade operacional da grelha atual.

### Edição em Propriedades

- [x] Parede: editar Serviço, Artigo, Piso, Altura, Largura e Espessura.
- [x] Vão: editar Designação, Largura e Altura.
- [x] Título: editar o código sem truncar/perder a descrição persistida.
- [x] Manter Comprimento como valor geométrico apenas de leitura.
- [x] Manter Área bruta, desconto, Área líquida, Quantidade, Volume e Pré-aros como cálculos de leitura.
- [x] Preservar multisseleção e operações em lote para Artigo, Piso e dimensões já suportadas.
- [x] Validar valores antes de chamar os métodos existentes de `AlvRepo.cs`.

### Ações

- [x] Barra principal: Excel ao Vivo, Exportar XLSX e Atualizar.
- [x] Menu `Mais`: Remover, Linha branca, Artigo, Reclassificar e Limpar tudo.
- [x] `Limpar tudo` opera sobre handles distintos da fonte completa, nunca sobre as linhas visíveis/filtradas.
- [x] Preservar a prioridade atual do alvo contextual: última interação entre seleção da paleta e célula do Excel; sem ambas, última medição.
- [x] Selecionar um resultado altera apenas Propriedades e o alvo de ações contextuais; não altera a próxima medição.
- [x] `Medir aqui` aparece apenas num nó de Artigo e copia explicitamente Piso + Serviço + Artigo para `Config`, mantendo as dimensões atuais.
- [x] Marcar o artigo resultante com `PRÓXIMA` e atualizar os dois resumos da próxima medição.

### Critérios de conclusão

- [~] Existe paridade entre todos os campos/comandos atuais e o respetivo local novo.

  > **Nota 2026-09-18.** Comparadas as colunas do `_dgv` morto (`servico,
  > artigo, alcado, bloco, piso, comp, alt, larg, esp, bruta, vaos, liq,
  > qtd, vol, aroUn, aroMl`) com as `Propriedade` de
  > `PropriedadesDaParede`/vão/título em `ResultadosArvore.cs`. Sem lacunas:
  > `num` e `sep` eram artefactos da grelha antiga (número de linha e uma
  > coluna que nem o próprio código morto lia); `comp` numa linha de parede
  > parecia editável na UI antiga mas `OnCellEndEdit` nunca a gravava (só
  > `alt/larg/esp`) — o painel novo, ao deixar Comprimento só de leitura,
  > corrige esse aparente-mas-falso campo editável. Título ganhou uma
  > capacidade a mais (editar a Descrição, não só o Código).
- [~] Reclassificar continua a aceitar Ctrl/Shift e vários handles.

  > **Nota 2026-09-18.** Confirmado: `_dgvCompacto` tem `MultiSelect = true`
  > e `SelectionMode = FullRowSelect` (Ctrl/Shift nativos do WinForms), e
  > `HandlesSeleccionados()` agrega `SelectedCells`/`SelectedRows` ignorando
  > grupos/títulos e deduplicando vão→parede. Nada por terminar.
- [~] Remover distingue corretamente medição, vão e título.

  > **Nota 2026-09-18.** Confirmado: `RemoverParede()` despacha por
  > `NoSeleccionado()` — grupo recusa, título usa `AlternarTitulo` (só o
  > título sai), vão delega em `RemoverVaoSeleccionado()`, resto apaga a
  > medição. Opera sobre um nó só, nunca sobre multisselecção, por isso não
  > há "seleção mista" para confundir.
- [x] Atualizar continua a forçar a escrita imediata no Excel quando este está ligado.
- [~] Apenas `Medir aqui` altera a próxima medição.

  > **Nota 2026-09-18.** Confirmado por leitura de todos os `Config.` em
  > `Palette.cs`: o único ponto que escreve `Config.Piso/Servico/Artigo` a
  > partir da árvore é `MedirAqui()`.

  Os quatro critérios acima ficam `[~]` e não `[x]` porque a verificação foi
  só por leitura — `Palette.cs` não compila neste sandbox (sem AutoCAD/
  WinForms). Nenhum deles precisou de código novo.

## Fase 6 — Unificação num painel só [x]

Objetivo original: reutilizar o padrão estabilizado nas quatro abas, sem criar
uma árvore global nem uma nova leitura do DWG.

> **Superada pela decisão de 2026-09-05: um painel só.** Em vez de repetir o
> padrão em quatro abas, as quatro deixaram de existir. Não há
> especialidades — mede-se arquitetura — e o que distinguia Alvenaria,
> Materiais, Lineares e Contagens passou a ser o TIPO DE MEDIDA, um nível da
> própria árvore. Isso torna a maior parte dos critérios abaixo obsoletos por
> construção: não há "abas" para manter sincronizadas, porque só há uma
> árvore e um `ResultadosPainel`.

### Ficheiros

- `ResultadosAdaptadores.cs` — Materiais, Lineares e Contagens traduzidos para
  o mesmo `MedicaoResultado` da alvenaria, com o `TipoMedida` declarado.
- `ResultadosPainel.cs` — o `UserControl` único (árvore + pesquisa + filtros +
  mosaicos de propriedades) que substituiu as quatro grelhas.
- `DefinicoesTipoDialog.cs` — diálogo modal para os campos que só faziam
  sentido dentro de uma aba antiga (material e altura de piso dos panos;
  nome, categoria, raio e texto das contagens) e que o painel único não tem
  onde alojar sem os mostrar sempre, mesmo quando não se aplicam.
- `Palette.cs` — `BindData` combinado, botão «Definições…» na grelha de MEDIR.

### Trabalho

- [x] Criar adaptadores de Materiais, Lineares e Contagens para os modelos
  comuns de resultado (com `TipoMedida`).
- [x] Um `ResultadosPainel` só, reutilizado — não há "reutilizar entre quatro
  abas" porque não há quatro abas.
- [x] Configuração, ações e remoção específicas por tipo: os campos que só
  um tipo usa (material, raio de contagem) foram para `DefinicoesTipoDialog`,
  aberto pelo botão «Definições…»; a edição de cada tipo continua a passar
  pelo repositório certo (`AlvRepo`, `FacRepo`, `ContRepo`).
- [x] Desativar/omitir filtros sem significado real em vez de inventar dados
  persistidos.
- [x] Manter `Leitura.Tudo` como uma única travessia — o `BindData` combinado
  lê as quatro listas de uma vez e projecta-as todas antes de construir a
  árvore.
- [x] `Comprimento` e `Altura` de cada tipo de medida propagados até à árvore
  (`Comp.`/`Altura` na grelha), com `NaN` a significar "não se aplica".

### Critérios de conclusão

- [x] Não há abas para trocar — não há DWG a reler ao "mudar de aba".
- [x] O painel único mostra os totais correctos por unidade de cada tipo de
  medida, lado a lado, nunca somados entre si (testado em
  `ResultadosTotaisTests`).
- [x] Não há alteração do esquema XData — os quatro repositórios de leitura e
  escrita (`AlvRepo`, `FacRepo`, `ContRepo`, leitura de lineares) não mudaram.
- [ ] Confirmar no AutoCAD que o botão «Definições…» grava e que o TSKRET, o
  TSKPOLF e o TSKCONTAR lêem os valores gravados (falta a matriz manual da
  Fase 8 — o código está sob compilação e teste unitário, não sob teste de
  interface).

## Fase 7 — Teclado, acessibilidade, DPI e desempenho [~]

Objetivo: concluir os refinamentos identificados em `eval-resultados-compacto.md` e validar uso prolongado no AutoCAD.

### Teclado e acessibilidade

- [x] Enter/Espaço alternam grupos quando a árvore compacta está focada; a navegação vertical fica a cargo do DataGridView.
- [x] `Left/Right` recolhem/expandem; `Home/End` vão ao primeiro/último nó.
- [x] `Enter/Espaço` ativam a ação válida do nó.
- [x] `Ctrl/Shift` mantêm multisseleção.
- [x] `Escape` fecha filtros/menu e devolve foco ao botão que os abriu.
- [~] Definir `AccessibleName`, `AccessibleDescription`, `TabIndex`, `TabStop` e `ToolTipText`.

  > **Nota 2026-09-18.** `AccessibleName`/`AccessibleDescription`/
  > `ToolTipText` já existiam nos cinco controlos principais (árvore,
  > pesquisa, filtros, propriedades, barra de ações), da passagem de
  > 2026-08-29. Faltava `TabIndex`/`TabStop`: os quatro filhos diretos de
  > `_resultadosCompactos` (`barra` de pesquisa/filtros, `_barraResultados`,
  > `_dgvCompacto`, painel `propriedades`) entravam em `Controls.Add` numa
  > ordem que, para `Dock=Top`, é o inverso da ordem visual — o próprio
  > código já comentava isto ("o último a entrar fica mais acima") — e o
  > Tab entrava pela árvore antes da pesquisa. Acrescentado `TabIndex`
  > explícito (0=pesquisa/filtros, 1=barra de ações, 2=árvore,
  > 3=propriedades) e `TabStop = true` em `_barraResultados` (`ToolStrip`
  > nasce fora da ordem de Tab por omissão). `[~]`: só propriedades de
  > inicialização, sem lógica nova, mas não compilado neste sandbox — aguarda
  > build manual. Falta ainda ordenar os controlos DENTRO de cada um destes
  > quatro grupos (por exemplo, os botões da barra de pesquisa) se a matriz
  > manual da Fase 8 revelar que a ordem interna também confunde.
- [x] `PRÓXIMA`, alertas e `Por classificar` têm texto/ícone e não dependem apenas de cor.
- [~] Foco visual uniforme em abas, filtros, árvore, propriedades e ações.

  > **Nota 2026-09-19.** Não há abas (painel único desde a Fase 0/2). Dos
  > quatro grupos restantes, pesquisa/filtros, árvore e barras de ação já
  > tinham o mesmo contorno de acento a 2px via `PaletteTheme.ComFoco`
  > (aplicado a todos pelo `PrepararInteraccao`). Só `PROPRIEDADES`
  > (`MosaicoMetricas`) ficava de fora: é um `Panel` pintado à mão sem
  > `TabStop` nem qualquer manuseamento de teclado — inatingível por Tab e
  > sem foco visual. Acrescentado `TabStop`/`ControlStyles.Selectable`,
  > `IsInputKey`/`OnKeyDown` para `←/→/↑/↓/Home/End` (mover o mosaico "com
  > foco", independente do hover do rato) e `Enter/Espaço` (editar), e o
  > mesmo contorno de acento desenhado à volta do mosaico alvo. Ligado
  > também ao `PaletteTheme.ComFoco` habitual (contorno do controlo inteiro
  > quando tem foco), o que exigiu `base.OnPaint(e)` no fim do `OnPaint`
  > próprio do mosaico — antes ausente, por isso o `Paint` externo nunca
  > disparava. `TabIndex` explícito em `cabecalhoProps`/`_mosaico` (mesma
  > razão do TabIndex dos quatro painéis de RESULTADOS corrigido a
  > 2026-09-18). `[~]`: lógica completa e revista por leitura cuidadosa,
  > mas não compilada neste sandbox — aguarda build manual. Falta ainda
  > confirmar visualmente no AutoCAD (Fase 8) que o contorno não fica
  > confuso sobreposto ao próprio realce do mosaico, e considerar
  > `AccessibleObject` por mosaico se a Fase 8 revelar que o leitor de ecrã
  > não anuncia qual medida está em foco (fora do âmbito desta passagem).

### Dimensão e desempenho

- [ ] Validar larguras de 480, 520, 560 e 760 px e DPI de 100%, 125%, 150% e 200%.
- [ ] Rever `PaletteHost.MinimumSize` após o teste a 480 px.
- [ ] Medir construção do modelo, binding e scroll com pelo menos 461 linhas e com um cenário de milhares de nós.
- [ ] Manter `Rows.AddRange` se o refresh p95 for até 100 ms e o scroll permanecer fluido.
- [ ] Considerar `VirtualMode` apenas se o perfil exceder esse limite ou mostrar pausas/memória excessivas; a eventual mudança fica isolada em `ResultadosTreeControl.cs`.

### Critérios de conclusão

- [ ] Todas as operações principais funcionam sem rato.
- [ ] Não há foco preso num painel fechado.
- [ ] Não há regressão perceptível face aos cerca de 20 ms já medidos para 461 linhas com `Rows.AddRange`.
- [ ] Alto contraste mantém texto, seleção e alertas legíveis.

## Fase 8 — Regressão, documentação e build final [ ]

Objetivo: confirmar paridade funcional e produzir uma única build versionada.

### Verificação automatizada

- [x] Executar `dotnet test Tests/TSKTakeOff.Tests.csproj`.
- [x] Confirmar testes de árvore, filtros, unidades, PISO 1, seleção e IDs.
- [ ] Executar uma única vez `dotnet build -c Release` no fim, porque o build incrementa `Version.build` e sincroniza o deploy.
- [ ] Validar `net48` e `net8.0-windows` quando as referências do AutoCAD 2025 estiverem disponíveis.
- [ ] Executar `python verificar.py` e rever todas as alterações automáticas de versão/deploy.
- [ ] Remover o código morto herdado do painel único (achado 2026-09-17):
      `ResultadosPainel.cs`, `PaletteFachada.cs`, `PaletteLinear.cs`,
      `PaletteContagem.cs` (`FachadaControl`, `LinearControl`,
      `ContagemControl`) e as três instâncias em `PaletteHost.Show` que
      nunca são acrescentadas ao `PaletteSet`. Confirmar antes que nada os
      referencia (`FiltrosPopup` de `PaletteFiltros.cs` não é afetado — é
      usado directamente por `Palette.cs`).

### Matriz manual no AutoCAD

- [ ] Abrir/fechar/ancorar a paleta e trocar de DWG.
- [ ] Criar cada tipo de medição e confirmar seguimento da nova linha.
- [ ] Adicionar/editar/remover vãos e títulos.
- [ ] Reclassificar e editar em lote.
- [ ] Testar pesquisa/filtros durante atualizações.
- [ ] Testar Excel desligado, ligado e com célula selecionada.
- [ ] Exportar XLSX e comparar totais/unidades.
- [ ] Repetir cenários principais por teclado.

### Documentação

- [x] Atualizar `Docs/MANUAL.md` para quatro abas e novo fluxo.
- [x] Atualizar `README.md` onde a paleta/abas estejam descritas.
- [ ] Registar limitações conhecidas e resultados do perfil.

### Critérios de conclusão

- [ ] Zero perda de comandos, campos editáveis ou comportamentos de seleção.
- [ ] Totais do painel, DWG e Excel concordam por unidade.
- [ ] Build, verificador e testes passam.
- [ ] Instalação validada nas versões de AutoCAD disponíveis.

## Riscos a vigiar durante todas as fases

| Risco | Controlo |
|---|---|
| Índices visuais mudam ao filtrar/recolher | Usar ID estável com tipo + handle + índice do filho |
| Grupo tratado como medição | Nós de grupo nunca têm handle nem ações destrutivas |
| Soma de grandezas incompatíveis | Dicionário de totais por unidade; apresentação lado a lado |
| Seleção muda a próxima medição sem intenção | Uma única operação autorizada: `Medir aqui` |
| Alvo da paleta diverge do Excel | Preservar timestamps e `PaletteHost.HandleAlvo` |
| Filtro deixa uma ação apontada para linha invisível | Limpar/desativar seleção e ações quando o nó deixa de estar visível |
| `Limpar tudo` apaga apenas a vista filtrada ou duplica handles | Operar sobre a fonte completa e handles distintos |
| Nova hierarquia muda a folha Excel | Manter `FolhaMedicao.cs` independente da árvore visual |
| Refactor degrada a fluidez | Medir antes/depois; `VirtualMode` condicionado ao perfil |
| Build incrementa versão durante iterações | Testes primeiro; uma única build Release na Fase 8 |

## Registo de progresso

| Data | Fase | Alteração | Verificação |
|---|---|---|---|
| 2026-08-28 | 0 | Variante compacta aprovada e roteiro criado | Revisão de mockup, avaliação e arquitetura atual |
| 2026-08-29 | 1 | `ResultadosModelo.cs` e `ResultadosArvore.cs` criados; 84 testes novos em `ResultadosArvoreTests.cs`, `ResultadosFiltroTests.cs` e `ResultadosTotaisTests.cs` | `dotnet test` 267/267; compilação isolada em `net48`/C# 7.3 sem avisos; nenhum `Autodesk.*` nem `System.Windows.Forms` nos ficheiros novos |
| 2026-08-29 | 2-3 | Build reparado (`CS0841` no lambda da CONFIGURAÇÃO; `PlaceholderText` não existe em net48). Abas reordenadas. Árvore posta em duas colunas com indentação, guias, ícone por tipo, `▸/▾` e seleção/scroll por ID estável; segue a medição nova pelo handle. `PROPRIEDADES` com `Essenciais`/`Tudo` e `Medir aqui` só em nós de artigo. Cores e medidas centralizadas em `PaletteTheme.cs` | Compilação isolada `net48`/AutoCAD 2021 sem erros nem avisos, sem incrementar `Version.build`; `dotnet test` 267/267 |
| 2026-08-29 | 5 | `PROPRIEDADES` passou a escrever no desenho pelos mesmos métodos do `AlvRepo`, com edição em lote sobre a multisseleção da árvore. Ações re-apontadas do `_dgv` para os nós: `HandlesSeleccionados`, `SelectedHandle`, `UltimoHandle`, `DescreverAlvo`, `RemoverParede`, `RemoverVaoSeleccionado`. Barra de Resultados separada da de MEDIR, com menu `Mais`. `Limpar tudo` sobre handles distintos da fonte, com aviso quando há filtro. Etiqueta `PRÓXIMA` no nó de artigo. Grelha larga fora do painel e já não construída | Compilação isolada `net48`/AutoCAD 2021 sem erros nem avisos; `dotnet test` 267/267; `Version.build` intacto. **Falta a matriz manual no AutoCAD** |
| 2026-08-29 | 2, 4, 6, 7 | Fase 2 fechada (`Mais opções`, DWG activo no cabeçalho). Fase 4: popup de filtros com rascunho + `Aplicar`/`Repor`, badge, chips, `Ctrl+F`, debounce de 220 ms e estado vazio que distingue "nada medido" de "o filtro escondeu tudo". Fase 6: `ResultadosAdaptadores.cs` para Materiais, Lineares e Contagens. Fase 7: `←/→/Home/End/Enter/Espaço` na árvore, `Esc` nos filtros, `AccessibleName`/`Description`. Documentação: `Docs/MANUAL.md` e `README.md` | `dotnet test` 290/290 (13 testes novos de adaptadores, 10 de valores de filtro); compilação isolada `net48`/AutoCAD 2021 sem erros nem avisos; `Version.build` intacto. `verificar.py` **não corre** — não há Python instalado nesta máquina |
| 2026-09-05 | 0, 2, 3, 6 | **Painel único.** As quatro abas foram substituídas por uma, e o 2.º nível da árvore passou a ser o TIPO DE MEDIDA (`Alvenaria · m²`, `Camadas · m³`, `Lineares · m`, `Contagens · un.`) — é ele que fixa a unidade. Tema **grafite** aprovado (`index-premium.html`), com `AplicarTema` a descer a árvore de controlos. Colunas `COMP.`/`ALTURA` na árvore, com `NaN` = "não se aplica" e a unidade dentro da célula. Propriedades como **mosaicos de métrica** desenhados. Configuração em **pares** (4 colunas). Botões de MEDIR em 3×2 com desenho próprio | `dotnet test` 304/304 (8 testes novos dos factores, 5 do tipo de medida); compilação `net48` sem erros nem avisos; testado no AutoCAD 2021 a partir da 1.2.6.62 |
| 2026-09-09 | 6 | `DefinicoesTipoDialog.cs`: diálogo modal com os campos que ficaram sem casa no painel único — material e altura de piso dos panos (lidos por `TSKRET`/`TSKPOLF`), nome/categoria/raio/texto das contagens (lido por `TSKCONTAR`). Grava directamente em `FachadaConfig`/`ContagemConfig`, os mesmos estáticos que os comandos já liam — nenhum comando foi alterado. Botão «Definições…» da grelha de MEDIR ligado a ele. Fase 6 fechada, com nota a explicar que "aplicar às quatro abas" foi superada pelo painel único | `dotnet test` 304/304; compilação isolada `net48`/AutoCAD 2021 sem erros nem avisos; build real **1.2.6.66** gerada em `bin\Release\net48\TSKTakeOff.dll`. **Falta confirmar no AutoCAD** que o diálogo grava e que os três comandos leem o valor gravado |
| 2026-09-17 | 3 | `PROPRIEDADES` (o painel real, `MedPanelControl` em `Palette.cs`) ganhou o botão `▼/▶ PROPRIEDADES` que faltava para recolher/expandir o mosaico, igual ao padrão já usado em CONFIGURAÇÃO e Mais opções; `Essenciais`/`Tudo` confirmados correctos por leitura do modelo (ver nota na Fase 3). Corrigido também um `CS0428`/`CS0019` pré-existente em `ResultadosAdaptadores.DeMateriais` — chamava `f.AreaLiquida`/`f.DescontoVaos` como propriedades quando `MedFachada` (em `Medicoes.cs`) só os expõe como métodos com `RegraDesconto`; impedia `dotnet test` de sequer compilar. Achado código morto herdado do "painel único": `ResultadosPainel.cs`, `PaletteFachada.cs`, `PaletteLinear.cs` e `PaletteContagem.cs` continuam no projecto mas nada os instancia a partir de `PaletteHost.Show` — ver nota abaixo | `dotnet test` 304/304 (agora compila; antes desta correcção o projecto de testes nem chegava a compilar); `Palette.cs` revisto por leitura cuidadosa (chavetas/tipos/uso de `PaletteTheme` conferidos contra o resto do ficheiro) mas **não compilado** — sem AutoCAD/WinForms neste sandbox |
| 2026-09-18 | 3, 5, 7 | Fila do agente noturno percorrida quase toda por verificação: seleção/scroll por Id ao expandir/recolher/Atualizar, foco da medição nova sem tocar em `Config`, paridade completa da grelha antiga com `PROPRIEDADES`, `Reclassificar` (Ctrl/Shift), `Remover` (medição/vão/título) e "só `Medir aqui` mexe em `Config`" — todos já correctos no código existente (Fases 2-3/5, 2026-08-29), confirmados por leitura linha a linha sem precisar de código novo (ver notas nos critérios de conclusão das Fases 3 e 5). Único código novo: Fase 7 — `TabIndex`/`TabStop` explícitos nos quatro painéis directos de `_resultadosCompactos` (pesquisa/filtros, barra de ações, árvore, propriedades), porque a ordem de `Controls.Add` era o inverso da ordem visual para `Dock=Top` e o Tab entrava pela árvore antes da pesquisa; `AccessibleName`/`Description`/`ToolTipText` já lá estavam | `dotnet test` 304/304 (sem alterações ao projeto de testes); `Palette.cs` revisto por leitura cuidadosa e por um verificador de chavetas/parênteses próprio — **não compilado**, sem AutoCAD/WinForms neste sandbox |
| 2026-09-19 | 7 | Continuação do PR aberto (`agent/paleta-resultados-2026-09-17`, Passo 0). Fila do agente: "Uniformizar o foco visual" — `PROPRIEDADES` (`MosaicoMetricas`, um `Panel` pintado à mão) era o único dos quatro grupos sem `TabStop` nem foco de teclado; pesquisa/filtros, árvore e ações já tinham o contorno de `PaletteTheme.ComFoco`. Acrescentado `ControlStyles.Selectable`/`TabStop`, `IsInputKey`/`OnKeyDown` (`←/→/↑/↓/Home/End` movem um `_foco` próprio; `Enter/Espaço` editam o mosaico alvo se for editável), o mesmo contorno de acento à volta do mosaico com foco, e ligação ao `ComFoco` habitual (exigiu `base.OnPaint(e)` no fim do `OnPaint`, antes ausente). `TabIndex` explícito em `cabecalhoProps`/`_mosaico` (mesmo motivo do TabIndex de 2026-09-18, um nível mais fundo). Sair da edição por Enter/Escape devolve o foco ao mosaico; por Tab ou clique fora, não (deixa a escolha do utilizador) | `dotnet test` não correu — SDK .NET não instalado neste sandbox e a instalação falhou por política de rede (domínios da Microsoft bloqueados pelo proxy); sem alterações a ficheiros do projeto de testes, sem impacto no risco. `Palette.cs`/`PalettePanelShell.cs` revistos por leitura cuidadosa e por um verificador de chavetas/parênteses (equilibrados) — **não compilados**, sem AutoCAD/WinForms neste sandbox |

### Decisões desta passagem

- **Não há especialidades.** Mede-se arquitetura; o que varia é o tipo de
  medida. O filtro de «especialidade» saiu e o nível da árvore que se chamava
  `Serviço` passou a `Tipo` — o serviço continua a identificar a medição na
  folha e vive nas propriedades. Isto altera a Fase 0, que fixava quatro abas.
- **O comprimento soma-se, a altura não.** Três paredes de 2,80 m não fazem uma
  de 8,40. A altura de um grupo só aparece quando é a mesma em todos os filhos.
  E comprimentos de unidades diferentes — metros de parede e m² em planta de uma
  camada — não se somam entre si.
- **Em fundo escuro, cor herdada é cor perdida.** Um `Button` com `FlatStyle.Flat`
  e `BackColor` definido continua a passar pelo renderizador do Windows, que
  dentro do AutoCAD impunha o cinzento claro do sistema por cima. Os botões de
  acção e os mosaicos passaram a desenhar-se a si próprios (`UserPaint`).
- **Editável só onde há quem grave.** Um campo com realce que deixa escrever e
  não grava é pior do que um bloqueado. Nos Materiais só o artigo se edita; nas
  Contagens e Lineares, nada — os repositórios não têm métodos para isso.
| 2026-08-29 | 2–3 | Aba renomeada para `Arquitetura`; árvore compacta, pesquisa, expansão e propriedades integradas na `MedPanelControl` | `dotnet test` 267/267; build do projeto de testes sem avisos |

### Registo da implementação das Fases 2–3

A implementação segue o mockup `Deploy/MockupPalette/index-resultados-compacto.html`, mas mantém a grelha operacional legada durante esta passagem para evitar duplicar ações destrutivas antes da validação no AutoCAD. A árvore compacta é uma vista adicional alimentada pelo mesmo modelo neutro; a consolidação final da UI ocorrerá após o build e teste manual.

### Nota sobre as Fases 2 e 3

O painel compacto foi construído dentro do `MedPanelControl`, e não nos
`ResultadosTreeControl.cs` / `PropriedadesControl.cs` previstos. Fica assim por
agora; extraí-los é trabalho da Fase 6, quando as outras abas precisarem do
mesmo controlo.

**A grelha larga antiga saiu do painel** (Fase 5 feita). Já não é construída
nem mostrada: a árvore compacta é a única vista de resultados e a edição vive
em `PROPRIEDADES`. O campo `_dgv` e os ajudantes que só ele usava
(`AddCol`, `OnCellEndEdit`, `GarantirEstilos`, `AcrescentarLinhaVao`,
`AcrescentarLinhaTitulo`, `RestaurarSeleccao`, `NovaLinha`, `Col`) ficam
declarados mas inalcançáveis — a limpeza é da Fase 8, para não misturar
remoção de código morto com mudança de comportamento.

Dois gestos da grelha antiga mudaram de forma:

- **`⊕ classificar…`** numa célula → `Mais ▸ Reclassificar…`, que já aceita
  vários handles.
- **`Delete` na coluna Piso/Artigo** para limpar em bloco → escolher as
  medições na árvore e deixar o campo vazio em `PROPRIEDADES`, que chama o
  mesmo `DefinirPisoEmVarias` / `DefinirArtigoEmVarias`.

### Achado 2026-09-17: código morto herdado do painel único

Ao procurar onde vivia o `PROPRIEDADES` recolhível, apareceu um segundo
`ResultadosPainel.cs` — um `UserControl` completo, com árvore, pesquisa,
filtros e propriedades em `DataGridView`, com o SEU PRÓPRIO botão
`▼ PROPRIEDADES` e modos `Essenciais`/`Tudo`. Só é usado por
`FachadaControl` (`PaletteFachada.cs`), `LinearControl` (`PaletteLinear.cs`)
e `ContagemControl` (`PaletteContagem.cs`) — as três abas antigas
(Materiais, Lineares, Contagens) que a decisão "painel único" de
2026-09-05 substituiu.

`PaletteHost.Show` (`Palette.cs`) continua a instanciar as quatro
(`_ctrlFachada`, `_ctrlLinear`, `_ctrl`, `_ctrlContagem`), mas só faz
`_ps.Add("Arquitetura", _ctrl)` — as outras três nunca são acrescentadas ao
`PaletteSet` e por isso nunca aparecem. É o mesmo tipo de resto que o plano
já regista para o `_dgv`/`AddCol`/etc. da grelha larga antiga (ver "Nota
sobre as Fases 2 e 3" acima), só que desta vez são quatro ficheiros
inteiros, não um campo dentro de um. A Fase 3 desta sessão foi feita no
painel REAL (`MedPanelControl`/`Palette.cs`, com
`PalettePanelShell.MosaicoMetricas`) — não em `ResultadosPainel.cs`, que
não é alcançado por nenhum caminho de execução.

Não apagados agora: é limpeza, não a tarefa desta passagem, e apagar
`ResultadosPainel.cs` faria perder o `FiltrosPopup` de `PaletteFiltros.cs`?
Não — confirmado que `Palette.cs` usa `FiltrosPopup` directamente (não é
código morto). Mas `ResultadosPainel.cs`, `PaletteFachada.cs`,
`PaletteLinear.cs` e `PaletteContagem.cs` (as classes `FachadaControl`,
`LinearControl`, `ContagemControl`) são candidatos a remoção na Fase 8,
juntos com o `_dgv` da grelha larga.

### Decisões tomadas dentro da Fase 1

Ficam registadas porque não estavam no plano e mudam o que as fases seguintes
podem assumir:

- **Serviço vazio dá um terceiro grupo residual, `Sem serviço`.** O plano só
  nomeava `Sem piso` e `Por classificar`, mas o `Servico` de uma parede também
  pode vir vazio, e um grupo com rótulo em branco era pior do que um nomeado.
  Os três descem para o fim dos irmãos: o que falta classificar lê-se no fim,
  como na folha.
- **O `Id` de uma medição é só o handle**, sem o caminho do grupo. Reclassificar
  uma parede ou mudá-la de piso não lhe muda a identidade — senão perdia-se a
  seleção e o scroll de quem estava precisamente a editá-la.
- **O alerta de artigo entra por um `Func<string,bool>`.** É assim que o mapa de
  quantidades chega ao modelo sem o `MapaQuantidades.cs` ter de vir para o
  projeto de testes. Nulo significa «sem mapa importado», e aí não ter artigo
  não é alerta — a mesma regra que a grelha atual já usa.
- **Recolher não é filtrar.** Um grupo fechado continua a mostrar o total de
  tudo o que tem dentro; só pesquisa e filtros alteram os totais visíveis.
- **As linhas em branco (`LinhasEmBrancoDepois`) não são nós da árvore.** São
  uma decisão de folha, e ficam como propriedade da medição.
- **A árvore aceita `MedicaoResultado` além de `Parede`.** É o ponto de entrada
  que a Fase 6 vai usar para Materiais, Lineares e Contagens sem duplicar a
  hierarquia; a projeção da alvenaria é apenas o primeiro adaptador.
