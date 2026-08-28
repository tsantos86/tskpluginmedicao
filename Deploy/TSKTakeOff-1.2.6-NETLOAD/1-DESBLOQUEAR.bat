@echo off
REM ---------------------------------------------------------------------
REM  TSK TakeOff - tirar a marca de "vindo da internet"
REM
REM  Porque isto existe:
REM  o Windows marca tudo o que se descarrega, e o .NET RECUSA-SE a carregar
REM  uma DLL marcada. O AutoCAD responde ao NETLOAD com
REM      "Could not load file or assembly ... Operation is not supported"
REM  e nao diz que a causa e a marca. Nao ha como evitar a marca do lado de
REM  quem envia: ela e posta no computador de quem recebe, no momento em que
REM  descarrega.
REM
REM  Isto NAO instala nada, NAO copia nada para lado nenhum e NAO precisa de
REM  permissoes de administrador. So tira a marca aos ficheiros desta pasta.
REM ---------------------------------------------------------------------
setlocal

echo.
echo   TSK TakeOff - desbloquear ficheiros
echo   ===================================
echo.
echo   Pasta: %~dp0
echo.

powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$n=0; Get-ChildItem -LiteralPath '%~dp0' -Recurse -File | ForEach-Object { Unblock-File -LiteralPath $_.FullName -ErrorAction SilentlyContinue; $n++ }; Write-Host ('   ' + $n + ' ficheiro(s) desbloqueado(s).')"

if errorlevel 1 goto falhou

echo.
echo   Pronto.
echo.
echo   Agora abra o AutoCAD, escreva NETLOAD e escolha o TSKTakeOff.dll
echo   desta pasta. No aviso de "editor nao verificado", escolha
echo   "Carregar sempre".
echo.
echo   Depois escreva TSKPAINEL para abrir o painel de medicoes.
echo.
pause
exit /b 0

:falhou
echo.
echo   Nao consegui desbloquear automaticamente.
echo.
echo   Faca a mao: botao direito em cada ficheiro .dll -^> Propriedades -^>
echo   marque "Desbloquear" no fundo do separador Geral -^> OK.
echo.
pause
exit /b 1
