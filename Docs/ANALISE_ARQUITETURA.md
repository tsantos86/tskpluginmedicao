# TSK TakeOff — Auditoria de arquitetura e prontidão comercial

Análise feita em 2026-09-26, por leitura direta do código-fonte real (não da
documentação nem dos nomes de ficheiros). Todas as citações são
`ficheiro:linha` do estado do repositório nesta data. Lidos previamente
`Docs/CHECKLIST_COMERCIAL.md`, `Docs/EULA_RASCUNHO.md`,
`Docs/POLITICA_PRIVACIDADE_RASCUNHO.md` e `Docs/TAREFAS_AGENTE.md` — o que já
está confirmado nesses três documentos não é repetido aqui, salvo quando esta
auditoria encontra algo novo ou o aprofunda.

## 1. Veredito

**6/10 — comercial em preparação.** É um projeto de engenharia séria: camada
de domínio puro isolada e testada (341 testes, `dotnet test` 0 falhas),
comentários que documentam decisões e bugs históricos com honestidade rara, um
processo de build/instalação bem pensado (deteção de AutoCAD instalado/aberto,
duas séries num só bundle, versão sincronizada por script). Não é um
protótipo. Mas ainda não está pronto para distribuição: falta assinatura de
código, falta validação real do alvo `net8.0-windows` (nunca compilado numa
máquina com AutoCAD 2025), há pelo menos um bug funcional confirmado por
leitura (coluna "Item" vazia no Excel ao vivo em modo item), um padrão
recorrente de estado estático partilhado entre documentos AutoCAD nunca
validado com dois desenhos abertos, e as peças comerciais (EULA, privacidade,
emissão de licença, auto-update) continuam em rascunho ou manuais. O caminho
até 9/10 é sobretudo validação (matriz manual no AutoCAD, build net8.0-windows
real) mais um punhado de correcções pontuais — não uma reescrita.

## 2. Pontos fortes

- **Camada de domínio puro isolada de propósito e sob teste.** `Medicoes.cs`,
  `DimensaoVao.cs`, `ChaveArtigo.cs`, `Armadura.cs`, `ResultadosModelo.cs`,
  `ResultadosArvore.cs` não referenciam `Autodesk.*` nem
  `System.Windows.Forms` — confirmado por leitura e por grep. O próprio
  `Tests/TSKTakeOff.Tests.csproj` usa `Compile Include` explícito (16
  ficheiros, sem globbing) precisamente para que um `using Autodesk...`
  introduzido por engano parta a compilação dos testes como alarme.
- **341 testes automatizados, 0 falhas, 330 ms** (`dotnet test
  Tests/TSKTakeOff.Tests.csproj`), cobrindo hierarquia de resultados, filtros,
  totais por unidade, desconto de vãos, serialização XData ida-e-volta,
  estrutura do `.bc3`, ordenação da folha de medição.
- **Honestidade documental incomum no próprio código.** Vários ficheiros
  citam bugs históricos reais e a correcção aplicada (`AlvRepo.cs:947-957`,
  cast `Polyline`→`Entity` que fazia hachuras desaparecerem da edição;
  `MedirSeleccao.cs:1110-1126`, largura de vão medida errada entre dois
  vãos; `MapaQuantidades.cs:369-379`, chave truncada a 255 caracteres a
  deixar medições órfãs). Isto facilita imenso uma auditoria como esta.
- **Instalador cuidadoso** (`Deploy/Installer/TSKTakeOff.iss`): deteta se há
  AutoCAD instalado e se está aberto antes de instalar/desinstalar
  (`InitializeSetup`/`InitializeUninstall`), remove a marca "vindo da
  internet" do modelo Excel pós-instalação, instala por utilizador
  (`PrivilegesRequired=lowest`) por decisão deliberada, não por omissão.
- **Leitura única do Model Space.** `Leitura.Tudo` (`Leitura.cs:16-102`)
  substituiu três travessias separadas por uma só, com comentário a
  explicar o porquê — decisão de desempenho correcta e documentada.
- **Tratamento de erro COM maduro em `ExcelLiveSync.cs`**: deteta Excel não
  instalado, referência COM morta, célula em edição e Vista Protegida, com
  mensagens explícitas ao utilizador (`ExcelLiveSync.cs:64-80,102-105,
  169-183,210-221`).
- **Licenciamento fail-safe para o utilizador**: expirar/revogar bloqueia só
  `MEDIR`, nunca o painel de resultados nem a exportação
  (`Licenca.cs:208-250`), e o ficheiro local tem backup automático
  (`Licenca.cs:586-619`).
- **`RibbonIconRenderer`/`Ribbon.cs` protegido correctamente contra NETLOAD
  repetido**, consultando o estado nativo do AutoCAD (`ribbon.Tabs`) em vez
  de uma flag gerida — ver achado #6 sobre o contraste com `Palette.cs`.

## 3. Problemas mais graves (15, por severidade)

1. **[Alto] Área/comprimento zero silencioso ao ler geometria problemática.**
   `AlvRepo.LerParede` (`AlvRepo.cs:66`) e `LerParedeDeHatch`
   (`AlvRepo.cs:101`) engolem qualquer excepção de `pl.Area`/`h.Area` num
   `catch { }` e deixam `area = 0`. A medição sobrevive na lista com
   quantidade **zero**, sem aviso ao utilizador. Impacto: total errado
   silencioso numa parede/hachura com geometria auto-intersectante.
   Recomendação: registar (`PaletteHost.Log`) sempre que isto acontece, para
   o utilizador poder pelo menos localizar a entidade. Confiança: alta.
   FACTO (por leitura de código).

2. **[Alto] Uma única entidade corrompida pode abortar a leitura de TODAS as
   medições do desenho.** `LadosDoRetangulo` (`AlvRepo.cs:431-446`) não tem
   try/catch; se `pl.GetPoint2dAt` lançar, a excepção sobe sem ser apanhada
   por `Leitura.Tudo` (`Leitura.cs:36-97`) nem por
   `AlvRepo.CarregarParedes` (`AlvRepo.cs:19-36`) — falta isolamento por
   entidade. Impacto: grelha, Excel e totais ficam indisponíveis por causa
   de uma polyline só. Recomendação: try/catch por entidade dentro do
   `foreach` de `Leitura.Tudo`. Confiança: alta. FACTO/RISCO POTENCIAL
   (mecanismo confirmado; frequência real PRECISA DE TESTE NO AUTOCAD).

3. **[Alto] Coluna "Item" (numeração `A001…`) fica vazia nas linhas de
   medição no Excel ao vivo em modo item.** `EscreverFolhaItem`
   (`ExcelLiveSync.cs:1214`) só escreve `m.ColItem` dentro do bloco
   `if (!string.IsNullOrEmpty(l.HandleOrigem))` (linhas 1190-1215) — que só
   é verdade para linhas de título. Medição/Dedução/Capítulo/Alçado/Piso
   ficam sem número, ao contrário do `.xlsx` exportado
   (`ExcelExporter.cs:76`) e do modo clássico ao vivo
   (`ExcelLiveSync.cs:1847`). Quebra a paridade que o próprio
   `FolhaMedicao.cs:70-71` promete entre os dois caminhos. Recomendação:
   escrever `l.Item` também para os outros tipos de linha. Confiança: alta
   por leitura; efeito visual final PRECISA DE TESTE NO EXCEL REAL.

4. **[Médio-Alto] Estado de configuração (`Config`, `FachadaConfig`,
   `ContagemConfig`) é estático ao processo, não por documento.**
   `Models.cs:400-561`, `Fachada.cs:14-149`, `Contagem.cs:11-92` — Piso,
   Serviço, Artigo, Alçado, Material "corrente" são partilhados por TODOS
   os desenhos abertos no mesmo AutoCAD. Com dois DWG abertos, medir
   alternadamente em cada um pode escrever com a configuração pensada para
   o outro. Recomendação: validar com um teste manual de dois documentos
   antes de assumir que é seguro; se confirmado problemático, associar o
   estado ao `Database`/`Document` em vez de ao `AppDomain`. RISCO
   POTENCIAL — PRECISA DE TESTE NO AUTOCAD (é o ponto de maior incerteza
   arquitectural desta auditoria).

5. **[Médio-Alto] Trocar de documento activo não actualiza a paleta.**
   `AcadApp.DocumentManager.DocumentActivated += (s,e) =>
   ObservarDocumentoActivo();` (`Palette.cs:272`) troca os reactors de
   `ObjectModified`/`ObjectAppended`/`ObjectErased` mas nunca chama
   `RefreshData()`. Ao alternar entre dois desenhos já abertos, a árvore e
   o Excel ao vivo continuam a mostrar dados do documento anterior até
   alguém editar algo ou clicar "Atualizar". RISCO POTENCIAL — PRECISA DE
   TESTE NO AUTOCAD.

6. **[Médio] NETLOAD repetido pode duplicar handlers `Idle`/
   `DocumentActivated`.** `_autoSincronizacaoLigada` (`Palette.cs:224,
   266-274`) é uma flag *gerida* que nasce `false` outra vez a cada nova
   carga de assembly, mas os delegates da assembly antiga continuam
   subscritos em eventos nativos (`AcadApp.Idle`, `DocumentActivated`) —
   `PluginInit.Terminate` (`Commands.cs:54-60`) não os desliga. Contraste
   directo: `Ribbon.cs:44-50` resolve o mesmo problema correctamente
   consultando `ribbon.Tabs` (estado nativo, sobrevive ao reload). INFERÊNCIA
   fundamentada (padrão de falha conhecido em plugins .NET AutoCAD) —
   PRECISA DE TESTE NO AUTOCAD com NETLOAD repetido.

7. **[Médio] Esquema XData 100% posicional e sem chave por campo.**
   `AlvRepo.GravarXData`/`ParseXData` (`AlvRepo.cs:876-932,980-1031`) e o
   equivalente em `Fachada.cs:228-251,419-447` dependem só da ORDEM dos
   `TypedValue` na `ResultBuffer`. Hoje escrita e leitura conferem campo a
   campo (confirmado), mas qualquer alteração futura que insira um campo
   NO MEIO da sequência (em vez de no fim, como os comentários avisam)
   desalinha tudo silenciosamente — sem excepção, valores semanticamente
   trocados. Recomendação: ao adicionar o próximo campo, considerar migrar
   para XData nomeada ou Xrecord com chave, mesmo que só para campos novos.
   RISCO POTENCIAL estrutural, mitigado só por disciplina/comentário.

8. **[Médio] Chave de artigo truncada a 255 caracteres torna dois artigos
   diferentes indistinguíveis.** `ChaveArtigo.Truncar`
   (`ChaveArtigo.cs:30,55-61`) admite explicitamente (linhas 73-77) que dois
   artigos com o mesmo código e descrições que só diferem depois do
   carácter ~251 ficam iguais depois de gravados em XData — uma medição
   pode ficar associada ao artigo errado dos dois. Mitigado parcialmente em
   `MapaQuantidades.cs` (marca a colisão como `null` em vez de adivinhar,
   linhas 330-339), mas sem aviso visível ao utilizador. FACTO/RISCO
   POTENCIAL.

9. **[Médio] `FacRepo.Editar` repete um padrão de bug já corrigido noutro
   lado.** `Fachada.cs:284-285` faz `tr.GetObject(id, OpenMode.ForWrite) as
   Polyline; if (pl == null) return false;` — exactamente o padrão que
   `AlvRepo` já teve como bug documentado (`AlvRepo.cs:947-957`, cast a
   `Polyline` fazia hachuras "desaparecerem" da edição) e que foi corrigido
   ali para `Entity`. A lição não foi generalizada a `FacRepo`. Hoje sem
   impacto prático (`FacRepo.Carregar` só itera `Polyline`), mas é dívida
   técnica que se tornaria bug real se um pano de fachada alguma vez for
   outra entidade. FACTO (latente).

10. **[Médio] `RemoverParede()` no menu da árvore ignora o valor de
    retorno.** `Palette.cs:4147` chama `AlvRepo.RemoverParede(handle)` sem
    verificar o `bool`, ao contrário do padrão vizinho para vão
    (`Palette.cs:4082-4085`) e para "Limpar tudo" (`Palette.cs:4336-4338`).
    Se o handle já não existir (`AlvRepo.cs:854-860`), o utilizador que
    confirmou "Apagar" não recebe qualquer feedback de que nada foi
    apagado. FACTO, confiança alta.

11. **[Médio] `TSKEXPORT`/`ExcelExporter.Export` sem a rede de segurança
    genérica.** `MedExport()` (`Commands.cs:2323-2385`, por trás de
    `MEDEXPORT`/`TSKEXPORT`) não passa por `Util.Seguro`
    (`Commands.cs:2579-2597`, usado por quase todos os outros comandos); só
    há um `catch (IOException)` local em torno de `wb.SaveAs`
    (`Commands.cs:2375-2384`, `ExcelExporter.cs:40`). Qualquer outra
    excepção (permissões, bug interno do ClosedXML, falha a carregar
    paredes/fachadas/contagens) propaga-se sem a mensagem tratada habitual.
    Recomendação: envolver `MedExportImpl` em `Util.Seguro` como os
    restantes comandos. FACTO, confiança alta; impacto real PRECISA DE
    TESTE NO AUTOCAD.

12. **[Médio] Diálogo modal aberto dentro de transacção de base de dados e
    documento bloqueado.** `MedirSeleccao.Criar` abre a transacção
    (`MedirSeleccao.cs:111-112`) e só a fecha em `MedirSeleccao.cs:413`;
    entre estes pontos, `VaosConfirmados` (linhas 324,388) abre um `Form`
    modal (`VaosDetectados.cs:27,31,38-39`) enquanto a transacção e o
    `LockDocument` continuam activos. Padrão desaconselhado na API AutoCAD
    .NET (transacções devem ser de vida curta); uma excepção inesperada
    durante o diálogo deixaria a transacção pendente até ao fim do `using`.
    RISCO POTENCIAL arquitectural — sem evidência de ter causado problema
    real, mas vale confirmar com testes de reentrância.

13. **[Médio-Baixo] Importação de Excel reclassifica dados inválidos sem
    aviso.** `ImportarExcel.Numero()` (`ImportarExcel.cs:414-435`) devolve
    `null` silenciosamente se o texto não parsear como número; a linha cai
    então no ramo de "não tem dimensão" e pode ser interpretada como título
    de artigo ou nome de piso/alçado (`ImportarExcel.cs:225-229,291,306`),
    sem qualquer mensagem de que foi ignorada/mal-classificada. Mesmo
    padrão para linhas de contagem (quantidade sem Comp/Alt) — o
    importador foi pensado para paredes, não para contagens. FACTO/RISCO
    POTENCIAL; frequência real PRECISA DE TESTE COM FICHEIROS DE CLIENTES.

14. **[Médio-Baixo] `net8.0-windows` pode ficar com a versão dessincronizada
    de `net48`/instalador/bundle.** Os targets que incrementam e propagam
    `Version.build` (`TskIncrementarBuild`/`TskSincronizarVersao`,
    `TSKTakeOff.csproj:171-225`) só correm com
    `Condition="'$(TargetFramework)' == 'net48'"`. Numa compilação
    multi-target, cada `TargetFramework` é uma invocação MSBuild separada;
    nada garante que o `net8.0-windows` releia o `Version.build` já
    incrementado pelo `net48`. Plausível que a DLL AutoCAD 2025/2026 fique
    sempre com `VersaoBuild=0`, divergindo do instalador/bundle. Não há CI
    nem build real do `net8.0-windows` neste repositório para confirmar.
    INFERÊNCIA fundamentada, NÃO VERIFICADO — mesma cautela que
    `Docs/PLANO_PALETA_RESULTADOS_COMPACTA.md` já pede para este alvo.

15. **[Baixo-Médio] Política de "tolerância offline" documentada não
    corresponde literalmente ao código.** `Licenca.cs:40`
    (`DiasToleranciaOffline = 30`) e o comentário na linha 39 sugerem um
    cronómetro de "30 dias sem servidor e bloqueia", mas o valor só é usado
    como fallback quando o servidor devolve `ok:true` sem `expira_em`
    parseável (`Licenca.cs:479-483`) — a tolerância real é a `ExpiraEm` já
    em cache. Com licença válida em cache, o comportamento é *fail-open*
    indefinidamente enquanto a rede estiver em baixo; numa instalação nova
    sem rede no primeiro uso, é *fail-closed* total (não há cache para
    recorrer). Vale alinhar o comentário ao comportamento real antes de
    outra pessoa assumir uma garantia que o código não implementa. RISCO
    POTENCIAL de documentação interna, não de dados.

## 4. Acoplamento por área

| Área | Ficheiros | Avaliação |
|---|---|---|
| Domínio puro | `Models.cs` (parcial), `Medicoes.cs`, `DimensaoVao.cs`, `ChaveArtigo.cs`, `Armadura.cs`, `ResultadosModelo.cs`, `ResultadosArvore.cs` | **Bem separada** — sem `Autodesk.*`/WinForms, testada, sem dependências cruzadas entre si. |
| XData/repositórios | `AlvRepo.cs`, `Fachada.cs` (FacRepo), `Contagem.cs` (ContRepo) | **Aceitável, mas frágil.** Esquema posicional (achado #7); acoplamento ascendente confirmado a `Commands.LayerPrefix` e `ImportarExcel.LayerImportado` (`AlvRepo.cs:694,698,734`) — o repositório de dados conhece convenções de um módulo de comandos/UI, não deveria. |
| Comandos | `Commands.cs` | **Aceitável.** Separação `Commands`/`Util` já existe; MED*/TSK* sem duplicação real (TSK* são wrappers de uma linha sobre MED*Impl). Ponto fraco: `TSKEXPORT` sem `Util.Seguro` (achado #11). |
| Paleta/UI | `Palette.cs`, `PalettePanelShell.cs` | **Excessiva em `Palette.cs`.** `MedPanelControl` com ~3823 das 4487 linhas do ficheiro; `BuildUi()` só tem ~849 linhas. UI + regras de negócio + escrita directa em `AlvRepo` + integração Excel + disparo de comandos, tudo na mesma classe. `PalettePanelShell.cs`, em contraste, é o ficheiro mais limpo da UI — puramente visual, sem estado estático, sem tocar no desenho. |
| Excel/exportação | `ExcelExporter.cs`, `ExcelLiveSync.cs`, `ImportarExcel.cs`, `ImportarMQT.cs`, `ModeloExcel.cs`, `FolhaMedicao.cs` | **Aceitável a excessiva.** `ExcelLiveSync.cs` (2497 linhas, 3 modos de escrita com desempenho muito diferente) é o maior ficheiro do repositório sem qualquer teste. `FolhaMedicao.cs` é o motor comum bem isolado (sem COM) partilhado por exportação e Excel ao vivo — boa decisão. |
| Exportadores alternativos | `FiebdcExporter.cs`, `MapaQuantidades.cs` | **Aceitável.** `FiebdcExporter` é experimental e admite-o no próprio código (`FiebdcExporter.cs:15-16`) e no comando (`Commands.cs:2402-2410`) — risco comunicado, não escondido. |
| Licenciamento/telemetria | `Licenca.cs`, `Telemetria.cs`, `AutocadRuntime.cs` | **Bem separada.** `AutocadRuntime.cs` existe especificamente para dar a threads de rede um snapshot thread-safe sem tocar na API AutoCAD fora da thread principal (`AutocadRuntime.cs:7-13`) — boa prática. |
| Testes | `Tests/` | **Bem isolados por design** (Compile Include explícito, sem `ProjectReference`), mas cobertura desigual — ver secção 6. |
| Build/deploy | `TSKTakeOff.csproj`, `Deploy/` | **Aceitável**, com o risco concreto do achado #14 (versão multi-target). |

**Estado estático global com vários desenhos abertos** (pedido explicitamente
no ponto 4 da tarefa): confirmado em `Config` (`Models.cs:400`),
`FachadaConfig` (`Fachada.cs:14`), `ContagemConfig` (`Contagem.cs:11`),
`DeteccaoVaos.Tolerancia` (`DeteccaoVaos.cs:54`), `AlvRepo._avisados`
(`AlvRepo.cs:458`), `PaletteHost._ps`/`_ctrl`/`Excel` (`Palette.cs:19-24`) e a
fila de escrita adiada do Excel (`Palette.cs:124-133`). É o padrão mais
transversal encontrado nesta auditoria (achados #4-#6) — o plugin foi
desenhado a pensar em "um desenho activo por vez", mas a API AutoCAD .NET é
MDI (vários documentos no mesmo processo). Não há evidência de que isto já
causou um incidente reportado; é o ponto de maior incerteza a validar
manualmente.

## 5. Fluxos reais

**Parede (alvenaria):** `Commands.MedParedeImpl` desenha/hachura no desenho →
`AlvRepo.GravarXData` (`AlvRepo.cs:876-932`, `AppName="CASQUILHO_ALV"`) grava
XData posicional na entidade → `Leitura.Tudo`/`AlvRepo.CarregarParedes` lê de
volta (`AlvRepo.cs:19-36,66`) → `ResultadosArvore`/`ResultadosAdaptadores`
projecta para a árvore → `Palette.MedPanelControl` mostra/edita
(`AplicarEdicaoDePropriedade` → `AlvRepo.EditarParede`/`DefinirServico`) →
`FolhaMedicao.OrdenarComoFolha`/`Numerar` organiza → `ExcelExporter`/
`ExcelLiveSync` escreve.

**Linear:** `Commands.MedirImpl` desenha polyline com XData simples
(`AppNameLinear`) → `Leitura.Tudo` lê via `GetXDataForApplication`
(`Leitura.cs:59-82`, extrai categoria) → `ResultadosAdaptadores` traduz para
`MedicaoResultado` comum → mesma árvore/exportação da alvenaria.

**Fachada:** `Commands.MedRetImpl`/`MedPolFachadaImpl` → `FacRepo.GravarXData`
(`Fachada.cs:228-251`, `AppName="CASQUILHO_FAC"`) → `FacRepo.LerFachada`
(`Fachada.cs:187-196`) lê de volta — usa `AlvRepo.LadosDoRetangulo`
directamente, SEM revalidar com `AlvRepo.EhRectangulo` primeiro (achado
adicional do agente de domínio, ponto 21 do seu relatório: inconsistência com
a defesa que `AlvRepo.LerParede` aplica a si mesmo) → adaptado → árvore/
exportação.

**Vãos:** `DeteccaoVaos` só lê texto/blocos e devolve candidatos em memória
(não persiste nada) → `VaosDialog`/`VaosDetectados` pedem confirmação manual →
`AlvRepo.AdicionarVaos` grava `Vao.Serialize` (`Models.cs:53-64`) dentro da
XData da parede-mãe → lido de volta por `Vao.Deserialize`
(`Models.cs:66-85`, aceita formato antigo de 5 ou novo de 7 campos) →
descontos aplicados na árvore/folha conforme `RegraDesconto`.

**Contagens:** `Commands.TSKCONTAR` desenha `Circle`+texto →
`ContRepo.GravarXData` (`Contagem.cs:132-140`, `AppName="CASQUILHO_CONT"`) →
`ContRepo.Carregar` lê de volta (`Leitura.cs:91-96`, filtra
`Util.ClCircle`) → adaptado → árvore/exportação. Remoção
(`ContRepo.Limpar`/`ApagarTextos`) associa rótulos MText por nome de LAYER,
não por handle (`Contagem.cs:227-254`) — risco de colisão entre nomes que
sanitizem para o mesmo prefixo (achado #27 do agente de domínio).

**Exportação XLSX:** `FolhaMedicao.OrdenarComoFolha` é literalmente a mesma
função que a grelha da paleta usa (`Palette.cs:3566,3569`) — a ordem da folha
e a ordem visual **não são independentes**, ao contrário do que a
documentação geral podia sugerir; foram deliberadamente unificadas
(`FolhaMedicao.cs:299-306`). `ExcelExporter.Export` escreve via ClosedXML sem
try/catch amplo (achado #11).

**Excel ao vivo:** `ExcelLiveSync` liga por COM `dynamic`/late-binding
(`Type.GetTypeFromProgID`, `ExcelLiveSync.cs:102`) e tem TRÊS modos com
desempenho muito diferente: modo item (valores em lote, formatação
incremental — ganho medido de 10081ms→643ms documentado no próprio código,
linhas 1374-1391, mas com o bug do achado #3); modo clássico (reescreve a
folha toda, em blocos); modo simples — **o modo por omissão sem modelo
registado** — sem optimização nenhuma, célula a célula (`ExcelLiveSync.cs:
2112-2224`), facilmente 10+ idas ao COM por linha.

## 6. Matriz de testes

| Área | Cobertura | Risco | Teste recomendado | Prioridade |
|---|---|---|---|---|
| Domínio puro (Models, Medicoes, DimensaoVao, ChaveArtigo, Armadura, ResultadosArvore) | Alta — 341 testes xUnit | Baixo | Manter regressão ao alterar regras de desconto/unidade/chave | Baixa |
| Persistência XData (AlvRepo/FacRepo/ContRepo) | Nenhuma (exige AutoCAD) | Alto (achados #1,#2,#7) | Editar em lote, geometria degenerada (auto-intersecção), handle inválido pós-purge | Alta — exige AutoCAD |
| `ExcelExporter.cs` | Nenhuma | Médio (achado #11) | Exportar com ficheiro aberto/bloqueado, pasta só-leitura | Média — exige Excel real |
| `ExcelLiveSync.cs` (2497 linhas, sem `Autodesk.*`/WinForms — podia entrar no projecto de testes) | Nenhuma | Alto (maior ficheiro do repo sem teste; bug já confirmado, achado #3) | Testar os 3 modos com Excel real; extrair a montagem de `dados[,]` para lógica testável em `Tests/` | Alta |
| `ImportarExcel.cs`/`ImportarMQT.cs` | Nenhuma | Médio (achado #13) | Ficheiros malformados: texto onde se espera número, linhas de contagem sem dimensão | Média |
| `FiebdcExporter.cs` | Boa (estrutura do `.bc3` testada) | Alto (nunca validado contra importador real, auto-documentado) | Importar o `.bc3` gerado num software real (Arquimedes/CYPECAD) | Alta — exige software de terceiros |
| Multi-documento (`Config` estático, `DocumentActivated`) | Nenhuma | Médio-Alto (achados #4,#5) | Dois DWG abertos, medir alternadamente, confirmar que Piso/Artigo não "vaza" entre eles | Alta — exige AutoCAD |
| NETLOAD repetido | Nenhuma | Médio (achado #6) | NETLOAD 2×, confirmar que `RefreshData` não corre em duplicado por Idle | Média — exige AutoCAD |
| Build multi-target (`net48`/`net8.0-windows`) | Nenhuma | Médio (achado #14) | Build real com AutoCAD 2025 instalado; confirmar `FileVersion` do DLL `net8.0-windows` | Alta — exige AutoCAD 2025 + build real |
| Licenciamento (`Licenca.cs`) | Nenhuma automatizada (acoplado a rede/AutoCAD) | Médio | Revogar licença, desligar rede, simular Supabase em baixo, instalação nova sem rede | Média |

## 7. Build/distribuição

- **Caminhos:** `bin\Release\net48\TSKTakeOff.dll` (e `bin\Release\
  net8.0-windows\TSKTakeOff.dll` se existir AutoCAD 2025 na máquina de
  build — `TSKTakeOff.csproj:22`). `Deploy/TSKTakeOff.bundle/
  PackageContents.xml` tem dois blocos `<Components>` que escolhem o
  runtime certo por série (`SeriesMin`/`SeriesMax` R24.0-R24.3 vs.
  R25.0-R25.1) — confirmado, incluindo `SeriesMax="R25.1"` presente (item
  já fechado no checklist comercial).
- **DLLs em falta:** `CopyLocalLockFileAssemblies=true`
  (`TSKTakeOff.csproj:48`) garante que o ClosedXML e dependências
  acompanham a DLL; as DLLs do AutoCAD (`ExcludeAssets=runtime`) não são
  copiadas por design — correcto, vêm da instalação do AutoCAD do cliente.
- **Versões divergentes:** risco concreto identificado no achado #14 — o
  alvo `net8.0-windows` provavelmente não recebe o `Version.build`
  incrementado pelo alvo `net48`. `verificar.py:735-747` valida consistência
  entre `.iss`/`PackageContents.xml`/`Version.props`, mas nunca lê o
  `FileVersion` real de um DLL compilado — não apanharia este cenário.
- **Artefactos antigos em `Deploy/`:** existem pastas de builds anteriores
  (`Deploy/TSKTakeOff-1.2.6/`, `Deploy/TSKTakeOff-1.2.6-NETLOAD/`) ao lado do
  `TSKTakeOff.bundle/` actual — risco baixo de confundir qual pasta é a
  distribuição corrente ao preparar um release manualmente.
- **Assinatura:** confirmado de novo, com citação exacta — `SignTool=
  assinatura`/`SignedUninstaller=yes` comentados
  (`Deploy/Installer/TSKTakeOff.iss:64-65`); `assinar.ps1` só corre com
  `-Assinar` explícito e sai em silêncio (sem erro) se
  `TSK_CERT_THUMBPRINT` não estiver definida — mesmo pedindo para assinar,
  sem certificado a build "passa" sem assinar.
- **`net8.0-windows` (AutoCAD 2025+):** confirmado, mais uma vez — **NÃO
  VERIFICADO**. O alvo só entra na compilação se existir
  `$(ProgramFiles)\Autodesk\AutoCAD 2025\acmgd.dll` na máquina
  (`TSKTakeOff.csproj:22`), condição nunca satisfeita nas máquinas usadas
  até agora para build real (ver "Registo de progresso" do plano — todas
  as builds reais citadas são AutoCAD 2021/`net48`).

## 8. Roadmap em 3 fases

**Fase 1 — alto impacto / baixo risco (semanas):**
1. Corrigir a coluna "Item" vazia no modo item do Excel ao vivo (achado #3).
2. Isolar a leitura por entidade em `Leitura.Tudo` com try/catch por item
   (achado #2) e registar (`Log`) quando `AlvRepo.LerParede`/
   `LerParedeDeHatch` caem no `catch` de área (achado #1).
3. Envolver `MedExportImpl`/`TSKEXPORT` em `Util.Seguro` (achado #11).
4. Verificar o retorno de `AlvRepo.RemoverParede` em `Palette.cs:4147`
   (achado #10) e generalizar o cast `Entity` a `FacRepo.Editar` (achado #9).
5. Confirmar/corrigir a sincronização de versão do alvo `net8.0-windows`
   (achado #14) — só verificável com build real, mas a condição no
   `.csproj` pode ser corrigida e revista por leitura primeiro.
6. Validar com um teste manual no AutoCAD o comportamento com dois
   documentos abertos (achados #4, #5) e com NETLOAD repetido (achado #6) —
   são os itens de maior incerteza arquitectural.

**Fase 2 — robustez comercial (1-2 meses):**
1. Assinatura de código (compra de certificado + activar `assinar.ps1` por
   omissão no build de release).
2. Fechar o EULA e a política de privacidade com revisão jurídica real
   (já em rascunho), decidir e-mail de suporte oficial e prazo de trial.
3. Automatizar a emissão de licença (hoje é um INSERT SQL manual por
   cliente) e considerar um mecanismo mínimo de auto-update.
4. Cobrir com testes de fumo (mesmo que manuais e não automatizados) os
   três modos do Excel ao vivo e os importadores, dado que são os
   ficheiros de maior risco sem qualquer rede de segurança automatizada.

**Fase 3 — evolução arquitectural (contínua, sem pressa):**
1. Considerar migrar o esquema XData posicional (`AlvRepo`/`Fachada`/
   `Contagem`) para chaves nomeadas nos PRÓXIMOS campos a acrescentar, sem
   reescrever o que já existe (mantém compatibilidade).
2. Reduzir o "God Object" `Palette.MedPanelControl` extraindo
   responsabilidades (ex.: os `ResultadosTreeControl.cs`/
   `PropriedadesControl.cs` que o plano já previa e nunca se tornaram
   necessários) — só se uma alteração futura o justificar, não como
   exercício isolado.
3. Se a validação manual confirmar problemas reais com múltiplos
   documentos, mover `Config`/`FachadaConfig`/`ContagemConfig` de estático
   por processo para estado por `Document`.

## 9. O que NÃO fazer agora

- Não reescrever `Palette.cs` nem extrair `ResultadosTreeControl.cs`/
  `PropriedadesControl.cs` só por estética — o padrão actual funciona e
  está testado indirectamente; extrair sem uma razão funcional concreta é
  risco sem benefício claro.
- Não trocar o esquema XData existente para um formato nomeado
  retroactivamente — quebraria compatibilidade com desenhos já medidos por
  clientes reais. Só aplicar chaves nomeadas a campos NOVOS.
- Não introduzir microserviços, base de dados central para os dados de
  medição, ou qualquer arquitectura distribuída — os dados vivem no DWG do
  cliente por decisão de produto (linha de venda: "os teus dados ficam no
  teu ficheiro"), e é uma decisão correcta a manter.
- Não activar `VirtualMode` no `DataGridView` da árvore sem primeiro medir
  que o `Rows.AddRange` actual deixou de aguentar o volume real — não há
  evidência de que seja necessário.
- Não tentar "resolver" a ribbon por código vs. CUIX (exigência formal da
  Autodesk Store) como parte desta auditoria — é uma decisão de produto
  cara de mudar, já documentada como conflito no checklist comercial.

## 10. Notas

- **Qualidade técnica:** acima da média para um plugin comercial de nicho —
  comentários que documentam o "porquê", não só o "o quê", e um histórico
  de bugs corrigidos e explicados no próprio código.
- **Arquitectura:** domínio puro bem separado; UI e persistência
  aceitáveis mas com um padrão recorrente de estado estático global que
  nunca foi validado com múltiplos documentos — é a maior incerteza desta
  auditoria, não um bug confirmado.
- **Testes:** excelente cobertura da lógica pura (341 testes), zero
  cobertura automatizada nos ficheiros que tocam COM/Excel/importação —
  exactamente onde os bugs mais caros (achado #3) escondem-se.
- **Prontidão comercial:** as peças de produto (EULA, privacidade,
  suporte, auto-update, assinatura) estão identificadas e em progresso,
  não ignoradas — mas nenhuma está fechada.
- **Geral:** o maior risco não é um bug espectacular, é a combinação de
  vários silêncios pequenos (excepções engolidas em pontos de leitura,
  retorno ignorado, contagens de exclusão que não existem) que tornam um
  total errado difícil de diagnosticar quando acontece.
- **O que faria subir para 9/10:** matriz manual da Fase 8 do plano feita
  no AutoCAD real (incluindo dois documentos abertos), build `net8.0-
  windows` real e validado, os 5 primeiros itens da Fase 1 do roadmap
  acima corrigidos, e assinatura de código activa.
