-- =====================================================================
--  TSK TakeOff — FECHAR A FUGA AO anon         *** URGENTE ***
--
--  Correr no SQL Editor, a seguir ao correccao_permissoes.sql.
--
--  O QUE SE PASSA
--  --------------
--  O schema.sql e o comercial.sql fazem `revoke all ... from public`.
--  Isso NÃO chega: `PUBLIC` e `anon` são coisas diferentes. Projectos
--  Supabase criados com os privilégios por omissão antigos
--
--      alter default privileges in schema public
--        grant all on tables/functions to anon, authenticated, service_role;
--
--  dão ao `anon` uma concessão PRÓPRIA e explícita em cada objecto novo.
--  Revogar de PUBLIC não lhe toca. Verificado neste projecto:
--
--      has_function_privilege('anon','emitir_licenca','EXECUTE')  -> TRUE
--      has_table_privilege   ('anon','vw_licencas','SELECT')      -> TRUE
--
--  Porque é que isto é sério, e não apenas feio:
--
--    * emitir_licenca é SECURITY DEFINER. Nenhuma RLS a protege. Quem
--      extrair a chave anon da DLL — que é público por construção —
--      emite licenças a si próprio sem limite.
--
--    * vw_licencas é uma VISTA. Em Postgres, uma vista corre com as
--      permissões do DONO e IGNORA a RLS das tabelas de origem, a menos
--      que se ligue security_invoker. Ou seja: o código de licença de
--      todos os clientes está legível com a chave da DLL.
--
--    * a tabela licencas em si está tapada pela RLS (nega tudo, sem
--      políticas). O privilégio existe mas não devolve linhas. Mesmo
--      assim retira-se — um privilégio que não se quer não se deixa.
-- =====================================================================


-- ---------------------------------------------------------------------
-- 1. Funções de administração: fora do alcance do plugin
-- ---------------------------------------------------------------------
revoke all on function
    public.emitir_licenca(text, text, integer, integer, date, text)
    from anon, authenticated, public;

revoke all on function public.renovar_licenca(text, date)
    from anon, authenticated, public;

revoke all on function public.revogar_licenca(text)
    from anon, authenticated, public;

revoke all on function
    public.licenca_por_pagamento(text, text, text, text, text, integer, integer)
    from anon, authenticated, public;

revoke all on function public.gerar_codigo()
    from anon, authenticated, public;

-- E devolver ao painel o que ele precisa (o revoke acima é indiscriminado).
grant execute on function
    public.emitir_licenca(text, text, integer, integer, date, text) to service_role;
grant execute on function public.renovar_licenca(text, date)        to service_role;
grant execute on function public.revogar_licenca(text)              to service_role;
grant execute on function
    public.licenca_por_pagamento(text, text, text, text, text, integer, integer)
    to service_role;
grant execute on function public.gerar_codigo()                     to service_role;


-- ---------------------------------------------------------------------
-- 2. Vistas: são a fuga real, porque ignoram a RLS
-- ---------------------------------------------------------------------
revoke all on public.vw_licencas from anon, authenticated, public;
revoke all on public.vw_postos   from anon, authenticated, public;
revoke all on public.vw_funil    from anon, authenticated, public;

grant select on public.vw_licencas to service_role;
grant select on public.vw_postos   to service_role;
grant select on public.vw_funil    to service_role;

-- Cinto e suspensórios: com security_invoker a vista passa a correr com
-- as permissões de quem a consulta, e a RLS das tabelas volta a aplicar-se.
-- Se amanhã alguém voltar a conceder SELECT ao anon por engano, a vista
-- continua a não devolver nada. (Postgres 15+; o Supabase está no 17.)
alter view public.vw_licencas set (security_invoker = true);
alter view public.vw_postos   set (security_invoker = true);
alter view public.vw_funil    set (security_invoker = true);


-- ---------------------------------------------------------------------
-- 3. Tabelas: o anon não tem nada a fazer em nenhuma delas
-- ---------------------------------------------------------------------
revoke all on public.licencas   from anon, authenticated, public;
revoke all on public.activacoes from anon, authenticated, public;
revoke all on public.trials     from anon, authenticated, public;
revoke all on public.pagamentos from anon, authenticated, public;

-- A telemetria é a excepção deliberada: o plugin insere sessões e há uma
-- política de RLS que o permite. Insere e mais nada — não lê de volta.
revoke all    on public.plugin_sessoes from anon, authenticated, public;
grant  insert on public.plugin_sessoes to   anon, authenticated;


-- ---------------------------------------------------------------------
-- 4. Impedir que o próximo objecto criado repita o erro
-- ---------------------------------------------------------------------
-- Sem isto, a próxima vista ou função que se crie volta a nascer aberta
-- ao anon, e a auditoria de hoje tem de ser repetida daqui a um ano.
alter default privileges for role postgres in schema public
    revoke all on tables    from anon, authenticated;
alter default privileges for role postgres in schema public
    revoke all on functions from anon, authenticated;
alter default privileges for role postgres in schema public
    revoke all on sequences from anon, authenticated;


-- =====================================================================
--  VERIFICAÇÃO — em linhas, para não ficar cortada no ecrã.
--  As falhas aparecem em cima. Se a primeira linha disser "passa",
--  está tudo fechado.
-- =====================================================================
select case when obtido is distinct from esperado then '>>> FALHA' else 'passa' end as estado,
       verificacao, obtido, esperado
  from (values
    ('anon NAO emite licencas',
       has_function_privilege('anon',
         'public.emitir_licenca(text,text,integer,integer,date,text)','EXECUTE'), false),
    ('anon NAO renova',
       has_function_privilege('anon','public.renovar_licenca(text,date)','EXECUTE'), false),
    ('anon NAO revoga',
       has_function_privilege('anon','public.revogar_licenca(text)','EXECUTE'), false),
    ('anon NAO gera codigos',
       has_function_privilege('anon','public.gerar_codigo()','EXECUTE'), false),
    ('anon NAO le vw_licencas',
       has_table_privilege('anon','public.vw_licencas','SELECT'), false),
    ('anon NAO le vw_postos',
       has_table_privilege('anon','public.vw_postos','SELECT'), false),
    ('anon NAO le vw_funil',
       has_table_privilege('anon','public.vw_funil','SELECT'), false),
    ('anon NAO le licencas',
       has_table_privilege('anon','public.licencas','SELECT'), false),
    ('anon NAO le activacoes',
       has_table_privilege('anon','public.activacoes','SELECT'), false),
    ('anon NAO le trials',
       has_table_privilege('anon','public.trials','SELECT'), false),
    ('anon NAO le pagamentos',
       has_table_privilege('anon','public.pagamentos','SELECT'), false),
    ('anon NAO le telemetria',
       has_table_privilege('anon','public.plugin_sessoes','SELECT'), false),

    -- o que TEM de continuar a funcionar
    ('plugin activa',
       has_function_privilege('anon',
         'public.activar_licenca(text,text,text,text,text)','EXECUTE'), true),
    ('plugin verifica',
       has_function_privilege('anon','public.verificar_licenca(text,text)','EXECUTE'), true),
    ('plugin pede trial',
       has_function_privilege('anon',
         'public.pedir_trial(text,text,text,text,text)','EXECUTE'), true),
    ('plugin envia telemetria',
       has_table_privilege('anon','public.plugin_sessoes','INSERT'), true),
    ('painel emite',
       has_function_privilege('service_role',
         'public.emitir_licenca(text,text,integer,integer,date,text)','EXECUTE'), true),
    ('painel renova',
       has_function_privilege('service_role','public.renovar_licenca(text,date)','EXECUTE'), true),
    ('painel revoga',
       has_function_privilege('service_role','public.revogar_licenca(text)','EXECUTE'), true),
    ('painel le vw_licencas',
       has_table_privilege('service_role','public.vw_licencas','SELECT'), true),
    ('painel le vw_postos',
       has_table_privilege('service_role','public.vw_postos','SELECT'), true),
    ('painel le vw_funil',
       has_table_privilege('service_role','public.vw_funil','SELECT'), true)
  ) t(verificacao, obtido, esperado)
 order by (obtido is distinct from esperado) desc, verificacao;
