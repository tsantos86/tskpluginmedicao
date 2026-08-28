@echo off
REM ---------------------------------------------------------------
REM  TSK TakeOff — remover deste computador.
REM
REM  Apaga so o plugin. As medicoes ficam nos DWG (estao em XData) e
REM  as definicoes ficam em %APPDATA%\TSKTakeOff.
REM ---------------------------------------------------------------
setlocal

set DESTINO=%APPDATA%\Autodesk\ApplicationPlugins\TSKTakeOff.bundle

tasklist /FI "IMAGENAME eq acad.exe" | find /I "acad.exe" >nul
if not errorlevel 1 (
    echo.
    echo  ERRO: feche o AutoCAD primeiro.
    echo.
    pause
    exit /b 1
)

if not exist "%DESTINO%" (
    echo.
    echo  O plugin nao esta instalado.
    echo.
    pause
    exit /b 0
)

rmdir /S /Q "%DESTINO%"

echo.
echo  Plugin removido.
echo.
echo  Mantidos de proposito:
echo    %APPDATA%\TSKTakeOff   (licenca, modelo, materiais)
echo    as medicoes dentro dos ficheiros DWG
echo.
echo  Para apagar tambem as definicoes, apague essa pasta a mao.
echo.
pause
