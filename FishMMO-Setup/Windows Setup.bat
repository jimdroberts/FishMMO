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
echo  a FishMMO server. Please ensure all of the programs are installed.
echo  Type the number of the option you wish to execute, followed by the [ENTER] key
echo  =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-
echo  -=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
echo.
echo                         1 - Install WSL
echo                         2 - Install DotNet 7
echo                         3 - Install Docker
echo                         4 - Install Database
echo                         5 - Create New Migration
echo                         6 - Exit

set Choice=
set /p Choice=""

if '%Choice%'=='1' goto installWSL
if '%Choice%'=='2' goto installDOTNET
if '%Choice%'=='3' goto installDocker
if '%Choice%'=='4' goto installDatabase
if '%Choice%'=='5' goto createNewMigration
if '%Choice%'=='6' goto exit

:installWSL
where /q wsl.exe
if %ERRORLEVEL% NEQ 0 (
    echo Installing WSL...
    dism.exe /online /enable-feature /featurename:Microsoft-Windows-Subsystem-Linux /all /norestart
    echo WSL has been installed.
    pause
    goto Start
) else (
	echo WSL is already installed.
	pause
    goto Start
)

:installDOTNET
where /q dotnet
if %ERRORLEVEL% EQU 0 (
    dotnet --list-sdks | findstr /B "7." > nul
    if %ERRORLEVEL% NEQ 0 (
        echo Downloading and installing DotNet 7...
        powershell.exe -ExecutionPolicy Bypass -Command "& {Invoke-WebRequest -Uri https://dot.net/v1/dotnet-install.ps1 -OutFile dotnet-install.ps1}"
        powershell.exe -ExecutionPolicy Bypass -Command "& {.\dotnet-install.ps1 -Version 7.0.202 -InstallDir 'C:\Program Files\dotnet'}"
		
		:: Install DotNet-EF
		goto installDOTNETEF
    ) else (
		echo DotNet 7 is already installed.
		
		:: Install DotNet-EF
        goto installDOTNETEF
    )
) else (
    echo Downloading and installing DotNet 7...
    powershell.exe -ExecutionPolicy Bypass -Command "& {Invoke-WebRequest -Uri https://dot.net/v1/dotnet-install.ps1 -OutFile dotnet-install.ps1}"
    powershell.exe -ExecutionPolicy Bypass -Command "& {.\dotnet-install.ps1 -Version 7.0.202 -InstallDir 'C:\Program Files\dotnet'}"
	
	:: Install DotNet-EF
	goto installDOTNETEF
)

:installDOTNETEF
dotnet ef --version >nul 2>&1
if %ERRORLEVEL% NEQ 0 (
    echo Downloading and installing DotNet-EF...
    dotnet tool install --global dotnet-ef
    pause
    goto Start
) else (
	echo DotNet-EF is already installed.
	pause
    goto Start
)

:installDocker
where /q docker
if %ERRORLEVEL% NEQ 0 (
    echo Downloading and installing Docker...
    curl https://download.docker.com/win/stable/Docker%20Desktop%20Installer.exe -o DockerInstaller.exe
    start /wait DockerInstaller.exe
    del DockerInstaller.exe
    echo Docker has been installed.
    pause
    goto Start
) else (
	echo Docker is already installed.
	pause
    goto Start
)

:installDatabase
where /q docker
if %ERRORLEVEL% EQU 0 (
    echo Attempting to create a new Docker container with Postgresql and Redis...
	
	setlocal enabledelayedexpansion
	
	for /f "tokens=2 delims=:" %%a in ('findstr /C:"Npgsql" appsettings.json') do (
	  set connection_string=%%a
	)
	set "connection_string=!connection_string:~2,-2!"

	:: Set up POSTGRES env
	set "prevKey="
	for %%A in (!connection_string!) do (
		set "line=%%A"
		for %%B in (!line!) do (
			if "!prevKey!" == "Host" (
				set POSTGRES_HOST=%%B
			) else if "!prevKey!" == "Port" (
				set POSTGRES_PORT=%%B
			) else if "!prevKey!" == "ID" (
				set POSTGRES_USER=%%B
			) else if "!prevKey!" == "Password" (
				set POSTGRES_PASSWORD=%%B
			) else if "!prevKey!" == "Database" (
				set POSTGRES_DB=%%B
			)
			set "prevKey=%%B"
		)
	)
	
	echo.
	:: Echo the values of PostgreSQL connection variables
	echo Postgresql configuration:
	echo User ID: !POSTGRES_USER!
    echo Password: *******
    echo Host: !POSTGRES_HOST!
    echo Port: !POSTGRES_PORT!
    echo Database: !POSTGRES_DB!
	echo.
	
	:: Set up REDIS env
	for /f "tokens=1,* delims=:" %%a in ('findstr /C:"Redis" appsettings.json') do (
	  set REDIS_HOST_STRING=%%b
	)
	set "REDIS_HOST_STRING=!REDIS_HOST_STRING:~2,-1!"

	:: Echo the Redis host string
	echo Redis configuration:
	echo Redis: !REDIS_HOST_STRING!
	echo.

	:: Start the docker container
	docker-compose up -d

	:: Setup the initial database migration
    set "projectPath=./FishMMO-Database/FishMMO-DB/FishMMO-DB.csproj"
    set "startupProject=./FishMMO-Database/FishMMO-DB-Migrator/FishMMO-DB-Migrator.csproj"
	dotnet ef migrations add Initial -p "!projectPath!" -s "!startupProject!"
    dotnet ef database update -p "!projectPath!" -s "!startupProject!"
	
    pause
    goto Start
) else (
	echo Docker is not installed.
	pause
    goto Start
)

:createNewMigration
where /q docker
if %ERRORLEVEL% EQU 0 (
    setlocal enabledelayedexpansion
    set "projectPath=./FishMMO-Database/FishMMO-DB/FishMMO-DB.csproj"
    set "startupProject=./FishMMO-Database/FishMMO-DB-Migrator/FishMMO-DB-Migrator.csproj"
    set "timestamp=%DATE:/=-%%TIME::=-%"
    set "timestamp=!timestamp:.=-!"
    set "migrationName=Migration_!timestamp!"
    dotnet ef migrations add "!migrationName!" -p "!projectPath!" -s "!startupProject!"
    dotnet ef database update -p "!projectPath!" -s "!startupProject!"
    pause
    goto Start
) else (
	echo Docker is not installed.
	pause
    goto Start
)

:end