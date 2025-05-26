from PIL import Image
import io
import base64


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
