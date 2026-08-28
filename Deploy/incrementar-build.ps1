<#
    Incrementa o quarto componente da versao.

    O ficheiro e deliberadamente simples e ASCII para funcionar no Windows
    PowerShell 5.1. Se o contador nao puder ser lido ou gravado, a
    compilacao falha em vez de produzir duas DLLs com o mesmo numero.
#>

param(
    [Parameter(Mandatory = $true)]
    [string]$Ficheiro
)

$ErrorActionPreference = 'Stop'

try {
    $pasta = Split-Path -Parent $Ficheiro
    if (-not [string]::IsNullOrWhiteSpace($pasta) -and -not (Test-Path -LiteralPath $pasta)) {
        New-Item -ItemType Directory -Path $pasta -Force | Out-Null
    }

    $actual = 0
    if (Test-Path -LiteralPath $Ficheiro) {
        $texto = ([System.IO.File]::ReadAllText($Ficheiro)).Trim()
        if ($texto -ne '') { $actual = [int]$texto }
    }

    if ($actual -lt 0) { throw 'O contador de build nao pode ser negativo.' }
    $seguinte = $actual + 1
    [System.IO.File]::WriteAllText(
        $Ficheiro,
        ($seguinte.ToString() + [Environment]::NewLine),
        [System.Text.Encoding]::ASCII)

    Write-Host $seguinte
    exit 0
}
catch {
    Write-Error ("Nao foi possivel incrementar o build: " + $_.Exception.Message)
    exit 1
}
