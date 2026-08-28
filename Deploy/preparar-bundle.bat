@echo off
REM ---------------------------------------------------------------
REM  Prepara o bundle do TSK TakeOff para distribuir noutros PCs.
REM  Copia apenas as DLLs necessarias, ignorando a build antiga.
REM ---------------------------------------------------------------
setlocal

set ORIGEM=%~dp0..\bin\Debug
set DESTINO=%~dp0TSKTakeOff.bundle\Contents

if not exist "%ORIGEM%\TSKTakeOff.dll" (
    set ORIGEM=%~dp0..\bin\Release
)

if not exist "%ORIGEM%\TSKTakeOff.dll" (
    echo.
    echo  ERRO: nao encontrei TSKTakeOff.dll em bin\Debug nem bin\Release.
    echo  Compila o projeto primeiro ^(Ctrl+Shift+B, com o AutoCAD fechado^).
    echo.
    pause
    exit /b 1
)

echo A copiar de: %ORIGEM%
if not exist "%DESTINO%" mkdir "%DESTINO%"

copy /Y "%ORIGEM%\TSKTakeOff.dll"              "%DESTINO%\" >nul
copy /Y "%ORIGEM%\ClosedXML.dll"               "%DESTINO%\" >nul
copy /Y "%ORIGEM%\DocumentFormat.OpenXml.dll"  "%DESTINO%\" >nul
copy /Y "%ORIGEM%\ExcelNumberFormat.dll"       "%DESTINO%\" >nul
copy /Y "%ORIGEM%\System.IO.Packaging.dll"     "%DESTINO%\" >nul

REM Desbloquear ficheiros marcados pelo Windows (vindos de rede/email).
powershell -NoProfile -Command "Get-ChildItem -Path '%DESTINO%' -Recurse | Unblock-File" 2>nul

echo.
echo  Bundle pronto em:
echo    %~dp0TSKTakeOff.bundle
echo.
echo  Para instalar noutro PC, copiar essa pasta inteira para:
echo    %%APPDATA%%\Autodesk\ApplicationPlugins\
echo.
echo  Fechar e reabrir o AutoCAD. O separador "TSK TakeOff" aparece sozinho.
echo.
pause
