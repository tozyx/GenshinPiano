@echo off
setlocal
pushd "%~dp0"

set "PUBLISH_DIR=%~dp0publish\GenshinPiano-win-x64"

echo Cleaning previous publish output...

if exist "%PUBLISH_DIR%" (
  rmdir /s /q "%PUBLISH_DIR%"
)

dotnet publish ".\src\GenshinPiano.App\GenshinPiano.App.csproj" ^
  -c Release ^
  -r win-x64 ^
  --self-contained true ^
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

echo.
echo Publish completed:
echo %PUBLISH_DIR%
pause

popd
endlocal
