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
- [x] Fase 3 — Garantir que expandir/recolher grupos e `Atualizar` preservam
      a seleção e a posição de scroll sempre que o nó selecionado ainda
      existir depois da reconstrução da árvore.
      **Feito 2026-09-18**: confirmado por leitura cuidadosa, sem necessidade
      de código novo. `AtualizarResultadosCompactos` (chamada por TODOS os
      gatilhos — duplo-clique, `Enter/Espaço/Left/Right`, `AbrirFiltros` e
      `BindData`/`Atualizar`, um único caminho) captura o Id seleccionado e o
      Id do nó no topo do scroll ANTES de limpar as linhas, e repõe os dois
      por Id (`IndiceDe` + `ReporSeleccaoDaArvore`) depois de reconstruir.
      Os Ids são determinísticos (handle para medições, caminho de rótulos
      para grupos — `ResultadosArvore.Grupo`), confirmado pelos testes
      `Os_Ids_sao_estaveis_ao_reconstruir_a_arvore` e
      `Os_Ids_sobrevivem_a_recolher_e_filtrar`. **Não implica código WinForms
      novo — não há nada para compilar além do que já existe.**
- [x] Fase 3 — Garantir que a medição acabada de criar recebe o foco na
      árvore sem alterar a configuração da próxima medição (`Config`).
      **Feito 2026-09-18**: confirmado por leitura cuidadosa. O mecanismo
      (`PaletteHost.MedicaoNova`/`RegistarMedicao`/`LimparMedicaoNova`) é
      totalmente independente de `Config` — só marca o Id a seguir na árvore
      (`"M:" + handle`) e consome-se uma vez. Revistos TODOS os pontos que
      escrevem em `Config` a partir da árvore: o único é `MedirAqui()`
      (comentário no próprio código: "É a ÚNICA operação da árvore que mexe
      na Config"). Nenhuma alteração necessária.
- [x] Fase 5 — Rever paridade completa entre os campos/comandos da grelha
      antiga (já removida do painel) e o local novo em `PROPRIEDADES`/barras
      de ação; fechar qualquer lacuna encontrada.
      **Feito 2026-09-18**: comparadas as colunas do `_dgv` morto
      (`servico, artigo, alcado, bloco, piso, comp, alt, larg, esp, bruta,
      vaos, liq, qtd, vol, aroUn, aroMl`) com as `Propriedade` de
      `ResultadosArvore.PropriedadesDaParede`/vão/título. Sem lacunas: `num`
      e `sep` eram artefactos da grelha (número de linha e uma coluna nunca
      lida por `Col(...)`, nem sequer dentro do próprio código morto); `comp`
      numa linha de parede estava marcada editável na UI mas `OnCellEndEdit`
      nunca a gravava (só `alt/larg/esp` — o novo painel corrige isto ao
      deixar Comprimento só de leitura, como o plano já decidira). Título
      ganhou mesmo uma capacidade nova (editar a Descrição, não só o
      Código). Nenhuma lacuna a fechar.
- [x] Fase 5 — Terminar `Reclassificar` (item `[~]`): confirmar que aceita
      `Ctrl`/`Shift` para selecionar vários handles de uma vez e que aplica
      a reclassificação a todos eles.
      **Feito 2026-09-18**: confirmado por leitura. `_dgvCompacto` tem
      `MultiSelect = true` e `SelectionMode = FullRowSelect` (nativo do
      WinForms — Ctrl/Shift já funcionam sem código adicional), e
      `Reclassificar()` → `HandlesSeleccionados()` agrega
      `SelectedCells`/`SelectedRows`, ignora grupos e títulos, e junta vãos à
      parede-mãe sem repetir handles. `ClassificarMedicoes` aplica a todos
      via `AlvRepo.DefinirArtigoEmVarias`. O plano mantinha `[~]` por
      cautela; não havia nada por terminar.
- [x] Fase 5 — Terminar `Remover` (item `[~]`): confirmar que distingue
      corretamente entre remover uma medição, um vão e um título, sem
      confundir o alvo quando a seleção mistura tipos.
      **Feito 2026-09-18**: confirmado por leitura. `RemoverParede()` (o
      único ponto de entrada do botão «Remover») despacha por `NoSeleccionado()`:
      grupo → recusa com mensagem; título → `AlvRepo.AlternarTitulo` (só o
      título sai, a medição fica); vão → `RemoverVaoSeleccionado()`; caso
      contrário → apaga a medição. Opera sobre um único nó
      (`NoSeleccionado()`), não sobre multisselecção — por isso não há
      "seleção mista" possível de confundir: o Remover nunca olhou para
      `HandlesSeleccionados()`.
- [x] Fase 5 — Confirmar/fechar que **só** `Medir aqui` altera a próxima
      medição (item `[~]`): rever todos os outros pontos de seleção/edição
      para garantir que nenhum deles muda `Config` por engano.
      **Feito 2026-09-18**: ver nota da Fase 3 acima (mesma verificação,
      revistos todos os `Config.` de `Palette.cs`) — confirmado que só
      `MedirAqui()` escreve `Config.Piso/Servico/Artigo`.
- [x] Fase 7 — Definir `AccessibleName`, `AccessibleDescription`, `TabIndex`,
      `TabStop` e `ToolTipText` nos controlos principais do painel (árvore,
      pesquisa, filtros, propriedades, barras de ação).
      **Feito 2026-09-18**: `AccessibleName`/`AccessibleDescription`/
      `ToolTipText` já existiam nos cinco controlos principais (herdados da
      passagem de 2026-08-29). Faltava `TabIndex`/`TabStop`: os quatro
      filhos directos de `_resultadosCompactos` (`barra` de
      pesquisa/filtros, `_barraResultados`, `_dgvCompacto` e o painel
      `propriedades`) eram acrescentados por `Controls.Add` numa ordem que,
      para `Dock=Top`, é o INVERSO da ordem visual (o próprio código já
      comentava isto: "o último a entrar fica mais acima") — o Tab entrava
      pela árvore antes da pesquisa. Acrescentado `TabIndex` explícito
      (0=pesquisa/filtros, 1=barra de ações, 2=árvore, 3=propriedades) e
      `TabStop = true` em `_barraResultados` (`ToolStrip` nasce fora da
      ordem de Tab por omissão). **Implementado, aguarda build manual** —
      só toca em propriedades de inicialização (`TabIndex`/`TabStop`), sem
      lógica nova; revisto por leitura contra o resto do ficheiro.
- [x] Fase 7 — Uniformizar o foco visual (contorno/realce ao navegar por
      teclado) entre abas, filtros, árvore, propriedades e barras de ação.
      Não iniciado nesta sessão (2026-09-18); fica para a próxima.
      **Feito 2026-09-19**: não há "abas" (painel único, ver nota da Fase 2);
      dos quatro grupos que restavam — pesquisa/filtros, árvore, ações — já
      tinham o mesmo contorno de acento a 2px (`PaletteTheme.ComFoco`,
      aplicado a todos pelo `PrepararInteraccao`); só `PROPRIEDADES`
      (`MosaicoMetricas` em `PalettePanelShell.cs`) ficava de fora — é um
      `Panel` pintado à mão (mosaicos desenhados, não controlos), sem
      `TabStop` nem qualquer manuseamento de teclado, portanto inatingível
      por Tab e sem foco visual nenhum. Corrigido: `MosaicoMetricas` ganhou
      `TabStop`/`ControlStyles.Selectable` (o truque habitual para um Panel
      aceitar foco), `IsInputKey`/`OnKeyDown` para `←/→/↑/↓/Home/End` (mover
      o mosaico "com foco") e `Enter/Espaço` (editar o mosaico com foco se
      for editável), um `_foco` próprio (independente do `_sobre` do rato) e
      o MESMO contorno de acento desenhado à volta do mosaico alvo em
      `OnPaint`. Também ligado ao `PaletteTheme.ComFoco` da forma habitual
      (contorno do controlo inteiro quando ele, e não um filho, tem foco;
      exige `base.OnPaint(e)` no fim do `OnPaint` próprio, que faltava).
      `TabIndex` explícito em `cabecalhoProps`/`_mosaico` dentro de
      `propriedades` (mesma razão do TabIndex dos quatro painéis de
      RESULTADOS já corrigido a 2026-09-18: sem isso os dois nascem com
      TabIndex empatado a 0). Ao sair da edição por Enter/Escape (não por
      Tab ou clique fora, para não prender quem tenta sair), o foco volta ao
      mosaico. **Implementado, aguarda build manual** — só toca em
      `Palette.cs`/`PalettePanelShell.cs` (WinForms, sem AutoCAD neste
      sandbox); revisto por leitura cuidadosa contra o resto do ficheiro e
      por um verificador de chavetas/parênteses (equilibrados nos dois
      ficheiros). `dotnet test` não correu nesta sessão — o SDK .NET não
      está instalado neste sandbox e a instalação falhou por política de
      rede (domínios da Microsoft bloqueados); sem impacto no risco, porque
      nenhum ficheiro do projeto de testes foi tocado.
- [ ] Fase 8 — Remover o código morto herdado do painel único: `ResultadosPainel.cs`,
      `PaletteFachada.cs`, `PaletteLinear.cs`, `PaletteContagem.cs`
      (classes `FachadaControl`, `LinearControl`, `ContagemControl`) e as
      três instâncias correspondentes em `PaletteHost.Show`, que nunca são
      acrescentadas ao `PaletteSet` (achado registado em
      `Docs/PLANO_PALETA_RESULTADOS_COMPACTA.md`, Fase 8).
      IMPORTANTE — confirmar antes de apagar: procurar por referências reais
      a cada classe/ficheiro em todo o projeto (não só em `PaletteHost.Show`;
      confirmar também que não são usados por testes, por `Commands.cs`, nem
      por outro código). `FiltrosPopup` de `PaletteFiltros.cs` NÃO é afetado
      — é usado directamente por `Palette.cs`, não faz parte deste código
      morto. Se ao investigar encontrares qualquer referência viva a um
      destes ficheiros/classes, não apagues esse — documenta o achado e
      apaga só o que estiver confirmado morto. Depois de remover, correr
      `dotnet test Tests/TSKTakeOff.Tests.csproj` (os ficheiros removidos não
      devem fazer parte do projeto de testes) e confirmar que nenhum
      `Compile Include`/referência ao ficheiro removido sobra em
      `TSKTakeOff.csproj`.

      ATENÇÃO (2026-09-19): `ResultadosPainel.cs` não é só código morto —
      tinha a implementação CORRETA das colunas `Comp.`/`Altura` da árvore,
      que faltavam no painel real (`_dgvCompacto` em `Palette.cs`) até hoje
      (ver nota em `Docs/PLANO_PALETA_RESULTADOS_COMPACTA.md`, Fase 3). Antes
      de apagar qualquer um destes ficheiros, compara-o com cuidado ao
      equivalente já ligado (`Palette.cs`/`MedPanelControl`) — se houver
      OUTRA diferença de comportamento ou de campo entre os dois, corrige o
      painel real primeiro (como uma tarefa própria, não como parte desta
      limpeza) e só depois remove o ficheiro morto.

- [ ] Documentação — Checklist de prontidão comercial / Autodesk Store
      (`Docs/CHECKLIST_COMERCIAL.md`, novo ficheiro).

      Contexto (conversa de 2026-09-21 com o utilizador, "o que falta para
      este produto ser comercial ou ir para a Autodesk Store"): pesquisa
      feita nas diretrizes reais do publisher da Autodesk
      (aps.autodesk.com/marketplace/publisher-center/autocad-publisher-guidelines)
      cruzada com o estado do projeto. Cria um ficheiro novo que organize
      isto como checklist rastreável (não só prosa), com uma linha por
      item: descrição, estado (✅ feito / ⚠️ por confirmar / ❌ falta),
      e onde no projeto isso se vê (ficheiro/pasta).

      Itens a incluir, com o estado já apurado nessa conversa (ATUALIZA se
      encontrares prova em contrário no código — não assumas, confirma):
      - Ribbon por CUIX parcial (exigido pela Autodesk) — ❌ falta;
        `README.md` diz explicitamente que a ribbon é criada por código,
        sem CUIX. Não tentes resolver isto agora, só documentar o conflito.
      - Autoloader (`PackageContents.xml`, pasta `ApplicationPlugins`) —
        ✅ já existe em `Deploy/TSKTakeOff.bundle/`.
      - `SeriesMax` no `RuntimeRequirements` do `PackageContents.xml` para
        AutoCAD 2025+ — ⚠️ POR CONFIRMAR: abre
        `Deploy/TSKTakeOff.bundle/PackageContents.xml` e verifica se existe
        de facto; regista o que encontrares (presente/ausente, e o valor).
      - Instalador com privilégio de administrador — ⚠️ POR CONFIRMAR: abre
        `Deploy/Installer/TSKTakeOff.iss` e procura `PrivilegesRequired`;
        regista o valor encontrado.
      - Licenciamento com trial funcional — ✅ já existe (Supabase, 15 dias
        — ver `Supabase/LICENCIAMENTO.md`, `Licenca.cs`).
      - Assinatura digital do `.dll`/`.msi` (SignTool + certificado
        comprado de uma CA) — ❌ falta; não há nenhum passo de assinatura
        no `TSKTakeOff.csproj` nem nos scripts de `Deploy/`. Isto é uma
        despesa/decisão do utilizador, não algo para o agente resolver.
      - Estabilidade validada (Fase 8, "matriz manual no AutoCAD") — ❌
        nunca foi feita; ver `Docs/PLANO_PALETA_RESULTADOS_COMPACTA.md`,
        secção "Matriz manual no AutoCAD" — todos os itens por marcar.
      - `net8.0-windows` (AutoCAD 2025+) nunca validado — ❌; mesmo
        documento, "Validar net48 e net8.0-windows quando as referências
        do AutoCAD 2025 estiverem disponíveis" continua por marcar.
      - Emissão de licença é manual (INSERT SQL no Supabase por cliente) —
        ❌ não escala; ver `Supabase/LICENCIAMENTO.md`, secção "Emitir uma
        licença".
      - EULA / termos de uso — ❌ não existe nenhum no repositório.
      - Política de privacidade — ❌ não existe, apesar de `Telemetria.cs`
        recolher dados de sessão (RGPD, dado o mercado PT/UE).
      - Canal de suporte — ⚠️ é um Gmail pessoal
        (`tsantos.fullstack@gmail.com`), citado em `Licenca.cs`/diálogos.
      - Mecanismo de auto-update — ❌ não encontrado; confirma que não há
        nenhuma verificação de versão nova no arranque (`PluginInit.Initialize`
        em `Commands.cs`) antes de dar como confirmado.
      - Interface só em português — ⚠️ decisão de mercado, não bug; só
        regista, não "corrijas".

      Para cada item ⚠️/❌ desta lista, o teu trabalho é CONFIRMAR (ler o
      ficheiro certo, citar o que encontraste) — não implementar a
      correção. Isto é um documento de estado, não uma lista de tarefas de
      código. Não abras PR de código nenhum só para isto — se não houver
      mais nada a fazer na sessão, um PR só com este documento novo está
      bem.

- [ ] Documentação — Rascunho de EULA / termos de licenciamento
      (`Docs/EULA_RASCUNHO.md`, novo ficheiro, em português).

      AVISO GRANDE, para não escapar a ninguém que leia isto depois: é um
      RASCUNHO PARA REVISÃO HUMANA/JURÍDICA, nunca um documento a usar
      como está. Marca isto de forma bem visível no TOPO do próprio
      ficheiro gerado, em maiúsculas, antes de mais nada — algo como
      "RASCUNHO — NÃO TEM VALIDADE LEGAL ATÉ SER REVISTO POR UM ADVOGADO.
      Gerado automaticamente a partir do que o código realmente faz; pode
      conter erros ou omissões com consequências legais reais."

      Base-te SÓ no que o código realmente faz — não inventes cláusulas
      genéricas de EULA copiadas de outro lado sem verificar que fazem
      sentido aqui. Lê antes de escrever: `Supabase/LICENCIAMENTO.md`
      (como a licença funciona, ativação, revalidação, o que o comando
      `TSKLICENCARESET` faz), `Licenca.cs` (o que é validado e onde),
      `Telemetria.cs` (que dados são enviados — confirma que não há PII,
      como o próprio `Supabase/schema.sql` já documenta na tabela
      `plugin_sessoes`), e `Deploy/Obfuscation/confuser.crproj` (se o
      binário é ofuscado, isso normalmente entra numa cláusula de
      proibição de engenharia reversa).

      Cobrir, com base no que encontraste (não em suposições): concessão
      de licença (por posto, `max_dispositivos`), o que a revogação
      (`activa = false`) e o bloqueio de posto (`bloqueada = true`)
      significam na prática, limitação de responsabilidade (o próprio
      código já assume isto — "licenciamento é um dissuasor, não uma
      fortaleza" — mas o EULA precisa de dizer isso em linguagem
      contratual), proibição de engenharia reversa (coerente com a
      ofuscação), e uma cláusula a dizer claramente que isto é um rascunho
      técnico, não redigido por um advogado.

- [x] Documentação — Rascunho de política de privacidade
      (`Docs/POLITICA_PRIVACIDADE_RASCUNHO.md`, novo ficheiro, em
      português).
      **Feito 2026-09-25**: criado com o aviso de rascunho bem visível no
      topo. Achado importante durante a leitura do código (não estava
      previsto no enunciado deste item): `Telemetria.TextoExplicativo()`
      diz ao utilizador que nome de máquina/utilizador/domínio nunca são
      enviados, mas isso só é verdade para a telemetria de arranque
      (`plugin_sessoes`) — `Licenca.Activar` (ativação paga) envia
      `Licenca.Maquina` (`MAQUINA\UTILIZADOR`) e `Licenca.PedirTrial`
      (avaliação gratuita) envia `Environment.UserName`, ambos guardados
      em texto simples na tabela `activacoes`. Documentado no ficheiro
      novo (secção 6, "Achado importante") em vez de decidido/corrigido —
      é comportamento intencional já comentado no próprio `Licenca.cs`
      ("identificador histórico de activações pagas"), não um bug desta
      tarefa. Confirmado por leitura de `Telemetria.cs`, `Licenca.cs`,
      `Supabase/schema.sql` (tabelas `plugin_sessoes`/`activacoes`/
      `licencas`, comentários e RLS) e `Commands.cs` (`TSKPRIVACIDADE`,
      `TSKLICENCARESET`); confirmada só a existência de
      `Deploy/supabase.json` (4 linhas), sem expor URL/chave no
      documento. Nenhum ficheiro `.cs` alterado — não aplicável correr
      `dotnet test`.

      MESMO AVISO da tarefa do EULA acima — rascunho para revisão
      humana/jurídica, marcado bem visível no topo do ficheiro. Não
      reescreves isto num único texto genérico: um RGPD mal escrito é
      pior do que nenhum, porque dá falsa sensação de conformidade.

      Base-te em `Telemetria.cs`, `Supabase/schema.sql` (tabela
      `plugin_sessoes` e o comentário que já explica, dentro do próprio
      schema, PORQUE não há nome de máquina/utilizador/domínio — usa esse
      raciocínio já escrito, não inventes outro) e `Licenca.cs`/
      `instalacao.dat` (identificador aleatório persistente, cifrado com
      DPAPI local). Lista concretamente: que dados são recolhidos
      (produto, versão, versão do AutoCAD, sistema, cultura, timestamp,
      identificador de instalação opaco), que dados NÃO são recolhidos
      (nome, e-mail além do da licença, nome de máquina/utilizador/
      domínio), onde ficam guardados (Supabase, projeto de produção — ver
      `Deploy/supabase.json` só para confirmar que existe, NÃO exponhas a
      URL/chave no documento gerado), e quem pode pedir a remoção dos
      seus dados (o e-mail de suporte já usado no resto do projeto).
