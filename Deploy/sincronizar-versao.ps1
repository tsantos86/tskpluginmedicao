<#
    Poe a versao do Version.props no instalador e no bundle.

    Corre sozinho a cada compilacao (ver o alvo TskSincronizarVersao no
    .csproj). Existe porque a versao estava escrita a mao em tres sitios e
    os tres podiam divergir sem ninguem dar por isso: um instalador a
    dizer 1.0.0 e uma DLL a dizer outra coisa transforma um pedido de
    suporte num exercicio de adivinhacao.

    ESTE FICHEIRO E DELIBERADAMENTE SO ASCII.
    O powershell.exe do Windows (5.1) le um .ps1 sem BOM como ANSI, nao
    como UTF-8. Acentos guardados em UTF-8 chegam mangados, e ja fizeram
    este script sair com codigo 1. Escrever sem acentos remove o problema
    de vez, e um script de build nao precisa de ser bonito.

    NUNCA FALHA A COMPILACAO.
    Termina sempre com exit 0. Se nao conseguir sincronizar, avisa e
    segue: perder a sincronizacao e chato, mas impedir alguem de compilar
    por causa dela seria pior. O verificar.py apanha a divergencia depois.
#>

param(
    # "1.2.6.1": o que se mostra e nomeia o ficheiro do setup.
    [string]$Versao = "",
    # "1.2.6.1": so numeros, para os campos que recusam sufixos.
    [string]$VersaoNum = "",
    [string]$Raiz = ""
)

# Nao usar 'Stop': queremos avisar e continuar, nunca rebentar o build.
$ErrorActionPreference = 'Continue'

function Actualizar {
    param([string]$Ficheiro, [string]$Padrao, [string]$Novo, [string]$Descricao)

    if (-not (Test-Path -LiteralPath $Ficheiro)) {
        Write-Host "  versao: nao encontrei $Ficheiro (saltado)"
        return
    }

    # Ler e escrever em UTF-8 explicito. Estes ficheiros tem acentos, e uma
    # gravacao em ASCII transformava "Medicoes" com cedilha em interrogacoes.
    $texto = [System.IO.File]::ReadAllText($Ficheiro, [System.Text.Encoding]::UTF8)

    $m = [regex]::Match($texto, $Padrao)
    if (-not $m.Success) {
        Write-Host "  versao: nao encontrei o $Descricao em $Ficheiro (NAO actualizado)"
        return
    }

    $actual = $m.Groups[1].Value
    if ($actual -eq $Novo) {
        Write-Host "  versao: $Descricao ja esta em $Novo"
        return
    }

    # Substituir SO o grupo capturado, deixando o resto do ficheiro intacto.
    # Uma reescrita completa poderia estragar o instalador, e isso seria uma
    # troca pessima por um numero de versao.
    $i = $m.Groups[1].Index
    $f = $i + $m.Groups[1].Length
    $novoTexto = $texto.Substring(0, $i) + $Novo + $texto.Substring($f)

    [System.IO.File]::WriteAllText($Ficheiro, $novoTexto,
        (New-Object System.Text.UTF8Encoding($false)))
    Write-Host "  versao: $Descricao  $actual -> $Novo"
}

try {
    if ([string]::IsNullOrWhiteSpace($Versao)) {
        Write-Host "  versao: parametro -Versao vazio; nada a fazer."
        exit 0
    }

    # A raiz pode chegar com uma barra invertida no fim (o
    # MSBuildThisFileDirectory tem sempre uma). Numa linha de comandos do
    # Windows, a sequencia \" ESCAPA a aspa, e o argumento chega partido ao
    # PowerShell. Limpa-se aqui tambem, para o script nao depender de quem o
    # chama ter feito isso bem.
    if ([string]::IsNullOrWhiteSpace($Raiz)) {
        $Raiz = Split-Path -Parent $PSScriptRoot
    }
    $Raiz = $Raiz.Trim().Trim('"').TrimEnd('\')

    if (-not (Test-Path -LiteralPath $Raiz)) {
        Write-Host "  versao: raiz nao encontrada ($Raiz); nada a fazer."
        exit 0
    }

    # Sem numerica, cai-se na completa. Nao e o ideal, mas e melhor do que
    # nao escrever nada: quem chamar este script a mao, sem o parametro
    # novo, continua a sincronizar o que consegue.
    if ([string]::IsNullOrWhiteSpace($VersaoNum)) { $VersaoNum = $Versao }

    # O padrao do AppVersao nao apanha o AppVersaoNum: a seguir a
    # "AppVersao" exige \s+, e ali vem "Num". Continua a ser preciso
    # cuidado se alguem acrescentar mais um #define comecado por AppVersao,
    # e e por isso que fica escrito.
    Actualizar -Ficheiro (Join-Path $Raiz 'Deploy\Installer\TSKTakeOff.iss') `
               -Padrao '#define\s+AppVersao\s+"([^"]*)"' `
               -Novo $Versao -Descricao 'AppVersao do instalador'

    Actualizar -Ficheiro (Join-Path $Raiz 'Deploy\Installer\TSKTakeOff.iss') `
               -Padrao '#define\s+AppVersaoNum\s+"([^"]*)"' `
               -Novo $VersaoNum -Descricao 'AppVersaoNum do instalador'

    # O bundle leva a NUMERICA: o AppVersion do PackageContents e um campo
    # de versao para o AutoCAD comparar, nao um rotulo para se ler.
    Actualizar -Ficheiro (Join-Path $Raiz 'Deploy\TSKTakeOff.bundle\PackageContents.xml') `
               -Padrao 'AppVersion\s*=\s*"([^"]*)"' `
               -Novo $VersaoNum -Descricao 'AppVersion do bundle'
}
catch {
    Write-Host "  versao: nao consegui sincronizar - $($_.Exception.Message)"
    Write-Host "  versao: a compilacao segue. Corra verificar.py para confirmar."
}

# SEMPRE zero. Um numero de versao nao vale uma compilacao falhada.
exit 0
