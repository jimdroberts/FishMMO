#!/bin/bash

# Read settings from config file
source Database.cfg

# Check if Docker is installed
if ! command -v docker &> /dev/null
then
    echo "Docker is not installed."
    read -p "Do you want to install Docker? (y/n)" install_docker
    if [ "$install_docker" != "y" ] && [ "$install_docker" != "Y" ]; then
        echo "Aborting script."
        exit 1
    fi
    # Install Docker
    sudo apt-get update
    sudo apt-get install docker.io
    sudo systemctl start docker
    sudo systemctl enable docker
    echo "Docker installed successfully."
else
    echo "Docker is already installed."
fi

# Ask to install PostgreSQL container
read -p "Do you want to install PostgreSQL in a Docker container? (y/n)" install_postgres
if [ "$install_postgres" == "y" ] || [ "$install_postgres" == "Y" ]; then
    # Check if PostgreSQL container is already running
    if ! sudo docker ps --format '{{.Names}}' | grep -q "^$dbName\$"; then
        echo "Starting PostgreSQL Docker container..."
        # Run PostgreSQL Docker container
        sudo docker run --name "$DbName" \
            -e POSTGRES_USER="$DbUsername" \
            -e POSTGRES_PASSWORD="$DbPassword" \
            -p "$DbAddress":"$DbPort":"$DbPort" \
            -d postgres:14
        echo "PostgreSQL Docker container started successfully."
    else
        echo "PostgreSQL Docker container is already running."
    fi
fi

# Check if dotnet is installed and output the version
if ! command -v dotnet &> /dev/null
then
    echo "dotnet is not installed."
    read -p "Do you want to install dotnet 7? (y/n)" install_dotnet
    if [ "$install_dotnet" != "y" ] && [ "$install_dotnet" != "Y" ]; then
        echo "Aborting script."
        exit 1
    fi
    # Download and install dotnet 7
    wget https://download.visualstudio.microsoft.com/download/pr/ade531ca-5b4a-4f17-8c34-9faa0a0c5101/895d6fde24a55f7b2df05089b124915e/dotnet-sdk-7.1.403-linux-x64.tar.gz
    mkdir -p "$HOME/dotnet"
    tar zxf dotnet-sdk-7.1.403-linux-x64.tar.gz -C "$HOME/dotnet"
    export PATH="$HOME/dotnet:$PATH"
    echo "dotnet 7 installed successfully."
else
    echo "dotnet version:"
    dotnet --version
fi

# Perform initial migration
echo "Performing initial migration..."
cd /path/to/codebase
dotnet ef database update --connection "Host=$DbAddress;Port=$DbPort;Database=$DbName;Username=$DbUsername;Password=$DbPassword;" --project MyProject
echo "Initial migration completed successfully."