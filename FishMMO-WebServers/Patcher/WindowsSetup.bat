@echo off
echo Installing required Python packages...
python -m pip install --upgrade pip
pip install psycopg2
pip install asyncpg
pip install aiohttp
echo Installation complete.
pause