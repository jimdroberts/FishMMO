@echo off
echo Installing required Python packages...
python -m pip install --upgrade pip
pip install cryptography
echo Installation complete.
pause