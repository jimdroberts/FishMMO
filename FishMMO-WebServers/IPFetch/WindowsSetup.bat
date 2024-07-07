@echo off
echo Installing required Python packages...
python -m pip install --upgrade pip
pip install asyncpg
pip install aiohttp
echo Installation complete.
pause