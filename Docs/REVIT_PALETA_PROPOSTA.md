# DIMSCALE · Medições BIM para Revit — proposta de paleta

Mockup de design e especificação inicial, 23/09/2026. Não é um add-in instalado nem uma integração já validada no Revit. A expressão do pedido foi interpretada como **medições semiautomáticas**: selecionar, conferir e confirmar. Todos os elementos e valores da prévia são fictícios.

## Contexto DIMSCALE

O utilizador indicou que vai trabalhar na DIMSCALE. A proposta passa a usar **DIMSCALE · Medições BIM** como nome de trabalho, mantendo o projeto TSK TakeOff como referência funcional. Este nome é uma proposta, não um produto oficial anunciado pela empresa.

O site apresenta serviços de Quantity Surveying, Cost Management, Project Revision e BIM. Por isso, o desenho dá prioridade ao vínculo entre elemento, artigo e quantidade, à conferência humana e ao mapa de entrega. Não foi fornecido um mapa de artigos ou manual de medição interno: códigos, descrições, projeto e critérios são exemplos, não padrões atribuídos à empresa.

A apresentação adapta a direção visual observada no site: nome em caixa alta, neutros, preto/branco e pequeno acento verde. O verde `#81c94c` aparece no CSS público; a sua utilização nesta paleta é uma escolha de design, não uma afirmação sobre o manual oficial da marca. Segoe UI mantém a legibilidade e a implementação simples no Windows. A assinatura é tipográfica, sem reproduzir exatamente o logótipo.

Prévia: `output/revit-palette/dimscale-revit-paleta.html`. A imagem `dimscale-logo-reference.png`, obtida do site público, é apenas referência visual e não é carregada pela prévia.

## Base no projeto atual

- `PaletteTheme.cs`: referência de densidade, Segoe UI e estados. A prévia adapta as cores à proposta DIMSCALE e acompanha a aparência clara/escura da aplicação.
- `PalettePanelShell.cs` / `ResultadosPainel.cs`: destino da próxima medição explícito, propriedades do elemento e organização dos resultados.
- `ResultadosModelo.cs`: agrupamento e totais por unidade. Reutilização depende de revisão das dependências e adaptação do modelo de domínio.
- `Models.cs`: regras de vãos e modelos existentes servem de referência; não aplicar a serialização XData nem os pressupostos geométricos 2D ao Revit.
- `ExcelExporter.cs` / `FolhaMedicao.cs`: aproveitar o formato de entrega e a experiência de exportação; criar um adaptador de linhas para medições BIM. Não enviar a área já líquida para uma rotina que volte a deduzir vãos.

## Interface proposta

Paleta WPF acoplada à direita, 420 unidades independentes de dispositivo como largura inicial; mínimo proposto de 360. No Revit, conteúdo central rolável e área de confirmação fixa no rodapé, com validação a 100%, 150% e 200% de escala. O mockup cresce verticalmente para ser legível na conversa.

Três separadores: **Medir**, **Registos** e **Critérios**.

1. **Destino:** serviço, filtro de nível e artigo do mapa. A unidade vem do artigo, sem campo livre que permita somar grandezas incompatíveis. Selecionar um resultado para consultar não altera este destino; a ação explícita “Medir neste artigo” altera-o.
2. **Elementos:** selecionar no modelo ou usar a seleção atual; filtrar categoria, nível e fase definidos. O nível é um filtro de origem, não uma edição do nível do elemento.
3. **Conferência:** inclusão por elemento, ID, origem da quantidade, unidade e total. O total considera apenas linhas válidas, incluídas e ainda não registadas nesse contexto.
4. **Registar:** confirma um instantâneo de quantidade, origem, artigo e critério. A medição não fica aprovada apenas por selecionar elementos.
5. **Registos:** níveis e artigos expansíveis, propriedades e localização do elemento, totais por unidade e exportação Excel.

Na prévia funcionam: troca de serviço/artigo/piso, seleção simulada, inclusão e exclusão de linhas, consulta do elemento, confirmação, bloqueio de repetição, navegação ao artigo, desfazer último lote e prévia do mapa Excel. Não há comunicação com o Revit, persistência após recarregar ou geração real de XLSX.

## Primeira versão implementável

| Serviço | Quantidade | Origem proposta | Limite inicial |
|---|---|---|---|
| Alvenaria | m² | Parâmetro de área calculada da parede | Paredes básicas do documento atual; validar tipos suportados |
| Pavimentos | m² | Parâmetro de área calculada do pavimento | Elementos de pavimento; não inferir revestimento de uma camada não modelada |
| Tubagens | m | Comprimento do elemento | Troços de tubo; acessórios não entram por aproximação |
| Portas | un. | Contagem de instâncias | Instâncias válidas no filtro; sem contagem recursiva de componentes aninhados |

Selecionar um artigo classifica o elemento; não prova que a sua composição corresponde ao serviço. O utilizador confere tipo, material e unidade antes de confirmar. Para medir uma camada específica, será necessário um modo de materiais próprio.

O volume (m³) é uma extensão natural com artigos compatíveis e leitura do parâmetro correto, mas não está simulado nesta primeira prévia. Totais de m, m², m³ e un. permanecem separados.

## Implementação no Revit

| Ação / componente | Implementação proposta |
|---|---|
| Paleta | `Page` WPF + `IDockablePaneProvider`, registada no arranque pelo add-in; MVVM para estado e apresentação |
| Botões que acedem ao modelo | Fila de pedidos via `ExternalEvent` / `IExternalEventHandler`; não chamar a API diretamente do evento WPF |
| Selecionar | `Selection.PickObjects` e `ISelectionFilter`; tratar cancelamento por Esc como saída normal |
| Usar seleção | Ler IDs da seleção atual em contexto válido de API e validar categoria, fase, nível e documento |
| Quantidades | Ler parâmetros por identificadores da API, evitando nomes localizados; verificar presença, tipo, finitude e unidade |
| Unidades | `UnitUtils.ConvertFromInternalUnits`; guardar precisão integral e arredondar só na apresentação / entrega |
| Localizar | Selecionar IDs e enquadrar com a API de UI; informar quando a vista ou fase impedir a visualização |
| Guardar registos | `DataStorage` + Extensible Storage versionado, em transação; guardar o RVT para persistir em disco |
| Excel | Gerar linhas independentes do Revit a partir dos registos confirmados e escrever XLSX com ClosedXML |

Estas são escolhas de arquitetura para a implementação, não código já portado. A DLL AutoCAD atual não carrega no Revit: será necessário um projeto de add-in próprio, manifesto `.addin`, referências e compilação para a versão de Revit escolhida. A versão alvo ainda não foi informada.

Separação sugerida: domínio e regras sem dependências Autodesk; adaptador Revit para seleção/leitura/persistência; interface WPF; adaptador Excel. Verificar a compatibilidade das bibliotecas e o carregamento das dependências na versão alvo antes de reaproveitar pacotes do projeto atual.

## Regras que evitam medições erradas

- **Vãos:** o modo “Quantidade do modelo” usa a área fornecida pelo Revit sem nova dedução. Regras contratuais de área bruta / descontos exigem outro modo com apuramento explícito da geometria e validação das famílias. Não converter a regra existente denominada SINAPI numa regra universal.
- **Identidade:** guardar identificador do documento e `Element.UniqueId`; ID numérico é útil na interface, mas não deve ser a única identidade persistente.
- **Duplicação:** chave proposta por documento, elemento, artigo, serviço/critério e, quando aplicável, material/face. Bloquear repetição exata; permitir conscientemente serviços distintos sobre o mesmo elemento.
- **Alterações do modelo:** registos são instantâneos confirmados. Eventos de alteração marcam pendências; recalcular por solicitação explícita, comparar anterior / atual e voltar a confirmar. Nunca apresentar quantidade antiga como atualizada silenciosamente.
- **Elemento eliminado ou parâmetro ausente:** apresentar pendência, não converter em zero sem aviso. Excluir pendências da confirmação e identificar no relatório.
- **Documento ativo:** bloquear durante pedidos pendentes, cancelar pedidos de documentos fechados e invalidar a prévia ao mudar de documento. Revalidar antes de gravar.
- **Transações / colaboração:** lidar com documento só de leitura e indisponibilidade de edição do armazenamento em modelos partilhados. Informar falha sem mostrar o lote como guardado.
- **Níveis / fases:** definir política por categoria para elementos sem nível ou que atravessam pisos. Não repartir automaticamente paredes entre pisos no MVP. A fase “Nova construção” da prévia é apenas exemplo; a implementação lista as fases reais.

## Evolução posterior

Materiais por camada; apuramento de área bruta e regras de vãos; revestimentos por face; elementos vinculados com identidade do vínculo e do documento; filtros avançados; importação do mapa; atualização assistida; Excel ao vivo. Cada função requer validação própria. Evitar prometer correspondência automática entre qualquer família BIM e qualquer artigo de orçamento.

## Validação necessária antes de aplicar

Comparar quantidades com tabelas Revit equivalentes, usando as mesmas categorias, fases e filtros. Conferir paredes com portas/janelas, uniões, paredes inclinadas e perfis editados; pavimentos com aberturas; tubos e acessórios; portas aninhadas; unidades do projeto distintas. Para o MVP, rejeitar explicitamente situações não suportadas.

Testar cancelar seleção, confirmar o mesmo lote duas vezes, trocar de documento, eliminar elemento, desfazer, guardar/reabrir, falha de transação e exportar unidades distintas. A inspeção e simulação deste mockup não substituem esses testes dentro do Revit.

## Referências consultadas

- [DIMSCALE — Home](https://www.dimscale.com/) e [Services](https://www.dimscale.com/consulting_services.html): posicionamento Architecture Cost Management e serviços de medições, gestão de custos, revisão e BIM.
- [Autodesk — Dockable Dialog Panes](https://help.autodesk.com/cloudhelp/2026/ENU/Revit-API/files/Revit_API_Developers_Guide/Advanced_Topics/Revit_API_Revit_API_Developers_Guide_Advanced_Topics_Dockable_Dialog_Panes_html.html): suporte a WPF e acoplamento.
- [Autodesk — External Events](https://help.autodesk.com/cloudhelp/2026/ENU/Revit-API/files/Revit_API_Developers_Guide/Advanced_Topics/Revit_API_Revit_API_Developers_Guide_Advanced_Topics_External_Events_html.html): execução dos pedidos de uma interface não modal.
- [Autodesk — Selection](https://help.autodesk.com/cloudhelp/2026/ENU/Revit-API-MainReference/files/html/31b73d46-7d67-5dbb-4dad-80aa597c9afc.htm): seleção e filtros.
- [Autodesk — UnitUtils](https://help.autodesk.com/cloudhelp/2026/ENU/Revit-API-MainReference/files/html/128dd879-fea8-5d7b-1eb2-d64f87753990.htm): conversão de unidades.
- [Autodesk — ExtensibleStorage](https://help.autodesk.com/cloudhelp/2026/ENU/Revit-API-MainReference/files/html/79486a74-376c-9555-c873-45d5a750f051.htm): armazenamento de dados do add-in.
- [Autodesk — Material quantities](https://help.autodesk.com/cloudhelp/2017/ESP/Revit-API/files/GUID-101E6FBF-D776-4CFA-A40D-D2DB7C0BDE9A.htm): áreas/volumes de materiais, restrições e distinção entre quantidades brutas e líquidas. Referência conceitual antiga; validar na versão alvo.
