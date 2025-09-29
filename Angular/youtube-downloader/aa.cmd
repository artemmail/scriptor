@echo off
setlocal enabledelayedexpansion

:: === ����ன�� ===
:: ��� ������, ����� �㦭� �������:
set COMMIT=ac4adc280df56285c5793431ea0720baca319ff0

:: ������ ९����਩:
set REMOTE=origin

:: �᫨ ��⠢��� �����, ����� ⥪���� ���� ��⮬���᪨
set BRANCH=

:: === �஢�ન ���㦥��� ===
where git >nul 2>nul
if errorlevel 1 (
  echo [ERROR] Git �� ������ � PATH.
  exit /b 1
)

git rev-parse --is-inside-work-tree >nul 2>nul
if errorlevel 1 (
  echo [ERROR] ������ ����� �� ���� git-९����ਥ�.
  exit /b 1
)

:: �஢�ਬ, �� ������ ������� �����쭮
git cat-file -e %COMMIT%^{commit} 2>nul
if errorlevel 1 (
  echo [ERROR] ������ %COMMIT% �� ������ �����쭮.
  echo ����ﭨ� ���: git fetch --all
  exit /b 1
)

:: ��।���� ����, �᫨ �� ������
if "%BRANCH%"=="" (
  for /f "usebackq delims=" %%i in (`git rev-parse --abbrev-ref HEAD`) do set BRANCH=%%i
)

if "%BRANCH%"=="HEAD" (
  echo [INFO] ����� � detached HEAD. ������ ���� ������, ������� ��६����� BRANCH ������ �ਯ�.
  exit /b 1
)

echo ==================================================
echo ��९��뢠�� ���� %BRANCH% �� ������:
echo   %COMMIT%
echo ������: %REMOTE%
echo ==================================================
echo.
echo ��������: �㤥� �ᯮ�짮��� push � --force-with-lease.
echo ������ CTRL+C ��� �⬥�� ��� ���� ������� ��� �த�������...
pause >nul

:: ���⪮ ��६�頥� �������� ���� �� 㪠����� ������
git reset --hard %COMMIT%
if errorlevel 1 (
  echo [ERROR] �� 㤠���� �믮����� git reset --hard.
  exit /b 1
)

:: ��ࠢ�塞 ���������, �����⭮ ��९��뢠� 㤠���� ����
git push %REMOTE% %BRANCH% --force-with-lease
if errorlevel 1 (
  echo [ERROR] �� 㤠���� �믮����� git push.
  exit /b 1
)

echo [OK] ��⪠ %BRANCH% �� %REMOTE% 㪠�뢠�� �� %COMMIT%.
endlocal
exit /b 0
