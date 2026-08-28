@echo off
REM ---------------------------------------------------------------
REM  TSK TakeOff — instalar neste computador (versao em pasta).
REM
REM  Copia a pasta TSKTakeOff.bundle para onde o AutoCAD a procura.
REM  Nao precisa de permissoes de administrador.
REM
REM  Feche o AutoCAD antes de correr.
REM ---------------------------------------------------------------
setlocal

set ORIGEM=%~dp0TSKTakeOff.bundle
set DESTINO=%APPDATA%\Autodesk\ApplicationPlugins\TSKTakeOff.bundle
set CONFIG=%APPDATA%\TSKTakeOff

if not exist "%ORIGEM%\PackageContents.xml" (
    echo.
    echo  ERRO: nao encontrei o bundle em:
    echo    %ORIGEM%
    echo.
    echo  Corra primeiro:  powershell -ExecutionPolicy Bypass -File publicar.ps1
    echo.
    pause
    exit /b 1
)

tasklist /FI "IMAGENAME eq acad.exe" | find /I "acad.exe" >nul
if not errorlevel 1 (
    echo.
    echo  ERRO: o AutoCAD esta aberto.
    echo  Feche-o e volte a correr este ficheiro.
    echo.
    pause
    exit /b 1
)

echo.
echo  A instalar...

if exist "%DESTINO%" rmdir /S /Q "%DESTINO%"
mkdir "%DESTINO%" 2>nul
xcopy "%ORIGEM%" "%DESTINO%" /E /I /Y /Q >nul

if errorlevel 1 (
    echo.
    echo  ERRO: a copia falhou.
    echo.
    pause
    exit /b 1
)

REM ---- configuracao do servidor e modelo: nunca por cima do que ja existe ----
if not exist "%CONFIG%" mkdir "%CONFIG%"

if exist "%~dp0supabase.json" (
    if not exist "%CONFIG%\supabase.json" (
        copy /Y "%~dp0supabase.json" "%CONFIG%\supabase.json" >nul
        echo    supabase.json instalado
    )
)

if exist "%~dp0..\Excel\modelo-medicoes.xlsx" (
    REM  Nao instalar o modelo padrao se o utilizador ja tiver qualquer
    REM  modelo suportado ou um modelo.txt valido — preserva modelos .xls
    REM  antigos e evita que o runtime troque silenciosamente de modelo.
    set "MODELO_EXISTENTE=0"
    if exist "%CONFIG%\modelo.xlsx" set "MODELO_EXISTENTE=1"
    if exist "%CONFIG%\modelo.xlsm" set "MODELO_EXISTENTE=1"
    if exist "%CONFIG%\modelo.xls" set "MODELO_EXISTENTE=1"
    if exist "%CONFIG%\modelo.xltm" set "MODELO_EXISTENTE=1"
    if exist "%CONFIG%\modelo.xlt" set "MODELO_EXISTENTE=1"
    if exist "%CONFIG%\modelo.txt" (
        powershell -NoProfile -Command "$r=(Get-Content -LiteralPath ($env:APPDATA + '\\TSKTakeOff\\modelo.txt') -Raw).Trim(); if ($r -and (Test-Path -LiteralPath $r)) { exit 0 } else { exit 1 }" >nul 2>&1
        if not errorlevel 1 set "MODELO_EXISTENTE=1"
    )
    if "%MODELO_EXISTENTE%"=="0" (
        copy /Y "%~dp0..\Excel\modelo-medicoes.xlsx" "%CONFIG%\modelo.xlsx" >nul
        REM  Tirar a marca de "vindo da internet": sem isto o Excel abre o
        REM  modelo em Vista Protegida e o plugin nao consegue la escrever.
        powershell -NoProfile -Command "Unblock-File -LiteralPath '%CONFIG%\modelo.xlsx'" >nul 2>&1
        echo    modelo.xlsx instalado
    )
)

echo.
echo  Instalado em:
echo    %DESTINO%
echo.
echo  Abra o AutoCAD. O separador "TSK TakeOff" aparece sozinho.
echo  Se nao aparecer, escreva TSKPAINEL na linha de comandos.
echo.
pause
