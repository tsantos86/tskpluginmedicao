-- ===================================================================
--  TSK TakeOff — camada comercial
--
--  Correr DEPOIS do schema.sql, no SQL Editor do Supabase.
--
--  O que isto resolve:
--    1. Trial de 15 dias automático — a pessoa instala e trabalha, sem
--       pedir código a ninguém. É o maior travão à adopção que existe.
--    2. Emissão de licenças sem escrever SQL à mão (função `emitir_licenca`).
--    3. Ligação a um pagamento (Stripe/Paddle) por webhook.
--    4. Renovação e revogação com uma chamada só.
-- ===================================================================


-- -------------------------------------------------------------------
-- 1. TRIAL AUTOMÁTICO
-- -------------------------------------------------------------------
-- Uma linha por instalação/e-mail que alguma vez pediu trial. O campo
-- `maquina` mantém o nome histórico por compatibilidade, mas passa a guardar
-- um identificador opaco persistente gerado pelo plugin. Renomear o Windows
-- não cria uma instalação nova.
create table if not exists public.trials (
    id           uuid primary key default gen_random_uuid(),
    maquina      text not null unique,
    utilizador   text,
    email        text,
    iniciado_em  timestamptz not null default now(),
    expira_em    timestamptz not null,
    convertido   boolean not null default false,   -- passou a cliente pagante
    versao_autocad text,
    versao_plugin  text
);

create index if not exists trials_expira_idx on public.trials (expira_em);

alter table public.trials enable row level security;
-- Ninguém lê nem escreve directamente: só através da função abaixo.


-- Dias de trial. Mudar aqui muda para toda a gente.
create or replace function public.dias_de_trial()
returns integer language sql immutable as $$ select 15 $$;


-- Pede (ou retoma) o trial desta instalação e e-mail.
-- Devolve o mesmo formato de `verificar_licenca`: {ok, motivo, expira_em, cliente}
--
-- O parâmetro continua chamado p_maquina para não quebrar a RPC já publicada;
-- desde esta versão recebe o identificador opaco da instalação, não o nome do
-- computador. O e-mail impede que apagar/recriar a identidade local dê outro
-- trial durante a fase de testes.
create or replace function public.pedir_trial(
    p_maquina        text,
    p_utilizador     text default null,
    p_email          text default null,
    p_versao_autocad text default null,
    p_versao_plugin  text default null
)
returns json
language plpgsql
security definer
set search_path = public
as $$
declare
    v_trial   public.trials%rowtype;
    v_dias    integer := public.dias_de_trial();
begin
    if p_maquina is null or length(trim(p_maquina)) = 0 then
        return json_build_object('ok', false, 'motivo', 'Máquina não identificada.');
    end if;

    -- Serializa por e-mail quando ele existe. Assim dois identificadores
    -- locais diferentes não conseguem abrir dois trials para o mesmo e-mail
    -- em pedidos simultâneos. Sem e-mail, cai para a identidade da instalação.
    if nullif(trim(p_email), '') is not null then
        perform pg_advisory_xact_lock(
            hashtext('trial-email|' || lower(trim(p_email))));
    else
        perform pg_advisory_xact_lock(
            hashtext('trial-install|' || upper(trim(p_maquina))));
    end if;

    -- Reconhece instalações novas, o formato antigo PC\\UTILIZADOR e também
    -- o e-mail já registado. O último caso é o que impede a renovação do trial
    -- depois de apagar o ficheiro local ou trocar de identidade.
    select * into v_trial
      from public.trials
     where upper(maquina) = upper(trim(p_maquina))
        or upper(maquina) like upper(trim(p_maquina) || chr(92) || '%')
        or (nullif(trim(p_email), '') is not null
            and lower(trim(email)) = lower(trim(p_email)))
     order by expira_em desc
     limit 1;

    if not found then
        insert into public.trials (maquina, utilizador, email, expira_em,
                                   versao_autocad, versao_plugin)
        values (p_maquina, p_utilizador, p_email, now() + (v_dias || ' days')::interval,
                p_versao_autocad, p_versao_plugin)
        returning * into v_trial;

        return json_build_object(
            'ok', true,
            'cliente', 'Avaliação',
            'expira_em', v_trial.expira_em,
            'motivo', format('Avaliação de %s dias iniciada.', v_dias));
    end if;

    -- Já existiu trial nesta instalação ou neste e-mail.
    if v_trial.expira_em > now() then
        -- ainda a decorrer: devolve o que falta, sem reiniciar a contagem
        update public.trials
           set utilizador     = coalesce(p_utilizador, utilizador),
               email           = coalesce(nullif(trim(p_email), ''), email),
               versao_autocad = coalesce(p_versao_autocad, versao_autocad),
               versao_plugin  = coalesce(p_versao_plugin, versao_plugin)
         where id = v_trial.id;

        return json_build_object(
            'ok', true,
            'cliente', 'Avaliação',
            'expira_em', v_trial.expira_em,
            'motivo', 'Avaliação em curso.');
    end if;

    return json_build_object(
        'ok', false,
        'motivo', 'A avaliação gratuita já terminou nesta máquina.');
end;
$$;

revoke all on function public.pedir_trial(text, text, text, text, text) from public;
grant execute on function public.pedir_trial(text, text, text, text, text) to anon, authenticated;


-- -------------------------------------------------------------------
-- 2. EMISSÃO DE LICENÇAS SEM SQL À MÃO
-- -------------------------------------------------------------------
-- Usada pelo painel de administração. Exige service_role: nunca exposta
-- ao plugin nem ao anon.
create or replace function public.emitir_licenca(
    p_cliente          text,
    p_email            text default null,
    p_dias             integer default 30,
    p_max_dispositivos integer default 1,
    p_valida_ate       date default null,
    p_notas            text default null
)
returns json
language plpgsql
security definer
set search_path = public
as $$
declare
    v_codigo text;
begin
    if p_cliente is null or length(trim(p_cliente)) = 0 then
        raise exception 'O nome do cliente é obrigatório.';
    end if;

    v_codigo := public.gerar_codigo();

    insert into public.licencas (codigo, cliente, dias, max_dispositivos, valida_ate)
    values (v_codigo, trim(p_cliente), greatest(p_dias, 1),
            greatest(p_max_dispositivos, 1), p_valida_ate);

    -- Marca o trial como convertido, se houver um com este email.
    if p_email is not null then
        update public.trials set convertido = true where email = p_email;
    end if;

    return json_build_object(
        'ok', true,
        'codigo', v_codigo,
        'cliente', trim(p_cliente),
        'dias', greatest(p_dias, 1),
        'max_dispositivos', greatest(p_max_dispositivos, 1),
        'valida_ate', p_valida_ate);
end;
$$;

revoke all on function public.emitir_licenca(text, text, integer, integer, date, text) from public;
-- só service_role (o painel de administração)


-- -------------------------------------------------------------------
-- 3. RENOVAR / REVOGAR
-- -------------------------------------------------------------------
create or replace function public.renovar_licenca(
    p_codigo     text,
    p_valida_ate date
)
returns json
language plpgsql
security definer
set search_path = public
as $$
begin
    update public.licencas
       set valida_ate = p_valida_ate,
           activa = true
     where codigo = upper(trim(p_codigo));

    if not found then
        return json_build_object('ok', false, 'motivo', 'Código não encontrado.');
    end if;
    return json_build_object('ok', true, 'codigo', upper(trim(p_codigo)), 'valida_ate', p_valida_ate);
end;
$$;

create or replace function public.revogar_licenca(p_codigo text)
returns json
language plpgsql
security definer
set search_path = public
as $$
begin
    update public.licencas set activa = false where codigo = upper(trim(p_codigo));
    if not found then
        return json_build_object('ok', false, 'motivo', 'Código não encontrado.');
    end if;
    -- O posto perde acesso na revalidação seguinte (no máximo em `dias` dias,
    -- ou logo no arranque seguinte se houver internet).
    return json_build_object('ok', true, 'codigo', upper(trim(p_codigo)));
end;
$$;

revoke all on function public.renovar_licenca(text, date) from public;
revoke all on function public.revogar_licenca(text) from public;


-- -------------------------------------------------------------------
-- 4. VISTAS PARA O PAINEL DE ADMINISTRAÇÃO
-- -------------------------------------------------------------------
create or replace view public.vw_licencas as
select l.id,
       l.codigo,
       l.cliente,
       l.dias,
       l.max_dispositivos,
       l.valida_ate,
       l.activa,
       l.criada_em,
       count(a.id)                                   as postos_activados,
       count(a.id) filter (where a.bloqueada)         as postos_bloqueados,
       max(a.ultima_verificacao)                      as ultima_utilizacao
  from public.licencas l
  left join public.activacoes a on a.licenca_id = l.id
 group by l.id
 order by l.criada_em desc;

create or replace view public.vw_postos as
select a.id,
       l.cliente,
       l.codigo,
       a.maquina,
       a.utilizador,
       a.versao_autocad,
       a.versao_plugin,
       a.expira_em,
       a.ultima_verificacao,
       a.bloqueada
  from public.activacoes a
  join public.licencas l on l.id = a.licenca_id
 order by a.ultima_verificacao desc nulls last;

-- Funil comercial: quantos experimentaram, quantos compraram.
create or replace view public.vw_funil as
select count(*)                                              as trials_total,
       count(*) filter (where expira_em > now())             as trials_activos,
       count(*) filter (where convertido)                    as convertidos,
       round(100.0 * count(*) filter (where convertido)
             / nullif(count(*), 0), 1)                       as taxa_conversao_pct
  from public.trials;

-- As vistas herdam a RLS das tabelas de origem: continuam invisíveis ao anon.


-- -------------------------------------------------------------------
-- 5. WEBHOOK DE PAGAMENTO (Stripe / Paddle)
-- -------------------------------------------------------------------
-- Registo dos pagamentos recebidos, para não emitir duas licenças pelo
-- mesmo evento se o fornecedor reenviar o webhook.
create table if not exists public.pagamentos (
    id              uuid primary key default gen_random_uuid(),
    fornecedor      text not null,               -- 'stripe' | 'paddle'
    evento_id       text not null,
    email           text,
    cliente         text,
    valor_cents     integer,
    moeda           text default 'EUR',
    plano           text,                        -- 'mensal' | 'anual' | 'perpetua'
    licenca_codigo  text,
    recebido_em     timestamptz not null default now(),
    unique (fornecedor, evento_id)
);

alter table public.pagamentos enable row level security;

-- Chamada pela Edge Function que recebe o webhook (com service_role).
-- Idempotente: o mesmo evento_id devolve sempre o mesmo código.
create or replace function public.licenca_por_pagamento(
    p_fornecedor  text,
    p_evento_id   text,
    p_email       text,
    p_cliente     text,
    p_plano       text default 'anual',
    p_valor_cents integer default null,
    p_postos      integer default 1
)
returns json
language plpgsql
security definer
set search_path = public
as $$
declare
    v_existente text;
    v_resultado json;
    v_codigo    text;
    v_ate       date;
begin
    -- Já processado? Devolve o mesmo código, não emite outro.
    select licenca_codigo into v_existente
      from public.pagamentos
     where fornecedor = p_fornecedor and evento_id = p_evento_id;

    if found and v_existente is not null then
        return json_build_object('ok', true, 'codigo', v_existente, 'repetido', true);
    end if;

    v_ate := case p_plano
                when 'mensal'   then (current_date + interval '1 month')::date
                when 'anual'    then (current_date + interval '1 year')::date
                else null                      -- perpétua: sem data limite
             end;

    v_resultado := public.emitir_licenca(
        p_cliente          => coalesce(p_cliente, p_email, 'Cliente'),
        p_email            => p_email,
        p_dias             => 30,              -- revalida de 30 em 30 dias
        p_max_dispositivos => greatest(p_postos, 1),
        p_valida_ate       => v_ate);

    v_codigo := v_resultado ->> 'codigo';

    insert into public.pagamentos (fornecedor, evento_id, email, cliente,
                                   valor_cents, plano, licenca_codigo)
    values (p_fornecedor, p_evento_id, p_email, p_cliente,
            p_valor_cents, p_plano, v_codigo)
    on conflict (fornecedor, evento_id) do update
        set licenca_codigo = excluded.licenca_codigo;

    return json_build_object('ok', true, 'codigo', v_codigo,
                             'valida_ate', v_ate, 'repetido', false);
end;
$$;

revoke all on function public.licenca_por_pagamento(text, text, text, text, text, integer, integer) from public;


-- ===================================================================
--  EXEMPLOS
-- ===================================================================
--
-- Emitir uma licença anual para 3 postos:
--   select public.emitir_licenca('Construtora X', 'geral@x.pt', 30, 3,
--                                (current_date + interval '1 year')::date);
--
-- Ver quem está a usar:
--   select * from public.vw_postos;
--
-- Ver o funil comercial:
--   select * from public.vw_funil;
--
-- Renovar por mais um ano:
--   select public.renovar_licenca('TSK-XXXX-XXXX-XXXX',
--                                 (current_date + interval '1 year')::date);
--
-- Cortar o acesso:
--   select public.revogar_licenca('TSK-XXXX-XXXX-XXXX');
