@echo off
REM ---------------------------------------------------------------
REM  Instala a configuracao do servidor neste posto.
REM  Correr uma vez por computador que va usar o TSK TakeOff.
REM ---------------------------------------------------------------
setlocal

set DESTINO=%APPDATA%\TSKTakeOff

if not exist "%DESTINO%" mkdir "%DESTINO%"
copy /Y "%~dp0supabase.json" "%DESTINO%\supabase.json" >nul

if errorlevel 1 (
    echo.
    echo  ERRO: nao foi possivel copiar a configuracao.
    echo.
    pause
    exit /b 1
)

echo.
echo  Configuracao instalada em:
echo    %DESTINO%\supabase.json
echo.
echo  Abrir o AutoCAD e escrever TSKLICENCA para activar o posto.
echo.
pause
