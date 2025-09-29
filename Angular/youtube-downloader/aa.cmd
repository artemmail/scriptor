@echo off
setlocal enabledelayedexpansion

:: === Настройки ===
:: Хэш коммита, который нужно запушить:
set COMMIT=ac4adc280df56285c5793431ea0720baca319ff0

:: Удалённый репозиторий:
set REMOTE=origin

:: Если оставить пустым, возьмём текущую ветку автоматически
set BRANCH=

:: === Проверки окружения ===
where git >nul 2>nul
if errorlevel 1 (
  echo [ERROR] Git не найден в PATH.
  exit /b 1
)

git rev-parse --is-inside-work-tree >nul 2>nul
if errorlevel 1 (
  echo [ERROR] Текущая папка не является git-репозиторием.
  exit /b 1
)

:: Проверим, что коммит существует локально
git cat-file -e %COMMIT%^{commit} 2>nul
if errorlevel 1 (
  echo [ERROR] Коммит %COMMIT% не найден локально.
  echo Подтяните его: git fetch --all
  exit /b 1
)

:: Определим ветку, если не задана
if "%BRANCH%"=="" (
  for /f "usebackq delims=" %%i in (`git rev-parse --abbrev-ref HEAD`) do set BRANCH=%%i
)

if "%BRANCH%"=="HEAD" (
  echo [INFO] Сейчас в detached HEAD. Укажите ветку вручную, изменив переменную BRANCH вверху скрипта.
  exit /b 1
)

echo ==================================================
echo Переписываем ветку %BRANCH% на коммит:
echo   %COMMIT%
echo Удалённый: %REMOTE%
echo ==================================================
echo.
echo ВНИМАНИЕ: будет использован push с --force-with-lease.
echo Нажмите CTRL+C для отмены или любую клавишу для продолжения...
pause >nul

:: Жёстко перемещаем локальную ветку на указанный коммит
git reset --hard %COMMIT%
if errorlevel 1 (
  echo [ERROR] Не удалось выполнить git reset --hard.
  exit /b 1
)

:: Отправляем изменения, аккуратно переписывая удалённую ветку
git push %REMOTE% %BRANCH% --force-with-lease
if errorlevel 1 (
  echo [ERROR] Не удалось выполнить git push.
  exit /b 1
)

echo [OK] Ветка %BRANCH% на %REMOTE% указывает на %COMMIT%.
endlocal
exit /b 0
