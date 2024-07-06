import os
import re
import logging
from http.server import BaseHTTPRequestHandler, HTTPServer
import json

# Constants for directories
PATCHES_DIR = './patches/'

LATEST_VERSION = None

# Configure logging
logging.basicConfig(level=logging.DEBUG, format='%(asctime)s - %(levelname)s - %(message)s')

def read_configuration_file_version(file_path):
    global LATEST_VERSION  # Access the global variable 'version'
    
    if not os.path.exists(file_path):
        # Trim double quotes from start or end of file_path if they exist
        if file_path.startswith('"'):
            file_path = file_path[1:]
        if file_path.endswith('"'):
            file_path = file_path[:-1]
    
    if os.path.exists(file_path):
        config = {}
        with open(file_path, 'r') as f:
            for line in f:
                line = line.strip()
                if line and '=' in line:
                    key, value = line.split('=', 1)
                    config[key.strip()] = value.strip()
        LATEST_VERSION = config.get('Version')  # Update the global 'version' variable

# HTTP Request Handler class
class DiffRequestHandler(BaseHTTPRequestHandler):
    def do_GET(self):
        try:
            if self.path == "/latest_version":
                self.send_response(200)
                self.send_header('Content-type', 'application/json')
                self.end_headers()
                self.wfile.write(json.dumps({'latest_version': LATEST_VERSION}).encode())
                return

            # Extract version number from client request path
            version_requested = self.path.strip('/')

            if version_requested == LATEST_VERSION:
                self.send_response(200)
                self.send_header('Content-type', 'application/json')
                self.end_headers()
                self.wfile.write(b'{"status": "AlreadyUpdated"}')
                return

            # Construct the filename of the diff file
            diff_filename = f"{version_requested}-{LATEST_VERSION}.patch"
            diff_filepath = os.path.join(PATCHES_DIR, diff_filename)

            # Check if the diff file exists in old patches
            if os.path.exists(diff_filepath):
                # Get the size of the diff file
                diff_file_size = os.path.getsize(diff_filepath)

                # Read the diff file
                with open(diff_filepath, 'rb') as f_diff:
                    diff_content = f_diff.read()

                # Send response with the diff content and Content-Length header
                self.send_response(200)
                self.send_header('Content-type', 'application/octet-stream')
                self.send_header('Content-Length', str(diff_file_size))  # Set Content-Length header
                self.end_headers()
                self.wfile.write(diff_content)
                logging.info(f"Sent diff file {diff_filepath}")
            else:
                logging.warning(f"Diff file {diff_filepath} does not exist.")
                self.send_response(404)
                self.end_headers()

        except Exception as e:
            logging.error(f"An error occurred: {e}", exc_info=True)
            self.send_response(500)
            self.end_headers()

# Main function to start the HTTP server
def run(server_class=HTTPServer, handler_class=DiffRequestHandler, port=8000):
    server_address = ('', port)
    httpd = server_class(server_address, handler_class)
    logging.info(f"Starting HTTP server on port {port}")
    try:
        httpd.serve_forever()
    except KeyboardInterrupt:
        pass
    httpd.server_close()
    logging.info("Stopping HTTP server")

if __name__ == '__main__':
    config_file_path = input("Enter the path to the latest configuration file: ").strip()
    read_configuration_file_version(config_file_path)
    if LATEST_VERSION:
        print(f"Version: {LATEST_VERSION}")
    else:
        print("Version not found in the configuration file.")
    run()