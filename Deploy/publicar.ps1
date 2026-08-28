<#
    Monta o TSK TakeOff como PASTA pronta a copiar — sem instalador.

        .\publicar.ps1              compila e monta a pasta
        .\publicar.ps1 -Instalar    idem, e instala já neste computador
        .\publicar.ps1 -Zip         idem, e cria o .zip para enviar a alguém

    Resultado:  Deploy\TSKTakeOff.bundle\
                    PackageContents.xml
                    Contents\net48\TSKTakeOff.dll            (AutoCAD 2021-2024)
                    Contents\net8.0-windows\TSKTakeOff.dll   (AutoCAD 2025-2026)

    Para instalar, basta copiar essa pasta inteira para:
        %APPDATA%\Autodesk\ApplicationPlugins\

    O AutoCAD carrega-a sozinho no arranque seguinte. Sem NETLOAD.
#>

param(
    [switch]$Instalar,
    [switch]$Zip,
    [switch]$Assinar,
    [string]$Configuracao = "Release",
    [string]$Versao = "1.0.0"
)

$ErrorActionPreference = "Stop"

$Deploy = $PSScriptRoot
$Raiz   = Split-Path -Parent $Deploy
$Bundle = Join-Path $Deploy "TSKTakeOff.bundle"
$Csproj = Join-Path $Raiz "TSKTakeOff.csproj"
$Alvos  = @("net48", "net8.0-windows")

function Passo($t) { Write-Host "`n=== $t" -ForegroundColor Cyan }

# -------------------------------------------------------------------
Passo "Verificar que o AutoCAD está fechado"
if (Get-Process acad -ErrorAction SilentlyContinue) {
    throw "O AutoCAD está aberto. Feche-o primeiro — com ele aberto a DLL não pode ser substituída."
}

# -------------------------------------------------------------------
Passo "Compilar"
& dotnet build $Csproj -c $Configuracao -p:Version=$Versao
if ($LASTEXITCODE -ne 0) { throw "A compilação falhou." }

# -------------------------------------------------------------------
Passo "Montar o bundle"

# O PackageContents.xml é escrito à mão e fica; só o Contents é regenerado.
$Contents = Join-Path $Bundle "Contents"
if (Test-Path $Contents) { Remove-Item $Contents -Recurse -Force }
New-Item -ItemType Directory -Path $Contents -Force | Out-Null

$Compilados = 0
foreach ($Alvo in $Alvos) {

    $Saida = Join-Path $Raiz "bin\$Configuracao\$Alvo"
    $Dll   = Join-Path $Saida "TSKTakeOff.dll"

    if (-not (Test-Path $Dll)) {
        Write-Warning "$Alvo não compilou (falta o AutoCAD dessa geração instalado?) — a saltar."
        continue
    }

    if ($Assinar) {
        $Script = Join-Path $Deploy "Installer\assinar.ps1"
        if (Test-Path $Script) { & $Script -Ficheiro $Dll }
    }

    $Destino = Join-Path $Contents $Alvo
    New-Item -ItemType Directory -Path $Destino -Force | Out-Null
    Copy-Item $Dll $Destino -Force

    # A configuração acompanha cada pasta compilada: assim o piloto pode
    # simplesmente copiar a pasta e fazer NETLOAD, sem instalação adicional.
    $Config = Join-Path $Deploy "supabase.json"
    if (Test-Path $Config) { Copy-Item $Config $Destino -Force }

    # dependências próprias (ClosedXML etc). As do AutoCAD ficam de fora:
    # já estão carregadas no processo e duplicá-las dá conflitos de versão.
    Get-ChildItem $Saida -Filter *.dll |
        Where-Object { $_.Name -ne "TSKTakeOff.dll" -and
                       $_.Name -notmatch "^(acmgd|acdbmgd|accoremgd|AdWindows)" } |
        ForEach-Object { Copy-Item $_.FullName $Destino -Force }

    Write-Host "  $Alvo pronto" -ForegroundColor DarkGray
    $Compilados++
}

if ($Compilados -eq 0) { throw "Nenhum alvo compilou — não há nada para distribuir." }

# manual, se existir
$Manual = Join-Path $Raiz "Docs\MANUAL.pdf"
if (Test-Path $Manual) { Copy-Item $Manual $Bundle -Force }

Write-Host "`nBundle em: $Bundle" -ForegroundColor Green

# -------------------------------------------------------------------
if ($Instalar) {
    Passo "Instalar neste computador"

    $Alvo = Join-Path $env:APPDATA "Autodesk\ApplicationPlugins\TSKTakeOff.bundle"
    if (Test-Path $Alvo) { Remove-Item $Alvo -Recurse -Force }
    Copy-Item $Bundle $Alvo -Recurse -Force

    # Configuração do servidor de licenças e modelo de medições.
    # onlyifdoesntexist à mão: nunca por cima do que o utilizador já afinou.
    $Pasta = Join-Path $env:APPDATA "TSKTakeOff"
    New-Item -ItemType Directory -Path $Pasta -Force | Out-Null

    $Config = Join-Path $Deploy "supabase.json"
    $ConfigAlvo = Join-Path $Pasta "supabase.json"
    if ((Test-Path $Config) -and -not (Test-Path $ConfigAlvo)) {
        Copy-Item $Config $ConfigAlvo -Force
        Write-Host "  supabase.json instalado" -ForegroundColor DarkGray
    }

    $Modelo = Join-Path $Raiz "Excel\modelo-medicoes.xlsx"
    $ModeloAlvo = Join-Path $Pasta "modelo.xlsx"
    $RegistoModelo = Join-Path $Pasta "modelo.txt"
    $ModeloTxtValido = $false
    if (Test-Path $RegistoModelo) {
        $CaminhoRegistado = (Get-Content $RegistoModelo -Raw).Trim()
        $ModeloTxtValido = $CaminhoRegistado.Length -gt 0 -and (Test-Path $CaminhoRegistado)
    }
    $ModelosRegistados = @("modelo.xlsx", "modelo.xlsm", "modelo.xls", "modelo.xltm", "modelo.xlt") |
        Where-Object { Test-Path (Join-Path $Pasta $_) }
    if ((Test-Path $Modelo) -and -not $ModeloTxtValido -and $ModelosRegistados.Count -eq 0) {
        Copy-Item $Modelo $ModeloAlvo -Force
        # Sem isto o Excel abre-o em Vista Protegida e o plugin não lá escreve.
        Unblock-File -LiteralPath $ModeloAlvo -ErrorAction SilentlyContinue
        Write-Host "  modelo.xlsx instalado" -ForegroundColor DarkGray
    }

    Write-Host "`nInstalado em: $Alvo" -ForegroundColor Green
    Write-Host "Abra o AutoCAD — o separador TSK TakeOff aparece sozinho." -ForegroundColor Green
}

# -------------------------------------------------------------------
if ($Zip) {
    Passo "Criar o .zip"
    $Ficheiro = Join-Path $Deploy "TSKTakeOff-$Versao.zip"
    if (Test-Path $Ficheiro) { Remove-Item $Ficheiro -Force }
    Compress-Archive -Path $Bundle -DestinationPath $Ficheiro
    Write-Host "`nZip: $Ficheiro" -ForegroundColor Green
}
