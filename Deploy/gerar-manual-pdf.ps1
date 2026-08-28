<#
    Gera o Docs\MANUAL.pdf a partir do Docs\MANUAL.md.

        .\gerar-manual-pdf.ps1

    Sem dependencias a instalar: converte o Markdown em HTML e manda-o ao
    Edge (ou Chrome) em modo headless, que sabe imprimir para PDF. Foi por
    isso que se escolheu este caminho em vez do pandoc ou do wkhtmltopdf --
    numa maquina Windows o browser ja la esta, e o manual tem de poder ser
    regerado por quem nao e programador.

    ESTE FICHEIRO E ASCII PURO, de proposito. O PowerShell 5.1 le os scripts
    como ANSI quando nao ha BOM, e um travessao ou um acento no codigo-fonte
    rebentava o parser com erros que nao apontam para a causa. Os acentos do
    manual entram por dados (o MANUAL.md e lido como UTF-8), nunca por aqui.

    O PDF acompanha o produto: sempre que o MANUAL.md mudar, correr isto.
#>

param(
    [string]$Origem,
    [string]$Destino,
    # Guarda o HTML intermedio ao lado do PDF. Quando a conversao sai torta,
    # e no HTML que se ve porque -- o PDF ja e a fotografia do estrago.
    [switch]$ManterHtml
)

$ErrorActionPreference = "Stop"

$Raiz = Split-Path -Parent $PSScriptRoot
if (-not $Origem)  { $Origem  = Join-Path $Raiz "Docs\MANUAL.md" }
if (-not $Destino) { $Destino = Join-Path $Raiz "Docs\MANUAL.pdf" }

if (-not (Test-Path $Origem)) { throw "Nao encontrei o manual: $Origem" }

$Browser = @(
    "C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
    "C:\Program Files\Microsoft\Edge\Application\msedge.exe",
    "C:\Program Files\Google\Chrome\Application\chrome.exe",
    "C:\Program Files (x86)\Google\Chrome\Application\chrome.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $Browser) { throw "Nao encontrei o Edge nem o Chrome: sao eles que imprimem o PDF." }

# ---------------------------------------------------------------------
# Markdown -> HTML
#
# Conversor pequeno e deliberado: cobre o que o MANUAL.md usa (titulos,
# tabelas, listas, blocos de codigo, negrito, codigo em linha) e nada mais.
# Um Markdown completo aqui seria uma dependencia a manter sem retorno.
# ---------------------------------------------------------------------
function Escapar([string]$t) {
    $t = $t -replace '&', '&amp;'
    $t = $t -replace '<', '&lt;'
    $t = $t -replace '>', '&gt;'
    return $t
}

function Inline([string]$t) {
    $t = Escapar $t
    $t = [regex]::Replace($t, '`([^`]+)`', '<code>$1</code>')
    $t = [regex]::Replace($t, '\*\*([^*]+)\*\*', '<strong>$1</strong>')
    # Italico DEPOIS do negrito: com o negrito ja convertido, os asteriscos
    # que sobram sao sempre de italico e nao ha como confundir os dois.
    $t = [regex]::Replace($t, '\*([^*]+)\*', '<em>$1</em>')
    return $t
}

$linhas = Get-Content $Origem -Encoding UTF8
$out = New-Object System.Text.StringBuilder

$emLista = $false
$emCodigo = $false
$emTabela = $false

# O MANUAL.md quebra as linhas aos ~78 caracteres para se ler no editor.
# Em Markdown isso e uma quebra SUAVE: as linhas seguidas formam um paragrafo
# so. Sem juntar, cada linha saia um paragrafo proprio e o PDF ficava com
# buracos a meio das frases.
$para = New-Object System.Collections.ArrayList   # paragrafo em curso
$item = New-Object System.Collections.ArrayList   # item de lista em curso
$itemNum = ""                                     # numero, quando e lista numerada

function Fechar-Item {
    if ($script:item.Count -gt 0) {
        $t = Inline ($script:item -join " ")
        if ($script:itemNum) {
            [void]$script:out.AppendLine("<li><span class='n'>$($script:itemNum).</span> $t</li>")
        } else {
            [void]$script:out.AppendLine("<li>$t</li>")
        }
        $script:item.Clear()
        $script:itemNum = ""
    }
}

function Fechar-Para {
    if ($script:para.Count -gt 0) {
        [void]$script:out.AppendLine("<p>" + (Inline ($script:para -join " ")) + "</p>")
        $script:para.Clear()
    }
}

function Fechar-Lista {
    Fechar-Item
    if ($script:emLista) { [void]$script:out.AppendLine("</ul>"); $script:emLista = $false }
}

function Fechar-Tudo {
    Fechar-Para
    Fechar-Lista
    if ($script:emTabela) { [void]$script:out.AppendLine("</tbody></table>"); $script:emTabela = $false }
}

for ($i = 0; $i -lt $linhas.Count; $i++) {
    $l = $linhas[$i]

    # --- blocos de codigo ---
    if ($l -match '^\s*```') {
        if ($emCodigo) {
            [void]$out.AppendLine("</code></pre>")
            $emCodigo = $false
        } else {
            Fechar-Tudo
            [void]$out.AppendLine("<pre><code>")
            $emCodigo = $true
        }
        continue
    }
    if ($emCodigo) {
        [void]$out.AppendLine((Escapar $l))
        continue
    }

    # --- tabelas: cabecalho seguido da linha de tracos ---
    $abreTabela = $false
    if ($l -match '^\s*\|' -and ($i + 1) -lt $linhas.Count) {
        if ($linhas[$i + 1] -match '^\s*\|[\s:\-\|]+\|\s*$') { $abreTabela = $true }
    }
    if ($abreTabela) {
        Fechar-Tudo
        [void]$out.AppendLine("<table><thead><tr>")
        foreach ($c in ($l.Trim().Trim('|') -split '\|')) {
            [void]$out.AppendLine("<th>" + (Inline $c.Trim()) + "</th>")
        }
        [void]$out.AppendLine("</tr></thead><tbody>")
        $emTabela = $true
        $i++
        continue
    }
    if ($emTabela) {
        if ($l -match '^\s*\|') {
            [void]$out.AppendLine("<tr>")
            foreach ($c in ($l.Trim().Trim('|') -split '\|')) {
                [void]$out.AppendLine("<td>" + (Inline $c.Trim()) + "</td>")
            }
            [void]$out.AppendLine("</tr>")
            continue
        }
        [void]$out.AppendLine("</tbody></table>")
        $emTabela = $false
    }

    # --- separador ---
    if ($l -match '^\s*---\s*$') {
        Fechar-Tudo
        [void]$out.AppendLine("<hr/>")
        continue
    }

    # --- titulos ---
    if ($l -match '^(#{1,4})\s+(.*)$') {
        Fechar-Tudo
        $n = $Matches[1].Length
        $txt = Inline $Matches[2]
        [void]$out.AppendLine("<h$n>$txt</h$n>")
        continue
    }

    # --- listas ---
    if ($l -match '^\s*[\*\-]\s+(.*)$') {
        Fechar-Para
        Fechar-Item
        if (-not $emLista) { [void]$out.AppendLine("<ul>"); $emLista = $true }
        [void]$item.Add($Matches[1])
        continue
    }
    if ($l -match '^\s*(\d+)\.\s+(.*)$') {
        Fechar-Para
        Fechar-Item
        if (-not $emLista) { [void]$out.AppendLine("<ul class='num'>"); $emLista = $true }
        $itemNum = $Matches[1]
        [void]$item.Add($Matches[2])
        continue
    }

    # --- linha em branco: fecha o que estiver aberto ---
    if ([string]::IsNullOrWhiteSpace($l)) {
        Fechar-Para
        Fechar-Lista
        continue
    }

    # --- texto solto: continua o item de lista, ou o paragrafo ---
    if ($item.Count -gt 0) { [void]$item.Add($l.Trim()) }
    else                   { [void]$para.Add($l.Trim()) }
}

if ($emCodigo) { [void]$out.AppendLine("</code></pre>") }
Fechar-Tudo

$corpo = $out.ToString()

# ---------------------------------------------------------------------
$css = @'
@page { size: A4; margin: 18mm 16mm 16mm 16mm; }
body { font-family: "Segoe UI", system-ui, sans-serif; font-size: 10.5pt;
       line-height: 1.55; color: #1a1a1a; margin: 0; }
h1 { font-size: 21pt; margin: 0 0 4pt; color: #0f2b46; letter-spacing: -.4pt; }
h2 { font-size: 14pt; margin: 20pt 0 6pt; color: #0f2b46;
     border-bottom: 1.5pt solid #d8dee6; padding-bottom: 3pt;
     page-break-after: avoid; break-after: avoid; }
h3 { font-size: 11.5pt; margin: 14pt 0 4pt; color: #24486b;
     page-break-after: avoid; break-after: avoid; }
p { margin: 5pt 0; }
hr { border: 0; border-top: .75pt solid #e3e7ec; margin: 14pt 0; }
code { font-family: Consolas, monospace; font-size: 9pt; background: #eef1f5;
       padding: 1pt 3.5pt; border-radius: 2.5pt; color: #0f2b46; }
pre { background: #f6f8fa; border: .75pt solid #e0e5ea; border-radius: 4pt;
      padding: 7pt 9pt; margin: 7pt 0; page-break-inside: avoid; }
pre code { background: none; padding: 0; font-size: 8.5pt; white-space: pre-wrap; }
table { border-collapse: collapse; width: 100%; margin: 7pt 0; font-size: 9.5pt;
        page-break-inside: avoid; break-inside: avoid; }
th { background: #0f2b46; color: #fff; text-align: left; font-weight: 600;
     padding: 4.5pt 7pt; }
td { border-bottom: .75pt solid #e3e7ec; padding: 4.5pt 7pt; vertical-align: top; }
tr:nth-child(even) td { background: #f8fafb; }
ul { margin: 5pt 0; padding-left: 16pt; }
ul.num { list-style: none; padding-left: 4pt; }
ul.num .n { color: #24486b; font-weight: 600; margin-right: 3pt; }
li { margin: 2.5pt 0; }
strong { color: #0f2b46; }
.marca { border-bottom: 2.5pt solid #0f2b46; padding-bottom: 7pt; margin-bottom: 10pt;
         font-size: 9.5pt; letter-spacing: 2pt; color: #24486b; font-weight: 600; }
.rodape { color: #5a6b7d; font-size: 9pt; margin-top: 4pt; }
'@

$build = "0"
$fBuild = Join-Path $Raiz "Version.build"
if (Test-Path $fBuild) { $build = (Get-Content $fBuild -Raw).Trim() }
$data = Get-Date -Format "dd/MM/yyyy"

$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine('<!doctype html><html lang="pt"><head><meta charset="utf-8">')
[void]$sb.AppendLine('<title>TSK TakeOff - Manual</title>')
[void]$sb.AppendLine('<style>' + $css + '</style></head><body>')
[void]$sb.AppendLine('<div class="marca">TSK DIGITAL</div>')
[void]$sb.AppendLine($corpo)
[void]$sb.AppendLine('<hr/>')
[void]$sb.AppendLine('<p class="rodape">TSK TakeOff 1.2.6.' + $build +
                     ' &middot; AutoCAD 2021-2024 &middot; ' + $data + '</p>')
[void]$sb.AppendLine('</body></html>')

$tmpHtml = Join-Path $env:TEMP "tsk-manual.html"
[IO.File]::WriteAllText($tmpHtml, $sb.ToString(), (New-Object System.Text.UTF8Encoding $false))

if (Test-Path $Destino) { Remove-Item $Destino -Force }

$perfil = Join-Path $env:TEMP "tsk-pdf-perfil"
$url = "file:///" + $tmpHtml.Replace('\', '/')

& $Browser --headless --disable-gpu --no-pdf-header-footer `
    "--user-data-dir=$perfil" "--print-to-pdf=$Destino" $url | Out-Null

if (-not (Test-Path $Destino)) { throw "O browser nao gerou o PDF." }

if ($ManterHtml) {
    $htmlAoLado = [IO.Path]::ChangeExtension($Destino, ".html")
    Copy-Item $tmpHtml $htmlAoLado -Force
    Write-Host ("  HTML intermedio: " + $htmlAoLado) -ForegroundColor DarkGray
}

Remove-Item $tmpHtml -Force -ErrorAction SilentlyContinue
Remove-Item $perfil -Recurse -Force -ErrorAction SilentlyContinue

$info = Get-Item $Destino
Write-Host ""
Write-Host ("Manual gerado: " + $info.FullName) -ForegroundColor Green
Write-Host ("  {0:N0} KB   {1}" -f ($info.Length / 1KB), $info.LastWriteTime) -ForegroundColor DarkGray
