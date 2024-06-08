from http.server import BaseHTTPRequestHandler, HTTPServer
import json
import psycopg2
import time

class RequestHandler(BaseHTTPRequestHandler):
    # Define a cache dictionary
    cache = {}
    # Define the cache timeout in seconds (5 minutes)
    CACHE_TIMEOUT = 300

    def do_GET(self):
        print(f"Received GET request for path: {self.path}")
        if self.path == '/':
            try:
                current_time = time.time()
                
                # Check if the result is already cached and not expired
                if 'ip_list' in self.cache and (current_time - self.cache['timestamp']) < self.CACHE_TIMEOUT:
                    print("Cache hit. Returning cached data.")
                    ip_list = self.cache['ip_list']
                else:
                    print("Cache miss. Fetching data from the database.")
                    # Read database connection parameters from appsettings.json
                    with open('appsettings.json', 'r') as config_file:
                        config = json.load(config_file)
                    
                    print("Connecting to the database...")
                    # Connect to PostgreSQL database
                    conn = psycopg2.connect(
                        dbname=config['Npgsql']['Database'],
                        user=config['Npgsql']['Username'],
                        password=config['Npgsql']['Password'],
                        host=config['Npgsql']['Host'],
                        port=config['Npgsql']['Port']
                    )
                    cursor = conn.cursor()
                    print("Database connection established.")

                    # Fetch IP list from database
                    print("Executing database query...")
                    cursor.execute("SELECT address, port FROM fish_mmo_postgresql.login_servers;")
                    ip_list = cursor.fetchall()
                    print("Query executed successfully. Fetched data from the database.")

                    # Cache the result and current timestamp
                    self.cache['ip_list'] = ip_list
                    self.cache['timestamp'] = current_time
                    print("Data cached.")

                    cursor.close()
                    conn.close()
                    print("Database connection closed.")

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
                print("Response sent successfully.")
            except psycopg2.Error as db_error:
                self.send_response(500)
                self.end_headers()
                self.wfile.write("Internal server error".encode())
                print(f"Database Error: {db_error}")
            except Exception as e:
                self.send_response(500)
                self.end_headers()
                self.wfile.write("Internal server error".encode())
                print(f"Error: {e}")
        else:
            self.send_response(404)
            self.end_headers()
            self.wfile.write("Not found".encode())
            print(f"Path '{self.path}' not found. Sent 404 response.")

def run_server():
    server_address = ('', 8080)
    httpd = HTTPServer(server_address, RequestHandler)
    print('Starting server...')
    try:
        httpd.serve_forever()
    except KeyboardInterrupt:
        pass
    httpd.server_close()
    print('Server stopped.')

if __name__ == '__main__':
    run_server()