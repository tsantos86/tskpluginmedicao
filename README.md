# TSK TakeOff

**Medições de construção civil no AutoCAD, com registo no DWG e exportação para Excel.**

O TSK TakeOff é um plugin em C# para apoiar a medição de alvenarias, fachadas, áreas e elementos lineares. Liga a geometria do desenho ao mapa de quantidades: as medições ficam identificadas no DWG e podem ser organizadas por artigo, serviço e piso.

Desenvolvido no âmbito da **TSK Digital**, aproxima o trabalho de preparação de obra das ferramentas de software usadas para medir, conferir e entregar quantitativos.

[Começar](#fluxo-de-uso) · [Comandos](#comandos) · [Compilar](#compilar) · [Testes](#testes) · [Documentação](./Docs/)

## Funcionalidades

| Área | O que permite fazer |
|---|---|
| Medição no desenho | Medir paredes, fachadas, contornos, hachuras e elementos lineares. |
| Organização | Associar medições a artigos do mapa, serviços e pisos. |
| Vãos | Detetar vãos e confirmar os descontos aplicáveis. |
| Rastreabilidade | Registar medições em XData no próprio DWG, com identificação visual. |
| Excel | Acompanhar medições ao vivo ou exportar um mapa em `.xlsx`. |

## Ambiente e compatibilidade

O projeto tem dois alvos de compilação, destinados a diferentes gerações do AutoCAD:

| AutoCAD | Alvo .NET | Plataforma |
|---|---|---|
| 2021–2024 | `net48` — .NET Framework 4.8 | Windows x64 |
| 2025–2026 | `net8.0-windows` — .NET 8 | Windows x64 |

A configuração atual compila `net48` por omissão e adiciona `net8.0-windows` quando encontra as referências locais do AutoCAD 2025. Consulte [TSKTakeOff.csproj](./TSKTakeOff.csproj) para os caminhos de referência e as dependências.

- **Unidades:** desenhos em metros; medições 2D no plano XY.
- **Excel ao vivo:** requer Microsoft Excel instalado, através de COM.
- **Exportação XLSX:** usa ClosedXML e não requer Excel instalado.
- **Carregamento:** `NETLOAD` da biblioteca `TSKTakeOff.dll`.

## Comandos

| Comando | Função |
|---|---|
| `TSKPAINEL` | Abre o painel lateral (abas Arquitetura, Materiais, Lineares e Contagens). |
| `TSKPAREDE` | Mede uma parede de alvenaria com o serviço/altura do painel; deteta vãos no fim. |
| `TSKAREA` | Desenha o contorno de uma área em planta, preenche-a e mede **área × altura** (m³) — betonilhas, enchimentos, camadas. |
| `TSKAREASEL` | O mesmo, mas sobre hachuras/polylines fechadas **que já estão no projeto**. Mede sobre cópias; o desenho original não se toca. |
| `TSKRET` | Fachada/ETICS por retângulo: 2 cliques no alçado, comp × alt saem da geometria. |
| `TSKPOLF` | Fachada por polyline em planta × altura do piso. |
| `TSKLINEAR` | Medição linear simples por categoria (tubos, rodapés…). |
| `TSKEXCEL` | Liga/desliga o Excel ao vivo. |
| `TSKEXPORT` | Exporta o mapa de medições .xlsx ao lado do DWG. |
| `TSKRIBBON` | Recria a ribbon (após NETLOAD repetido). |

Os comandos antigos `MEDPAREDE`, `MEDRET`, `MEDPOLF`, `MEDIR`, `MEDEXPORT` continuam a funcionar como atalhos.

## Ribbon

Separador **TSK TakeOff** com os painéis Projeto | Arquitetura | Materiais | Lineares | Contagens | Excel.
É criada por código — **não existe CUIX para carregar**, basta o NETLOAD da DLL.

## Fluxo de uso

1. `NETLOAD` → `TSKTakeOff.dll`.
2. Separador **TSK TakeOff** na ribbon (ou `TSKPAINEL`).
3. No painel, define **Material/Serviço**, **Piso** (com cor) e a **Altura**.
4. **Excel ao Vivo** abre o Excel ao lado; cada medição aparece na hora.
5. Mede: **Medir Parede** (planta) ou **Retângulo** (alçado, com hatch gradiente na cor do piso).
6. Os vãos são detectados por texto/bloco (`VE.02` + `1190x2350`) e confirmados num diálogo.
7. **Exportar XLSX** gera o mapa final ao lado do DWG.

## Regras e dados

- **Artigo do mapa**: escolhe-se em *Artigo (mapa)* e acompanha as medições que se fizerem a seguir.
  Para o mudar em medições **já feitas** — mapa importado a meio da obra, ou versão nova do mapa —
  selecionam-se as medições na árvore (Ctrl/Shift para várias) e usa-se **Mais ▸ Reclassificar…**.
  As que ainda não têm artigo juntam-se no grupo **Por classificar**, marcadas com `⚑`,
  e saem no **fim** da folha.
- **Medir aqui**: selecionar um resultado serve para consultar e editar. Para mandar as próximas
  medições para um artigo, escolhe-se o nó desse artigo e carrega-se em **Medir aqui** — a única
  operação que altera a próxima medição. O nó fixado mostra a etiqueta `PRÓXIMA`.
- **Vãos**: descontar tudo · SINAPI (excedente de 2 m²) · não descontar — configurável no painel.
- **Pré-aro**: unidades e metros lineares (porta = 2×alt + larg; janela = perímetro).
- Tudo gravado em **XData no próprio DWG** — salvar o desenho salva as medições.

## Compilar

Visual Studio 2019/2022 (workload ".NET desktop development"):

1. Abrir `TSKTakeOff.slnx` (ou `TSKTakeOff.csproj`). A solução inclui o plugin e os testes.
2. **Build → Compilar Solução** (Ctrl+Shift+B). Não usar ▶/F5: é uma biblioteca, não se executa.
3. O alvo `net48` compila por omissão. O alvo `net8.0-windows` só entra quando existe a instalação/referência do AutoCAD 2025.
4. Saída: `bin\Debug\net48\TSKTakeOff.dll` ou `bin\Release\net48\TSKTakeOff.dll`. Manter as restantes DLLs na mesma pasta.

## Testes

O projeto [TSKTakeOff.Tests](./Tests/TSKTakeOff.Tests.csproj) usa xUnit e .NET 8 para testar lógica de cálculo sem depender de uma instalação do AutoCAD. Inclui código de medições, dimensões de vãos, artigos e organização da folha de medição.

Com o SDK .NET 8 disponível:

```bash
dotnet test Tests/TSKTakeOff.Tests.csproj
```

Estes testes cobrem a lógica incluída no projeto de testes; a interface e a integração com o AutoCAD precisam de validação no próprio programa.

## Organização do código

| Local | Responsabilidade |
|---|---|
| [Commands.cs](./Commands.cs) | Comandos disponibilizados no AutoCAD. |
| [Ribbon.cs](./Ribbon.cs) e [Palette.cs](./Palette.cs) | Interface do plugin. |
| [Medicoes.cs](./Medicoes.cs) e [Models.cs](./Models.cs) | Medições e modelos de dados. |
| [ExcelExporter.cs](./ExcelExporter.cs) e [ExcelLiveSync.cs](./ExcelLiveSync.cs) | Exportação e sincronização com Excel. |
| [Tests](./Tests/) | Testes automatizados da lógica independente do AutoCAD. |
| [Docs](./Docs/) | Documentação complementar. |
| [Deploy](./Deploy/) | Recursos e scripts de distribuição. |

## Notas técnicas

- Desenho assumido em **metros**; medição 2D (plano XY).
- Ícones gerados em runtime (GDI+), sem ficheiros de recursos.
- Excel ao vivo via COM (requer Excel instalado); export via ClosedXML (não requer).
- A aba **Arquitetura** usa uma árvore compacta de resultados (`Pavimentos > Piso > Serviço > Artigo > Medição > Vão/Título`) com duas colunas — elemento e quantidade —, pesquisa (`Ctrl+F`), filtros com `Aplicar`, painel de propriedades editável e totais **por unidade**, nunca somados entre grandezas diferentes.
- A hierarquia, a pesquisa, os filtros e a agregação vivem em `ResultadosModelo.cs` / `ResultadosArvore.cs`, **fora do WinForms e do AutoCAD**, e estão sob teste — é o que permite verificar um total sem abrir o AutoCAD. Os adaptadores das outras abas estão em `ResultadosAdaptadores.cs`.
- As abas Materiais, Lineares e Contagens mantêm por agora os seus fluxos e grelhas específicos; os adaptadores para a árvore comum já existem e estão testados.
