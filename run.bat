@echo off
setlocal
set ASPNETCORE_ENVIRONMENT=Development

echo ServerBackup baslatiliyor...
start "ServerBackup.Service" cmd /k "cd /d "%~dp0" && dotnet run --project src\ServerBackup.Service"

timeout /t 5 /nobreak >nul
start "" http://localhost:5000

endlocal
