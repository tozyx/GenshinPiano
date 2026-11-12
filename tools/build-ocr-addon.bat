@echo off
setlocal
if /i "%~1"=="--help" goto usage
if /i "%~1"=="/?" goto usage
for %%I in ("%~dp0..") do set "REPO_ROOT=%%~fI"
pushd "%REPO_ROOT%"
if errorlevel 1 exit /b 1

set "EXIT_CODE=1"
set "SIGNING_KEY=%GENSHINPIANO_UPDATE_SIGNING_KEY%"
set "VERSION=%~1"
if not defined VERSION set "VERSION=1.0.0"
set "SMT_RESOURCE_DIR=%~2"
if not defined SMT_RESOURCE_DIR set "SMT_RESOURCE_DIR=%REPO_ROOT%\publish\smt-resource"
set "VC_RUNTIME_DIR=%~3"
if not defined VC_RUNTIME_DIR set "VC_RUNTIME_DIR=%SystemRoot%\System32"

set "POWERSHELL_EXE=%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe"
if exist "%ProgramFiles%\PowerShell\7\pwsh.exe" set "POWERSHELL_EXE=%ProgramFiles%\PowerShell\7\pwsh.exe"
where pwsh.exe >nul 2>nul
if not errorlevel 1 set "POWERSHELL_EXE=pwsh.exe"
if not defined SIGNING_KEY (
  echo GENSHINPIANO_UPDATE_SIGNING_KEY is not set.
  echo Set it to the private update signing key before packaging OCR.
  goto finish
)
if not exist "%SIGNING_KEY%" (
  echo Update signing private key not found:
  echo "%SIGNING_KEY%"
  goto finish
)

echo Publishing the standalone OCR add-on %VERSION%...
"%POWERSHELL_EXE%" -NoProfile -ExecutionPolicy Bypass -File "%~dp0Publish-OcrAddon.ps1" ^
  -Version "%VERSION%" ^
  -AddonRootDirectory "%REPO_ROOT%\publish\addons" ^
  -ArtifactsDirectory "%REPO_ROOT%\publish" ^
  -PrivateKeyPath "%SIGNING_KEY%" ^
  -SmtTrialDirectory "%SMT_RESOURCE_DIR%" ^
  -MsvcRuntimeDirectory "%VC_RUNTIME_DIR%"
set "EXIT_CODE=%ERRORLEVEL%"
if not "%EXIT_CODE%"=="0" (
  echo.
  echo OCR add-on packaging failed.
  goto finish
)
echo.
echo OCR add-on packaging completed. Output directory:
echo "%REPO_ROOT%\publish"

:finish
echo.
pause
popd
endlocal & exit /b %EXIT_CODE%

:usage
echo Usage: %~nx0 [VERSION] [SMT_RESOURCE_DIR] [VC_RUNTIME_DIR]
echo.
echo Requires .NET 10 SDK and GENSHINPIANO_UPDATE_SIGNING_KEY.
echo Uses PowerShell 7 when available, otherwise built-in Windows PowerShell 5.1.
echo Default OCR version: 1.0.0 - edit the VERSION default in this BAT to change it.
echo Example: %~nx0 1.0.1
echo Default SMT resources: publish\smt-experiment
echo Required resources: crispembed-windows-x86_64.zip and smt-grandstaff-q8_0.gguf
echo The model may also be in the resource directory's models subfolder.
echo Default VC runtime directory: Windows System32
echo The selected version is applied to the build, packaged manifest, and ZIP name.
endlocal
exit /b 0
