@echo off
setlocal

set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
if not exist "%VSWHERE%" (
    echo ERROR: vswhere.exe not found.
    echo Install Visual Studio 2017 or later ^(any edition^) and re-run.
    exit /b 1
)

for /f "usebackq tokens=*" %%i in (
    `"%VSWHERE%" -latest -products * -find MSBuild\**\Bin\MSBuild.exe`
) do set "MSBUILD=%%i"

if not defined MSBUILD (
    echo ERROR: MSBuild not found via vswhere.
    echo Ensure Visual Studio is installed with build tools.
    exit /b 1
)

"%MSBUILD%" "%~dp0WSJTX_Controller\Jimmy.csproj" /p:Configuration=Debug /t:Build /v:minimal > "%~dp0build_out.txt" 2>&1
echo Exit code: %ERRORLEVEL% >> "%~dp0build_out.txt"
