#!/bin/bash
set -e

echo "[*] Checking Python3..."
if ! command -v python3 &> /dev/null; then
  echo "[!] Python3 not found. Please install Python3 first."
  exit 1
fi

echo "[*] Creating virtual environment..."
python3 -m venv ocr-env
source ocr-env/bin/activate

echo "[*] Installing Python dependencies..."
pip install --upgrade pip
pip install -r requirements.txt

echo "[*] Installing system dependencies..."

if ! command -v tesseract &> /dev/null; then
    echo "[!] Installing Tesseract OCR with all languages..."
    sudo apt update
    sudo apt install -y tesseract-ocr tesseract-ocr-all
else
    echo "[✓] Tesseract already installed."
fi

if ! command -v pdftoppm &> /dev/null; then
    echo "[!] Installing Poppler utils..."
    sudo apt update
    sudo apt install -y poppler-utils
else
    echo "[✓] Poppler already installed."
fi

if ! command -v libreoffice &> /dev/null; then
    echo "[!] Installing LibreOffice..."
    sudo apt update
    sudo apt install -y libreoffice
else
    echo "[✓] LibreOffice already installed."
fi

echo "[✓] Setup complete. Run with: source ocr-env/bin/activate && uvicorn main:app --reload"

