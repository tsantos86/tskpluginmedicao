# Plano de implementação — Paleta de resultados compacta

Referência visual aprovada: `Deploy/MockupPalette/index-resultados-compacto.html`

Última atualização: 2026-08-28

Estado geral: **planeamento concluído; implementação por iniciar**

## Como acompanhar

- `[ ]` pendente
- `[~]` em curso
- `[x]` concluído
- `[!]` bloqueado ou a aguardar decisão

| Fase | Entrega | Estado |
|---|---|---|
| 0 | Decisões e âmbito | [x] |
| 1 | Modelo de resultados e testes | [ ] |
| 2 | Estrutura visual compacta de Alvenaria | [ ] |
| 3 | Árvore de resultados e propriedades | [ ] |
| 4 | Pesquisa, filtros e totais visíveis | [ ] |
| 5 | Edição, ações e `Medir aqui` | [ ] |
| 6 | Aplicação às restantes abas | [ ] |
| 7 | Teclado, acessibilidade, DPI e desempenho | [ ] |
| 8 | Regressão, documentação e build final | [ ] |

## Fase 0 — Decisões e âmbito [x]

### Direção aprovada

- O HTML compacto é a referência para densidade, cores, barras, pesquisa, filtros, árvore, propriedades e feedback.
- A revisão mais recente de `Deploy/MockupPalette/brief.md` prevalece onde diverge do HTML:
  - a árvore terá apenas `Estrutura / elemento` e `Quantidade`;
  - comprimento, altura e restantes dimensões ficam em `PROPRIEDADES`;
  - selecionar serve para consultar/editar; só `Medir aqui` altera a próxima medição;
  - `Medir aqui` aparece apenas em nós de artigo;
  - os totais dos grupos são sempre calculados a partir dos filhos.
- Manter as quatro abas nativas do `PaletteSet`, sem introduzir um segundo `TabControl`. A ordem passa a `Alvenaria`, `Materiais`, `Lineares`, `Contagens`.
- Implementar primeiro Alvenaria e estabilizar o padrão antes de migrar as restantes abas.
- Hierarquia visual de Alvenaria: `Pavimentos > Piso > Serviço > Artigo > Medição > Vão/Título`.
- A ordem visual não altera a ordem da folha Excel, que continua a ser definida por `FolhaMedicao.cs`.
- `Especialidade` será representada pelo `Serviço` existente em Alvenaria. Não será acrescentado um campo novo ao DWG/XData nesta implementação.
- `Sem piso` e `Por classificar` serão grupos explícitos.
- Filtros afetam apenas a vista. Não alteram o DWG, a exportação nem os dados enviados para o Excel.
- A árvore e os resumos filtrados mostram os totais visíveis e a indicação `n visíveis de N`.
- Manter `Rows.AddRange`, `SuspendLayout` e duplo buffer. `VirtualMode` só será ativado se a medição real justificar a complexidade.
- Não eliminar campos, comandos, edição em lote, seleção Excel/paleta, seguimento da medição nova ou restauração de seleção/scroll.

### Fora do âmbito desta passagem

- Alterar o formato persistido no DWG ou a ordem do Excel.
- Criar uma árvore única que misture as quatro abas.
- Reescrever os comandos de medição ou os repositórios AutoCAD sem necessidade funcional.
- Introduzir um tema escuro novo; a primeira implementação segue a variante clara aprovada, com cores centralizadas e suporte para alto contraste.

## Fase 1 — Modelo de resultados e testes [ ]

Objetivo: retirar da interface a lógica de hierarquia, identidade, filtros e agregação, para poder testá-la sem AutoCAD nem WinForms.

### Trabalho

- [ ] Criar `ResultadosModelo.cs` com modelos C# 7.3 para:
  - identidade estável do nó;
  - tipo, profundidade, pai e estado expandido;
  - origem por `handle` e índice de vão/título;
  - quantidade por unidade;
  - propriedades essenciais e completas;
  - estado de pesquisa e filtros.
- [ ] Criar `ResultadosArvore.cs` com:
  - projeção de `Parede` e `Vao` para a hierarquia aprovada;
  - achatamento dos nós visíveis;
  - agregação separada de `m²`, `m³`, `m` e `un.`;
  - grupos `Sem piso` e `Por classificar`;
  - pesquisa normalizada em serviço, artigo, piso, bloco, alçado, nota, handle e designação dos vãos;
  - propagação dos antepassados de cada resultado encontrado.
- [ ] Adicionar os ficheiros puros a `Tests/TSKTakeOff.Tests.csproj` por `Compile Include`.
- [ ] Criar `Tests/ResultadosArvoreTests.cs`, `Tests/ResultadosFiltroTests.cs` e `Tests/ResultadosTotaisTests.cs`.

### Critérios de conclusão

- [ ] Uma fixture com `PISO 0` e `PISO 1` produz ambos os pisos a partir de dados reais.
- [ ] O total do topo é a soma dos filhos por unidade; unidades incompatíveis nunca são somadas.
- [ ] O comprimento de uma parede não entra no total em metros de artigos lineares.
- [ ] Pesquisa e filtros mantêm visíveis todos os antepassados necessários.
- [ ] IDs permanecem estáveis após recolher, filtrar e reconstruir a árvore.
- [ ] Os novos ficheiros não usam `Autodesk.*` nem `System.Windows.Forms`.
- [ ] `dotnet test Tests/TSKTakeOff.Tests.csproj` passa.

## Fase 2 — Estrutura visual compacta de Alvenaria [ ]

Objetivo: aplicar a composição aprovada sem mudar ainda as regras operacionais.

### Ficheiros previstos

- Novo: `PalettePanelShell.cs`
- Novo: `PaletteTheme.cs`
- Alterar: `Palette.cs`
- Alterar apenas se faltarem símbolos: `IconFactory.cs`

### Trabalho

- [ ] Reordenar as abas nativas para `Alvenaria`, `Materiais`, `Lineares`, `Contagens`.
- [ ] Criar o cabeçalho compacto com DWG ativo e resumo da próxima medição.
- [ ] Fazer `CONFIGURAÇÃO` iniciar recolhida e manter o resumo sincronizado com `Config`.
- [ ] Manter no corpo principal Artigo, Serviço, Bloco/etiqueta, Piso/Cor, Alçado/zona, Altura, Espessura e Regra de vãos.
- [ ] Colocar `Texto do título`, `Layer` manual e layer efetiva em `Mais opções`.
- [ ] Criar a barra `MEDIR` com as seis ações existentes:
  - Retângulo;
  - Polyline;
  - Área;
  - Área da seleção;
  - Medir seleção;
  - Adicionar vão.
- [ ] Criar a barra de Resultados com Excel ao Vivo, Exportar XLSX, Atualizar e `Mais`.
- [ ] Eliminar a duplicação visual de `Atualizar`; haverá uma ação inequívoca.
- [ ] Centralizar cores, fontes, margens, alturas e estados de foco em `PaletteTheme.cs`.

### Critérios de conclusão

- [ ] Todos os campos e seis comandos atuais continuam acessíveis.
- [ ] Recolher/expandir Configuração não perde valores nem altera `Config`.
- [ ] O painel permanece legível entre 480 e 760 px.
- [ ] A alteração de layout não muda XData, comandos ou resultados do Excel.

## Fase 3 — Árvore de resultados e propriedades [ ]

Objetivo: substituir a grelha larga por uma árvore achatada compacta sem perder identidade, seleção ou edição futura.

### Ficheiros previstos

- Novo: `ResultadosTreeControl.cs`
- Novo: `PropriedadesControl.cs`
- Alterar: `Palette.cs`
- Usar: `ResultadosModelo.cs`, `ResultadosArvore.cs`

### Trabalho

- [ ] Criar um `DataGridView` com duas colunas: `Estrutura / elemento` e `Quantidade`.
- [ ] Desenhar indentação, guias, ícones e comandos de expandir/recolher por tipo de nó.
- [ ] Mostrar as quantidades com unidade na própria célula.
- [ ] Nos grupos com unidades diferentes, mostrar valores lado a lado, por exemplo `62,93 m² · 36,90 m · 2 un.`.
- [ ] Mostrar vãos como deduções negativas conforme a regra em vigor.
- [ ] Manter títulos/separadores como nós identificáveis, sem lhes atribuir um total falso.
- [ ] Restaurar seleção e scroll por ID estável, não por índice visual da linha.
- [ ] Seguir a medição acabada de criar pelo respetivo handle.
- [ ] Nós de grupo não transportam handle e não podem ser removidos, editados ou reclassificados.
- [ ] Criar `PROPRIEDADES` recolhível com modos `Essenciais` e `Tudo`.
- [ ] Mostrar propriedades de grupo, medição, vão e título de forma coerente; a edição entra na Fase 5.

### Critérios de conclusão

- [ ] Expandir/recolher e atualizar preservam seleção e posição sempre que o nó ainda existe.
- [ ] Uma seleção escondida por filtros não fica como alvo invisível de ações; a seleção visual e as ações são limpas/desativadas.
- [ ] A nova medição recebe foco sem mudar a configuração da próxima medição.
- [ ] Alertas de artigo desconhecido, por classificar e vãos excessivos continuam visíveis sem depender só da cor.
- [ ] A árvore apresenta apenas quantidades; todas as dimensões vivem em Propriedades.

## Fase 4 — Pesquisa, filtros e totais visíveis [ ]

Objetivo: permitir localizar e conferir resultados sem modificar os dados do desenho.

### Trabalho

- [ ] Adicionar pesquisa incremental com `Ctrl+F` e pequeno debounce.
- [ ] Adicionar filtros com estado de rascunho e botão `Aplicar`:
  - Pavimento;
  - Serviço;
  - Artigo;
  - Tipo/unidade;
  - Estado (`Por classificar`, `Com vãos`, `Com alerta`).
- [ ] Mostrar badge com o número de filtros aplicados, até dois chips e ação `Limpar`.
- [ ] Implementar `Repor` apenas para o rascunho e `Limpar` para pesquisa + filtros aplicados.
- [ ] Durante pesquisa/filtro, mostrar os antepassados dos resultados e ignorar temporariamente recolhas que os esconderiam.
- [ ] Calcular contagem e totais sobre os resultados visíveis, mostrando também o total global (`n visíveis de N`).
- [ ] Mostrar um estado vazio claro quando não há correspondências.

### Critérios de conclusão

- [ ] Combinações dos cinco filtros são determinísticas e cobertas por testes.
- [ ] Limpar repõe exatamente a vista anterior, sem alterar expansão persistida em memória.
- [ ] Filtrar nunca muda `Config`, o DWG, o Excel ou a seleção do Excel.
- [ ] A contagem apresentada é calculada; não existe texto fixo herdado do mockup.

## Fase 5 — Edição, ações e `Medir aqui` [ ]

Objetivo: recuperar no novo painel compacto toda a capacidade operacional da grelha atual.

### Edição em Propriedades

- [ ] Parede: editar Serviço, Artigo, Piso, Altura, Largura e Espessura.
- [ ] Vão: editar Designação, Largura e Altura.
- [ ] Título: editar o código sem truncar/perder a descrição persistida.
- [ ] Manter Comprimento como valor geométrico apenas de leitura.
- [ ] Manter Área bruta, desconto, Área líquida, Quantidade, Volume e Pré-aros como cálculos de leitura.
- [ ] Preservar multisseleção e operações em lote para Artigo, Piso e dimensões já suportadas.
- [ ] Validar valores antes de chamar os métodos existentes de `AlvRepo.cs`.

### Ações

- [ ] Barra principal: Excel ao Vivo, Exportar XLSX e Atualizar.
- [ ] Menu `Mais`: Remover, Linha branca, Artigo, Reclassificar e Limpar tudo.
- [ ] `Limpar tudo` opera sobre handles distintos da fonte completa, nunca sobre as linhas visíveis/filtradas.
- [ ] Preservar a prioridade atual do alvo contextual: última interação entre seleção da paleta e célula do Excel; sem ambas, última medição.
- [ ] Selecionar um resultado altera apenas Propriedades e o alvo de ações contextuais; não altera a próxima medição.
- [ ] `Medir aqui` aparece apenas num nó de Artigo e copia explicitamente Piso + Serviço + Artigo para `Config`, mantendo as dimensões atuais.
- [ ] Marcar o artigo resultante com `PRÓXIMA` e atualizar os dois resumos da próxima medição.

### Critérios de conclusão

- [ ] Existe paridade entre todos os campos/comandos atuais e o respetivo local novo.
- [ ] Reclassificar continua a aceitar Ctrl/Shift e vários handles.
- [ ] Remover distingue corretamente medição, vão e título.
- [ ] Atualizar continua a forçar a escrita imediata no Excel quando este está ligado.
- [ ] Apenas `Medir aqui` altera a próxima medição.

## Fase 6 — Aplicação às restantes abas [ ]

Objetivo: reutilizar o padrão estabilizado sem criar uma árvore global nem uma nova leitura do DWG.

### Ficheiros previstos

- Alterar: `PaletteFachada.cs`
- Alterar: `PaletteLinear.cs`
- Alterar: `PaletteContagem.cs`
- Alterar apenas para distribuição: `Palette.cs`

### Trabalho

- [ ] Criar adaptadores de Materiais, Lineares e Contagens para os modelos comuns de resultado.
- [ ] Reutilizar cabeçalho, secções, pesquisa, filtros, árvore, propriedades e estados de foco.
- [ ] Manter configuração, ações, edição e remoção específicas de cada tipo.
- [ ] Desativar/omitir filtros sem significado real em vez de inventar dados persistidos.
- [ ] Guardar filtros, expansão e seleção independentemente por aba.
- [ ] Manter `Leitura.Tudo` como uma única travessia e os quatro `BindData` existentes.

### Critérios de conclusão

- [ ] Mudar de aba não relê o DWG.
- [ ] As quatro abas mantêm comandos e totais corretos nas respetivas unidades.
- [ ] Estado visual de uma aba não contamina outra.
- [ ] Não há alteração do esquema XData.

## Fase 7 — Teclado, acessibilidade, DPI e desempenho [ ]

Objetivo: concluir os refinamentos identificados em `eval-resultados-compacto.md` e validar uso prolongado no AutoCAD.

### Teclado e acessibilidade

- [ ] Setas verticais navegam pelos nós visíveis.
- [ ] `Left/Right` recolhem/expandem; `Home/End` vão ao primeiro/último nó.
- [ ] `Enter/Espaço` ativam a ação válida do nó.
- [ ] `Ctrl/Shift` mantêm multisseleção.
- [ ] `Escape` fecha filtros/menu e devolve foco ao botão que os abriu.
- [ ] Definir `AccessibleName`, `AccessibleDescription`, `TabIndex`, `TabStop` e `ToolTipText`.
- [ ] `PRÓXIMA`, alertas e `Por classificar` têm texto/ícone e não dependem apenas de cor.
- [ ] Foco visual uniforme em abas, filtros, árvore, propriedades e ações.

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

- [ ] Executar `dotnet test Tests/TSKTakeOff.Tests.csproj`.
- [ ] Confirmar testes de árvore, filtros, unidades, PISO 1, seleção e IDs.
- [ ] Executar uma única vez `dotnet build -c Release` no fim, porque o build incrementa `Version.build` e sincroniza o deploy.
- [ ] Validar `net48` e `net8.0-windows` quando as referências do AutoCAD 2025 estiverem disponíveis.
- [ ] Executar `python verificar.py` e rever todas as alterações automáticas de versão/deploy.

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

- [ ] Atualizar `Docs/MANUAL.md` para quatro abas e novo fluxo.
- [ ] Atualizar `README.md` onde a paleta/abas estejam descritas.
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
