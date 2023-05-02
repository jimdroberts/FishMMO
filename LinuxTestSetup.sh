#!/bin/bash

cd "$(dirname "$0")"

if [ "$EUID" -ne 0 ]; then
    echo "Requesting administrator privileges..."
    sudo "$0" "$@"
    exit
fi

echo "Administrator privileges acquired."

if [ "$(uname)" != "Linux" ]; then
    echo "Error: This script must be run under Linux."
    exit
fi

Start() {
    clear
    echo $(date '+%T')
    echo "=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-"
    echo "-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-="
    echo "This program is designed to help install all the applications required to run"
    echo "a FishMMO server. Please ensure all of the following "
    echo "Type the number of the option you wish to execute, followed by the [ENTER] key"
    echo "=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-"
    echo "-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-="
    echo ""
    echo "1 - Install dotnet 7"
    echo "2 - Install dotnet-ef"
    echo "3 - Install docker"
    echo "4 - Create docker container"
    echo "5 - Create initial migration"
    echo "6 - Create new migration"
    echo "7 - Exit"
    
    read -p "Enter your choice: " Choice

    case $Choice in
        1)
            installDOTNET
            ;;
        2)
            installDOTNETEF
            ;;
        3)
            installDocker
            ;;
        4)
            createDockerContainer
            ;;
        5)
            createInitialMigration
            ;;
        6)
            createNewMigration
            ;;
        7)
            exit
            ;;
        *)
            echo "Invalid choice. Try again."
            Start
            ;;
    esac
}

installDOTNET() {
    if command -v dotnet >/dev/null 2>&1; then
        if ! dotnet --list-sdks | grep -q "^7\."; then
            echo "Downloading and installing dotnet v7..."
            powershell.exe -ExecutionPolicy Bypass -Command "& {Invoke-WebRequest -Uri https://dot.net/v1/dotnet-install.ps1 -OutFile dotnet-install.ps1}"
            powershell.exe -ExecutionPolicy Bypass -Command "& {.\dotnet-install.ps1 -Version 7.0.202 -InstallDir '/usr/local/share/dotnet'}"
            read -p "Press enter to continue."
            Start
        else
            Start
        fi
    else
        echo "Downloading and installing dotnet v7..."
        powershell.exe -ExecutionPolicy Bypass -Command "& {Invoke-WebRequest -Uri https://dot.net/v1/dotnet-install.ps1 -OutFile dotnet-install.ps1}"
        powershell.exe -ExecutionPolicy Bypass -Command "& {.\dotnet-install.ps1 -Version 7.0.202 -InstallDir '/usr/local/share/dotnet'}"
        read -p "Press enter to continue."
        Start
    fi
}

installDOTNETEF() {
    if ! command -v dotnet ef >/dev/null 2>&1; then
        echo "Downloading and installing dotnet-ef..."
        dotnet tool install --global dotnet-ef
        read -p "Press enter to continue."
        Start
    else
        Start
    fi
}

installDocker() {
    if ! command -v docker >/dev/null 2>&1; then
        echo "Downloading and installing docker..."
        curl -fsSL https://get.docker.com -o get-docker.sh
        sudo sh get-docker.sh
        sudo usermod -aG docker $USER
        rm get-docker.sh
        echo "Docker has been installed."
        read -p "Press enter to continue."
        Start
    else
        Start
    fi
}

createDockerContainer() {
    if command -v docker &> /dev/null; then
        echo "Attempting to create a new docker container with postgresql v14..."
        while IFS='=' read -r name value; do
            export "$name=$value"
        done < ./FishMMO/Database.cfg
        docker run --name $DbName -e POSTGRES_USER=$DbUsername -e POSTGRES_PASSWORD=$DbPassword -p $DbAddress:$DbPort:$DbPort -d postgres:14
        read -p "Press enter to continue"
        Start
    else
        Start
    fi
}

createInitialMigration() {
    if command -v docker &> /dev/null; then
        projectPath=./FishMMO-DB/FishMMO-DB/FishMMO-DB.csproj
        startupProject=./FishMMO-DB/FishMMO-DB-Migrator/FishMMO-DB-Migrator.csproj
        dotnet ef migrations add Initial -p "$projectPath" -s "$startupProject"
        dotnet ef database update -p "$projectPath" -s "$startupProject"
        read -p "Press enter to continue"
        Start
    else
        Start
    fi
}

createNewMigration() {
    if command -v docker &> /dev/null; then
        projectPath=./FishMMO-DB/FishMMO-DB/FishMMO-DB.csproj
        startupProject=./FishMMO-DB/FishMMO-DB-Migrator/FishMMO-DB-Migrator.csproj
        timestamp=$(date +%F-%H-%M-%S)
        migrationName=Migration_$timestamp
        dotnet ef migrations add "$migrationName" -p "$projectPath" -s "$startupProject"
        dotnet ef database update -p "$projectPath" -s "$startupProject"
        read -p "Press enter to continue"
        Start
    else
        Start
    fi
}

Start