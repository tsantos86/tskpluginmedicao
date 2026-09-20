# Tarefas para o agente noturno

Fila de trabalho para a rotina automática que roda todas as manhãs e continua
a implementação da paleta de resultados compacta.

## Como usar

- Escreva aqui, um item por linha com `- [ ]`, o que quer que o agente
  implemente na próxima execução. Pode escrever mais do que uma linha por
  item se precisar de dar contexto.
- A cada execução, o agente pega o **primeiro** item `- [ ]` desta lista,
  implementa, e marca `- [x]` quando terminar, com uma nota curta de como
  verificou (testes, ou "implementado, aguarda build manual" quando o
  sandbox não conseguiu compilar).
- Se a lista não tiver nenhum item `[ ]`, o agente escolhe sozinho o próximo
  passo pendente em `Docs/PLANO_PALETA_RESULTADOS_COMPACTA.md`, pela mesma
  ordem de prioridade de sempre (Fase 3 → Fase 5 → Fase 7).
- Pode adicionar quantos itens quiser de uma vez; ficam na fila para as
  próximas execuções, um por dia.
- Itens que dependem de abrir o AutoCAD (matriz manual da Fase 8, DPI real,
  perfil de desempenho ao vivo) não devem entrar aqui — o agente não
  consegue verificá-los no sandbox.

## Fila

- [x] Fase 3 — Terminar `PROPRIEDADES` recolhível com os modos `Essenciais` e
      `Tudo` (item ainda `[~]` no plano): confirmar que os dois modos mostram
      o conjunto certo de campos por tipo de nó (medição, vão, título, grupo)
      e que a alternância entre modos não perde o valor em edição.
      Verificado por leitura cuidadosa do código (sem AutoCAD): os campos
      `Essencial` de `PropriedadesDaParede`, `DeducaoDoVao`, `TituloDaMarca`,
      `PropriedadesDosGrupos` (`ResultadosArvore.cs`) e dos três adaptadores
      (`ResultadosAdaptadores.cs`) já cobriam corretamente medição, vão,
      título e grupo. Corrigido o único problema real encontrado: o botão
      Essenciais/Tudo (`Palette.cs`) chama `AtualizarPropriedadesCompactas`
      → `MosaicoMetricas.Definir`, que descartava (`Cancelar()`) uma edição
      em curso no mosaico em vez de a gravar; passou a chamar `Confirmar()`
      primeiro (`PalettePanelShell.cs`). Durante a verificação apareceu um
      bug de build não relacionado, mais grave: `ResultadosAdaptadores.DeMateriais`
      ainda chamava `MedFachada.AreaLiquida`/`DescontoVaos` sem o parâmetro
      `RegraDesconto` que esses métodos exigem desde a criação do ficheiro —
      o projeto de testes nunca tinha sido compilado com um SDK real (não
      havia `dotnet` neste ambiente) e isto também partiria o build `net48`
      real. Corrigido nos três sítios (`ResultadosAdaptadores.cs`,
      `Palette.cs`, `PaletteFachada.cs`) e nos 9 testes afetados. Verificado
      com `dotnet test` (304/304, SDK instalado nesta sessão via apt) — a
      parte WinForms (`Palette.cs`, `PalettePanelShell.cs`, `PaletteFachada.cs`)
      não foi compilada em net48/AutoCAD e precisa de build manual.
- [ ] Fase 3 — Garantir que expandir/recolher grupos e `Atualizar` preservam
      a seleção e a posição de scroll sempre que o nó selecionado ainda
      existir depois da reconstrução da árvore.
- [ ] Fase 3 — Garantir que a medição acabada de criar recebe o foco na
      árvore sem alterar a configuração da próxima medição (`Config`).
- [ ] Fase 5 — Rever paridade completa entre os campos/comandos da grelha
      antiga (já removida do painel) e o local novo em `PROPRIEDADES`/barras
      de ação; fechar qualquer lacuna encontrada.
- [ ] Fase 5 — Terminar `Reclassificar` (item `[~]`): confirmar que aceita
      `Ctrl`/`Shift` para selecionar vários handles de uma vez e que aplica
      a reclassificação a todos eles.
- [ ] Fase 5 — Terminar `Remover` (item `[~]`): confirmar que distingue
      corretamente entre remover uma medição, um vão e um título, sem
      confundir o alvo quando a seleção mistura tipos.
- [ ] Fase 5 — Confirmar/fechar que **só** `Medir aqui` altera a próxima
      medição (item `[~]`): rever todos os outros pontos de seleção/edição
      para garantir que nenhum deles muda `Config` por engano.
- [ ] Fase 7 — Definir `AccessibleName`, `AccessibleDescription`, `TabIndex`,
      `TabStop` e `ToolTipText` nos controlos principais do painel (árvore,
      pesquisa, filtros, propriedades, barras de ação).
- [ ] Fase 7 — Uniformizar o foco visual (contorno/realce ao navegar por
      teclado) entre abas, filtros, árvore, propriedades e barras de ação.
