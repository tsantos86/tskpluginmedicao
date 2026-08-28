<#
    Testa o Supabase COM A CHAVE ANON, como um atacante faria.

    A chave anon viaja dentro da DLL e qualquer pessoa a extrai. Este
    script usa-a exactamente como ela e acessivel a qualquer cliente, e
    verifica as duas coisas que importam:

      NEGATIVO  o anon NAO pode emitir licencas nem ler os codigos dos
                clientes. Antes da correccao, podia as duas.

      POSITIVO  o anon TEM de continuar a poder activar, verificar e
                pedir avaliacao. Fechar de mais e pior do que a fuga:
                a fuga e um risco, mas um plugin que nao licencia esta
                simplesmente avariado para toda a gente.

    Uso:
        .\testar_anon.ps1
        .\testar_anon.ps1 -Config "C:\caminho\para\supabase.json"

    Le a url e a chave do supabase.json, o mesmo ficheiro que o plugin usa.
#>

param(
    [string]$Config = ""
)

$ErrorActionPreference = 'Continue'

# ---- localizar o supabase.json -----------------------------------------
if ([string]::IsNullOrWhiteSpace($Config)) {
    $candidatos = @(
        (Join-Path (Split-Path -Parent $PSScriptRoot) 'Deploy\supabase.json'),
        (Join-Path $env:APPDATA 'TSKTakeOff\supabase.json')
    )
    foreach ($c in $candidatos) {
        if (Test-Path -LiteralPath $c) { $Config = $c; break }
    }
}
if (-not (Test-Path -LiteralPath $Config)) {
    Write-Host "Nao encontrei o supabase.json. Passe-o com -Config." -ForegroundColor Red
    exit 1
}

$cfg = Get-Content -Raw -LiteralPath $Config | ConvertFrom-Json
$url = $cfg.url.TrimEnd('/')
$key = if ($cfg.chave) { $cfg.chave } else { $cfg.key }

Write-Host ""
Write-Host "projecto : $url"
Write-Host "ficheiro : $Config"
Write-Host ""

$cab = @{ apikey = $key; Authorization = "Bearer $key" }
$falhas = 0

function Testar {
    param(
        [string]$Nome,
        [scriptblock]$Accao,
        [bool]$DeveFalhar   # $true = esperamos recusa
    )

    $passou = $false
    $detalhe = ""
    try {
        $r = & $Accao
        # Chegou aqui sem excepcao: o servidor aceitou.
        $passou = (-not $DeveFalhar)
        $detalhe = if ($r) { ($r | ConvertTo-Json -Compress -Depth 3) } else { "(vazio)" }
        if ($detalhe.Length -gt 90) { $detalhe = $detalhe.Substring(0, 90) + "..." }
    }
    catch {
        $codigo = ""
        try { $codigo = $_.Exception.Response.StatusCode.value__ } catch { }
        $passou = $DeveFalhar
        $detalhe = "recusado (HTTP $codigo)"
    }

    $marca = if ($passou) { "  OK  " } else { " FALHA" }
    $cor = if ($passou) { "Green" } else { "Red" }
    Write-Host ("{0}  {1,-46} {2}" -f $marca, $Nome, $detalhe) -ForegroundColor $cor
    if (-not $passou) { $script:falhas++ }
}

Write-Host "NEGATIVO - o anon nao pode fazer isto" -ForegroundColor Yellow

Testar -Nome "emitir_licenca" -DeveFalhar $true -Accao {
    Invoke-RestMethod -Method Post -Uri "$url/rest/v1/rpc/emitir_licenca" `
        -Headers $cab -ContentType 'application/json' `
        -Body '{"p_cliente":"teste de intrusao"}'
}

Testar -Nome "ler vw_licencas (codigos dos clientes)" -DeveFalhar $true -Accao {
    Invoke-RestMethod -Method Get -Uri "$url/rest/v1/vw_licencas" -Headers $cab
}

Testar -Nome "ler a tabela licencas" -DeveFalhar $true -Accao {
    Invoke-RestMethod -Method Get -Uri "$url/rest/v1/licencas?select=codigo" -Headers $cab
}

Testar -Nome "ler trials (emails dos clientes)" -DeveFalhar $true -Accao {
    Invoke-RestMethod -Method Get -Uri "$url/rest/v1/trials?select=email" -Headers $cab
}

Write-Host ""
Write-Host "POSITIVO - o plugin PRECISA disto" -ForegroundColor Yellow

# Codigo inventado: esperamos {ok:false,motivo:"Codigo nao encontrado"} -
# uma RESPOSTA, nao uma recusa de permissao. Se der recusa, o plugin ja nao
# consegue activar em maquina nenhuma.
Testar -Nome "activar_licenca responde (codigo invalido)" -DeveFalhar $false -Accao {
    Invoke-RestMethod -Method Post -Uri "$url/rest/v1/rpc/activar_licenca" `
        -Headers $cab -ContentType 'application/json' `
        -Body '{"p_codigo":"TSK-TEST-TEST-TEST","p_maquina":"TESTE-PS"}'
}

Testar -Nome "verificar_licenca responde" -DeveFalhar $false -Accao {
    Invoke-RestMethod -Method Post -Uri "$url/rest/v1/rpc/verificar_licenca" `
        -Headers $cab -ContentType 'application/json' `
        -Body '{"p_codigo":"TSK-TEST-TEST-TEST","p_maquina":"TESTE-PS"}'
}

# NAO pedimos trial a serio: gastaria a avaliacao desta maquina no servidor.
# Uma maquina vazia da erro de validacao, nao de permissao - e o que se quer
# saber e que a funcao esta ao alcance do anon.
Testar -Nome "pedir_trial responde (maquina vazia)" -DeveFalhar $false -Accao {
    Invoke-RestMethod -Method Post -Uri "$url/rest/v1/rpc/pedir_trial" `
        -Headers $cab -ContentType 'application/json' `
        -Body '{"p_maquina":""}'
}

Write-Host ""
if ($falhas -eq 0) {
    Write-Host "Tudo certo: a fuga esta fechada e o plugin continua a licenciar." -ForegroundColor Green
} else {
    Write-Host "$falhas verificacao(oes) falharam. Ver acima." -ForegroundColor Red
    Write-Host ""
    Write-Host "Se falhou um POSITIVO, o revoke apanhou algo que o plugin precisa:"
    Write-Host "  correr outra vez a seccao 1 do correccao_fuga_anon.sql, que"
    Write-Host "  volta a conceder activar_licenca, verificar_licenca e pedir_trial."
}
Write-Host ""
exit 0
