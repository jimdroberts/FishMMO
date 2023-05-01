@echo off
setlocal enableextensions

cd /d "%~dp0"

>nul 2>&1 "%SYSTEMROOT%\system32\cacls.exe" "%SYSTEMROOT%\system32\config\system"
if '%errorlevel%' NEQ '0' (
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

where /q docker
if '%errorlevel%' EQU '0' (
    choice /M "Do you want to create a Docker container for PostgreSQL 14?"
    if '%errorlevel%' EQU '0' (
        echo Creating Docker container for PostgreSQL 14...
        setlocal enabledelayedexpansion
        for /f "tokens=1,* delims==" %%a in (./FishMMO/Database.cfg) do (
            set "%%a=%%b"
        )
        docker run --name !DbName! -e POSTGRES_USER=!DbUsername! -e POSTGRES_PASSWORD=!DbPassword! -p !DbAddress!:!DbPort!:!DbPort! -d postgres:14
        echo Docker container for PostgreSQL 14 has been created.

        set migrationExists=
        dotnet ef migrations list -p ./FishMMO-DB/FishMMO-DB/FishMMO-DB.csproj -s ./FishMMO-DB/FishMMO-DB-Migrator/FishMMO-DB-Migrator.csproj | findstr /c:"Initial" > nul
        if '%errorlevel%' EQU '0' (
            set migrationExists=true
        )
        if not defined migrationExists (
            choice /M "Do you want to create the database and initial migration?"
            if '%errorlevel%' EQU '0' (
                dotnet ef migrations add Initial -p ./FishMMO-DB/FishMMO-DB/FishMMO-DB.csproj -s ./FishMMO-DB/FishMMO-DB-Migrator/FishMMO-DB-Migrator.csproj
                dotnet ef database update -p ./FishMMO-DB/FishMMO-DB/FishMMO-DB.csproj -s ./FishMMO-DB/FishMMO-DB-Migrator/FishMMO-DB-Migrator.csproj
            ) else (
                echo Migration failed.
            )
        ) else (
            echo Initial migration already exists.

            choice /M "Do you want to create a new migration?"
            if '%errorlevel%' EQU '0' (
                set migrationName=Migration_%date:/=-%_%time::=-%
                set migrationName=!migrationName: =0!
                dotnet ef migrations add !migrationName! -p ./FishMMO-DB/FishMMO-DB/FishMMO-DB.csproj -s ./FishMMO-DB/FishMMO-DB-Migrator/FishMMO-DB-Migrator.csproj
                dotnet ef database update -p ./FishMMO-DB/FishMMO-DB/FishMMO-DB.csproj -s ./FishMMO-DB/FishMMO-DB-Migrator/FishMMO-DB-Migrator.csproj
            ) else (
                echo New migration creation cancelled.
            )
        )
    ) else (
        echo Error: Docker is not installed.
    )
) 

pause
:end