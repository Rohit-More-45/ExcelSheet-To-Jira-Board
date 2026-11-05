@echo off
title Live Sheet Jira Board - Viewer
echo ========================================================
echo   Live Sheet Jira Board - Viewer
echo   100%% Generic: Driven Entirely by User-Entered Link
echo   Zero Hardcoded Links - Streamlined Jira Interface
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
