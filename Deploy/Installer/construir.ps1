<#
    Constrói o instalador do TSK TakeOff de ponta a ponta.

        .\construir.ps1                     -> build + payload + instalador
        .\construir.ps1 -Assinar            -> idem, mas assina DLL e setup
        .\construir.ps1 -Ofuscar            -> passa a DLL pelo ConfuserEx

    Ordem que interessa: compilar -> ofuscar -> assinar -> empacotar.
    Assinar antes de ofuscar invalida a assinatura.
#>

param(
    [switch]$Assinar,
    [switch]$Ofuscar,
    [string]$Configuracao = "Release",
    [string]$Versao = "1.0.0"
)

$ErrorActionPreference = "Stop"

$Raiz      = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)   # ...\PluginMedicoes
$Instalador = $PSScriptRoot
$Payload   = Join-Path $Instalador "Payload"
$Csproj    = Join-Path $Raiz "TSKTakeOff.csproj"

function Passo($texto) { Write-Host "`n=== $texto" -ForegroundColor Cyan }

# Um alvo por geração de AutoCAD: net48 = 2021-2024, net8.0-windows = 2025+.
$Alvos = @("net48", "net8.0-windows")

# -------------------------------------------------------------------
Passo "Limpar payload anterior"
if (Test-Path $Payload) { Remove-Item $Payload -Recurse -Force }
New-Item -ItemType Directory -Path (Join-Path $Payload "Contents") -Force | Out-Null

# -------------------------------------------------------------------
Passo "Compilar ($Configuracao)"
& dotnet build $Csproj -c $Configuracao -p:Version=$Versao
if ($LASTEXITCODE -ne 0) { throw "A compilação falhou." }

# -------------------------------------------------------------------
Passo "Montar o payload"

foreach ($Alvo in $Alvos) {

    $Saida = Join-Path $Raiz "bin\$Configuracao\$Alvo"
    $Dll   = Join-Path $Saida "TSKTakeOff.dll"

    if (-not (Test-Path $Dll)) {
        Write-Warning "$Alvo não foi compilado (falta o AutoCAD dessa geração?) — a saltar."
        continue
    }

    if ($Ofuscar) {
        Write-Host "  a ofuscar $Alvo..." -ForegroundColor DarkGray
        $Confuser = Join-Path $Raiz "Deploy\Obfuscation\Confuser.CLI.exe"
        $Projecto = Join-Path $Raiz "Deploy\Obfuscation\confuser.crproj"
        if (-not (Test-Path $Confuser)) {
            # Antes isto era um Write-Warning e o build seguia. O resultado era
            # a pior combinação possível: uma DLL nua, entregue ao cliente, na
            # convicção de estar protegida — porque se pediu -Ofuscar e não
            # apareceu erro nenhum. Quem pede ofuscação tem de a ter ou saber
            # que não a teve.
            throw @"
ConfuserEx não encontrado.

  Esperado em: $Confuser

  Descarregue a versão CLI de https://github.com/mkaring/ConfuserEx/releases
  e ponha o Confuser.CLI.exe (e as DLLs que o acompanham) nessa pasta.

  Para compilar SEM ofuscação, corra sem o parâmetro -Ofuscar — mas não
  entregue essa DLL a um cliente a pensar que vai protegida.
"@
        }

        & $Confuser -n $Projecto
        if ($LASTEXITCODE -ne 0) { throw "A ofuscação de $Alvo falhou (código $LASTEXITCODE)." }

        $Ofuscada = Join-Path $Raiz "Deploy\Obfuscation\Confused\TSKTakeOff.dll"
        if (-not (Test-Path $Ofuscada)) {
            throw "O ConfuserEx correu mas não produziu $Ofuscada. Verifique o confuser.crproj."
        }
        $Dll = $Ofuscada
        Write-Host "  ofuscada: $Dll" -ForegroundColor DarkGreen
    }

    if ($Assinar) {
        & (Join-Path $Instalador "assinar.ps1") -Ficheiro $Dll
    }

    $Destino = Join-Path $Payload "Contents\$Alvo"
    New-Item -ItemType Directory -Path $Destino -Force | Out-Null
    Copy-Item $Dll $Destino -Force

    # dependências que não vêm do AutoCAD (ClosedXML e companhia)
    Get-ChildItem $Saida -Filter *.dll |
        Where-Object { $_.Name -ne "TSKTakeOff.dll" -and $_.Name -notmatch "^(acmgd|acdbmgd|accoremgd|AdWindows)" } |
        ForEach-Object { Copy-Item $_.FullName $Destino -Force }

    Write-Host "  $Alvo pronto" -ForegroundColor DarkGray
}

# configuração do servidor de licenças
$Config = Join-Path $Raiz "Deploy\supabase.json"
if (Test-Path $Config) { Copy-Item $Config $Payload -Force }
else { Write-Warning "Sem supabase.json — o instalador sai sem ligação ao servidor." }

# modelo de folha de medições
$Modelo = Join-Path $Raiz "Excel\modelo-medicoes.xlsx"
if (Test-Path $Modelo) { Copy-Item $Modelo (Join-Path $Payload "modelo.xlsx") -Force }
else { Write-Warning "Sem modelo de medições — o instalador sai sem esse componente." }

# manual
$Manual = Join-Path $Raiz "Docs\MANUAL.pdf"
if (Test-Path $Manual) { Copy-Item $Manual $Payload -Force }

# -------------------------------------------------------------------
Passo "Gerar o instalador"
$Iscc = "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
if (-not (Test-Path $Iscc)) { throw "Inno Setup 6 não encontrado. Instale de https://jrsoftware.org/isdl.php" }

& $Iscc "/DAppVersao=$Versao" (Join-Path $Instalador "TSKTakeOff.iss")
if ($LASTEXITCODE -ne 0) { throw "O Inno Setup falhou." }

$Setup = Join-Path $Instalador "Output\TSKTakeOff-Setup-$Versao.exe"

if ($Assinar) {
    Passo "Assinar o instalador"
    & (Join-Path $Instalador "assinar.ps1") -Ficheiro $Setup
}

Passo "Pronto"
Write-Host $Setup -ForegroundColor Green
