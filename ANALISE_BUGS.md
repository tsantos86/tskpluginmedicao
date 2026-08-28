# Análise de bugs — TSK TakeOff · Estado final

> **Todos os bugs detetados na análise de 2026-08-11 foram corrigidos e testados.**
> Estado atual: **168/168 testes a passar**, build limpo (net48 e net8). O que
> resta são riscos de processo — não são bugs de código.
>
> **Verificação de 2026-08-27:** `dotnet test` → 168/168 ✅ · compilação net48 →
> 0 avisos, 0 erros ✅ · compilação net8.0-windows → 0 erros, 2 avisos ✅ ·
> `verificar.py` → sem erros ✅

## ✅ Correções aplicadas (22 pontos)

| # | Ponto | Onde |
|---|---|---|
| 🔴 C | Exportador ClosedXML escrevia `null` em células | `ExcelExporter.cs` |
| 🟠 3.1 | Lineares somiam do Excel ao vivo e do modelo | `Leitura`, `ExcelLiveSync`, `FolhaTemplate`, `Palette`… |
| 🟠 3.2 | `MEDRET` não transformava a polyline pelo UCS | `Commands.cs` |
| 🟡 4.1 | Licença expirada congelava o AutoCAD até 12 s | `Licenca.cs` (timeout 4 s + aviso) |
| 🟡 4.2 | Diálogo de privacidade abria atrás do AutoCAD | `PrivacidadeDialog.cs` |
| 🟡 4.3 | Textos das linhas de artigo perdiam-se no modelo clássico | `ExcelLiveSync`, `FolhaTemplate` |
| 🟡 4.4 | `DisplayAlerts` ficava desligado no Excel do utilizador | `ExcelLiveSync.cs` |
| 🟡 4.5 | `TSKVAO` rejeitava medições por hachura | `Commands.cs` |
| 🟡 4.6 | `Desbloquear` podia destruir o modelo original | `ModeloExcel.cs` |
| 🟡 4.7 | Cache do mapa por nome do desenho (colisão de nomes) | `MapaQuantidades.cs` |
| 🔵 5.1 | Transparência do hatch fixa em 120 em vez de 150 | `Commands.cs` |
| 🔵 5.2 | Linhas em branco consumiam numeração | `FolhaMedicao.cs` |
| 🔵 5.3 | `ContagemConfig.Piso` forçava "PISO 0" e não normalizava | `PaletteContagem.cs` |
| 🔵 5.4 | `label.TransformBy(ucs)` sem try/catch | `Commands.cs` |
| 🔵 5.5 | Materiais só suportavam um nível de título (CAP **ou** ART) | `MedFachada`, `FacRepo`, `PaletteFachada`, `FolhaMedicao` |
| 🔵 5.6 | Cópias de trabalho acumulavam-se em `%TEMP%\TSKTakeOff` | `ModeloExcel.cs` |
| 🔵 5.7 | `ContRepo.Limpar` sensível a maiúsculas | `Contagem.cs` |
| 🔵 5.8 | Excel ROT do utilizador ficava com definições alteradas | `ExcelLiveSync.cs` (repostas todas) |
| 🔵 5.9 | Licença local corrompida voltava a pedir trial | `Licenca.cs` (`.bak` + reparo) |
| 🔵 5.10 | Etiquetas centrais com `Z=0` fixo | `Commands.cs` |
| 🔵 5.11 | `VaoDetector` comparava com `Point3d.Origin` | `DeteccaoVaos.cs` (`Point3d?`) |
| 🔵 5.12 | `TskVao` abria uma transação por vão | `AlvRepo.cs` (`AdicionarVaos`) |

**Extra:** botão **"TSK DIGITAL"** (About) na ribbon (painel Mapas), comando `TSKSOBRE`,
diálogo com marca, texto do produto, versão/build e estado da licença (`SobreDialog.cs`).

**Testes:** 168/168. `verificar.py`: 43 comandos, sem erros e **sem falsos
positivos** — os 4 antigos (variável `p` = `Point3d`, não `Parede`) foram
eliminados na revisão de 2026-08-27.

---

## ✅ Revisão de 2026-08-27

| # | Ponto | Onde |
|---|---|---|
| 🔴 A | `AutoCAD.NET` 25.0.0 pede `AutoCAD.NET.Core` **[25.0.0-V058]**, versão que nunca foi publicada: o restore do alvo net8 morria em NU1102. Passou a **25.0.1** | `TSKTakeOff.csproj` |
| 🔴 B | `verificar.py` dava **35 erros, todos falsos**. Uma aspa solta num comentário (`/// o AutoCAD recusa < > / \ " : ; ...` no `NomeLayer`) abria uma "string" que comia a chaveta do método. Daí saíam o `'}' a mais`, o `Util` sem membros nenhuns, e o verificador sem serventia | `verificar.py` |
| 🟠 C | O `sem_texto()` tirava as strings **antes** dos comentários. Passou a percorrer o ficheiro uma vez, como tokenizador: dentro de um comentário a aspa é texto, dentro de uma string o `//` é texto | `verificar.py` |
| 🟡 D | `new Polyline()` do AutoCAD acusado de ser o `Polyline()` **privado** do `IconFactory`. Um `private static` de outra classe deixou de poder ser apontado como alvo | `verificar.py` |
| 🟡 E | Variáveis de `foreach` sem tipo conhecido davam falsos positivos (`p.X` em `Parede`). Passam a contar como tipo não-fiável e calam o aviso | `verificar.py` |
| 🔵 F | Comentário do csproj dizia que o `PreviewIcons` usa `new IconFactory()`; usa `typeof(IconFactory)` por reflexão | `TSKTakeOff.csproj` |

**Verificado sem tocar no `bin\`:** os fontes foram compilados num csproj
descartável, fora do projeto, para os dois alvos. O `verificar.py` corrigido foi
posto à prova com erros injetados (chaveta a mais, chaveta por fechar, membro
inexistente, estático por qualificar) e com as armadilhas que o partiam (aspa em
comentário, `//` dentro de string, chaveta dentro de string, verbatim
multilinha, literal `'"'`): 10/10.

---

## ⬜ Em aberto (riscos de processo, não bugs)

1. 🟠 **Alvo `net8.0-windows` (AutoCAD 2025–2026) compila, mas nunca correu** —
   em 2026-08-27 os fontes foram compilados contra o `AutoCAD.NET` 25.0.1 e o
   `ClosedXML` 0.104.2 (assemblies de referência do NuGet, sem AutoCAD 2025
   instalado): **0 erros**. Ou seja, as APIs mudadas entre o 24.0→25.0 e o
   ClosedXML 0.95.4→0.104.2 já não são incógnita. Falta o que a compilação não
   prova: **carregar a DLL num AutoCAD 2025 a sério e medir alguma coisa.**
2. ⬜ **Instalador** — por decisão do utilizador, ainda não. Quando se fizer: assinar
   o instalador (SignTool) para não mostrar "Editor desconhecido" e validar o alvo net8.
3. 🟡 Projeto em `rc1` — nunca correu num AutoCAD 2025, nunca mediu uma obra do
   princípio ao fim. As correções acima são exatamente o tipo de falha que só aparece
   em obra; validar num desenho real antes do lançamento.
4. ⚠️ Para testar no AutoCAD: fechar o AutoCAD antes do build net48 (a DLL em
   `bin\Debug\net48` fica bloqueada com o plugin carregado).
5. 🔵 `WebRequest.Create` está obsoleto no .NET 8 (SYSLIB0014) — `Licenca.cs:503`
   e `Telemetria.cs:276`. São os 2 únicos avisos do alvo net8. **Funciona à
   mesma**, não é urgente; quando se mexer, é trocar por `HttpClient`. No net48
   é a API normal e não dá aviso nenhum, por isso a troca tem de compilar nos
   dois alvos.

---

## Segurança (decisões de produto, não bugs)

- Limite de dispositivos conta strings inventáveis (`MÁQUINA\UTILIZADOR`) — dissuasão.
- `plugin_sessoes` sem rate-limit — spam possível (baixo impacto).
- Chave anon no bundle — as RPCs são o controlo real.
- Trial por `MachineName` — corrigido: o trial passa a usar ID persistente protegido por DPAPI e e-mail no servidor. Aplicar `Supabase/correccao_trial_identidade.sql` no projeto antes de distribuir.
