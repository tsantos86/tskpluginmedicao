@echo off
REM ---------------------------------------------------------------
REM  Instala o modelo de medicoes da casa neste posto.
REM  Copia o modelo-medicoes.xlsx para %APPDATA%\TSKTakeOff\modelo.xlsx, que e onde
REM  o plugin o procura sozinho - sem precisar do comando TSKMODELO.
REM  Correr uma vez por computador.
REM ---------------------------------------------------------------
setlocal

set DESTINO=%APPDATA%\TSKTakeOff
set ORIGEM=%~dp0modelo-medicoes.xlsx

if not exist "%ORIGEM%" (
    echo.
    echo  ERRO: nao encontrei "modelo-medicoes.xlsx" nesta pasta.
    echo  Coloque o livro-modelo aqui com esse nome e volte a correr.
    echo.
    pause
    exit /b 1
)

if not exist "%DESTINO%" mkdir "%DESTINO%"
copy /Y "%ORIGEM%" "%DESTINO%\modelo.xlsx" >nul

if errorlevel 1 (
    echo.
    echo  ERRO: nao foi possivel copiar o modelo.
    echo.
    pause
    exit /b 1
)

REM  Tirar a marca de "ficheiro vindo da internet". Sem isto o Excel abre o
REM  livro em Vista Protegida: o plugin nao le as folhas e as macros nao correm.
powershell -NoProfile -Command "Unblock-File -LiteralPath '%DESTINO%\modelo.xlsx'" >nul 2>&1

echo.
echo  Modelo instalado em:
echo    %DESTINO%\modelo.xlsx
echo.
echo  Opcional - mapear os capitulos para as folhas das macros:
echo    %DESTINO%\folhas.txt
echo      ALVENARIAS=1.1
echo      MATERIAIS=2.1
echo.
echo  Abrir o AutoCAD e carregar em "Excel ao Vivo".
echo.
pause
