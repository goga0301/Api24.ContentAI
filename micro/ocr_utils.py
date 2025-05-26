import os
import subprocess
from config import logger


def run_tesseract_cli(image_path: str, tess_lang: str) -> str:
    output_base = image_path + "_output"
    cmd = ["tesseract", image_path, output_base, "-l", tess_lang, "--psm", "3", "txt"]
    logger.debug(f"Running: {' '.join(cmd)}")
    subprocess.run(cmd, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)

    txt_file = output_base + ".txt"
    try:
        with open(txt_file, "r", encoding="utf-8") as f:
            return f.read()
    except Exception as e:
        logger.error(f"Reading TXT failed: {e}")
        return f"Error reading OCR output: {e}"
    finally:
        try:
            os.remove(txt_file)
        except Exception as e:
            logger.warning(f"Couldn't delete temp TXT: {e}")
