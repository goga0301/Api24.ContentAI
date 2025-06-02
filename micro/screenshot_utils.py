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

    with NamedTemporaryFile(delete=False, suffix=extension) as temp_doc:
        temp_doc.write(doc_bytes)
        temp_doc.flush()
        doc_path = temp_doc.name

    output_dir = os.path.dirname(doc_path)
    pdf_path = doc_path.replace(extension, ".pdf")

    try:
        subprocess.run(
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
        )

        with open(pdf_path, "rb") as f:
            pdf_bytes = f.read()
    finally:
        try:
            os.remove(doc_path)
            if os.path.exists(pdf_path):
                os.remove(pdf_path)
        except Exception as e:
            logger.warning(f"Cleanup failed: {e}")

    return pdf_bytes
