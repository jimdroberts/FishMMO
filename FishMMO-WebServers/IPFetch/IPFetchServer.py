import ssl
import os
import json
import time
import logging
import aiohttp
from aiohttp import web
import asyncio
import asyncpg

# Configure logging
logging.basicConfig(level=logging.DEBUG, format='%(asctime)s - %(levelname)s - %(message)s')

class RequestHandler:

    def __init__(self, app):
        self.app = app
        self.cache = {}
        self.CACHE_TIMEOUT = 300
        
    async def add_cors_headers(self, request, response):
        origin = request.headers.get('Origin', '*')
        response.headers['Access-Control-Allow-Origin'] = origin
        response.headers['Access-Control-Allow-Headers'] = 'Content-Type'
        response.headers['Access-Control-Allow-Methods'] = 'GET, POST, PUT, DELETE, OPTIONS'
        return response

    async def handle_patch_servers(self, request):
        conn = None
        try:
            conn = await self.connect_to_database()
            if conn is None:
                return aiohttp.web.Response(status=500, text="Failed to connect to the database")

            current_time = time.time()
            if 'patch_servers' in self.cache and (current_time - self.cache['timestamp']) < self.CACHE_TIMEOUT:
                logging.info("Cache hit for patch servers. Returning cached data.")
                patch_servers = self.cache['patch_servers']
            else:
                logging.info("Cache miss for patch servers. Fetching data from the database.")
                async with conn.transaction():
                    patch_servers = await conn.fetch("SELECT address, port FROM fish_mmo_postgresql.patch_servers;")
                logging.info("Query executed successfully. Fetched patch server data from the database.")

                self.cache['patch_servers'] = patch_servers
                self.cache['timestamp'] = current_time
                logging.info("Patch server data cached.")

            response = aiohttp.web.json_response([dict(record) for record in patch_servers])
            return await self.add_cors_headers(request, response)

        except Exception as e:
            logging.error(f"Error fetching patch servers: {e}")
            return aiohttp.web.Response(status=500, text="Internal Server Error")

        finally:
            if conn is not None:
                await conn.close()
                logging.info("Database connection closed.")

    async def handle_login_servers(self, request):
        conn = None
        try:
            conn = await self.connect_to_database()
            if conn is None:
                return aiohttp.web.Response(status=500, text="Failed to connect to the database")

            current_time = time.time()
            if 'login_servers' in self.cache and (current_time - self.cache['timestamp']) < self.CACHE_TIMEOUT:
                logging.info("Cache hit for login servers. Returning cached data.")
                login_servers = self.cache['login_servers']
            else:
                logging.info("Cache miss for login servers. Fetching data from the database.")
                async with conn.transaction():
                    login_servers = await conn.fetch("SELECT address, port FROM fish_mmo_postgresql.login_servers;")
                logging.info("Query executed successfully. Fetched login server data from the database.")

                self.cache['login_servers'] = login_servers
                self.cache['timestamp'] = current_time
                logging.info("Login server data cached.")

            response = aiohttp.web.json_response([dict(record) for record in login_servers])
            return await self.add_cors_headers(request, response)

        except Exception as e:
            logging.error(f"Error fetching login servers: {e}")
            return aiohttp.web.Response(status=500, text="Internal Server Error")

        finally:
            if conn is not None:
                await conn.close()
                logging.info("Database connection closed.")

    async def connect_to_database(self):
        try:
            # Replace with your own logic to read database connection parameters
            dn = os.path.dirname(os.path.realpath(__file__))
            fp = os.path.join(dn, 'appsettings.json')

            with open(fp, 'r') as config_file:
                config = json.load(config_file)

            logging.info("Connecting to the database...")
            conn = await asyncpg.connect(
                database=config['Npgsql']['Database'],
                user=config['Npgsql']['Username'],
                password=config['Npgsql']['Password'],
                host=config['Npgsql']['Host'],
                port=config['Npgsql']['Port']
            )
            logging.info("Database connection established.")
            return conn

        except Exception as e:
            logging.error(f"Error connecting to database: {e}")
            return None

async def run_server():
    # Ask the user for IP and Port
    external_ip = input("Enter the external IP address to bind to (leave blank for default): ").strip()
    if not external_ip:
        external_ip = ''  # This will bind to all available interfaces (including external)

    port = input("Enter the port you would like to bind to (leave blank for 8080): ").strip()
    if not port:
        port = 8080

    # Create an aiohttp.web.Application instance
    app = aiohttp.web.Application()

    # Create an instance of RequestHandler
    handler = RequestHandler(app)

    # Register routes and corresponding handler methods
    app.router.add_get('/patchserver', handler.handle_patch_servers)
    app.router.add_get('/loginserver', handler.handle_login_servers)

    # Path to your SSL certificate and key (using current working directory)
    cwd = os.path.dirname(__file__)
    ssl_cert = os.path.join(cwd, 'certificate.pem')
    ssl_key = os.path.join(cwd, 'privatekey.pem')

    # Create an SSL context
    ssl_context = ssl.create_default_context(ssl.Purpose.CLIENT_AUTH)
    ssl_context.load_cert_chain(certfile=ssl_cert, keyfile=ssl_key)

    # Setup the aiohttp.web.AppRunner
    runner = aiohttp.web.AppRunner(app)
    await runner.setup()

    # Create the TCP site with SSL context
    site = aiohttp.web.TCPSite(runner, external_ip, port, ssl_context=ssl_context)

    logging.info(f"Starting HTTPS server on {external_ip}:{port}")
    await site.start()

    try:
        while True:
            await asyncio.sleep(3600)  # or any other long-running tasks
    except asyncio.CancelledError:
        pass
    finally:
        await runner.cleanup()
        logging.info("Stopping HTTPS server")

if __name__ == '__main__':
    # Run the server using asyncio.run() to start the event loop
    asyncio.run(run_server())