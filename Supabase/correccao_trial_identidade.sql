-- =====================================================================
-- TSK TakeOff — correção da identidade do trial
--
-- Executar no SQL Editor DEPOIS de comercial.sql e das correções de
-- permissões/fuga anon.
--
-- A versão anterior usava o nome do computador em `p_maquina`. Renomear o
-- Windows criava uma chave nova e permitia outro trial. A DLL atual envia um
-- identificador persistente e opaco; esta função também impede mais de um
-- trial para o mesmo e-mail.
--
-- O parâmetro continua chamado p_maquina apenas por compatibilidade com a RPC
-- já publicada. O valor recebido agora é o ID da instalação, não o nome do PC.
-- =====================================================================

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
begin
    if p_maquina is null or length(trim(p_maquina)) = 0 then
        return json_build_object('ok', false, 'motivo', 'Instalação não identificada.');
    end if;

    -- O e-mail já é recolhido pelo diálogo do plugin. Nesta fase de testes
    -- ele funciona como segunda trava contra apagar o ID local. Mais tarde,
    -- deve ser acrescentada confirmação por e-mail para impedir endereços
    -- descartáveis.
    if nullif(trim(p_email), '') is not null then
        perform pg_advisory_xact_lock(
            hashtext('trial-email|' || lower(trim(p_email))));
    else
        perform pg_advisory_xact_lock(
            hashtext('trial-install|' || upper(trim(p_maquina))));
    end if;

    -- Compatibilidade com trials antigos guardados como PC ou PC\\UTILIZADOR.
    -- O e-mail também encontra o trial antigo quando o cliente atualiza o
    -- plugin e passa a usar o ID persistente.
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

    return json_build_object(
        'ok', false,
        'motivo', 'A avaliação gratuita já terminou nesta instalação ou e-mail.');
end;
$$;

revoke all on function public.pedir_trial(text, text, text, text, text)
    from public;
grant execute on function public.pedir_trial(text, text, text, text, text)
    to anon, authenticated;

-- Verificação rápida: a função continua disponível ao plugin.
select has_function_privilege(
    'anon',
    'public.pedir_trial(text,text,text,text,text)',
    'EXECUTE') as plugin_trial;
