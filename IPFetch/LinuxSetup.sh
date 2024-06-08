#!/bin/bash

# Display a message
echo "Installing required Python packages..."

# Install the psycopg2 package
pip install psycopg2

# Display completion message
echo "Installation complete."

# Pause the script (press any key to continue)
read -n 1 -s -r -p "Press any key to continue"