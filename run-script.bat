@echo off
setlocal enabledelayedexpansion

set REPO_ROOT=%~dp0
set REPO_ROOT=%REPO_ROOT:~0,-1%

set SCRIPTS_ROOT=%REPO_ROOT%\tools\scripts
set SCRIPTS_VENV=%SCRIPTS_ROOT%\venv

if not exist "%SCRIPTS_VENV%" (
    python3 -m venv "%SCRIPTS_VENV%"
    "%SCRIPTS_VENV%\Scripts\pip" install -r "%SCRIPTS_ROOT%\requirements.txt"
)

set SCRIPT_NAME=%1
if "%SCRIPT_NAME%"=="" (
    echo venv initialized: %SCRIPTS_VENV%
    exit /b 0
)

shift
"%SCRIPTS_VENV%\Scripts\python" "%SCRIPTS_ROOT%\%SCRIPT_NAME%.py" %*
