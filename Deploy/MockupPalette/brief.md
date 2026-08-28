# Mockup — Paleta TSK TakeOff

## Objetivo

Criar um mockup visual navegável da paleta lateral de medições do plugin TSK TakeOff para aprovação antes da implementação WinForms. O painel deve parecer um instrumento técnico CAD premium, compacto, confiável e nativo do ecossistema AutoCAD.

## Contexto e público

- Plugin de medições e orçamentação para AutoCAD.
- Uso prolongado num painel lateral estreito, normalmente ancorado à esquerda ou à direita.
- Utilizadores precisam configurar rapidamente a próxima medição, disparar comandos e conferir resultados/totais.
- Idioma: português de Portugal.

## Direção visual

- Estética: painel técnico de precisão, não dashboard web genérico.
- Tema principal escuro, compatível com AutoCAD; incluir controlo no mockup para alternar e demonstrar tema claro.
- Destaque azul-cobalto, superfícies grafite em camadas, bordas discretas e alto contraste.
- Tipografia: Segoe UI; números tabulares na grelha quando possível.
- Densidade compacta, mas com hierarquia e estados claros.
- Usar ícones vetoriais simples/inline e coerentes, sem emojis.
- Evitar gradientes decorativos, sombras excessivas, cards arredondados em toda parte e linguagem visual de landing page.

## Estrutura obrigatória

- Cabeçalho: `TSK TakeOff`, estado `DWG ativo` e subtítulo da próxima medição/layer efetiva.
- Abas: `Contagens`, `Materiais`, `Alvenaria`, com Alvenaria ativa.
- Secção `Configuração` compacta:
  - Artigo do mapa.
  - Serviço.
  - Bloco / etiqueta.
  - Piso / cor.
  - Alçado / zona.
  - Altura.
  - Espessura.
  - Regra de vãos.
  - `Mais opções` expansível para título e layer.
- Secção de ações primárias `Medir`:
  - Retângulo.
  - Polyline.
  - Área.
  - Área da seleção.
  - Medir seleção.
  - Adicionar vão.
- Barra secundária `Resultados`:
  - Excel ao vivo.
  - Exportar.
  - Atualizar.
  - Menu `Mais ações` com Remover, Linha branca, Artigo, Reclassificar e Limpar tudo.
- Grelha representativa com hierarquia de paredes e vãos, células editáveis, alerta de classificação e valores de área negativos para vãos.
- Rodapé: quantidade, área líquida, volume, pré-aros e alertas.

## Interações do mockup

- Alternar tema escuro/claro.
- Expandir/recolher `Mais opções`.
- Alternar entre as três abas; as abas secundárias podem ter conteúdo representativo, mas coerente.
- Selecionar linha da grelha.
- Abrir/fechar menu `Mais ações`.
- Estados hover/focus visíveis.

## Restrições

- Entregar um único `index.html`, autocontido, sem dependências externas e sem rede.
- Largura de referência: 560 px; deve continuar legível de 480 a 760 px.
- Não alterar o código C# do plugin nesta etapa; trata-se apenas de mockup para aprovação.
- Manter os nomes e fluxo funcional do painel existente.

## Saída

- `Deploy/MockupPalette/index.html`

---

## Revisão de 2026-08-28 — árvore por pavimento

A versão com árvore (`preview-eberick.png`) substituiu a grelha plana. Três
correções antes de isto poder ser aprovado como especificação:

**1. Uma coluna `Quantidade`, não `Comp. / Altura / Área`.**
Três colunas fixas não chegam para cinco grandezas, e o resultado era uma
contagem de 2 portas a aparecer debaixo de "Comp.". Comprimento e altura são
**dimensões**, não quantidades: vivem no painel de propriedades. A coluna
mostra a quantidade na unidade do artigo (`28,85 m²`, `29,15 m`, `2 un.`) e,
nos níveis que misturam unidades, lista-as lado a lado — `62,93 m² · 36,90 m ·
2 un.` — em vez de somar o que não soma.

Atenção ao caso que engana: o comprimento de uma parede e o comprimento de um
rodapé **não vão ao mesmo balde**. Um é dimensão de um artigo medido a m², o
outro *é* a quantidade de um artigo medido a metro. São dois campos distintos
no modelo.

**2. `PISO 1` povoado.** Com um piso só, os totais do topo eram iguais aos do
piso e a soma entre níveis não se via. Agora `Pavimentos = PISO 0 + PISO 1`
(87,64 m² = 62,93 + 24,71), e os totais dos nós de cima são somados dos filhos,
nunca escritos à mão.

**3. Selecção e próxima medição, mesmo por dentro.** A barra do topo já as
mostrava separadas, mas o JS ligava-as: clicar num nó qualquer reescrevia a
próxima medição. Quem só queria conferir uma parede antiga mudava o destino sem
dar por isso. Passou a haver:

- **selecção** — clicar num nó mostra as propriedades e mais nada;
- **fixação** — botão `Medir aqui`, só em nós de artigo, com a etiqueta
  `PRÓXIMA` no nó fixado.

### Por decidir antes do C#

O WinForms **não tem árvore com colunas**: o `TreeView` não tem colunas, o
`DataGridView` não tem hierarquia. O painel actual é `DataGridView`. A via
recomendada é manter o `DataGridView` com o modelo achatado e indentação —
expandir/recolher passa a ser recalcular as linhas visíveis — e ligar o
`VirtualMode`, que hoje não é usado em lado nenhum e é onde está a folga para
as máquinas de 8 GB.

