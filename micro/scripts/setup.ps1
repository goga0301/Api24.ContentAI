Write-Host "[*] Checking Python..."
if (-not (Get-Command python -ErrorAction SilentlyContinue)) {
    Write-Warning "Python is not installed. Please install from https://python.org/downloads/"
    exit 1
}

Write-Host "[*] Creating virtual environment..."
python -m venv ocr-envvenv

Write-Host "[*] Activating virtual environment..."
. .\ocr-envvenv\Scripts\Activate.ps1

Write-Host "[*] Installing Python dependencies..."
pip install --upgrade pip
pip install -r ..\requirements.txt

# Check if Chocolatey is installed
if (-not (Get-Command choco -ErrorAction SilentlyContinue)) {
    Write-Host "[*] Chocolatey not found. Installing Chocolatey..."

    Set-ExecutionPolicy Bypass -Scope Process -Force
    [System.Net.ServicePointManager]::SecurityProtocol = [System.Net.ServicePointManager]::SecurityProtocol -bor 3072
    iex ((New-Object System.Net.WebClient).DownloadString('https://community.chocolatey.org/install.ps1'))

    # Wait a bit to let install finish properly
    Start-Sleep -Seconds 10

    # Refresh environment PATH to include Chocolatey bin folder
    $env:Path += ";$env:ALLUSERSPROFILE\chocolatey\bin"

    # Verify choco is now available
    if (-not (Get-Command choco -ErrorAction SilentlyContinue)) {
        Write-Error "Chocolatey installation failed or PATH not updated. Please restart PowerShell and run this script again."
        exit 1
    }

    Write-Host "[✓] Chocolatey installed successfully."
} else {
    Write-Host "[✓] Chocolatey already installed."
}

# Refresh environment to find choco immediately
$env:Path += ";$env:ALLUSERSPROFILE\chocolatey\bin"

Write-Host "[*] Installing Tesseract OCR..."
if (-not (Get-Command tesseract -ErrorAction SilentlyContinue)) {
    choco install -y tesseract
} else {
    Write-Host "[✓] Tesseract already installed."
}

Write-Host "[*] Installing Poppler..."
if (-not (Test-Path "$env:ProgramFiles\poppler\bin\pdftoppm.exe")) {
    choco install -y poppler
} else {
    Write-Host "[✓] Poppler already installed."
}

Write-Host "[✓] Setup complete. Run with: .\ocr-envvenv\Scripts\Activate.ps1 && uvicorn main:app --reload"

