@echo off
title Live Sheet Jira Board - Viewer
echo ========================================================
echo   Live Sheet Jira Board - Viewer
echo   Zero Dummy Data - 544 Live Issues with Real Photos
echo   GLightbox HD Viewer - Compact Read-Only Interface
echo ========================================================
echo.
echo Building latest changes...
dotnet build -o bin\Build --nologo -v minimal
if %ERRORLEVEL% NEQ 0 (
    echo Build failed! Please check errors above.
    pause
    exit /b %ERRORLEVEL%
)
echo.
echo Opening http://localhost:5057 in your default browser...
start http://localhost:5057
echo.
echo Starting application on http://localhost:5057 ...
dotnet bin\Build\JiraSheetBoard.dll --urls "http://localhost:5057"
pause
