-- =====================================================================
--  TSK TakeOff — correcção de permissões
--
--  Correr no SQL Editor do Supabase DEPOIS do schema.sql e do comercial.sql.
--
--  PORQUE EXISTE ESTE FICHEIRO
--  ---------------------------
--  O comercial.sql faz `revoke all ... from public` nas funções de
--  administração e nunca as volta a conceder a ninguém. Em Postgres, uma
--  função nova nasce com EXECUTE para PUBLIC e é SÓ daí que o service_role
--  a herdava — o Supabase não lhe dá privilégios por omissão no schema
--  public. Revogar de PUBLIC revoga-lhe o acesso também.
--
--  Verificado experimentalmente num projecto Supabase real (Postgres 17):
--
--      has_function_privilege('service_role', 'emitir_licenca', 'EXECUTE')
--        antes do revoke -> true
--        depois          -> FALSE
--
--      has_table_privilege('service_role', 'vw_licencas', 'SELECT') -> FALSE
--
--  Consequência: o painel de administração não emite licenças, não renova,
--  não revoga e não mostra nada. Só o SQL Editor funciona, porque corre
--  como `postgres`, que é o dono das funções.
--
--  As funções do PLUGIN não têm este problema: o schema.sql concede-as
--  explicitamente a anon. O trial e a activação estão correctos.
-- =====================================================================


-- ---------------------------------------------------------------------
-- 1. Devolver ao painel o que lhe foi tirado
-- ---------------------------------------------------------------------
-- Só service_role. Nunca anon: a chave anon viaja dentro da DLL e qualquer
-- pessoa a extrai. Emitir licenças a partir daí seria oferecer o produto.
grant execute on function
    public.emitir_licenca(text, text, integer, integer, date, text)
    to service_role;

grant execute on function public.renovar_licenca(text, date) to service_role;
grant execute on function public.revogar_licenca(text)       to service_role;

grant execute on function
    public.licenca_por_pagamento(text, text, text, text, text, integer, integer)
    to service_role;

-- As vistas do painel. Sem isto o quadro fica vazio e parece avaria de rede.
grant select on public.vw_licencas to service_role;
grant select on public.vw_postos   to service_role;
grant select on public.vw_funil    to service_role;


-- ---------------------------------------------------------------------
-- 2. Fechar o que ficou aberto
-- ---------------------------------------------------------------------
-- O gerar_codigo() ficou com EXECUTE para PUBLIC, porque nunca foi
-- revogado. Não é grave — devolve uma string aleatória e não escreve
-- nada — mas não há razão para o deixar ao alcance de quem tem a chave
-- anon, e uma superfície a menos é uma superfície a menos.
revoke all on function public.gerar_codigo() from public;
grant execute on function public.gerar_codigo() to service_role;


-- ---------------------------------------------------------------------
-- 3. emitir_licenca: as notas eram aceites e deitadas fora
-- ---------------------------------------------------------------------
-- A função recebia p_notas e nunca o inseria. Quem escrevesse "pago por
-- transferência, factura 2026/114" perdia-o sem aviso — e é justamente
-- esse tipo de apontamento que faz falta um ano depois.
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

    insert into public.licencas
        (codigo, cliente, dias, max_dispositivos, valida_ate, notas)
    values
        (v_codigo, trim(p_cliente), greatest(p_dias, 1),
         greatest(p_max_dispositivos, 1), p_valida_ate,
         nullif(trim(coalesce(p_notas, '')), ''));

    -- Marca o trial como convertido, se houver um com este email.
    if p_email is not null then
        update public.trials set convertido = true
         where lower(email) = lower(trim(p_email));
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

revoke all on function
    public.emitir_licenca(text, text, integer, integer, date, text) from public;
grant execute on function
    public.emitir_licenca(text, text, integer, integer, date, text) to service_role;


-- ---------------------------------------------------------------------
-- 4. Renovar e revogar: aceitar o código como ele é escrito
-- ---------------------------------------------------------------------
-- O activar_licenca compara ignorando traços e maiúsculas; o renovar e o
-- revogar exigiam o código exacto. Escrever "tsk a1b2 c3d4 e5f6" no painel
-- devolvia "código não encontrado" — e num pedido de revogação urgente
-- isso é a diferença entre cortar o acesso e julgar que se cortou.
create or replace function public.renovar_licenca(
    p_codigo     text,
    p_valida_ate date
)
returns json
language plpgsql
security definer
set search_path = public
as $$
declare
    v_codigo text;
begin
    update public.licencas
       set valida_ate = p_valida_ate,
           activa = true
     where upper(regexp_replace(codigo,   '[^A-Za-z0-9]', '', 'g'))
         = upper(regexp_replace(p_codigo, '[^A-Za-z0-9]', '', 'g'))
    returning codigo into v_codigo;

    if v_codigo is null then
        return json_build_object('ok', false, 'motivo', 'Código não encontrado.');
    end if;
    return json_build_object('ok', true, 'codigo', v_codigo,
                             'valida_ate', p_valida_ate);
end;
$$;

create or replace function public.revogar_licenca(p_codigo text)
returns json
language plpgsql
security definer
set search_path = public
as $$
declare
    v_codigo text;
begin
    update public.licencas set activa = false
     where upper(regexp_replace(codigo,   '[^A-Za-z0-9]', '', 'g'))
         = upper(regexp_replace(p_codigo, '[^A-Za-z0-9]', '', 'g'))
    returning codigo into v_codigo;

    if v_codigo is null then
        return json_build_object('ok', false, 'motivo', 'Código não encontrado.');
    end if;

    -- O posto perde acesso na revalidação seguinte: no arranque seguinte se
    -- houver internet, ou no máximo dentro de `dias` dias. Não é imediato,
    -- e é bom saber isso antes de prometer a alguém que já está cortado.
    return json_build_object('ok', true, 'codigo', v_codigo);
end;
$$;

revoke all on function public.renovar_licenca(text, date) from public;
revoke all on function public.revogar_licenca(text)       from public;
grant execute on function public.renovar_licenca(text, date) to service_role;
grant execute on function public.revogar_licenca(text)       to service_role;


-- =====================================================================
--  VERIFICAÇÃO — correr isto a seguir e confirmar que dá tudo `true`
-- =====================================================================
select
    -- o painel tem de conseguir administrar
    has_function_privilege('service_role',
        'public.emitir_licenca(text,text,integer,integer,date,text)','EXECUTE')
                                                        as painel_emite,
    has_function_privilege('service_role',
        'public.renovar_licenca(text,date)','EXECUTE')   as painel_renova,
    has_function_privilege('service_role',
        'public.revogar_licenca(text)','EXECUTE')        as painel_revoga,
    has_table_privilege('service_role','public.vw_licencas','SELECT')
                                                        as painel_le_licencas,
    has_table_privilege('service_role','public.vw_postos','SELECT')
                                                        as painel_le_postos,
    has_table_privilege('service_role','public.vw_funil','SELECT')
                                                        as painel_le_funil,

    -- o plugin tem de conseguir activar e pedir avaliação
    has_function_privilege('anon',
        'public.activar_licenca(text,text,text,text,text)','EXECUTE')
                                                        as plugin_activa,
    has_function_privilege('anon',
        'public.verificar_licenca(text,text)','EXECUTE') as plugin_verifica,
    has_function_privilege('anon',
        'public.pedir_trial(text,text,text,text,text)','EXECUTE')
                                                        as plugin_trial,

    -- e o anon NÃO pode conseguir mais nada. Estes têm de dar `false`.
    has_function_privilege('anon',
        'public.emitir_licenca(text,text,integer,integer,date,text)','EXECUTE')
                                                        as FUGA_anon_emite,
    has_table_privilege('anon','public.licencas','SELECT')
                                                        as FUGA_anon_le_licencas,
    has_table_privilege('anon','public.vw_licencas','SELECT')
                                                        as FUGA_anon_le_vista;
