from fastapi import APIRouter, UploadFile, File
from fastapi.responses import FileResponse
from tempfile import NamedTemporaryFile
from pdf_utils import convert_pdf_to_images
from ocr_utils import run_tesseract_cli
from config import TESS_LANGS, logger
from pdf2image import convert_from_bytes
from screenshot_utils import crop_image, encode_image, convert_office_to_pdf_bytes
from image_utils import ImageProcessor
import os

router = APIRouter()


@router.post("/ocr")
async def ocr_pdf(file: UploadFile = File(...)):
    tess_lang = "+".join(TESS_LANGS)
    logger.info(f"OCR using: {tess_lang}")
    pdf_bytes = await file.read()

    with NamedTemporaryFile(delete=False, suffix=".pdf") as temp_pdf:
        temp_pdf.write(pdf_bytes)
        temp_pdf.flush()
        temp_pdf_name = temp_pdf.name

    image_paths = convert_pdf_to_images(temp_pdf_name)

    output_txt_path = NamedTemporaryFile(delete=False, suffix=".txt").name
    with open(output_txt_path, "w", encoding="utf-8") as out_f:
        for i, img_path in enumerate(image_paths):
            text = run_tesseract_cli(img_path, tess_lang)
            try:
                os.remove(img_path)
            except Exception as e:
                logger.warning(f"Failed to remove temp image: {e}")
            out_f.write(f"--- Page {i+1} ---\n{text}\n\n")

    try:
        os.remove(temp_pdf_name)
    except Exception as e:
        logger.warning(f"Failed to remove temp PDF: {e}")

    return FileResponse(
        output_txt_path, filename="ocr_output.txt", media_type="text/plain"
    )


@router.post("/screenshot")
async def screen_shot(file: UploadFile = File(...)):
    extension = os.path.splitext(file.filename)[1].lower()
    file_bytes = await file.read()

    if extension == ".pdf":
        pdf_bytes = file_bytes
    elif extension in [".doc", ".docx"]:
        try:
            pdf_bytes = convert_office_to_pdf_bytes(file_bytes, extension)
        except Exception as e:
            logger.error(f"Failed to convert Office document to PDF: {e}")
            return {"error": "Failed to convert Word document to PDF."}
    else:
        return {
            "error": "Unsupported file type. Only .pdf, .doc, and .docx are supported."
        }

    try:
        pages = convert_from_bytes(pdf_bytes)
    except Exception as e:
        logger.error(f"Failed to convert PDF to images: {e}")
        return {"error": "Failed to convert document to images."}

    result = []
    for i, page in enumerate(pages):
        cropped = crop_image(page)
        encoded = [encode_image(c) for c in cropped]
        result.append({"page": i + 1, "screenshots": encoded})

    return {"pages": result}


@router.post("/ocr-image")
async def ocr_image(file: UploadFile = File(...)):
    contents = await file.read()
    tess_lang = "+".join(TESS_LANGS)
    success, result, error = await ImageProcessor.process_image(contents, file.filename, tess_lang)
    
    if not success:
        return {"error": error}
    
    return {"text": result}
