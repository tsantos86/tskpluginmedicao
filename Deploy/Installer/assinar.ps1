<#
    Assina digitalmente um ficheiro (DLL ou instalador).

        .\assinar.ps1 -Ficheiro ..\..\bin\Release\TSKTakeOff.dll

    Porque é que isto importa mais do que parece:
    sem assinatura, o Windows SmartScreen mostra "Editor desconhecido" e um
    botão "Não executar" em destaque. Numa venda B2B a informática do cliente
    bloqueia à entrada. É o investimento com melhor retorno da lista toda.

    Certificados (preços indicativos de 2026, por ano):
      - OV (Organization Validation)  ~250-400 EUR  — precisa de reputação
        acumulada no SmartScreen; os primeiros downloads ainda avisam.
      - EV (Extended Validation)      ~450-700 EUR  — reputação imediata,
        chave num token físico ou HSM na nuvem. É o que recomendo para vender.

    Emissores usados em PT: DigiCert, Sectigo, GlobalSign, SSL.com.
    Para empresa portuguesa pedem certidão permanente e NIF.

    Configuração (uma vez), em variáveis de ambiente do utilizador:
      TSK_CERT_THUMBPRINT   impressão digital do certificado instalado
      TSK_TIMESTAMP_URL     opcional; por omissão usa a da DigiCert
#>

param(
    [Parameter(Mandatory = $true)][string]$Ficheiro,
    [string]$Thumbprint = $env:TSK_CERT_THUMBPRINT,
    [string]$TimestampUrl = $(if ($env:TSK_TIMESTAMP_URL) { $env:TSK_TIMESTAMP_URL } else { "http://timestamp.digicert.com" })
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $Ficheiro)) { throw "Ficheiro não encontrado: $Ficheiro" }

if (-not $Thumbprint) {
    Write-Warning @"
Sem certificado configurado — a saltar a assinatura de $([IO.Path]::GetFileName($Ficheiro)).

Para activar:
  1. Instale o certificado no arquivo pessoal do Windows (certmgr.msc).
  2. Copie a impressão digital (sem espaços).
  3. setx TSK_CERT_THUMBPRINT "a1b2c3..."
"@
    exit 0
}

# signtool vem com o Windows SDK; procura-se a versão mais recente.
$Signtool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin" -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -match "x64" } |
    Sort-Object FullName -Descending |
    Select-Object -First 1

if (-not $Signtool) {
    throw "signtool.exe não encontrado. Instale o Windows SDK (componente 'Signing Tools')."
}

Write-Host "A assinar $([IO.Path]::GetFileName($Ficheiro))..." -ForegroundColor Cyan

# /fd sha256 + /td sha256: exigido desde que o SHA-1 foi descontinuado.
# O carimbo temporal faz a assinatura continuar válida depois de o
# certificado expirar — sem ele, o software "estraga-se" ao fim de um ano.
& $Signtool.FullName sign `
    /sha1 $Thumbprint `
    /fd sha256 `
    /tr $TimestampUrl `
    /td sha256 `
    /d "TSK TakeOff" `
    $Ficheiro

if ($LASTEXITCODE -ne 0) { throw "A assinatura falhou." }

& $Signtool.FullName verify /pa /v $Ficheiro | Out-Null
Write-Host "Assinado e verificado." -ForegroundColor Green
