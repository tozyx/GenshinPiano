@echo off
setlocal
pushd "%~dp0"

set "VERSION=%~1"
if not defined VERSION set "VERSION=3.0.3"
set "PUBLISH_DIR=%~dp0publish\GenshinPiano-win-x64-framework"
set "SONGS_DIR=%~dp0publish\songs"
set "ZIP_PATH=%~dp0publish\GenshinPiano-%VERSION%-win-x64-framework.zip"
set "SHA_PATH=%ZIP_PATH%.sha256"
set "SIG_PATH=%ZIP_PATH%.sig"
set "SIGNING_KEY=%GENSHINPIANO_UPDATE_SIGNING_KEY%"
set "SANDBOX_DIR=%~dp0update-test-sandbox\install-current\win-x64-framework"

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

if exist "%PUBLISH_DIR%" (
  echo Removing framework publish directory...
  rmdir /s /q "%PUBLISH_DIR%"
  if exist "%PUBLISH_DIR%" (
    echo.
    echo Failed to remove framework publish directory. Close any running GenshinPiano instance from:
    echo %PUBLISH_DIR%
    pause
    popd
    exit /b 1
  )
)
if exist "%ZIP_PATH%" del /q "%ZIP_PATH%"
if exist "%SHA_PATH%" del /q "%SHA_PATH%"
if exist "%SIG_PATH%" del /q "%SIG_PATH%"

echo Publishing framework-dependent application...
dotnet publish ".\src\GenshinPiano.App\GenshinPiano.App.csproj" ^
  -c Release ^
  -r win-x64 ^
  --self-contained false ^
  -p:Version=%VERSION% ^
  -p:PublishSingleFile=true ^
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

echo Publishing standalone updater...
dotnet publish ".\src\GenshinPiano.Updater\GenshinPiano.Updater.csproj" ^
  -c Release ^
  -r win-x64 ^
  --self-contained false ^
  -p:Version=%VERSION% ^
  -p:PublishSingleFile=true ^
  -p:NuGetAudit=false ^
  -p:DebugType=None ^
  -p:DebugSymbols=false ^
  -o "%PUBLISH_DIR%"
if errorlevel 1 (
  echo Updater publish failed.
  pause
  popd
  exit /b 1
)

echo Publishing bundled and standalone OCR add-on packages...
powershell -NoProfile -ExecutionPolicy Bypass -File ".\tools\Publish-OcrAddon.ps1" -AddonRootDirectory "%~dp0publish\addons" -ArtifactsDirectory "%~dp0publish" -PrivateKeyPath "%SIGNING_KEY%"
if errorlevel 1 (
  echo.
  echo Failed to publish OCR add-on.
  pause
  popd
  exit /b 1
)

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
echo OCR add-on packages:
dir /b "%~dp0publish\ocr-addons-*-win-x64.zip*" 2>nul
echo.
echo Test sandbox copy:
echo %SANDBOX_DIR%
pause

popd
endlocal
