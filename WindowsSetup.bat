@echo off
setlocal enableextensions

cd /d "%~dp0"

>nul 2>&1 "%SYSTEMROOT%\system32\cacls.exe" "%SYSTEMROOT%\system32\config\system"
if '%ERRORLEVEL%' NEQ '0' (
    echo Requesting administrator privileges...
    goto UACPrompt
) else ( goto gotAdmin )

:UACPrompt
    echo Set UAC = CreateObject^("Shell.Application"^) > "%temp%\getadmin.vbs"
    set params = %*:"="
    echo UAC.ShellExecute "cmd.exe", "/c ""%~s0"" %params%", "", "runas", 1 >> "%temp%\getadmin.vbs"
    "%temp%\getadmin.vbs"
    del "%temp%\getadmin.vbs"
    exit /B

:gotAdmin
    echo Administrator privileges acquired.

ver > nul
if %ERRORLEVEL% NEQ 0 (
    echo Error: This script must be run under Windows.
    goto end
)

ver | find "Microsoft Windows [Version 10." > nul
if %ERRORLEVEL% NEQ 0 (
    echo Error: This script requires Windows 10 or later.
    goto end
)

where /q wsl.exe
if %ERRORLEVEL% EQU 0 (
    echo WSL is already installed.
) else (
    choice /C YN /M "Do you want to install WSL?"
    if errorlevel 1 (
        echo Installing WSL...
        dism.exe /online /enable-feature /featurename:Microsoft-Windows-Subsystem-Linux /all /norestart
        echo WSL has been installed.
    ) else (
        echo WSL installation cancelled.
    )
)

where /q dotnet
if %ERRORLEVEL% EQU 0 (
    dotnet --list-sdks | findstr /B "7." > nul
    if %ERRORLEVEL% EQU 0 (
        echo .NET 7 is already installed.
    ) else (
        choice /C YN /M "Do you want to install .NET 7?"
        if errorlevel  1 (
            echo .NET 7 is not installed. Installing...
            powershell.exe -ExecutionPolicy Bypass -Command "& {Invoke-WebRequest -Uri https://dot.net/v1/dotnet-install.ps1 -OutFile dotnet-install.ps1}"
            powershell.exe -ExecutionPolicy Bypass -Command "& {.\dotnet-install.ps1 -Version 7.x -InstallDir 'C:\Program Files\dotnet'}"
        ) else (
            echo .NET 7 installation cancelled.
        )
    )
) else (
    choice /C YN /M "Do you want to install .NET 7?"
    if errorlevel 1 (
        echo .NET is not installed. Installing...
        powershell.exe -ExecutionPolicy Bypass -Command "& {Invoke-WebRequest -Uri https://dot.net/v1/dotnet-install.ps1 -OutFile dotnet-install.ps1}"
        powershell.exe -ExecutionPolicy Bypass -Command "& {.\dotnet-install.ps1 -Version 7.x -InstallDir 'C:\Program Files\dotnet'}"
    ) else (
        echo .NET installation cancelled.
    )
)

dotnet ef --version >nul 2>&1
if %ERRORLEVEL% NEQ 0 (
    echo dotnet-ef is not installed.
    choice /C YN /M "Do you want to install the dotnet-ef tool?"
    if errorlevel 1 (
        dotnet tool install --global dotnet-ef
    ) else (
        echo .NET installation cancelled.
    )
) else (
    echo dotnet-ef is already installed.
)

where /q docker
if %ERRORLEVEL% EQU 0 (
    echo Docker is already installed.
) else (
    echo Docker is not installed.
    choice /C YN /M "Do you want to install Docker?"
    if errorlevel 1 (
        echo Downloading and installing Docker...
        curl https://download.docker.com/win/stable/Docker%20Desktop%20Installer.exe -o DockerInstaller.exe
        start /wait DockerInstaller.exe
        del DockerInstaller.exe
        echo Docker has been installed.
    ) else if errorlevel 2 (
        echo Docker installation cancelled.
    )
)

where /q docker
if %ERRORLEVEL% EQU 0 (
    choice /C YN /M "Do you want to create a Docker container for PostgreSQL 14?"
    if errorlevel 1 (
        echo Creating Docker container for PostgreSQL 14...
        setlocal enabledelayedexpansion
        for /f "tokens=1,* delims==" %%a in (./FishMMO/Database.cfg) do (
            set "%%a=%%b"
        )
        docker run --name !DbName! -e POSTGRES_USER=!DbUsername! -e POSTGRES_PASSWORD=!DbPassword! -p !DbAddress!:!DbPort!:!DbPort! -d postgres:14
        echo Docker container for PostgreSQL 14 has been created.
        choice /C YN /M "Do you want to create the database and initial migration?"
        if errorlevel 1 (
            dotnet ef migrations add Initial -p ./FishMMO-DB/FishMMO-DB/FishMMO-DB.csproj -s ./FishMMO-DB/FishMMO-DB-Migrator/FishMMO-DB-Migrator.csproj
            dotnet ef database update -p ./FishMMO-DB/FishMMO-DB/FishMMO-DB.csproj -s ./FishMMO-DB/FishMMO-DB-Migrator/FishMMO-DB-Migrator.csproj
        ) else if errorlevel 2 (
            echo Migration failed.
        )
    ) else if errorlevel 2 (
        echo Docker container creation cancelled.
    )
) else (
    echo Error: Docker is not installed.
)

pause
:end