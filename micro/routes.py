from fastapi import APIRouter, UploadFile, File, HTTPException
from fastapi.responses import FileResponse
from tempfile import NamedTemporaryFile
from pdf_utils import convert_pdf_to_images
from ocr_utils import run_tesseract_cli
from config import TESS_LANGS, logger
from pdf2image import convert_from_bytes
from screenshot_utils import crop_image, encode_image, convert_office_to_pdf_bytes
from image_utils import ImageProcessor
import os
import io
from docx import Document
import mammoth

router = APIRouter()

@router.get("/health")
async def health_check():
    """Health check endpoint for monitoring the OCR service"""
    return {"status": "healthy", "service": "ocr-service"}

@router.get("/")
async def root():
    return {"message": "OCR Service is running"}


@router.post("/ocr")
async def ocr_pdf(file: UploadFile = File(...)):
    temp_pdf_name = None
    output_txt_path = None
    image_paths = []
    
    try:
        tess_lang = "+".join(TESS_LANGS)
        logger.info(f"OCR using: {tess_lang}")
        
        # Validate file size (limit to 50MB for stability)
        if file.size and file.size > 50 * 1024 * 1024:
            logger.error(f"File too large: {file.size} bytes")
            raise HTTPException(status_code=413, detail="File too large. Maximum size is 50MB.")
        
        pdf_bytes = await file.read()
        
        if len(pdf_bytes) == 0:
            raise HTTPException(status_code=400, detail="Empty file received")

        with NamedTemporaryFile(delete=False, suffix=".pdf") as temp_pdf:
            temp_pdf.write(pdf_bytes)
            temp_pdf.flush()
            temp_pdf_name = temp_pdf.name
            
        logger.info(f"Created temp PDF: {temp_pdf_name}, size: {len(pdf_bytes)} bytes")

        try:
            image_paths = convert_pdf_to_images(temp_pdf_name)
            logger.info(f"Generated {len(image_paths)} images from PDF")
        except Exception as e:
            logger.error(f"Failed to convert PDF to images: {e}")
            raise HTTPException(status_code=500, detail=f"PDF conversion failed: {str(e)}")

        output_txt_path = NamedTemporaryFile(delete=False, suffix=".txt").name
        
        # Process images and write OCR results
        with open(output_txt_path, "w", encoding="utf-8") as out_f:
            for i, img_path in enumerate(image_paths):
                try:
                    logger.debug(f"Processing image {i+1}/{len(image_paths)}: {img_path}")
                    text = run_tesseract_cli(img_path, tess_lang)
                    out_f.write(f"--- Page {i+1} ---\n{text}\n\n")
                except Exception as e:
                    logger.error(f"OCR failed for image {img_path}: {e}")
                    out_f.write(f"--- Page {i+1} ---\n[OCR ERROR: {str(e)}]\n\n")
                finally:
                    # Clean up image file immediately after processing
                    try:
                        if os.path.exists(img_path):
                            os.remove(img_path)
                            logger.debug(f"Removed temp image: {img_path}")
                    except Exception as cleanup_e:
                        logger.warning(f"Failed to remove temp image {img_path}: {cleanup_e}")

        logger.info(f"OCR processing completed successfully for {file.filename}")
        
        # Return the OCR result as JSON instead of FileResponse to avoid streaming issues
        with open(output_txt_path, "r", encoding="utf-8") as f:
            ocr_text = f.read()
            
        return {"text": ocr_text, "status": "success", "pages_processed": len(image_paths)}
        
    except HTTPException:
        # Re-raise HTTP exceptions (400, 413, 500)
        raise
    except Exception as e:
        logger.error(f"Unexpected error in OCR processing: {e}")
        raise HTTPException(status_code=500, detail=f"OCR processing failed: {str(e)}")
    finally:
        # Cleanup temporary files
        cleanup_files = []
        if temp_pdf_name:
            cleanup_files.append(temp_pdf_name)
        if output_txt_path:
            cleanup_files.append(output_txt_path)
        
        for file_path in cleanup_files:
            try:
                if os.path.exists(file_path):
                    os.remove(file_path)
                    logger.debug(f"Cleaned up temp file: {file_path}")
            except Exception as cleanup_e:
                logger.warning(f"Failed to cleanup {file_path}: {cleanup_e}")
        
        # Clean up any remaining image files
        for img_path in image_paths:
            try:
                if os.path.exists(img_path):
                    os.remove(img_path)
            except:
                pass


@router.post("/screenshot")
async def screen_shot(file: UploadFile = File(...)):
    extension = os.path.splitext(file.filename)[1].lower()
    file_bytes = await file.read()

    if extension == ".pdf":
        pdf_bytes = file_bytes
    elif extension in [".doc", ".docx"]:
        try:
            pdf_bytes = convert_office_to_pdf_bytes(file_bytes, extension)
            logger.info(f"Successfully converted {extension} to PDF, size: {len(pdf_bytes)} bytes")
            if len(pdf_bytes) == 0:
                logger.error("Converted PDF is empty - LibreOffice conversion failed")
                return {"error": "Word document conversion produced empty PDF. Check LibreOffice installation."}
        except Exception as e:
            logger.error(f"Failed to convert Office document to PDF: {e}")
            return {"error": f"Failed to convert Word document to PDF: {str(e)}"}
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


@router.post("/process-word")
async def process_word_document(file: UploadFile = File(...)):
    extension = os.path.splitext(file.filename)[1].lower()
    file_bytes = await file.read()
    
    if extension not in [".doc", ".docx"]:
        return {"error": "Only .doc and .docx files are supported"}
    
    logger.info(f"Processing Word document: {file.filename} ({len(file_bytes)} bytes)")
    
    try:
        logger.info("Attempting LibreOffice conversion...")
        pdf_bytes = convert_office_to_pdf_bytes(file_bytes, extension)
        logger.info(f"Successfully converted to PDF: {len(pdf_bytes)} bytes")
        
        if len(pdf_bytes) > 0:
            # Convert PDF to images
            pages = convert_from_bytes(pdf_bytes)
            result = []
            for i, page in enumerate(pages):
                cropped = crop_image(page)
                encoded = [encode_image(c) for c in cropped]
                result.append({"page": i + 1, "screenshots": encoded})
            
            logger.info(f"Successfully created screenshots for {len(pages)} pages")
            return {"pages": result, "method": "screenshot"}
            
    except Exception as e:
        logger.warning(f"LibreOffice conversion failed: {e}")
        logger.info("Falling back to text extraction...")
    
    try:
        if extension == ".docx":
            doc_file = io.BytesIO(file_bytes)
            doc = Document(doc_file)
            
            pages_text = []
            current_page_text = []
            
            for paragraph in doc.paragraphs:
                text = paragraph.text.strip()
                if text:
                    current_page_text.append(text)
                    
                    if len(current_page_text) >= 50 or '\f' in text:
                        pages_text.append('\n'.join(current_page_text))
                        current_page_text = []
            
            if current_page_text:
                pages_text.append('\n'.join(current_page_text))
            
            if not pages_text and current_page_text:
                pages_text = ['\n'.join(current_page_text)]
                
        else: 
            doc_file = io.BytesIO(file_bytes)
            result_mammoth = mammoth.extract_raw_text(doc_file)
            full_text = result_mammoth.value
            
            page_size = 3000
            pages_text = [full_text[i:i+page_size] for i in range(0, len(full_text), page_size)]
        
        if not pages_text:
            pages_text = ["[Empty document or could not extract text]"]
        
        result = []
        for i, text in enumerate(pages_text):
            result.append({
                "page": i + 1, 
                "text": text,
                "screenshots": []  
            })
        
        logger.info(f"Successfully extracted text from {len(pages_text)} pages")
        return {"pages": result, "method": "text_extraction"}
        
    except Exception as e:
        logger.error(f"Text extraction also failed: {e}")
        return {"error": f"Failed to process Word document: {str(e)}"}
