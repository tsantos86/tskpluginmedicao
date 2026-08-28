# TSK TakeOff — licenciamento

## Como funciona

### Identidade do posto e do trial

O plugin gera um identificador aleatório na primeira utilização e guarda-o em
`%APPDATA%\\TSKTakeOff\\instalacao.dat`, protegido pelo DPAPI do Windows. Esse
ID é usado pelo servidor no lugar do nome do computador, portanto renomear o
Windows não cria um posto ou trial novo.

O trial também fica associado ao e-mail informado no diálogo. Nesta fase de
testes, isso impede que apagar o arquivo local ou mudar o nome do computador
reinicie a avaliação. Para produção, o passo seguinte deve ser confirmar o
e-mail e aplicar rate limiting.

1. Tu emites um **código** no Supabase (`TSK-A1B2-C3D4-E5F6`).
2. A pessoa cola o código uma vez no plugin (`TSKLICENCA` ou botão Licença na ribbon).
3. O posto fica autorizado durante os **dias definidos nessa licença** (30 por omissão).
4. A cada arranque, o plugin revalida em silêncio e **renova a janela**.
   Enquanto houver internet de vez em quando, a pessoa nunca mais vê o assunto.
5. Sem internet, vale a validade guardada localmente — em obra continua a trabalhar.

Sem licença válida, os comandos de **medir** param.
Ver o painel, consultar medições antigas e **exportar** continuam a funcionar:
ninguém fica com trabalho retido.

## Instalação (uma vez)

### 1. Criar as tabelas

No SQL Editor do Supabase, correr `schema.sql` de uma vez.

### 2. Configurar o plugin

Criar `%APPDATA%\TSKTakeOff\supabase.json` em cada posto:

```json
{
  "url": "https://xxxxxxxxxxxx.supabase.co",
  "chave": "sb_publishable_..."
}
```

Usar a chave **publishable / anon**, nunca a `service_role`.
O mesmo ficheiro serve para o registo de sessões (telemetria).

Este ficheiro pode ir junto no bundle, numa pasta partilhada, ou ser distribuído
por script — não tem segredos que importem (ver secção de segurança).

## Emitir uma licença

```sql
insert into public.licencas (codigo, cliente, dias, max_dispositivos)
values (public.gerar_codigo(), 'Nome do cliente', 30, 2)
returning codigo, cliente, dias, max_dispositivos;
```

- `dias` — validade de cada activação. Põe **90**, **365** ou o que quiseres.
- `max_dispositivos` — quantos postos podem usar o mesmo código.
- `valida_ate` (opcional) — data limite absoluta da licença, independente dos dias.

Exemplo de licença anual para 5 postos, a caducar no fim do contrato:

```sql
insert into public.licencas (codigo, cliente, dias, max_dispositivos, valida_ate)
values (public.gerar_codigo(), 'Construtora X', 30, 5, '2027-06-30')
returning codigo;
```

Aqui o posto revalida de 30 em 30 dias, mas nunca passa de 30/06/2027.

## Gerir

Ver quem está a usar:

```sql
select l.cliente, l.codigo, a.maquina, a.utilizador, a.versao_autocad,
       a.expira_em, a.ultima_verificacao, a.bloqueada
  from public.activacoes a
  join public.licencas l on l.id = a.licenca_id
 order by a.ultima_verificacao desc;
```

Bloquear um posto (ex.: computador que saiu da empresa):

```sql
update public.activacoes set bloqueada = true where maquina = 'PC-JOAO\JOAO';
```

Revogar uma licença inteira:

```sql
update public.licencas set activa = false where codigo = 'TSK-....';
```

Libertar um posto para reinstalar noutra máquina:

```sql
delete from public.activacoes where maquina = 'PC-ANTIGO\ANA';
```

O bloqueio faz efeito na próxima revalidação — no máximo em `dias` dias,
ou logo no arranque seguinte se houver internet.

## Comandos no AutoCAD

| Comando | Função |
|---|---|
| `TSKLICENCA` | Ver estado e introduzir/renovar o código |
| `TSKLICENCARESET` | Remover a licença deste posto (testes ou mudança de PC) |

## Segurança — o que esperar

A chave anon vai no ficheiro de configuração e **qualquer pessoa a consegue ler**.
Por isso o esquema não confia nela: as tabelas têm RLS a negar tudo e o plugin só
consegue chamar duas funções (`activar_licenca` e `verificar_licenca`), que correm
com privilégios do dono e devolvem apenas sim/não. Com a chave em mãos ninguém
lista licenças nem cria activações à mão.

O que a chave permite é inserir linhas de telemetria — dados inócuos.

Sendo isto um plugin .NET, alguém com conhecimentos consegue decompilar a DLL e
remover a verificação. **Licenciamento em software desktop é um dissuasor e uma
forma de saber quem usa, não uma fortaleza.** Ofuscação (ConfuserEx) sobe a
fasquia, mas não vale investir muito mais do que isso.

Os ficheiros locais (`licenca.dat` e `instalacao.dat`) são cifrados com
**DPAPI** no âmbito do utilizador Windows: não funcionam se forem copiados para
outra máquina ou outra conta, o que impede a partilha simples de uma ativação
ou da identidade do trial.

## Ficheiros no posto

```
%APPDATA%\TSKTakeOff\
    supabase.json      url + chave (tu distribuis)
    licenca.dat        ativação cifrada (o plugin cria)
    instalacao.dat     identidade persistente do posto (o plugin cria)
    materiais.txt      materiais, alçados e cores do utilizador
    sessoes.log        registo local das sessões
```
