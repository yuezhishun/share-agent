@echo off
setlocal enabledelayedexpansion

set "ProjectRoot=D:\workspace\code\ai-agent\share-agent\apps"
set "FrontendPath=%ProjectRoot%\secretary-web"
set "BackendPath=%ProjectRoot%\terminal-gateway-dotnet\TerminalGateway.Api"
set "WwwrootPath=%BackendPath%\wwwroot"
set "PublishPath=%BackendPath%\bin\Release\net10.0\publish"

echo === Step 1: Build Frontend ===
cd /d "%FrontendPath%"
call npm install
if errorlevel 1 (
    echo Frontend npm install failed!
    exit /b 1
)
call npm run build
if errorlevel 1 (
    echo Frontend build failed!
    exit /b 1
)

echo.
echo === Step 2: Clean wwwroot ===
if exist "%WwwrootPath%" (
    rmdir /s /q "%WwwrootPath%"
)
mkdir "%WwwrootPath%"

echo.
echo === Step 3: Copy build output to wwwroot ===
xcopy /s /e /y "%FrontendPath%\dist\*" "%WwwrootPath%\" >nul

echo.
echo === Step 4: Publish Backend ===
cd /d "%BackendPath%"
dotnet publish -c Release -o "%PublishPath%"
if errorlevel 1 (
    echo Backend publish failed!
    exit /b 1
)

echo.
echo === Deploy Complete ===
echo Backend published to: %PublishPath%
echo Frontend files copied to: %WwwrootPath%
