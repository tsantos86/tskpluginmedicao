-- =====================================================================
--  TSK TakeOff — licenciamento por código
--
--  Modelo: tu geras um código, a pessoa cola-o uma vez no plugin e fica
--  autorizada durante N dias (30 por omissão, definível por licença).
--
--  SEGURANÇA — ler antes de mexer:
--  A chave anon vai dentro da DLL e QUALQUER PESSOA a consegue extrair.
--  Por isso as tabelas ficam com RLS a negar tudo e o plugin só pode
--  chamar duas funções (SECURITY DEFINER). Assim, mesmo com a chave em
--  mãos, ninguém consegue listar licenças nem criar activações à mão.
-- =====================================================================

-- ---------------------------------------------------------------------
-- Licenças que tu emites
-- ---------------------------------------------------------------------
create table if not exists public.licencas (
    id                uuid primary key default gen_random_uuid(),
    codigo            text unique not null,          -- ex.: TSK-A1B2-C3D4-E5F6
    cliente           text,                          -- nome da empresa/pessoa
    dias              integer not null default 30,   -- validade de cada activação
    max_dispositivos  integer not null default 1,    -- postos permitidos
    activa            boolean not null default true, -- desligar revoga tudo
    valida_ate        timestamptz,                   -- limite absoluto (opcional)
    criada_em         timestamptz not null default now(),
    notas             text
);

comment on column public.licencas.dias is
    'Dias que cada activação dura antes de precisar de nova verificação online.';
comment on column public.licencas.valida_ate is
    'Data limite da licença em si. Se passar, nem renova nem activa.';

-- ---------------------------------------------------------------------
-- Uma linha por posto onde a licença foi activada
-- ---------------------------------------------------------------------
create table if not exists public.activacoes (
    id                 uuid primary key default gen_random_uuid(),
    licenca_id         uuid not null references public.licencas(id) on delete cascade,
    maquina            text not null,                -- identificador do posto
    utilizador         text,
    versao_autocad     text,
    versao_plugin      text,
    activada_em        timestamptz not null default now(),
    expira_em          timestamptz not null,
    ultima_verificacao timestamptz not null default now(),
    bloqueada          boolean not null default false,
    unique (licenca_id, maquina)
);

create index if not exists idx_activacoes_licenca on public.activacoes(licenca_id);
create index if not exists idx_activacoes_maquina on public.activacoes(maquina);

-- ---------------------------------------------------------------------
-- Registo de sessões (telemetria) — que versões, onde, com que frequência
--
-- Não há aqui nome de utilizador, de máquina nem de domínio, e a ausência é
-- deliberada. Num cliente empresarial esses três campos juntos identificam
-- uma pessoa concreta, o que faria desta tabela um ficheiro de dados
-- pessoais — com tudo o que isso obriga — para responder a perguntas que se
-- respondem igualmente bem com um código opaco.
--
-- `instalacao` é um identificador aleatório persistente, cifrado localmente
-- com DPAPI e enviado sem nome de máquina, utilizador ou domínio. Serve para
-- distinguir cinquenta arranques de uma instalação de cinquenta instalações,
-- mantendo a identidade opaca para o servidor.
-- ---------------------------------------------------------------------
create table if not exists public.plugin_sessoes (
    id             bigserial primary key,
    instalacao     text,
    produto        text,
    versao_autocad text,
    versao_plugin  text,
    sistema        text,
    cultura        text,
    inicio         timestamptz,
    recebido_em    timestamptz not null default now()
);

create index if not exists idx_sessoes_recebido on public.plugin_sessoes(recebido_em desc);
create index if not exists idx_sessoes_instalacao on public.plugin_sessoes(instalacao);

-- =====================================================================
--  RLS: negar tudo por omissão
-- =====================================================================
alter table public.licencas       enable row level security;
alter table public.activacoes     enable row level security;
alter table public.plugin_sessoes enable row level security;

-- Sem políticas de SELECT/UPDATE/DELETE: o anon não lê nem escreve nada.
-- Só a telemetria pode inserir. É seguro deixar inserir sem ler porque o que
-- entra já não identifica ninguém: ver o comentário da tabela acima.
drop policy if exists "sessoes: inserir" on public.plugin_sessoes;
create policy "sessoes: inserir"
    on public.plugin_sessoes for insert
    to anon, authenticated
    with check (true);

-- =====================================================================
--  activar_licenca — chamada quando a pessoa cola o código
-- =====================================================================
create or replace function public.activar_licenca(
    p_codigo         text,
    p_maquina        text,
    p_utilizador     text default null,
    p_versao_autocad text default null,
    p_versao_plugin  text default null
)
returns json
language plpgsql
security definer
set search_path = public
as $$
declare
    v_lic       public.licencas%rowtype;
    v_usados    integer;
    v_expira    timestamptz;
    v_existente public.activacoes%rowtype;
begin
    select * into v_lic
      from public.licencas
     where upper(regexp_replace(codigo, '[^A-Za-z0-9]', '', 'g'))
         = upper(regexp_replace(p_codigo, '[^A-Za-z0-9]', '', 'g'));

    if not found then
        return json_build_object('ok', false, 'motivo', 'Código não encontrado.');
    end if;

    if not v_lic.activa then
        return json_build_object('ok', false, 'motivo', 'Licença desactivada.');
    end if;

    if v_lic.valida_ate is not null and v_lic.valida_ate < now() then
        return json_build_object('ok', false, 'motivo', 'Licença caducada.');
    end if;

    select * into v_existente
      from public.activacoes
     where licenca_id = v_lic.id and maquina = p_maquina;

    -- Posto novo: verificar se ainda há lugares
    if not found then
        select count(*) into v_usados
          from public.activacoes
         where licenca_id = v_lic.id and not bloqueada;

        if v_usados >= v_lic.max_dispositivos then
            return json_build_object('ok', false, 'motivo',
                format('Licença já usada em %s posto(s). Limite atingido.', v_usados));
        end if;
    elsif v_existente.bloqueada then
        return json_build_object('ok', false, 'motivo', 'Este posto foi bloqueado.');
    end if;

    v_expira := now() + make_interval(days => v_lic.dias);
    if v_lic.valida_ate is not null and v_expira > v_lic.valida_ate then
        v_expira := v_lic.valida_ate;
    end if;

    insert into public.activacoes as a
        (licenca_id, maquina, utilizador, versao_autocad, versao_plugin, expira_em)
    values
        (v_lic.id, p_maquina, p_utilizador, p_versao_autocad, p_versao_plugin, v_expira)
    on conflict (licenca_id, maquina) do update
        set expira_em          = excluded.expira_em,
            utilizador         = coalesce(excluded.utilizador, a.utilizador),
            versao_autocad     = coalesce(excluded.versao_autocad, a.versao_autocad),
            versao_plugin      = coalesce(excluded.versao_plugin, a.versao_plugin),
            ultima_verificacao = now();

    return json_build_object(
        'ok', true,
        'cliente', v_lic.cliente,
        'expira_em', v_expira,
        'dias', v_lic.dias
    );
end;
$$;

-- =====================================================================
--  verificar_licenca — revalidação silenciosa no arranque
-- =====================================================================
create or replace function public.verificar_licenca(
    p_codigo  text,
    p_maquina text
)
returns json
language plpgsql
security definer
set search_path = public
as $$
declare
    v_lic public.licencas%rowtype;
    v_act public.activacoes%rowtype;
    v_expira timestamptz;
begin
    select * into v_lic
      from public.licencas
     where upper(regexp_replace(codigo, '[^A-Za-z0-9]', '', 'g'))
         = upper(regexp_replace(p_codigo, '[^A-Za-z0-9]', '', 'g'));

    if not found or not v_lic.activa then
        return json_build_object('ok', false, 'motivo', 'Licença inválida ou desactivada.');
    end if;

    if v_lic.valida_ate is not null and v_lic.valida_ate < now() then
        return json_build_object('ok', false, 'motivo', 'Licença caducada.');
    end if;

    select * into v_act
      from public.activacoes
     where licenca_id = v_lic.id and maquina = p_maquina;

    if not found then
        return json_build_object('ok', false, 'motivo', 'Posto não activado.');
    end if;

    if v_act.bloqueada then
        return json_build_object('ok', false, 'motivo', 'Este posto foi bloqueado.');
    end if;

    -- Renova a janela a cada verificação online bem sucedida
    v_expira := now() + make_interval(days => v_lic.dias);
    if v_lic.valida_ate is not null and v_expira > v_lic.valida_ate then
        v_expira := v_lic.valida_ate;
    end if;

    update public.activacoes
       set expira_em = v_expira, ultima_verificacao = now()
     where id = v_act.id;

    return json_build_object(
        'ok', true,
        'cliente', v_lic.cliente,
        'expira_em', v_expira
    );
end;
$$;

-- Só estas duas funções ficam ao alcance do plugin
revoke all on function public.activar_licenca(text, text, text, text, text) from public;
revoke all on function public.verificar_licenca(text, text) from public;
grant execute on function public.activar_licenca(text, text, text, text, text) to anon, authenticated;
grant execute on function public.verificar_licenca(text, text) to anon, authenticated;

-- =====================================================================
--  Emitir licenças (correr no SQL Editor do Supabase)
-- =====================================================================
-- Gerar um código legível no formato TSK-XXXX-XXXX-XXXX
create or replace function public.gerar_codigo()
returns text
language sql
as $$
    select 'TSK-' ||
           upper(substr(md5(random()::text), 1, 4)) || '-' ||
           upper(substr(md5(random()::text), 1, 4)) || '-' ||
           upper(substr(md5(random()::text), 1, 4));
$$;

-- Exemplo: licença de 30 dias para 2 postos
-- insert into public.licencas (codigo, cliente, dias, max_dispositivos)
-- values (public.gerar_codigo(), 'Casquilho — equipa de medições', 30, 2)
-- returning codigo, cliente, dias, max_dispositivos;

-- Ver quem está a usar
-- select l.cliente, l.codigo, a.maquina, a.utilizador, a.versao_autocad,
--        a.expira_em, a.ultima_verificacao, a.bloqueada
--   from public.activacoes a
--   join public.licencas l on l.id = a.licenca_id
--  order by a.ultima_verificacao desc;

-- Bloquear um posto específico
-- update public.activacoes set bloqueada = true where maquina = 'NOME-DO-PC';

-- Revogar uma licença inteira
-- update public.licencas set activa = false where codigo = 'TSK-....';
