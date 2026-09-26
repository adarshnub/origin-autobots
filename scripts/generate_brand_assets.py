"""Export the code-owned eyes mark and companion illustration to app icon formats.

Uses the already available PyMuPDF and Pillow; does not install packages.
"""
from pathlib import Path
from io import BytesIO
import fitz
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
DESKTOP = ROOT / "apps/desktop/shell/Autobots.Desktop/Assets"
WEB = ROOT / "apps/website/src/assets"
BRAND = ROOT / "assets/brand"

LOGO = '''<svg xmlns="http://www.w3.org/2000/svg" width="64" height="64" viewBox="0 0 64 64">
<rect x="4" y="6" width="56" height="56" rx="18" fill="#171926"/>
<rect x="4" y="3" width="56" height="56" rx="18" fill="#25273e"/>
<rect x="18" y="20" width="12" height="24" rx="6" fill="#b1a6f3"/>
<rect x="18" y="18" width="12" height="22" rx="6" fill="#e9e6ff"/>
<rect x="35" y="23" width="12" height="20" rx="6" fill="#b1a6f3"/>
<rect x="35" y="21" width="12" height="18" rx="6" fill="#e9e6ff"/>
</svg>'''

BOT = '''<svg xmlns="http://www.w3.org/2000/svg" width="320" height="360" viewBox="0 0 320 360">
<defs>
 <linearGradient id="shell" x1="0" y1="0" x2="1" y2="1"><stop stop-color="#fff9de"/><stop offset=".55" stop-color="#ffcb64"/><stop offset="1" stop-color="#cc852c"/></linearGradient>
 <linearGradient id="head" x1="0" y1="0" x2="0" y2="1"><stop stop-color="#fffef4"/><stop offset="1" stop-color="#d9d9e2"/></linearGradient>
 <linearGradient id="metal" x1="0" y1="0" x2="1" y2="1"><stop stop-color="#fff"/><stop offset=".4" stop-color="#c2c9d8"/><stop offset="1" stop-color="#66738a"/></linearGradient>
 <radialGradient id="lens"><stop stop-color="#315275"/><stop offset=".8" stop-color="#152235"/><stop offset="1" stop-color="#080f1e"/></radialGradient>
 <linearGradient id="flame" x1="0" y1="0" x2="0" y2="1"><stop stop-color="#a4f6ff"/><stop offset="1" stop-color="#b6c3ff" stop-opacity="0"/></linearGradient>
</defs>
<ellipse cx="157" cy="342" rx="57" ry="7" fill="#9385c3" opacity=".12"/>
<g transform="rotate(-7 160 180)">
 <path d="M132 274Q111 323 134 337Q157 312 147 274M179 274Q163 322 184 332Q207 308 194 274" fill="url(#flame)"/>
 <rect x="117" y="254" width="33" height="28" rx="10" fill="url(#metal)"/><rect x="170" y="254" width="33" height="28" rx="10" fill="url(#metal)"/>
 <rect x="205" y="187" width="23" height="64" rx="11" fill="url(#metal)" transform="rotate(-22 217 192)"/>
 <rect x="81" y="182" width="23" height="64" rx="11" fill="url(#metal)" transform="rotate(25 92 188)"/>
 <rect x="103" y="163" width="112" height="101" rx="27" fill="url(#shell)"/>
 <rect x="123" y="189" width="74" height="50" rx="13" fill="url(#head)"/>
 <rect x="146" y="203" width="9" height="19" rx="4.5" fill="#605591"/><rect x="161" y="206" width="9" height="16" rx="4.5" fill="#605591"/>
 <rect x="147" y="148" width="26" height="28" rx="10" fill="url(#metal)"/>
 <path d="M202 85L208 56" stroke="#8c95ae" stroke-width="7" stroke-linecap="round"/><circle cx="210" cy="51" r="10" fill="#fbc35b"/>
 <rect x="75" y="81" width="170" height="84" rx="29" fill="url(#head)"/>
 <g><circle cx="121" cy="124" r="37" fill="url(#metal)"/><circle cx="121" cy="124" r="30" fill="url(#lens)"/><circle cx="123" cy="125" r="17" fill="#81d4f1"/><circle cx="123" cy="125" r="9" fill="#142033"/><circle cx="114" cy="114" r="6" fill="white"/><circle cx="136" cy="137" r="3" fill="#def7ff"/></g>
 <g><circle cx="196" cy="124" r="37" fill="url(#metal)"/><circle cx="196" cy="124" r="30" fill="url(#lens)"/><circle cx="197" cy="125" r="17" fill="#81d4f1"/><circle cx="197" cy="125" r="9" fill="#142033"/><circle cx="188" cy="114" r="6" fill="white"/><circle cx="210" cy="137" r="3" fill="#def7ff"/></g>
 <rect x="62" y="233" width="29" height="29" rx="10" fill="url(#head)"/><rect x="230" y="228" width="29" height="29" rx="10" fill="url(#head)"/>
 <g transform="rotate(13 238 230)"><rect x="221" y="203" width="46" height="60" rx="9" fill="#e1ddee"/><rect x="219" y="200" width="46" height="60" rx="9" fill="#fffdf9"/><circle cx="231" cy="214" r="5" fill="#8f7ee0"/><path d="M228 229H254M228 236H254M228 243H243" stroke="#d2cce2" stroke-width="3" stroke-linecap="round"/></g>
</g></svg>'''

def rasterize(svg: str, size: int) -> Image.Image:
    with fitz.open(stream=svg.encode(), filetype="svg") as source:
        pdf_bytes = source.convert_to_pdf()
    with fitz.open(stream=pdf_bytes, filetype="pdf") as document:
        page = document[0]
        pix = page.get_pixmap(matrix=fitz.Matrix(size / page.rect.width, size / page.rect.width), alpha=True)
        return Image.open(BytesIO(pix.tobytes("png"))).convert("RGBA")

if __name__ == "__main__":
    for directory in (DESKTOP, WEB, BRAND):
        directory.mkdir(parents=True, exist_ok=True)
    (BRAND / "autobots-eyes.svg").write_text(LOGO, encoding="utf-8")
    (WEB / "autobots-eyes.svg").write_text(LOGO, encoding="utf-8")
    (BRAND / "bot-companion.svg").write_text(BOT, encoding="utf-8")
    logo = rasterize(LOGO, 512)
    logo.save(DESKTOP / "AutobotsIcon.png")
    logo.save(DESKTOP / "AutobotsIcon.ico", sizes=[(16,16),(24,24),(32,32),(48,48),(64,64),(128,128),(256,256)])
    logo.resize((180,180), Image.Resampling.LANCZOS).save(WEB / "apple-touch-icon.png")
    companion = rasterize(BOT, 480)
    companion.save(DESKTOP / "BotCompanion.png")
    companion.save(WEB / "bot-companion.png")
    print("Exported eyes logo, Windows icon sizes, touch icon, and companion artwork.")
