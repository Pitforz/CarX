@echo off
setlocal EnableExtensions
chcp 65001 >nul
rem ==========================================================
rem  CarX: сборка игры под Windows в ОТДЕЛЬНОЙ копии проекта.
rem  Пока идёт сборка, Unity с основным проектом можно не закрывать
rem  и играть в редакторе.
rem  Настройки (можно менять):
rem ==========================================================
rem Папка-копия проекта и готовой игры (по умолчанию рядом: C:\Dev\CarX_Build)
set "OUT_ROOT=%~dp0..\CarX_Build"
rem Путь к Unity.exe. Пусто = найти автоматически по версии проекта.
set "UNITY_EXE="
rem ==========================================================

set "SRC=%~dp0"
if "%SRC:~-1%"=="\" set "SRC=%SRC:~0,-1%"
for %%I in ("%OUT_ROOT%") do set "OUT_ROOT=%%~fI"
set "MIRROR=%OUT_ROOT%\Project"
set "GAME=%OUT_ROOT%\Game"
set "LOG=%OUT_ROOT%\build.log"

if not exist "%SRC%\ProjectSettings\ProjectVersion.txt" (
  echo [ОШИБКА] Скрипт должен лежать в корне проекта Unity.
  goto :fail
)

rem --- версия Unity из проекта
for /f "tokens=2" %%v in ('findstr /b /c:"m_EditorVersion:" "%SRC%\ProjectSettings\ProjectVersion.txt"') do set "UVER=%%v"

rem --- ищем Unity.exe
if not defined UNITY_EXE (
  if exist "%ProgramFiles%\Unity\Hub\Editor\%UVER%\Editor\Unity.exe" set "UNITY_EXE=%ProgramFiles%\Unity\Hub\Editor\%UVER%\Editor\Unity.exe"
)
if not defined UNITY_EXE (
  for /f "usebackq delims=" %%p in (`powershell -NoProfile -Command "try{Get-Content -Raw (Join-Path $env:APPDATA 'UnityHub\secondaryInstallPath.json') | ConvertFrom-Json}catch{}"`) do (
    if exist "%%p\%UVER%\Editor\Unity.exe" set "UNITY_EXE=%%p\%UVER%\Editor\Unity.exe"
  )
)
if not defined UNITY_EXE (
  echo [ОШИБКА] Не нашёл Unity %UVER%.
  echo Откройте этот файл в Блокноте и впишите путь в строку set "UNITY_EXE=..."
  goto :fail
)

echo Unity:  %UNITY_EXE%
echo Копия:  %MIRROR%
echo Игра:   %GAME%\CarX.exe
echo.
echo Не забудьте сохранить сцены в Unity (Ctrl+S): берётся то, что на диске.
echo.

if not exist "%MIRROR%" mkdir "%MIRROR%"

rem --- 1. Копируем проект (только изменения). Library копии не трогаем.
echo [1/3] Копирую Assets, Packages, ProjectSettings...
for %%D in (Assets Packages ProjectSettings) do (
  robocopy "%SRC%\%%D" "%MIRROR%\%%D" /MIR /R:2 /W:1 /NFL /NDL /NP /NJH /NJS >nul
  if errorlevel 8 (
    echo [ОШИБКА] Не удалось скопировать %%D
    goto :fail
  )
)

rem --- 2. Library: один раз, чтобы не импортировать всё с нуля.
if not exist "%MIRROR%\Library" (
  tasklist /FI "IMAGENAME eq Unity.exe" 2>nul | find /I "Unity.exe" >nul
  if errorlevel 1 (
    echo [2/3] Первый запуск: копирую Library, это ускорит сборку...
    robocopy "%SRC%\Library" "%MIRROR%\Library" /E /R:1 /W:1 /NFL /NDL /NP /NJH /NJS >nul
  ) else (
    echo [2/3] Unity открыт, Library не копирую. Первая сборка будет долгой
    echo       ^(полный импорт^), следующие быстрые.
  )
) else (
  echo [2/3] Library уже есть в копии.
)

rem --- 3. Сборка
echo [3/3] Собираю игру. Это несколько минут, лог: %LOG%
echo.
"%UNITY_EXE%" -batchmode -quit -projectPath "%MIRROR%" -buildTarget Win64 -buildWindows64Player "%GAME%\CarX.exe" -logFile "%LOG%"
set "RC=%ERRORLEVEL%"

if not "%RC%"=="0" goto :buildfail
if not exist "%GAME%\CarX.exe" goto :buildfail

echo.
echo ===== ГОТОВО =====
echo %GAME%\CarX.exe
start "" explorer "%GAME%"
pause
exit /b 0

:buildfail
echo.
echo [ОШИБКА] Сборка не удалась (код %RC%). Последние строки лога:
echo ---------------------------------------------------------
powershell -NoProfile -Command "Get-Content -Tail 40 '%LOG%'"
echo ---------------------------------------------------------
echo Весь лог: %LOG%
:fail
pause
exit /b 1
