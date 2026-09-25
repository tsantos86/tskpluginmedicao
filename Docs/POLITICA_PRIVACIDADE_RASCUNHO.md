# RASCUNHO — NÃO TEM VALIDADE LEGAL ATÉ SER REVISTO POR UM ADVOGADO

**Gerado automaticamente a partir do que o código realmente faz; pode conter
erros ou omissões com consequências legais reais (nomeadamente RGPD). Não
publicar, não distribuir e não anexar a nenhuma venda/instalação como está.**

---

# Política de privacidade — TSK TakeOff (rascunho técnico)

Este documento descreve, com base numa leitura do código-fonte a
2026-09-25, que dados o plugin TSK TakeOff recolhe, para quê, onde ficam
guardados e como pedir a sua remoção. **Não substitui aconselhamento
jurídico** — falta decidir a base legal de cada tratamento à luz do RGPD
(interesse legítimo vs. consentimento), o prazo de conservação de cada
tabela, e nomear um responsável pelo tratamento antes de este documento
poder ser usado com clientes reais.

## 1. O que este documento NÃO cobre

- O conteúdo dos desenhos, medições, artigos ou orçamentos: fica sempre no
  DWG do próprio cliente (XData) ou no Excel exportado, nunca é enviado
  para nenhum servidor do fornecedor.
- Dados de faturação/pagamento — não há integração de pagamento no código
  revisto; se existir noutro sistema (ex.: o processo de venda da
  licença), fica fora do âmbito deste ficheiro.

## 2. Duas recolhas de dados distintas, com garantias DIFERENTES

Há uma inconsistência real entre o que é dito ao utilizador sobre
telemetria e o que a ativação de licença paga efetivamente envia — ver a
secção "Achado importante" abaixo antes de publicar isto.

### 2.1 Telemetria de arranque (`Telemetria.cs`, tabela `plugin_sessoes`)

- **Opcional e com consentimento explícito**: nada é enviado até a pessoa
  responder "Sim" ao pedido de consentimento (comando `TSKPRIVACIDADE` ou
  o diálogo equivalente). A escolha fica gravada localmente
  (`%AppData%/TSKTakeOff/privacidade.txt`) e sobrevive a uma
  reinstalação — recusar uma vez não volta a perguntar.
- **O que é enviado**, por arranque do AutoCAD, quando aceite (ver
  `Telemetria.MontarJson`):
  - identificador de instalação (ver secção 3 — GUID opaco, não o nome da
    máquina/utilizador);
  - produto e versão do AutoCAD (`AutocadRuntime`);
  - versão do plugin;
  - versão do sistema operativo (`Environment.OSVersion.VersionString`) e
    cultura/idioma do Windows;
  - data/hora UTC do arranque.
- **O que NÃO é enviado por este caminho**: nome de utilizador Windows,
  nome da máquina, nome de domínio, e-mail, ou qualquer conteúdo de
  desenhos/medições. O próprio comentário em `Supabase/schema.sql` (tabela
  `plugin_sessoes`) documenta esta decisão deliberada.
- Fica sempre também uma cópia local em
  `%AppData%/TSKTakeOff/sessoes.log`, enviada ou não.
- Comando `TSKPRIVACIDADE` mostra o estado atual e permite mudar de ideias
  a qualquer momento, nos dois sentidos.

### 2.2 Ativação e verificação de licença (`Licenca.cs`, tabelas
`activacoes`/`licencas`)

Esta recolha **não é opcional** — é necessária para o plugin funcionar
além do período de avaliação, e envia mais do que a telemetria:

- **Ativação de licença paga** (`Licenca.Activar`, função `activar_licenca`):
  envia `p_maquina` = `Licenca.Maquina`, que é
  `NOME_DA_MAQUINA\NOME_DE_UTILIZADOR_WINDOWS` em maiúsculas (não o
  identificador opaco da secção 3), e ainda `p_utilizador` =
  `Environment.UserName` em separado. Ambos ficam guardados em texto
  simples na tabela `activacoes` (colunas `maquina`, `utilizador`).
- **Pedido de avaliação gratuita** (`Licenca.PedirTrial`, função
  `pedir_trial`): envia o identificador opaco da instalação (não o nome da
  máquina) como `p_maquina` por compatibilidade de nome de parâmetro, mas
  envia também `p_utilizador` = `Environment.UserName` e o e-mail indicado
  pela pessoa para iniciar o trial.
- Em ambos os casos: versão do AutoCAD e do plugin.
- **Revalidação periódica** (`VerificarOnline`): envia só o código da
  licença e o identificador de máquina já registado — sem dados novos.

## 3. Identificador de instalação (o "GUID opaco")

`Licenca.IdentificadorInstalacao` é um GUID gerado na primeira utilização,
guardado cifrado com DPAPI (preso ao utilizador Windows daquela máquina)
em `%AppData%/TSKTakeOff/instalacao.dat`, e reutilizado pela telemetria
(`Telemetria.IdInstalacao`) e pelo pedido de trial. Não deriva do nome da
máquina, do utilizador ou do domínio, e mantém-se igual se o computador for
renomeado.

## 4. Onde os dados ficam guardados

Num projeto Supabase (PostgreSQL gerido), cuja configuração
(`Deploy/supabase.json` — confirmado que o ficheiro existe; URL e chave
**não** reproduzidas aqui por não pertencerem a um documento público) é
distribuída com o instalador. As tabelas relevantes, com RLS (row level
security) activo:

- `plugin_sessoes` — telemetria, sem política de leitura para o cliente
  anon (só inserção é permitida; ninguém de fora lê estes dados pela API
  pública).
- `licencas` / `activacoes` — dados de licenciamento, incluindo
  `maquina`/`utilizador` da secção 2.2.

Este documento não confirma a localização geográfica do datacenter
Supabase nem o tempo de retenção de cada tabela — falta decidir/preencher
antes de publicar.

## 5. Direito de acesso e remoção

- **Localmente**: o comando `TSKLICENCARESET` remove a licença guardada
  neste posto (ficheiro local), mas **não** apaga nem os dados já enviados
  ao servidor nem a linha correspondente em `activacoes`/`plugin_sessoes`
  — isso continua a exigir pedido manual ao suporte.
- **No servidor**: não existe, no código revisto, nenhum mecanismo
  automático de exportação/remoção de dados por pedido do titular
  (nenhum comando ou função RPC para isso). Um pedido de acesso/remoção ao
  abrigo do RGPD tem, hoje, de ser tratado manualmente no Supabase.
- Contacto para exercer estes direitos: o mesmo e-mail de suporte usado no
  resto do projeto (ver nota abaixo sobre a inconsistência entre
  `tsantos.fullstack@gmail.com` em `Licenca.cs` e `suporte@tsktakeoff.pt`
  em `Deploy/TSKTakeOff.bundle/PackageContents.xml` — já sinalizada nos
  PRs do checklist comercial e do EULA).

## 6. Achado importante para revisão antes de publicar

O texto mostrado ao utilizador em `Telemetria.TextoExplicativo()` diz
explicitamente "NÃO é enviado: o teu nome de utilizador, da máquina ou do
domínio" — o que é verdade **só para a telemetria de arranque**. A
ativação de licença paga (`Licenca.Activar`) e o pedido de avaliação
gratuita (`Licenca.PedirTrial`) enviam, sim, `Environment.UserName`, e a
ativação paga envia também o nome da máquina, guardados em texto simples
na tabela `activacoes`. Não é um problema de código a corrigir por esta
tarefa (é comportamento intencional e documentado nos comentários do
próprio `Licenca.cs`, "identificador histórico de activações pagas"), mas
a política de privacidade final não pode reutilizar a frase da telemetria
como se cobrisse todo o produto — precisa de uma secção própria para a
licença, como este rascunho já faz acima.

## 7. Pendente antes de qualquer uso real deste documento

- Confirmar/decidir a base legal do tratamento (RGPD, art. 6.º) para cada
  uma das duas recolhas — telemetria por consentimento é clara; a recolha
  de nome de máquina/utilizador na ativação paga, sendo obrigatória para
  usar o produto, precisa de enquadramento próprio (execução de contrato?).
- Decidir e documentar o prazo de conservação de cada tabela.
- Nomear formalmente um responsável pelo tratamento e um contacto de
  encarregado de proteção de dados, se aplicável.
- Resolver a inconsistência do e-mail de suporte (secção 5) antes de a
  citar como canal oficial de exercício de direitos.
- Construir um processo real (ainda que manual) de resposta a pedidos de
  acesso/remoção, com prazo de resposta.
- Revisão jurídica completa, incluindo transferências internacionais de
  dados consoante a região do datacenter Supabase.
