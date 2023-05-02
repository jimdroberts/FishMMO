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

:Start
cls
echo                                   %TIME%
echo  =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-
echo  -=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
echo  This program is designed to help install all the applications required to run
echo  a FishMMO server. Please ensure all of the following 
echo  Type the number of the option you wish to execute, followed by the [ENTER] key
echo  =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-
echo  -=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
echo.
echo                         1 - Install wsl
echo                         2 - Install dotnet 7
echo                         3 - Install dotnet-ef
echo                         4 - Install docker
echo                         5 - Create docker container
echo                         6 - Create initial migration
echo                         7 - Create new migration
echo                         8 - Exit

set Choice=
set /p Choice=""

if '%Choice%'=='1' goto installWSL
if '%Choice%'=='2' goto installDOTNET
if '%Choice%'=='3' goto installDOTNETEF
if '%Choice%'=='4' goto installDocker
if '%Choice%'=='5' goto createDockerContainer
if '%Choice%'=='6' goto createInitialMigration
if '%Choice%'=='7' goto createNewMigration
if '%Choice%'=='8' goto exit

:installWSL
where /q wsl.exe
if %ERRORLEVEL% NEQ 0 (
    echo Installing WSL...
    dism.exe /online /enable-feature /featurename:Microsoft-Windows-Subsystem-Linux /all /norestart
    echo WSL has been installed.
    pause
    goto Start
) else (
    goto Start
)

:installDOTNET
where /q dotnet
if %ERRORLEVEL% EQU 0 (
    dotnet --list-sdks | findstr /B "7." > nul
    if %ERRORLEVEL% NEQ 0 (
        echo Downloading and installing dotnet v7...
        powershell.exe -ExecutionPolicy Bypass -Command "& {Invoke-WebRequest -Uri https://dot.net/v1/dotnet-install.ps1 -OutFile dotnet-install.ps1}"
        powershell.exe -ExecutionPolicy Bypass -Command "& {.\dotnet-install.ps1 -Version 7.0.202 -InstallDir 'C:\Program Files\dotnet'}"
        pause
        goto Start
    ) else (
        goto Start
    )
) else (
    echo Downloading and installing dotnet v7...
    powershell.exe -ExecutionPolicy Bypass -Command "& {Invoke-WebRequest -Uri https://dot.net/v1/dotnet-install.ps1 -OutFile dotnet-install.ps1}"
    powershell.exe -ExecutionPolicy Bypass -Command "& {.\dotnet-install.ps1 -Version 7.0.202 -InstallDir 'C:\Program Files\dotnet'}"
    pause
    goto Start
)

:installDOTNETEF
dotnet ef --version >nul 2>&1
if %ERRORLEVEL% NEQ 0 (
    echo Downloading and installing dotnet-ef...
    dotnet tool install --global dotnet-ef
    pause
    goto Start
) else (
    goto Start
)

:installDocker
where /q docker
if %ERRORLEVEL% NEQ 0 (
    echo Downloading and installing docker...
    curl https://download.docker.com/win/stable/Docker%20Desktop%20Installer.exe -o DockerInstaller.exe
    start /wait DockerInstaller.exe
    del DockerInstaller.exe
    echo Docker has been installed.
    pause
    goto Start
) else (
    goto Start
)

:createDockerContainer
where /q docker
if %ERRORLEVEL% EQU 0 (
    echo Attempting to create a new docker container with postgresql v14...
    setlocal enabledelayedexpansion
    for /f "tokens=1,* delims==" %%a in (./FishMMO/Database.cfg) do (
        set "%%a=%%b"
    )
    docker run --name !DbName! -e POSTGRES_USER=!DbUsername! -e POSTGRES_PASSWORD=!DbPassword! -p !DbAddress!:!DbPort!:!DbPort! -d postgres:14
    pause
    goto Start
) else (
    goto Start
)

:createInitialMigration
where /q docker
if %ERRORLEVEL% EQU 0 (
    set "projectPath=./FishMMO-DB/FishMMO-DB/FishMMO-DB.csproj"
    set "startupProject=./FishMMO-DB/FishMMO-DB-Migrator/FishMMO-DB-Migrator.csproj"
    dotnet ef migrations add Initial -p "!projectPath!" -s "!startupProject!"
    dotnet ef database update -p "!projectPath!" -s "!startupProject!"
    pause
    goto Start
) else (
    goto Start
)

:createNewMigration
where /q docker
if %ERRORLEVEL% EQU 0 (
    setlocal enabledelayedexpansion
    set "projectPath=./FishMMO-DB/FishMMO-DB/FishMMO-DB.csproj"
    set "startupProject=./FishMMO-DB/FishMMO-DB-Migrator/FishMMO-DB-Migrator.csproj"
    set "timestamp=%DATE:/=-%%TIME::=-%"
    set "timestamp=!timestamp:.=-!"
    set "migrationName=Migration_!timestamp!"
    dotnet ef migrations add "!migrationName!" -p "!projectPath!" -s "!startupProject!"
    dotnet ef database update -p "!projectPath!" -s "!startupProject!"
    pause
    goto Start
) else (
    goto Start
)

:end