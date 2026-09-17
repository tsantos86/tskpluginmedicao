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
      **Feito 2026-09-17**: os modos `Essenciais`/`Tudo` já existiam no
      painel real (`MedPanelControl` em `Palette.cs`) e ficaram confirmados
      correctos por leitura do modelo (`Propriedade.Essencial` por campo,
      sem `if` por tipo de nó — medição, vão, título e grupo mostram o
      conjunto certo dos dois lados). O que faltava era o "recolhível":
      acrescentado o botão `▼/▶ PROPRIEDADES`, igual ao padrão de
      CONFIGURAÇÃO/Mais opções. A perda de valor em edição ao trocar de
      modo está coberta pelo `MosaicoMetricas._editor.Leave → Confirmar()`
      já existente, que dispara antes do `Click` do botão de modo (ordem de
      foco normal do WinForms). De caminho, corrigido um `CS0428`/`CS0019`
      pré-existente em `ResultadosAdaptadores.DeMateriais` que impedia
      `dotnet test` de compilar (`f.AreaLiquida`/`f.DescontoVaos` chamados
      como propriedades quando são métodos com `RegraDesconto`). Verificado
      por `dotnet test` (304/304, agora consegue compilar) e leitura
      cuidadosa de `Palette.cs` — **implementado, aguarda build manual**
      (sem AutoCAD/WinForms neste sandbox). Detalhe completo e achado de
      código morto (`ResultadosPainel.cs` e as três abas antigas, que nunca
      são acrescentadas ao `PaletteSet`) em
      `Docs/PLANO_PALETA_RESULTADOS_COMPACTA.md`, Fase 3.
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
