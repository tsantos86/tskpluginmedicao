# RASCUNHO — NÃO TEM VALIDADE LEGAL ATÉ SER REVISTO POR UM ADVOGADO

**Este documento foi gerado automaticamente a partir do que o código do TSK
TakeOff realmente faz hoje (2026-09-24). Não é um contrato pronto a usar, nem
foi redigido ou revisto por um jurista. Pode conter erros, omissões ou
formulações sem efeito legal. Não distribuir, publicar nem anexar a nenhuma
venda ou instalação antes de revisão por um advogado especializado em
software e proteção de dados (RGPD).**

---

## Termos de Licenciamento de Utilizador Final — TSK TakeOff

### 1. Objeto

Este acordo regula a utilização do plugin **TSK TakeOff** (a "Aplicação"),
um suplemento para o AutoCAD desenvolvido pela TSK Digital / Casquilho, que
permite medir e orçamentar elementos de construção diretamente sobre
desenhos AutoCAD.

### 2. Concessão de licença

2.1. A Aplicação é licenciada, não vendida. O cliente ("Titular da Licença")
recebe um direito de utilização não exclusivo e intransmissível, limitado ao
número de postos contratado.

2.2. Cada licença é identificada por um **código** (formato `TSK-XXXX-XXXX-
XXXX`) e é emitida com um número de **dias de validade** e um número máximo
de **dispositivos** (`max_dispositivos`) que a podem ativar em simultâneo
(ver `Supabase/LICENCIAMENTO.md`, secção "Emitir uma licença"). Estes dois
valores são definidos por posto/contrato no momento da emissão e não estão
fixos neste documento — a nota de encomenda ou fatura de cada cliente é que
define o número de postos e a duração contratados.

2.3. **Identificação do posto.** A Aplicação não usa o nome do computador
nem o nome de utilizador do Windows para identificar um posto licenciado —
usa um identificador aleatório opaco, gerado na primeira utilização e
guardado cifrado localmente (`instalacao.dat`, ver secção 6). Isto significa
que renomear o computador ou a conta do Windows não liberta nem duplica uma
ativação.

2.4. **Revalidação periódica.** Com licença paga ativa, a Aplicação
revalida-se automaticamente junto do servidor a cada arranque (no máximo uma
vez a cada 24 horas) e renova a janela de validade local. Sem ligação à
internet, continua a valer a validade guardada localmente da última
verificação — a obra não fica parada por falta de rede. *(Nota para revisão
jurídica: o código define uma tolerância técnica interna de até 30 dias sem
contacto com o servidor antes de recusar a licença, ver `Licenca.cs`,
`DiasToleranciaOffline`; não é uma promessa contratual de "30 dias offline
garantidos", é um valor de engenharia que pode mudar em atualizações
futuras.)*

2.5. **Avaliação gratuita (trial).** É oferecido um período de avaliação
gratuita associado ao par posto+e-mail informado no início. *(Nota para
revisão jurídica — discrepância encontrada no código, a resolver antes de
publicar: a mensagem mostrada ao utilizador no primeiro arranque anuncia
"avaliação de 15 dias" (`Licenca.cs`, método `PodeMedir`), mas a duração
real é a que o servidor devolver na resposta a `pedir_trial`; a
documentação operacional interna, `Supabase/LICENCIAMENTO.md`, refere 30
dias por omissão para licenças em geral. Antes de publicar este EULA, decidir
e alinhar um único valor oficial de dias de avaliação em código, documentação
e neste texto.)*

2.6. Terminada a avaliação sem ativação paga, ou expirada/revogada uma
licença paga, os comandos de **medição** deixam de funcionar. O Titular da
Licença mantém, em qualquer circunstância, acesso de leitura ao painel de
resultados e capacidade de **exportar** as medições já efetuadas — a
Aplicação nunca retém trabalho já produzido (ver `Licenca.cs`, comentário de
cabeçalho: "expirada, bloqueia MEDIR mas deixa ver o painel e exportar").

### 3. Restrições de uso

3.1. É proibido:

- usar a Aplicação além do número de postos/dispositivos contratado
  (`max_dispositivos`);
- ceder, sublicenciar, alugar ou distribuir a Aplicação ou o código de
  licença a terceiros;
- contornar, desativar ou tentar remover o mecanismo de verificação de
  licença;
- fazer engenharia reversa, descompilar ou desmontar a Aplicação, exceto na
  medida em que a lei aplicável expressamente o permita e não possa ser
  contratualmente excluído.

3.2. *(Nota para revisão jurídica)* O binário distribuído é ofuscado
(ConfuserEx — ver `Deploy/Obfuscation/confuser.crproj`) precisamente para
dificultar engenharia reversa e a remoção da verificação de licença. O
próprio comentário no ficheiro de configuração da ofuscação é honesto sobre
os limites técnicos desta proteção: "isto não torna a DLL inquebrável […] o
que a ofuscação faz é subir o custo […] o suficiente para dissuadir o
utilizador comum". A cláusula de proibição de engenharia reversa acima deve
refletir esse mesmo realismo: é uma medida contratual e dissuasora, não uma
garantia técnica de inviolabilidade — não prometer no texto final algo que o
próprio código não garante.

### 4. Bloqueio e revogação

4.1. O Titular da Licença/Fornecedor pode bloquear um posto individual (por
exemplo, um computador que deixou de pertencer à empresa) ou revogar uma
licença inteira. Estas ações, quando feitas do lado do servidor, produzem
efeito na próxima revalidação do posto afetado — no limite, dentro do prazo
de validade ainda guardado localmente, ou de imediato se houver ligação à
internet nesse momento (ver `Supabase/LICENCIAMENTO.md`, secção "Gerir").

4.2. *(Nota para revisão jurídica)* Decidir e documentar aqui, em linguagem
contratual, em que circunstâncias o Fornecedor pode revogar ou bloquear uma
licença já paga (incumprimento de pagamento, violação destes termos, pedido
do próprio cliente) — o código só implementa o mecanismo técnico, não as
condições comerciais de quando o usar.

### 5. Propriedade intelectual

5.1. A Aplicação, incluindo o seu código-fonte, design e documentação,
permanece propriedade exclusiva do Fornecedor. Esta licença não transfere
qualquer direito de propriedade intelectual sobre a Aplicação.

5.2. Os desenhos, medições e dados de orçamento produzidos pelo Titular da
Licença com a Aplicação são e continuam a ser propriedade do Titular da
Licença. A Aplicação guarda os resultados de medição diretamente no próprio
ficheiro DWG do cliente (XData), não numa base de dados do Fornecedor — o
Fornecedor não tem acesso, cópia nem visibilidade sobre o conteúdo dos
desenhos ou medições de nenhum cliente.

### 6. Dados recolhidos e privacidade

6.1. Para efeitos de licenciamento, o Fornecedor recebe apenas: o código de
licença, o identificador opaco do posto (não o nome do computador ou do
utilizador — ver secção 2.3), a versão do AutoCAD e da Aplicação, e, no
início de uma avaliação gratuita, o e-mail fornecido nesse momento.

6.2. Adicionalmente, e apenas mediante consentimento explícito e revogável a
qualquer momento pelo comando `TSKPRIVACIDADE`, a Aplicação pode enviar um
registo por sessão com a versão do AutoCAD, a versão da Aplicação, a versão
do Windows, o idioma do sistema e a data/hora de arranque — nunca o nome de
utilizador, o nome do computador, o domínio, nem qualquer conteúdo dos
desenhos, medições ou ficheiros Excel (ver `Telemetria.cs`, `TextoExplicativo`
e `Docs/POLITICA_PRIVACIDADE_RASCUNHO.md` para o detalhe completo — este
EULA trata só do essencial de licenciamento; a política de privacidade é o
documento de referência para RGPD).

6.3. Estes dados ficam alojados num projeto Supabase operado pelo
Fornecedor, protegido por políticas de acesso que impedem o cliente de
licença (chave pública/anon) de ler, listar ou modificar registos de
qualquer posto — só o Fornecedor, com credenciais privilegiadas, tem esse
acesso (ver `Supabase/schema.sql`, secção RLS).

### 7. Limitação de responsabilidade

7.1. A Aplicação é fornecida "tal como está" ("as is"). O Fornecedor não
garante que a Aplicação esteja isenta de erros, nem assume responsabilidade
por medições, quantidades ou orçamentos produzidos pelo utilizador — a
verificação da correção dos resultados de medição é sempre responsabilidade
do utilizador que os usa em obra ou orçamento.

7.2. *(Nota para revisão jurídica)* Esta cláusula deve ser redigida com
especial cuidado: trata-se de uma ferramenta de medição usada para efeitos
de orçamentação de obra, onde um erro de quantidade pode ter consequências
financeiras reais para o cliente do Titular da Licença. Um advogado deve
avaliar até que ponto uma cláusula de exclusão total de responsabilidade é
válida e proporcional neste contexto de uso comercial, à luz da lei
portuguesa/da UE aplicável.

7.3. O mecanismo de licenciamento é, como o próprio código documenta
internamente, "um dissuasor e uma forma de saber quem usa, não uma
fortaleza" (ver `Supabase/LICENCIAMENTO.md`, secção "Segurança"). O
Fornecedor não garante proteção absoluta contra utilização não autorizada e
reserva-se o direito de perseguir violações destes termos pelos meios legais
aplicáveis.

### 8. Suporte

8.1. *(Nota para revisão jurídica/comercial — discrepância encontrada no
código, a resolver antes de publicar)* O contacto de suporte mostrado ao
utilizador dentro da Aplicação (diálogo "Sobre", diálogo de licença expirada)
é atualmente um endereço Gmail pessoal, `tsantos.fullstack@gmail.com`
(`Licenca.cs`, constante `EmailContacto`). Já existe, noutro ficheiro do
projeto (`Deploy/TSKTakeOff.bundle/PackageContents.xml`), um e-mail de
empresa diferente, `suporte@tsktakeoff.pt`. Decidir qual é o canal oficial
de suporte antes de publicar este EULA, e citar aqui o mesmo endereço usado
dentro da própria Aplicação — este documento não inventa um terceiro
endereço.

### 9. Vigência e rescisão

9.1. Esta licença mantém-se em vigor enquanto o período contratado
(secção 2.2) permanecer válido e renovado, ou até ser rescindida por
qualquer das partes nos termos deste acordo.

9.2. A violação de qualquer restrição da secção 3 pode determinar a
revogação imediata da licença, sem prejuízo de outras medidas legais ao
dispor do Fornecedor.

### 10. Lei aplicável

*(Nota para revisão jurídica: por omissão, e dado o mercado de origem
(português, ver interface só em português), o natural é lei portuguesa e
foro português — mas isto é uma decisão comercial/jurídica do Fornecedor,
não uma inferência do código, e deve ser confirmada explicitamente.)*

---

## Notas para quem for rever este rascunho

Resumo dos pontos que o código deixa em aberto ou inconsistente e que este
documento sinalizou em vez de decidir sozinho:

1. **Dias de avaliação gratuita**: mensagem no código diz 15 dias
   (`Licenca.cs`), documentação operacional diz 30 dias por omissão
   (`Supabase/LICENCIAMENTO.md`) — alinhar antes de publicar (secção 2.5).
2. **E-mail de suporte oficial**: Gmail pessoal no código vs. domínio da
   empresa no `PackageContents.xml` — escolher um (secção 8.1).
3. **Condições de revogação/bloqueio de uma licença paga**: o código só
   implementa o "como"; falta decidir e documentar o "quando" (secção 4.2).
4. **Alcance real da cláusula de limitação de responsabilidade** num
   contexto de orçamentação de obra — carece de avaliação jurídica
   específica (secção 7.2).
5. **Lei aplicável e foro** — não inferido do código, decisão do Fornecedor
   (secção 10).

Este rascunho não cobre pagamento, faturação, reembolsos, nem SLA de
suporte, porque não encontrei nenhuma dessas regras implementadas no código
— são decisões comerciais que ainda não existem em lado nenhum verificável
do repositório e que a próxima pessoa a rever isto terá de definir de raiz.
