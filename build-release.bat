@echo off
setlocal
pushd "%~dp0"

set "VERSION=%~1"
if not defined VERSION set "VERSION=3.0.0-preview.1"
set "PUBLISH_DIR=%~dp0publish\GenshinPiano-win-x64"
set "SONGS_DIR=%~dp0publish\songs"
set "ZIP_PATH=%~dp0publish\GenshinPiano-%VERSION%-win-x64.zip"

echo Cleaning previous publish output...

if exist "%PUBLISH_DIR%" (
  rmdir /s /q "%PUBLISH_DIR%"
)
if exist "%ZIP_PATH%" del /q "%ZIP_PATH%"

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

echo Copying bundled songs...
xcopy "%SONGS_DIR%\*" "%PUBLISH_DIR%\songs\" /E /I /Y >nul
if errorlevel 1 (
  echo.
  echo Failed to copy bundled songs.
  pause
  popd
  exit /b 1
)

echo Creating ZIP package...
powershell -NoProfile -Command "Compress-Archive -Path (Join-Path $env:PUBLISH_DIR '*') -DestinationPath $env:ZIP_PATH -Force"
if errorlevel 1 (
  echo.
  echo Failed to create ZIP package.
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
pause

popd
endlocal
