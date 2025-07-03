from PIL import Image
import io
import base64
from tempfile import NamedTemporaryFile
import os
from config import logger
import subprocess


def crop_image(img: Image.Image):
    w, h = img.size
    return [
        img.crop((0, 0, w, h // 2)),
        img.crop((0, h // 4, w, 3 * h // 4)),
        img.crop((0, h // 2, w, h)),
    ]


def encode_image(img: Image.Image) -> str:
    buf = io.BytesIO()
    img.save(buf, format="PNG")
    return base64.b64encode(buf.getvalue()).decode()


def convert_office_to_pdf_bytes(doc_bytes: bytes, extension: str) -> bytes:
    if extension not in [".doc", ".docx"]:
        raise ValueError("Unsupported extension for Office to PDF conversion.")

    logger.info(f"Converting {extension} document ({len(doc_bytes)} bytes) to PDF")

    with NamedTemporaryFile(delete=False, suffix=extension) as temp_doc:
        temp_doc.write(doc_bytes)
        temp_doc.flush()
        doc_path = temp_doc.name

    output_dir = os.path.dirname(doc_path)
    pdf_path = doc_path.replace(extension, ".pdf")
    
    logger.info(f"LibreOffice conversion: {doc_path} -> {pdf_path}")

    try:
        result = subprocess.run(
            [
                "libreoffice",
                "--headless",
                "--convert-to",
                "pdf",
                "--outdir",
                output_dir,
                doc_path,
            ],
            check=True,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            timeout=30  # 30 second timeout
        )
        
        logger.info(f"LibreOffice stdout: {result.stdout.decode()}")
        if result.stderr:
            logger.warning(f"LibreOffice stderr: {result.stderr.decode()}")

        if not os.path.exists(pdf_path):
            raise RuntimeError(f"LibreOffice did not create expected PDF file: {pdf_path}")

        with open(pdf_path, "rb") as f:
            pdf_bytes = f.read()
            
        logger.info(f"Successfully converted to PDF: {len(pdf_bytes)} bytes")
        
        if len(pdf_bytes) == 0:
            raise RuntimeError("LibreOffice produced empty PDF file")
            
    except subprocess.TimeoutExpired:
        logger.error("LibreOffice conversion timed out after 30 seconds")
        raise RuntimeError("LibreOffice conversion timed out")
    except subprocess.CalledProcessError as e:
        logger.error(f"LibreOffice conversion failed with exit code {e.returncode}")
        logger.error(f"LibreOffice stdout: {e.stdout.decode() if e.stdout else 'None'}")
        logger.error(f"LibreOffice stderr: {e.stderr.decode() if e.stderr else 'None'}")
        raise RuntimeError(f"LibreOffice conversion failed: {e.stderr.decode() if e.stderr else 'Unknown error'}")
    finally:
        try:
            os.remove(doc_path)
            if os.path.exists(pdf_path):
                os.remove(pdf_path)
        except Exception as e:
            logger.warning(f"Cleanup failed: {e}")

    return pdf_bytes
