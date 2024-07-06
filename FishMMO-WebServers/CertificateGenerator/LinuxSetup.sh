#!/bin/bash

# Display a message
echo "Installing required Python packages..."

python -m pip install --upgrade pip

# Install the psycopg2 package
pip install cryptography

# Display completion message
echo "Installation complete."

# Pause the script (press any key to continue)
read -n 1 -s -r -p "Press any key to continue"