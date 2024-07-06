@echo off
echo Installing required Python packages...
python -m pip install --upgrade pip
pip install psycopg2
echo Installation complete.
pause