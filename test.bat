@echo off
setlocal

set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
if not exist "%VSWHERE%" (
    echo ERROR: vswhere.exe not found.
    exit /b 1
)

for /f "usebackq tokens=*" %%i in (
    `"%VSWHERE%" -latest -products * -find MSBuild\**\Bin\MSBuild.exe`
) do set "MSBUILD=%%i"

if not defined MSBUILD (
    echo ERROR: MSBuild not found.
    exit /b 1
)

echo Building Jimmy...
call "%~dp0build.bat"
if errorlevel 1 (
    echo Jimmy build FAILED. Check build_out.txt.
    exit /b 1
)

echo.
echo Building JimmyTests...
"%MSBUILD%" "%~dp0JimmyTests\JimmyTests.csproj" /p:Configuration=Debug /t:Build /v:minimal
if errorlevel 1 (
    echo JimmyTests build FAILED.
    exit /b 1
)

echo.
echo Running parser unit tests...
echo.
"%~dp0JimmyTests\bin\Debug\JimmyTests.exe"
