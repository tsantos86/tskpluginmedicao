-- =====================================================================
-- TSK TakeOff — trial renovável pelo próprio utilizador
--
-- Executar no SQL Editor DEPOIS de correccao_trial_identidade.sql.
--
-- Antes: quando os 15 dias acabavam, clicar em "Experimentar 15 dias" dava
-- "A avaliação gratuita já terminou". Agora cada clique depois de o trial
-- acabar dá mais 15 dias, sem ninguém ter de mexer no Supabase.
--
-- Não é preciso mudar o plugin: o botão já aparece sempre que o trial está
-- vencido, e o plugin aceita a data nova que o servidor devolver.
--
-- O controlo continua do lado do Supabase:
--   * trials.renovacoes     quantas vezes cada pessoa renovou
--   * trials.bloqueado      true = esta pessoa deixa de poder renovar
--   * max_renovacoes_trial() null = sem limite; um número = limite para todos
--
-- Enquanto o trial está a decorrer, clicar outra vez NÃO acrescenta dias:
-- devolve o prazo que já existe. Só se renova depois de acabar.
-- =====================================================================

alter table public.trials add column if not exists renovacoes integer not null default 0;
alter table public.trials add column if not exists bloqueado  boolean not null default false;

-- Limite global de renovações. null = ilimitado.
-- Para limitar a, por exemplo, 3 renovações:  ... as $$ select 3 $$;
create or replace function public.max_renovacoes_trial()
returns integer language sql immutable as $$ select null::integer $$;

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
    v_trial public.trials%rowtype;
    v_dias  integer := public.dias_de_trial();
    v_max   integer := public.max_renovacoes_trial();
begin
    if p_maquina is null or length(trim(p_maquina)) = 0 then
        return json_build_object('ok', false, 'motivo', 'Instalação não identificada.');
    end if;

    if nullif(trim(p_email), '') is not null then
        perform pg_advisory_xact_lock(
            hashtext('trial-email|' || lower(trim(p_email))));
    else
        perform pg_advisory_xact_lock(
            hashtext('trial-install|' || upper(trim(p_maquina))));
    end if;

    select * into v_trial
      from public.trials
     where upper(maquina) = upper(trim(p_maquina))
        or upper(maquina) like upper(trim(p_maquina) || chr(92) || '%')
        or (nullif(trim(p_email), '') is not null
            and lower(trim(email)) = lower(trim(p_email)))
     order by expira_em desc
     limit 1;

    -- Primeira vez: igual a antes.
    if not found then
        insert into public.trials (maquina, utilizador, email, expira_em,
                                   versao_autocad, versao_plugin)
        values (trim(p_maquina), p_utilizador, nullif(trim(p_email), ''),
                now() + (v_dias || ' days')::interval,
                p_versao_autocad, p_versao_plugin)
        returning * into v_trial;

        return json_build_object(
            'ok', true,
            'cliente', 'Avaliação',
            'expira_em', v_trial.expira_em,
            'motivo', format('Avaliação de %s dias iniciada.', v_dias));
    end if;

    if v_trial.bloqueado then
        return json_build_object('ok', false,
            'motivo', 'A avaliação gratuita foi desactivada para este posto.');
    end if;

    -- Ainda a decorrer: devolve o prazo que existe, sem acrescentar dias.
    if v_trial.expira_em > now() then
        update public.trials
           set utilizador     = coalesce(p_utilizador, utilizador),
               email          = coalesce(nullif(trim(p_email), ''), email),
               versao_autocad = coalesce(p_versao_autocad, versao_autocad),
               versao_plugin  = coalesce(p_versao_plugin, versao_plugin)
         where id = v_trial.id;

        return json_build_object(
            'ok', true,
            'cliente', 'Avaliação',
            'expira_em', v_trial.expira_em,
            'motivo', 'Avaliação em curso.');
    end if;

    -- Acabou: renova, a não ser que o limite global já tenha sido atingido.
    if v_max is not null and v_trial.renovacoes >= v_max then
        return json_build_object('ok', false,
            'motivo', 'A avaliação gratuita já terminou nesta instalação ou e-mail.');
    end if;

    update public.trials
       set expira_em      = now() + (v_dias || ' days')::interval,
           renovacoes     = renovacoes + 1,
           utilizador     = coalesce(p_utilizador, utilizador),
           email          = coalesce(nullif(trim(p_email), ''), email),
           versao_autocad = coalesce(p_versao_autocad, versao_autocad),
           versao_plugin  = coalesce(p_versao_plugin, versao_plugin)
     where id = v_trial.id
    returning * into v_trial;

    return json_build_object(
        'ok', true,
        'cliente', 'Avaliação',
        'expira_em', v_trial.expira_em,
        'motivo', format('Avaliação renovada por %s dias.', v_dias));
end;
$$;

-- As permissões mantêm-se num "create or replace", mas repõem-se na mesma:
-- sem isto o plugin deixava de conseguir pedir o trial.
revoke all on function public.pedir_trial(text, text, text, text, text) from public;
grant execute on function public.pedir_trial(text, text, text, text, text) to anon, authenticated;
revoke all on function public.max_renovacoes_trial() from anon, authenticated, public;


-- =====================================================================
-- VERIFICAÇÃO — as duas colunas têm de dar true
-- =====================================================================
select has_function_privilege('anon', 'public.pedir_trial(text,text,text,text,text)', 'EXECUTE')
           as plugin_pede_trial,
       exists (select 1 from information_schema.columns
                where table_schema = 'public' and table_name = 'trials'
                  and column_name = 'renovacoes') as coluna_renovacoes;


-- =====================================================================
-- CONTROLO NO DIA-A-DIA (correr só quando for preciso)
-- =====================================================================
-- Quem está a renovar e quantas vezes:
--   select email, renovacoes, expira_em, versao_plugin, bloqueado
--     from public.trials order by renovacoes desc, expira_em desc;
--
-- Bloquear uma pessoa (deixa de renovar; o prazo actual acaba normalmente):
--   update public.trials set bloqueado = true where lower(email) = lower('x@y.pt');
--
-- Voltar ao comportamento antigo (nunca renovar sozinho):
--   create or replace function public.max_renovacoes_trial()
--   returns integer language sql immutable as $$ select 0 $$;
