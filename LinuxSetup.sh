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

# Function to check if .NET SDK 7.0.202 is installed
is_dotnet_installed() {
    if dotnet --list-sdks | grep -q "7.0.202"; then
        return 0  # Return success (true)
    else
        return 1  # Return failure (false)
    fi
}

# Function to check if .NET EF is installed
is_dotnet_ef_installed() {
    if dotnet tool list --global | grep -q "dotnet-ef"; then
        return 0  # Return success (true)
    else
        return 1  # Return failure (false)
    fi
}

# Function to check if Docker is installed
is_docker_installed() {
    if command -v docker &>/dev/null; then
        return 0  # Return success (true)
    else
        return 1  # Return failure (false)
    fi
}

installDOTNET() {
    if ! is_dotnet_installed; then
        echo "Downloading and installing .NET 7.0.202..."
        wget https://download.visualstudio.microsoft.com/download/pr/9a603243-a535-4f47-9359-089dccc1a27f/8f4c38b6f7867170f2e7d0153ea39ed5/dotnet-sdk-7.0.202-linux-x64.tar.gz
        tar -zxvf dotnet-sdk-7.0.202-linux-x64.tar.gz -C /opt/dotnet
        rm dotnet-sdk-7.0.202-linux-x64.tar.gz
        export PATH=$PATH:/opt/dotnet
        echo ".NET 7.0.202 has been installed."
        read -p "Press enter to continue."
        Start
    else
        Start
    fi
}

installDOTNETEF() {
    if ! is_dotnet_ef_installed; then
        echo "Downloading and installing dotnet-ef..."
        dotnet tool install --global dotnet-ef
        echo ".NET EF has been installed."
        read -p "Press enter to continue."
        Start
    else
        Start
    fi
}

installDocker() {
    if ! is_docker_installed; then
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
    if is_docker_installed; then
        echo "Attempting to create a new docker container with postgresql v14..."
        while IFS='=' read -r name value; do
            export "$name=$value"
        done < ./Database.cfg
        docker run --name $DbName -e POSTGRES_USER=$DbUsername -e POSTGRES_PASSWORD=$DbPassword -p $DbAddress:$DbPort:$DbPort -d postgres:14
        read -p "Press enter to continue"
        Start
    else
        Start
    fi
}

createInitialMigration() {
    if is_docker_installed; then
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
    if is_docker_installed; then
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

Start() {
    clear
    echo $(date '+%T')
    echo "=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-"
    echo "-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-="
    echo "This program is designed to help install all the applications required to run"
    echo "a FishMMO server. Please ensure all of the programs are installed."
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

Start