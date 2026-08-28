# TSK TakeOff — instalar noutro computador

Distribuição em **pasta**: copia-se uma pasta e o AutoCAD carrega o plugin
sozinho. Sem instalador, sem NETLOAD, sem direitos de administrador.

## Requisitos da máquina de destino

- **AutoCAD 2021 a 2026** (64 bits)
  - 2021–2024 usam a build `net48` (.NET Framework 4.8, já vem no Windows 10 1903+ e 11)
  - 2025–2026 usam a build `net8.0-windows` (.NET 8 Desktop Runtime, instalado com o AutoCAD)
- **Microsoft Excel** — só para o *Excel ao Vivo*. O *Exportar* funciona sem Excel.

## Preparar (no teu PC, uma vez por versão)

Com o **AutoCAD fechado**:

```powershell
cd Deploy
powershell -ExecutionPolicy Bypass -File publicar.ps1
```

Compila as duas gerações e monta a pasta `Deploy\TSKTakeOff.bundle`:

```
TSKTakeOff.bundle\
    PackageContents.xml
    Contents\
        net48\              AutoCAD 2021-2024
            TSKTakeOff.dll
            ClosedXML.dll  + dependências
        net8.0-windows\     AutoCAD 2025-2026
            TSKTakeOff.dll
            ClosedXML.dll  + dependências
```

Variantes úteis:

| Comando | O que faz |
|---|---|
| `.\publicar.ps1 -Instalar` | monta **e** instala já neste computador |
| `.\publicar.ps1 -Zip` | monta e cria o `.zip` para enviar a alguém |
| `.\publicar.ps1 -Assinar` | assina as DLLs (ver `Installer\assinar.ps1`) |

O AutoCAD lê os dois blocos do `PackageContents.xml` e carrega só a build da
sua série — por isso a mesma pasta serve para todas as versões.

Se só tiveres o AutoCAD 2021 instalado, o alvo `net8.0-windows` não compila e o
script avisa: a pasta sai só com `net48`, o que é suficiente para 2021–2024.

## Instalar (na máquina de destino)

Duas maneiras, à escolha.

**A — automática:** copiar a pasta `Deploy` para a máquina e correr
`instalar.bat`. Confirma que o AutoCAD está fechado, copia o bundle, instala a
configuração do servidor e o modelo de medições, e desbloqueia o modelo.

**B — à mão:** copiar a pasta `TSKTakeOff.bundle` inteira para

```
%APPDATA%\Autodesk\ApplicationPlugins\
```

ficando

```
C:\Users\<utilizador>\AppData\Roaming\Autodesk\ApplicationPlugins\TSKTakeOff.bundle\
```

Fechar e reabrir o AutoCAD. O separador **TSK TakeOff** aparece sozinho.

Para instalar para **todos os utilizadores**, usar antes
`C:\ProgramData\Autodesk\ApplicationPlugins\` (requer administrador).

Para remover: `desinstalar.bat`, ou apagar a pasta à mão.

## Se não carregar

1. **Ficheiros bloqueados** — a causa mais comum. O Windows marca ficheiros
   vindos de rede, pen ou email. Na máquina de destino:

   ```powershell
   Get-ChildItem "$env:APPDATA\Autodesk\ApplicationPlugins\TSKTakeOff.bundle" -Recurse | Unblock-File
   ```

2. **Segurança do AutoCAD** — variável `SECURELOAD`. Se estiver a bloquear,
   adicionar a pasta em `OPTIONS → Files → Trusted Locations`.

3. **Versão do AutoCAD** — o `PackageContents.xml` aceita R24.0 a R25.1
   (2021–2026). Fora disso, não carrega.

4. Confirmar que carregou: na linha de comando deve aparecer
   `[TSK TakeOff] carregado.` Se não, escrever `TSKPAINEL` para testar.

5. **Build antiga** — não deixar `MedicoesPlugin.dll` na pasta. Se for
   carregada em conjunto, ficam comandos duplicados.

## Ficheiros do utilizador

Tudo o que é do utilizador fica fora do bundle, em `%APPDATA%\TSKTakeOff\`:

| Ficheiro | O que guarda |
|---|---|
| `licenca.dat` | ativação, cifrada com DPAPI (não se copia entre máquinas) |
| `instalacao.dat` | identidade persistente do posto/trial, cifrada com DPAPI |
| `supabase.json` | url + chave do servidor de licenças |
| `modelo.xlsx` | modelo da folha de medições distribuído por omissão |
| `folhas.txt` | mapa capítulo → folha do modelo |
| `materiais.txt` | materiais, alçados e cores |
| `texto.txt` | altura dos rótulos no desenho |
| `sessoes.log` | registo local de sessões |

O modelo distribuído por omissão é `modelo.xlsx`. O instalador só o copia quando não existe nenhum modelo suportado nem `modelo.txt`, preservando modelos personalizados antigos. O plugin continua a aceitar modelos `.xls`, `.xlsm`, `.xltm` e `.xlt` escolhidos através de `TSKMODELO`.

Copiar esta pasta leva as preferências para o PC novo — excepto a `licenca.dat`
e a `instalacao.dat`, que são propositadamente intransferíveis: no PC novo
introduz-se o código outra vez e o trial não é transferido.

As medições em si viajam dentro dos DWG (XData), não aqui.

## Atualizar para uma versão nova

Correr `publicar.ps1 -Instalar` outra vez, ou substituir o conteúdo de
`Contents\` e reabrir o AutoCAD. Não é preciso desinstalar nada — a configuração
e o modelo nunca são substituídos por cima.
