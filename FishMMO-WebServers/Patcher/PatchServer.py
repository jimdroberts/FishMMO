import os
import logging
import json
import ssl
import socket
import psycopg2
import asyncio
import aiohttp
from aiohttp import web
import atexit
import signal
import requests

# Constants for directories
PATCHES_DIR = './patches/'

LATEST_VERSION = None

# Configure logging
logging.basicConfig(level=logging.DEBUG, format='%(asctime)s - %(levelname)s - %(message)s')

def get_external_ip():
    try:
        response = requests.get('https://checkip.amazonaws.com/')
        if response.status_code == 200:
            return response.text.strip()
        else:
            logging.error(f"Failed to retrieve external IP: {response.status_code} - {response.text}")
    except requests.RequestException as e:
        logging.error(f"Failed to retrieve external IP: {e}")
    return None
    
def read_configuration_file_version(file_path):
    global LATEST_VERSION
    
    if not os.path.exists(file_path):
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
        LATEST_VERSION = config.get('Version')

async def handle_request(request):
    try:
        if request.path == "/latest_version":
            return aiohttp.web.json_response({'latest_version': LATEST_VERSION})

        version_requested = request.path.strip('/')
        if version_requested == LATEST_VERSION:
            return aiohttp.web.json_response({'status': 'AlreadyUpdated'})

        diff_filename = f"{version_requested}-{LATEST_VERSION}.patch"
        diff_filepath = os.path.join(PATCHES_DIR, diff_filename)

        if os.path.exists(diff_filepath):
            with open(diff_filepath, 'rb') as f_diff:
                diff_content = f_diff.read()
            return aiohttp.web.Response(body=diff_content, content_type='application/octet-stream')

        logging.warning(f"Patch file {diff_filepath} does not exist.")
        return aiohttp.web.Response(status=404)

    except Exception as e:
        logging.error(f"An error occurred: {e}", exc_info=True)
        return aiohttp.web.Response(status=500)

async def add_to_database(port):
    ids_to_remove = []
    try:
        # Open appsettings.json
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
        
        external_ip = get_external_ip()

        # Insert into database and retrieve IDs
        sql = "INSERT INTO fish_mmo_postgresql.patch_servers (address, port) VALUES (%s, %s) RETURNING id"
        cursor.execute(sql, (external_ip, int(port)))
        ids_to_remove = [record[0] for record in cursor.fetchall()]  # Fetch all IDs
        conn.commit()

        cursor.close()
        conn.close()
        logging.info(f"Added Patch Server {external_ip}:{port} to the database.")

    except Exception as e:
        logging.error(f"Error while cleaning up database: {e}", exc_info=True)

    return ids_to_remove

async def cleanup_database(ids_to_remove):
    try:
        dn = os.path.dirname(os.path.realpath(__file__))
        fp = os.path.join(dn, 'appsettings.json')
        with open(fp, 'r') as config_file:
            config = json.load(config_file)

        conn = psycopg2.connect(
            dbname=config['Npgsql']['Database'],
            user=config['Npgsql']['Username'],
            password=config['Npgsql']['Password'],
            host=config['Npgsql']['Host'],
            port=config['Npgsql']['Port']
        )
        cursor = conn.cursor()
        logging.info("Database connection established.")

        placeholders = ','.join(['%s'] * len(ids_to_remove))
        sql = f"DELETE FROM fish_mmo_postgresql.patch_servers WHERE id IN ({placeholders})"
        cursor.execute(sql, ids_to_remove)
        conn.commit()

        cursor.close()
        conn.close()
        logging.info(f"Removed IDs {ids_to_remove} from database.")

    except Exception as e:
        logging.error(f"Error while cleaning up database: {e}", exc_info=True)

async def run_server():
    config_file_path = input("Enter the path to the latest configuration file: ").strip()
    read_configuration_file_version(config_file_path)
    if LATEST_VERSION:
        print(f"Version: {LATEST_VERSION}")
    else:
        print("Version not found in the configuration file.")

    # Ask the user for IP and Port
    external_ip = input("Enter the external IP address to bind to (leave blank for default): ").strip()
    if not external_ip:
        external_ip = ''  # This will bind to all available interfaces (including external)

    port = input("Enter the port you would like to bind to (leave blank for 8000): ").strip()
    if not port:
        port = 8000

    # Get the current working directory
    cwd = os.path.dirname(__file__)

    # Path to your SSL certificate and key (using current working directory)
    ssl_cert = os.path.join(cwd, 'certificate.pem')
    ssl_key = os.path.join(cwd, 'privatekey.pem')
    
    # Create an SSL context
    ssl_context = ssl.create_default_context(ssl.Purpose.CLIENT_AUTH)
    ssl_context.load_cert_chain(certfile=ssl_cert, keyfile=ssl_key)

    # Add the patch server to the database
    ids_to_remove = await add_to_database(port)

    app = aiohttp.web.Application()
    app.router.add_route('GET', '/{tail:.*}', handle_request)
    runner = aiohttp.web.AppRunner(app)

    await runner.setup()
    site = aiohttp.web.TCPSite(runner, external_ip, port, ssl_context=ssl_context)
    logging.info(f"Starting HTTP server on {external_ip}:{port}")
    await site.start()

    try:
        # Wait for cancellation signals
        await asyncio.gather(
            asyncio.create_task(asyncio.sleep(3600)),  # Or any long-running async task
            asyncio.create_task(signal_handler())
        )
    except asyncio.CancelledError:
        logging.info("Cancellation request received, stopping HTTP server")
        await runner.cleanup()
    except KeyboardInterrupt:
        logging.info("Stopping HTTP server")
        await runner.cleanup()
    finally:
        # Perform cleanup when exiting
        logging.info("Performing cleanup...")
        await runner.cleanup()
        await cleanup_database(ids_to_remove)

async def signal_handler():
    # Handle SIGINT and SIGTERM
    loop = asyncio.get_running_loop()
    for signame in ('SIGINT', 'SIGTERM'):
        logging.info(f"Registering signal {signame} handler...")
        try:
            loop.add_signal_handler(getattr(signal, signame), lambda: asyncio.create_task(cancel_tasks(signame)))
        except NotImplementedError:
            logging.warning(f"Signal {signame} is not supported on this platform")

async def cancel_tasks(signame):
    logging.info(f"Received signal {signame}, cancelling tasks...")
    tasks = [task for task in asyncio.all_tasks() if task is not asyncio.current_task()]
    for task in tasks:
        task.cancel()
    await asyncio.gather(*tasks, return_exceptions=True)

if __name__ == '__main__':
    try:
        asyncio.run(run_server())
    except KeyboardInterrupt:
        pass  # Handle keyboard interrupt gracefully