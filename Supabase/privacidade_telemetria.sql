-- =====================================================================
--  Telemetria sem dados pessoais — migração
--
--  O plugin deixou de enviar `utilizador`, `maquina` e `dominio` e passou a
--  enviar `instalacao`: um identificador aleatório persistente, cifrado com
--  DPAPI no cliente antes de sair da máquina.
--
--  Correr no SQL Editor do Supabase. A PARTE 1 é segura e basta para o
--  plugin voltar a inserir. A PARTE 2 apaga dados e fica por tua conta.
-- =====================================================================


-- ---------------------------------------------------------------------
--  PARTE 1 — obrigatória, não destrutiva
--
--  Sem isto, o INSERT do plugin novo falha: PostgREST rejeita uma coluna
--  que não existe, e as sessões ficam todas em fila no cliente.
-- ---------------------------------------------------------------------

alter table public.plugin_sessoes
    add column if not exists instalacao text;

create index if not exists idx_sessoes_instalacao
    on public.plugin_sessoes(instalacao);

-- As antigas passam a aceitar nulo: o plugin novo já não as preenche e, sem
-- isto, uma restrição NOT NULL herdada faria o insert falhar em silêncio.
alter table public.plugin_sessoes alter column utilizador drop not null;
alter table public.plugin_sessoes alter column maquina    drop not null;
alter table public.plugin_sessoes alter column dominio    drop not null;


-- ---------------------------------------------------------------------
--  PARTE 2 — destrutiva. Ler antes de correr.
--
--  As linhas gravadas até aqui têm nome de utilizador, de máquina e de
--  domínio, recolhidos sem consentimento explícito. Enquanto existirem,
--  esta tabela é um ficheiro de dados pessoais.
--
--  Duas opções, conforme o que queiras conservar:
--
--   (a) converter o histórico e apagar só as colunas pessoais — mantém a
--       contagem de instalações antigas, mas o ID NÃO coincide com o que o
--       plugin envia agora; ficam a valer como "instalação antiga, identidade
--       desconhecida".
--
--   (b) apagar o histórico todo. Mais limpo, e com uma base de pilotos
--       pequena não se perde praticamente nada.
--
--  Descomenta UM dos blocos. Nenhum corre por omissão — apagar dados de
--  clientes não é coisa que deva acontecer por se ter aberto um ficheiro.
-- ---------------------------------------------------------------------

-- ---- (a) conservar o histórico, anonimizado -------------------------
-- update public.plugin_sessoes
--    set instalacao = 'legado:' || encode(
--            digest(coalesce(maquina,'') || '|' ||
--                   coalesce(utilizador,'') || '|' ||
--                   coalesce(dominio,''), 'sha256'), 'hex')
--  where instalacao is null;
--
-- alter table public.plugin_sessoes drop column if exists utilizador;
-- alter table public.plugin_sessoes drop column if exists maquina;
-- alter table public.plugin_sessoes drop column if exists dominio;

-- ---- (b) apagar o histórico ------------------------------------------
-- delete from public.plugin_sessoes;
-- alter table public.plugin_sessoes drop column if exists utilizador;
-- alter table public.plugin_sessoes drop column if exists maquina;
-- alter table public.plugin_sessoes drop column if exists dominio;


-- ---------------------------------------------------------------------
--  Verificação
-- ---------------------------------------------------------------------

select
    case when exists (
        select 1 from information_schema.columns
         where table_schema = 'public'
           and table_name   = 'plugin_sessoes'
           and column_name  = 'instalacao')
    then 'passa: coluna instalacao existe'
    else '>>> FALHA: falta a coluna instalacao — a PARTE 1 não correu'
    end as parte_1;

select
    case when exists (
        select 1 from information_schema.columns
         where table_schema = 'public'
           and table_name   = 'plugin_sessoes'
           and column_name in ('utilizador', 'maquina', 'dominio'))
    then 'aviso: ainda há colunas pessoais (PARTE 2 por correr)'
    else 'passa: sem colunas pessoais'
    end as parte_2;
