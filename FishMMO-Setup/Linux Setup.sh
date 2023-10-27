#!/bin/sh

# Function to check if dos2unix is installed.
install_dos2unix() {
	if ! command -v dos2unix >/dev/null 2>&1; then
		echo "dos2unix is not installed. Installing..."
				
		# Install dos2unix using apt-get (Debian/Ubuntu)
		sudo apt-get update
		sudo apt-get install -y dos2unix
				
		# Check if the installation was successful
		if [ $? -eq 0 ]; then
			echo "dos2unix has been installed successfully."
		else
			echo "Failed to install dos2unix. Please install it manually."
			return 1
		fi
	else
		echo "dos2unix is already installed."
	fi
}

# Function to check if .NET SDK 7.0.202 is installed
is_dotnet_installed() {
    if dotnet --list-sdks | grep -q "7"; then
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
    if [ -x "$(command -v docker)" ]; then
        return 0  # Return success (true)
    else
        return 1  # Return failure (false)
    fi
}

# Function to start Docker service
start_docker() {
    if ! sudo systemctl is-active --quiet docker; then
        echo "Starting Docker service..."
        sudo systemctl start docker
    fi
}

# Function to install .NET SDK 7.0
install_dotnet() {
	if is_dotnet_installed; then
		echo ".Net SDK 7 is already installed."
	else
		echo "Installing .NET SDK 7..."
		sudo apt-get update && sudo apt-get install -y dotnet-sdk-7.0
	fi
}

# Function to install .NET EF
install_dotnet_ef() {
	if is_dotnet_ef_installed; then
		echo ".Net EF is already installed."
	else
		echo "Installing .NET EF..."
		dotnet tool install --global dotnet-ef
		export PATH=$PATH:$HOME/.dotnet/tools
		read -p "Press Enter to continue..."
	fi	
}

# Function to install Docker on various Unix platforms
install_docker() {
    if is_docker_installed; then
        echo "Docker is already installed."
    else
        echo "Installing Docker..."
        if [ -f /etc/os-release ]; then
            # Detect the Unix platform (e.g., Ubuntu, CentOS, etc.)
            . /etc/os-release
            case "$ID" in
                ubuntu|debian)
                    # Install Docker on Debian/Ubuntu
                    sudo apt-get update
                    sudo apt-get install -y docker.io
                    ;;
                centos|rhel|fedora)
                    # Install Docker on CentOS/RHEL/Fedora
                    sudo yum install -y docker
                    ;;
                *)
                    echo "Unsupported Unix platform: $ID"
                    return 1
                    ;;
            esac

            # Start and enable Docker service
            sudo systemctl start docker
            sudo systemctl enable docker

            echo "Docker has been successfully installed."
        else
            echo "Unable to detect the Unix platform. Please install Docker manually."
        fi
    fi
}

# Function to create a Docker container for PostgreSQL
create_postgres_container() {
	if is_docker_installed; then
		start_docker

		# Specify the full path to the Database.cfg file
        config_file="./Database.cfg"
		
		if [ ! -f "$config_file" ]; then
            echo "Database configuration file ($config_file) not found."
        else
			docker ps -a
		
			# Ensure that the line endings in the config file are in Unix format
			install_dos2unix
			dos2unix "$config_file"
		
			echo "Creating a PostgreSQL Docker container..."
			. "$config_file"  # Load database info from the configuration file
			
			# Create the container and install postgres 14
			docker run --name $DbName -e POSTGRES_USER=$DbUsername -e POSTGRES_PASSWORD=$DbPassword -p $DbAddress:$DbPort:$DbPort -d postgres:14
			
			# Ensure the container is running
			docker start fish_mmo_postgresql
			echo "PostgreSQL Docker container created with name: $DbName"
		fi
	else
		echo "Docker needs to be installed first."
	fi
}

# Function to create an initial migration and optionally update the database using Entity Framework
create_initial_migration() {
	if is_docker_installed; then
		start_docker
		
		projectPath="./FishMMO-Database/FishMMO-DB/FishMMO-DB.csproj"
        startupProject="./FishMMO-Database/FishMMO-DB-Migrator/FishMMO-DB-Migrator.csproj"
		export PATH="$PATH:$HOME/.dotnet/tools"
		
		echo "Creating an initial migration..."
		dotnet ef migrations add Initial -p "$projectPath" -s "$startupProject"
        dotnet ef database update -p "$projectPath" -s "$startupProject"
		echo "Database updated."
	else
		echo "Docker needs to be installed first."
	fi
}

# Function to create a new migration and update the database properly
createNewMigration() {
    if is_docker_installed; then
		start_docker
	
        projectPath="./FishMMO-Database/FishMMO-DB/FishMMO-DB.csproj"
        startupProject="./FishMMO-Database/FishMMO-DB-Migrator/FishMMO-DB-Migrator.csproj"
        export PATH="$PATH:$HOME/.dotnet/tools"
		
        # Generate a timestamp for the migration name
        timestamp=$(date +"%Y-%m-%d-%H-%M-%S")
        migrationName="Migration_$timestamp"
        
		echo "Creating new migration..."
        # Run the migrations and database update commands
        dotnet ef migrations add "$migrationName" -p "$projectPath" -s "$startupProject"
        dotnet ef database update -p "$projectPath" -s "$startupProject"
		echo "Migration applied."
    fi
}

# Function to display menu and get user choice
display_menu() {
	clear
	echo "Welcome to the Installation Script"
	echo "Choose an option:"
	echo "1. Install .NET SDK 7"
	echo "2. Install .NET EF"
	echo "3. Install Docker"
	echo "4. Create a PostgreSQL Docker container"
	echo "5. Create an Initial Migration"
	echo "6. Create a new Migration"
	echo "7. Quit"
}

# Main script
while true; do
	display_menu
	read -p "Enter your choice: " choice

	case $choice in
		1)
			install_dotnet
			;;
		2)
			install_dotnet_ef
			;;
		3)
			install_docker
			;;
		4)
			create_postgres_container
			;;
		5)
			create_initial_migration
			;;
		6)
			createNewMigration
			;;
		7)
			break
			;;
		*)
			echo "Invalid choice. Please select 1, 2, 3, 4, or 5."
			;;
	esac
	
	# Wait for user to press Enter before displaying the menu again
    echo "Press Enter to continue..."
    read dummy
done