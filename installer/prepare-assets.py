"""Convert the existing DigitalRCS logo into reproducible installer assets.

Run from any directory with Python 3 and Pillow. The original logo is not changed.
The wordmark text and background are installer layout; the logo's colors and
aspect ratio are preserved. Generated assets are checked into the repository.
"""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[1]
OUTPUT = Path(__file__).resolve().parent / "assets"
OUTPUT.mkdir(exist_ok=True)
logo = Image.open(ROOT / "src/MagicMusicWall.App/Assets/digitalrcs-logo.png").convert("RGBA")

icon = logo.resize((256, 256), Image.Resampling.LANCZOS)
icon.save(OUTPUT / "DigitalRCS.ico", sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
logo.save(OUTPUT / "wizard-small.png", optimize=True)

side = Image.new("RGB", (328, 628), "#101218")
draw = ImageDraw.Draw(side)
large = logo.resize((224, 224), Image.Resampling.LANCZOS)
side.paste(large, (52, 72), large)
font_directory = Path("C:/Windows/Fonts")
bold = ImageFont.truetype(str(font_directory / "segoeuib.ttf"), 29)
regular = ImageFont.truetype(str(font_directory / "segoeui.ttf"), 22)
small = ImageFont.truetype(str(font_directory / "segoeui.ttf"), 18)
for y, text, font, color in [
    (326, "MAGIC", bold, "#B8A5FF"),
    (370, "MUSIC WALL", bold, "#F4F3F8"),
    (451, "Your music.", regular, "#A3A9BD"),
    (482, "Your home.", regular, "#A3A9BD"),
    (566, "DigitalRCS.com", small, "#F4F3F8"),
]:
    draw.text((164, y), text, font=font, fill=color, anchor="mt")
side.save(OUTPUT / "wizard-side.png", optimize=True)
print(f"Installer assets generated in {OUTPUT}")
