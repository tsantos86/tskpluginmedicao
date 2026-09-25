# TSK TakeOff — Checklist de prontidão comercial / Autodesk Store

Estado apurado em 2026-09-22, a partir das diretrizes reais do publisher da
Autodesk (aps.autodesk.com/marketplace/publisher-center/autocad-publisher-guidelines)
cruzadas com o código e a documentação deste repositório. Cada linha diz
onde no projeto se confirmou o estado — não é opinião, é o que o código e os
ficheiros de configuração realmente contêm nesta data.

Legenda: ✅ feito · ⚠️ por confirmar/parcial · ❌ falta

| # | Item | Estado | Onde se confirma |
|---|------|--------|-------------------|
| 1 | Ribbon por CUIX parcial (exigido pela Autodesk) | ❌ falta | `README.md:57` diz explicitamente "É criada por código — **não existe CUIX para carregar**, basta o NETLOAD da DLL." Conflito direto com a diretriz do publisher. Não resolvido nesta sessão — só documentado. |
| 2 | Autoloader (`PackageContents.xml`, pasta `ApplicationPlugins`) | ✅ feito | `Deploy/TSKTakeOff.bundle/PackageContents.xml` existe, com dois blocos `<Components>` (net48 para 2021-2024, net8.0-windows para 2025-2026) e `LoadOnAutoCADStartup="True"`. |
| 3 | `SeriesMax` no `RuntimeRequirements` do `PackageContents.xml` | ✅ feito | `Deploy/TSKTakeOff.bundle/PackageContents.xml` — `SeriesMax="R25.1"` está presente no bloco `RuntimeRequirements` de topo e no segundo bloco `<Components>` (AutoCAD 2025/2026). Não está em falta, ao contrário do que a conversa de origem assumia. |
| 4 | Instalador com privilégio de administrador | ⚠️ decisão deliberada, não falta | `Deploy/Installer/TSKTakeOff.iss` — `PrivilegesRequired=lowest`, instala em `{userappdata}\Autodesk\ApplicationPlugins\...`. O próprio comentário no `.iss` explica que é intencional ("evita o bloqueio típico dos departamentos de informática"). Não exige admin — o item original da fila presumia que faltava privilégio; na verdade a ausência é a escolha, não uma lacuna. |
| 5 | Licenciamento com trial funcional | ✅ feito | `Supabase/LICENCIAMENTO.md`, `Licenca.cs`. Nota: a validade por omissão documentada é **30 dias** (`Supabase/LICENCIAMENTO.md:19`, campo `dias` no INSERT SQL), não 15 como uma conversa anterior assumira — corrigido aqui pela leitura do ficheiro. |
| 6 | Assinatura digital do `.dll`/`.msi` (SignTool + certificado) | ❌ falta | `Deploy/Installer/TSKTakeOff.iss` tem as linhas `SignTool=assinatura` / `SignedUninstaller=yes` **comentadas**, com nota no próprio ficheiro: "Sem isto o Windows mostra 'Editor desconhecido'...". Nenhum passo de assinatura em `TSKTakeOff.csproj` nem nos scripts de `Deploy/`. É despesa/decisão do utilizador (comprar certificado de uma CA), não algo para o agente resolver. |
| 7 | Estabilidade validada (Fase 8, "matriz manual no AutoCAD") | ❌ falta | `Docs/PLANO_PALETA_RESULTADOS_COMPACTA.md`, secção "Matriz manual no AutoCAD" — todos os itens por marcar. Não verificável neste sandbox (sem AutoCAD). |
| 8 | `net8.0-windows` (AutoCAD 2025+) validado | ❌ falta | Mesmo documento do plano — "Validar net48 e net8.0-windows quando as referências do AutoCAD 2025 estiverem disponíveis" continua por marcar. O `PackageContents.xml` já declara o bloco net8.0-windows (item 2/3), mas nunca foi corrido/testado num AutoCAD 2025/2026 real. |
| 9 | Emissão de licença é manual (INSERT SQL no Supabase por cliente) | ❌ não escala | `Supabase/LICENCIAMENTO.md`, secção "Emitir uma licença" — processo é literalmente um `insert into public.licencas (...)` copiado à mão no SQL Editor do Supabase para cada cliente. Sem painel de administração nem automação. |
| 10 | EULA / termos de uso | ❌ não existe | Nenhum ficheiro em `Docs/` com esse conteúdo nesta data. Rascunho pedido como item separado da fila (`Docs/EULA_RASCUNHO.md`). |
| 11 | Política de privacidade | ❌ não existe | Idem — apesar de `Telemetria.cs` recolher dados de sessão (produto, versão do AutoCAD, versão do plugin, sistema operativo, cultura, identificador de instalação opaco — ver `Telemetria.cs`, tabela `plugin_sessoes`), não há nenhum documento RGPD no repositório. Rascunho pedido como item separado da fila (`Docs/POLITICA_PRIVACIDADE_RASCUNHO.md`). |
| 12 | Canal de suporte | ⚠️ inconsistente entre ficheiros | `Licenca.cs:34` — `EmailContacto = "tsantos.fullstack@gmail.com"` (Gmail pessoal), usado em diálogos do plugin. Mas `Deploy/TSKTakeOff.bundle/PackageContents.xml` declara `<CompanyDetails ... Email="suporte@tsktakeoff.pt" />` — um endereço de empresa diferente. Os dois canais não coincidem; vale a pena decidir qual é o oficial antes de publicar. |
| 13 | Mecanismo de auto-update | ❌ não encontrado | `grep` por `auto.update`, `CheckForUpdate`, verificação de nova versão/GitHub release em todo o `.cs` do projeto (fora de `Tests/`) não encontrou nada. `PluginInit.Initialize()` em `Commands.cs` não faz nenhuma verificação de versão no arranque — confirmado por leitura do método. Atualização é sempre manual (reinstalar o `.exe`). |
| 14 | Interface só em português | ⚠️ decisão de mercado, não bug | Confirmado pela ausência de recursos multi-idioma nos ficheiros de UI verificados (`Ribbon.cs` e outros não têm strings condicionais por cultura). Registado apenas — não é para "corrigir". |

## Notas de leitura desta sessão

- Dois itens da lista original vieram invertidos em relação ao código real:
  o `SeriesMax` (item 3) já existe, e o privilégio de administrador do
  instalador (item 4) é uma escolha deliberada de instalar por utilizador,
  não uma omissão. Corrigidos aqui com a fonte exata.
- O prazo de trial (item 5) é 30 dias por omissão no schema/documentação
  atual, não 15 — o número de 15 dias não foi encontrado em nenhum ficheiro
  do repositório nesta verificação.
- Este documento é só de estado (confirmação, não implementação), como
  pedido pelo item da fila. Nenhum código foi alterado nesta sessão.
