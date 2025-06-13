import os
from tempfile import NamedTemporaryFile
from PIL import Image
from config import logger
from ocr_utils import run_tesseract_cli

class ImageProcessor:
    SUPPORTED_EXTENSIONS = ['.png', '.jpg', '.jpeg']
    
    @staticmethod
    def validate_extension(filename: str) -> tuple[bool, str]:
        extension = os.path.splitext(filename)[1].lower()
        if extension not in ImageProcessor.SUPPORTED_EXTENSIONS:
            return False, f"Unsupported file type. Only {', '.join(ImageProcessor.SUPPORTED_EXTENSIONS)} are supported."
        return True, ""

    @staticmethod
    async def process_image(file_contents: bytes, filename: str, tess_lang: str) -> tuple[bool, str, str]:
        try:
            is_valid, error_msg = ImageProcessor.validate_extension(filename)
            if not is_valid:
                return False, "", error_msg

            extension = os.path.splitext(filename)[1].lower()
            with NamedTemporaryFile(delete=False, suffix=extension) as temp_img:
                temp_img.write(file_contents)
                temp_img.flush()
                temp_img_name = temp_img.name

            try:
                logger.info(f"Processing image with Tesseract using languages: {tess_lang}")
                text = run_tesseract_cli(temp_img_name, tess_lang)
                return True, text, ""
            finally:
                try:
                    os.remove(temp_img_name)
                except Exception as e:
                    logger.warning(f"Failed to remove temp image: {e}")

        except Exception as e:
            logger.error(f"Error processing image: {e}")
            return False, "", f"Failed to process image: {str(e)}" 