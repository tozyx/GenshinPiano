@echo off
setlocal
pushd "%~dp0"

set "VERSION=%~1"
if not defined VERSION set "VERSION=3.0.1-preview.1"
set "PUBLISH_DIR=%~dp0publish\GenshinPiano-win-x64"
set "SONGS_DIR=%~dp0publish\songs"
set "ZIP_PATH=%~dp0publish\GenshinPiano-%VERSION%-win-x64.zip"
set "SHA_PATH=%ZIP_PATH%.sha256"
set "SANDBOX_DIR=%~dp0update-test-sandbox\install-current\win-x64"

echo Cleaning previous publish output...

if exist "%PUBLISH_DIR%" (
  rmdir /s /q "%PUBLISH_DIR%"
)
if exist "%ZIP_PATH%" del /q "%ZIP_PATH%"
if exist "%SHA_PATH%" del /q "%SHA_PATH%"

if not exist "%SONGS_DIR%\" (
  echo.
  echo Songs directory not found:
  echo %SONGS_DIR%
  pause
  popd
  exit /b 1
)

dotnet publish ".\src\GenshinPiano.App\GenshinPiano.App.csproj" ^
  -c Release ^
  -r win-x64 ^
  --self-contained true ^
  -p:Version=%VERSION% ^
  -p:PublishSingleFile=true ^
  -p:EnableCompressionInSingleFile=true ^
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
dotnet publish ".\src\GenshinPiano.Updater\GenshinPiano.Updater.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o "%PUBLISH_DIR%"
if errorlevel 1 (
  echo Updater publish failed.
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

echo.
echo Publish completed:
echo %PUBLISH_DIR%
echo.
echo ZIP package:
echo %ZIP_PATH%
echo %SHA_PATH%
echo.
echo Test sandbox copy:
echo %SANDBOX_DIR%
pause

popd
endlocal
