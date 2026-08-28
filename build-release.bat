@echo off
setlocal
pushd "%~dp0"

set "VERSION=%~1"
if not defined VERSION set "VERSION=3.0.1-preview.1"
set "PUBLISH_DIR=%~dp0publish\GenshinPiano-win-x64"
set "UPDATER_PUBLISH_DIR=%~dp0publish\.tmp\GenshinPiano.Updater-win-x64"
set "SONGS_DIR=%~dp0publish\songs"
set "ZIP_PATH=%~dp0publish\GenshinPiano-%VERSION%-win-x64.zip"
set "SHA_PATH=%ZIP_PATH%.sha256"
set "SIG_PATH=%ZIP_PATH%.sig"
set "SIGNING_KEY=%GENSHINPIANO_UPDATE_SIGNING_KEY%"
set "SANDBOX_DIR=%~dp0update-test-sandbox\install-current\win-x64"

if not exist "%SONGS_DIR%\" (
  echo.
  echo Songs directory not found:
  echo %SONGS_DIR%
  pause
  popd
  exit /b 1
)
if not defined SIGNING_KEY (
  echo.
  echo GENSHINPIANO_UPDATE_SIGNING_KEY is not set.
  echo Set it to the private update signing key before publishing.
  pause
  popd
  exit /b 1
)
if not exist "%SIGNING_KEY%" (
  echo.
  echo Update signing private key not found:
  echo %SIGNING_KEY%
  pause
  popd
  exit /b 1
)

echo Cleaning previous publish output...

if exist "%PUBLISH_DIR%" rmdir /s /q "%PUBLISH_DIR%"
if exist "%UPDATER_PUBLISH_DIR%" rmdir /s /q "%UPDATER_PUBLISH_DIR%"
if exist "%ZIP_PATH%" del /q "%ZIP_PATH%"
if exist "%SHA_PATH%" del /q "%SHA_PATH%"
if exist "%SIG_PATH%" del /q "%SIG_PATH%"

dotnet publish ".\src\GenshinPiano.App\GenshinPiano.App.csproj" ^
  -c Release ^
  -r win-x64 ^
  --self-contained true ^
  -p:Version=%VERSION% ^
  -p:PublishSingleFile=true ^
  -p:EnableCompressionInSingleFile=true ^
  -p:NuGetAudit=false ^
  -p:DebugType=None ^
  -p:DebugSymbols=false ^
  -o "%PUBLISH_DIR%"

if errorlevel 1 (
  echo.
  echo Publish failed.
  pause
  popd
  exit /b 1
)

echo Publishing standalone updater to a temporary folder...
dotnet publish ".\src\GenshinPiano.Updater\GenshinPiano.Updater.csproj" ^
  -c Release ^
  -r win-x64 ^
  --self-contained true ^
  -p:Version=%VERSION% ^
  -p:PublishAot=true ^
  -p:StripSymbols=true ^
  -p:NuGetAudit=false ^
  -p:DebugType=None ^
  -p:DebugSymbols=false ^
  -o "%UPDATER_PUBLISH_DIR%"
if errorlevel 1 (
  echo Updater publish failed.
  pause
  popd
  exit /b 1
)

if not exist "%UPDATER_PUBLISH_DIR%\GenshinPiano.Updater.exe" (
  echo.
  echo Updater executable was not generated:
  echo %UPDATER_PUBLISH_DIR%\GenshinPiano.Updater.exe
  pause
  popd
  exit /b 1
)

echo Copying standalone updater executable...
copy /y "%UPDATER_PUBLISH_DIR%\GenshinPiano.Updater.exe" "%PUBLISH_DIR%\" >nul
if errorlevel 1 (
  echo.
  echo Failed to copy updater executable.
  pause
  popd
  exit /b 1
)

echo Cleaning temporary updater output...
rmdir /s /q "%UPDATER_PUBLISH_DIR%"

echo Copying bundled songs...
xcopy "%SONGS_DIR%\*" "%PUBLISH_DIR%\songs\" /E /I /Y >nul
if errorlevel 1 (
  echo.
  echo Failed to copy bundled songs.
  pause
  popd
  exit /b 1
)

if not exist "%PUBLISH_DIR%\GenshinPiano.exe" (
  echo Main application executable is missing from publish output.
  pause
  popd
  exit /b 1
)
if not exist "%PUBLISH_DIR%\GenshinPiano.Updater.exe" (
  echo Updater executable is missing from publish output.
  pause
  popd
  exit /b 1
)
powershell -NoProfile -Command "if (-not (Get-ChildItem -LiteralPath (Join-Path $env:PUBLISH_DIR 'songs') -File -Recurse | Select-Object -First 1)) { exit 1 }"
if errorlevel 1 (
  echo No bundled songs were copied into publish output.
  pause
  popd
  exit /b 1
)

@REM echo Copying release to update test sandbox...
@REM if exist "%SANDBOX_DIR%" rmdir /s /q "%SANDBOX_DIR%"
@REM xcopy "%PUBLISH_DIR%\*" "%SANDBOX_DIR%\" /E /I /Y >nul
@REM if errorlevel 1 (
@REM   echo.
@REM   echo Failed to copy release to update test sandbox.
@REM   pause
@REM   popd
@REM   exit /b 1
@REM )

echo Creating ZIP package...
powershell -NoProfile -Command "Compress-Archive -Path (Join-Path $env:PUBLISH_DIR '*') -DestinationPath $env:ZIP_PATH -Force"
if errorlevel 1 (
  echo.
  echo Failed to create ZIP package.
  pause
  popd
  exit /b 1
)

echo Creating SHA-256 checksum...
powershell -NoProfile -Command "$hash=(Get-FileHash -LiteralPath $env:ZIP_PATH -Algorithm SHA256).Hash.ToLowerInvariant(); $name=[IO.Path]::GetFileName($env:ZIP_PATH); [IO.File]::WriteAllText($env:SHA_PATH, ($hash + '  ' + $name), [Text.Encoding]::ASCII)"
if errorlevel 1 (
  echo.
  echo Failed to create SHA-256 checksum.
  pause
  popd
  exit /b 1
)

echo Signing update package...
powershell -NoProfile -ExecutionPolicy Bypass -File ".\tools\Sign-UpdatePackage.ps1" -PackagePath "%ZIP_PATH%" -PrivateKeyPath "%SIGNING_KEY%" -SignaturePath "%SIG_PATH%"
if errorlevel 1 (
  echo.
  echo Failed to sign update package.
  pause
  popd
  exit /b 1
)
if not exist "%SIG_PATH%" (
  echo Update signature file was not generated.
  pause
  popd
  exit /b 1
)

echo.
echo Publish completed:
echo %PUBLISH_DIR%
echo.
echo ZIP package:
echo %ZIP_PATH%
echo %SHA_PATH%
echo %SIG_PATH%
echo.
echo Test sandbox copy:
echo %SANDBOX_DIR%
pause

popd
endlocal
