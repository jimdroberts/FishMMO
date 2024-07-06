from http.server import HTTPServer, BaseHTTPRequestHandler
import ssl
import os
import json
import psycopg2
import time
import logging
import ssl
from socketserver import ThreadingMixIn

# Configure logging
logging.basicConfig(level=logging.DEBUG, format='%(asctime)s - %(levelname)s - %(message)s')

class RequestHandler(BaseHTTPRequestHandler):
    # Define a cache dictionary
    cache = {}
    # Define the cache timeout in seconds (5 minutes)
    CACHE_TIMEOUT = 300

    def do_GET(self):
        logging.info(f"Received GET request for path: {self.path}")
        if self.path == '/':
            try:
                current_time = time.time()
                
                # Check if the result is already cached and not expired
                if 'ip_list' in self.cache and (current_time - self.cache['timestamp']) < self.CACHE_TIMEOUT:
                    logging.info("Cache hit. Returning cached data.")
                    ip_list = self.cache['ip_list']
                else:
                    logging.info("Cache miss. Fetching data from the database.")
                    dn = os.path.dirname(os.path.realpath(__file__))
                    fp = os.path.join(dn, 'appsettings.json')
                    # Read database connection parameters from appsettings.json
                    with open(fp, 'r') as config_file:
                        config = json.load(config_file)
                    
                    logging.info("Connecting to the database...")
                    # Connect to PostgreSQL database
                    conn = psycopg2.connect(
                        dbname=config['Npgsql']['Database'],
                        user=config['Npgsql']['Username'],
                        password=config['Npgsql']['Password'],
                        host=config['Npgsql']['Host'],
                        port=config['Npgsql']['Port']
                    )
                    cursor = conn.cursor()
                    logging.info("Database connection established.")

                    # Fetch IP list from database
                    logging.info("Executing database query...")
                    cursor.execute("SELECT address, port FROM fish_mmo_postgresql.login_servers;")
                    ip_list = cursor.fetchall()
                    logging.info("Query executed successfully. Fetched data from the database.")

                    # Cache the result and current timestamp
                    self.cache['ip_list'] = ip_list
                    self.cache['timestamp'] = current_time
                    logging.info("Data cached.")

                    cursor.close()
                    conn.close()
                    logging.info("Database connection closed.")

                # Send response
                self.send_response(200)
                self.send_header('Content-type', 'application/json')
                self.end_headers()
                
                # Convert the list of tuples to a list of dictionaries
                ip_list_json = [{"address": address, "port": port} for address, port in ip_list]
                
                # Convert the list of dictionaries to a JSON string without escaping characters
                json_string = json.dumps(ip_list_json)
                
                # Write the JSON string to the response
                self.wfile.write(json_string.encode())
                logging.info("Response sent successfully.")
            except psycopg2.Error as db_error:
                self.send_response(500)
                self.end_headers()
                self.wfile.write("Internal server error".encode())
                logging.error(f"Database Error: {db_error}")
            except Exception as e:
                self.send_response(500)
                self.end_headers()
                self.wfile.write("Internal server error".encode())
                logging.error(f"Error: {e}")
        else:
            self.send_response(404)
            self.end_headers()
            self.wfile.write("Not found".encode())
            print(f"Path '{self.path}' not found. Sent 404 response.")

def run(server_class=HTTPServer, handler_class=RequestHandler, port=8000, bind_address='', ssl_cert=None, ssl_key=None):
    server_address = (bind_address, int(port))
    
    # Create an SSL context
    context = ssl.create_default_context(ssl.Purpose.CLIENT_AUTH)
    context.load_cert_chain(certfile=ssl_cert, keyfile=ssl_key)

    # Instantiate the HTTP server with SSL context
    httpd = server_class(server_address, handler_class)
    httpd.socket = context.wrap_socket(httpd.socket, server_side=True)
    
    logging.info(f"Starting HTTPS server on {bind_address}:{port}")
    try:
        httpd.serve_forever()
    except KeyboardInterrupt:
        pass
    httpd.server_close()
    logging.info("Stopping HTTPS server")

if __name__ == '__main__':
    external_ip = input("Enter the external IP address to bind to (leave blank for default): ").strip()
    if not external_ip:
        external_ip = ''  # This will bind to all available interfaces (including external)
        
    port = input("Enter the port you would like to bind to (leave blank for 8080): ").strip()
    if not port:
        port = 8080
    
    # Get the current working directory
    cwd = os.getcwd()

    # Path to your SSL certificate and key (using current working directory)
    ssl_cert = os.path.join(cwd, 'certificate.pem')
    ssl_key = os.path.join(cwd, 'privatekey.pem')
    
    run(port=port, bind_address=external_ip, ssl_cert=ssl_cert, ssl_key=ssl_key)