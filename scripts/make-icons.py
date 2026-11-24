#!/usr/bin/env python3
"""
Génère les icônes d'Unpark à partir du logo (carrés arrondis), sans aucune bibliothèque externe.

    python3 scripts/make-icons.py

Produit :
  src/Unpark/Assets/Unpark.ico      icône de l'exe (16, 32, 48, 64, 256 px)
  site/favicon.ico                  favicon (16, 32, 48 px)
  site/apple-touch-icon.png         180 px (écran d'accueil iPhone)
  site/logo.svg, site/favicon.svg   logo vectoriel

Le premier rectangle (le contour) est retiré pour 48 px et moins : trop fin pour rester lisible.
Lissage : 4×4 échantillons par pixel. Petites tailles en BMP (compatibilité maximale), 256 px en PNG.
"""
import struct
import zlib
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent

LOGO_SVG = (
    '<svg viewBox="0 0 100 100" xmlns="http://www.w3.org/2000/svg">'
    '<rect x="55" y="17" width="28" height="28" rx="7" fill="none" stroke="#3DDC97" stroke-opacity="0.35" stroke-width="2"/>'
    '<rect x="16" y="54" width="30" height="30" rx="7.5" fill="#3DDC97"/>'
    '<rect x="54" y="54" width="30" height="30" rx="7.5" fill="#3DDC97"/>'
    '<rect x="16" y="16" width="30" height="30" rx="7.5" fill="#3DDC97"/>'
    '<rect x="62" y="8" width="30" height="30" rx="7.5" fill="#FFFFFF"/>'
    "</svg>"
)

GREEN = (0x3D, 0xDC, 0x97)
WHITE = (0xFF, 0xFF, 0xFF)

# (x, y, largeur, hauteur, rayon, couleur, opacité, épaisseur du contour ou None pour un remplissage)
OUTLINE = (55, 17, 28, 28, 7, GREEN, 0.35, 2.0)
FILLED = [
    (16, 54, 30, 30, 7.5, GREEN, 1.0, None),
    (54, 54, 30, 30, 7.5, GREEN, 1.0, None),
    (16, 16, 30, 30, 7.5, GREEN, 1.0, None),
    (62, 8, 30, 30, 7.5, WHITE, 1.0, None),
]


def inside_round_rect(px, py, x, y, w, h, r):
    if px < x or py < y or px > x + w or py > y + h:
        return False
    r = max(0.0, min(r, w / 2, h / 2))
    dx = max(x + r - px, 0.0, px - (x + w - r))
    dy = max(y + r - py, 0.0, py - (y + h - r))
    return dx * dx + dy * dy <= r * r


def covered(px, py, shape):
    x, y, w, h, r, _, _, stroke = shape
    if stroke is None:
        return inside_round_rect(px, py, x, y, w, h, r)
    half = stroke / 2
    outer = inside_round_rect(px, py, x - half, y - half, w + stroke, h + stroke, r + half)
    inner = inside_round_rect(px, py, x + half, y + half, w - stroke, h - stroke, max(0.0, r - half))
    return outer and not inner


def render(size, with_outline):
    """Image RGBA (liste de lignes de tuples), fond transparent."""
    shapes = ([OUTLINE] if with_outline else []) + FILLED
    n = 4  # échantillons par axe
    scale = 100.0 / size
    rows = []
    for j in range(size):
        row = []
        for i in range(size):
            r = g = b = a = 0.0
            for s in range(n):
                for t in range(n):
                    px = (i + (s + 0.5) / n) * scale
                    py = (j + (t + 0.5) / n) * scale
                    # Composition « par-dessus », forme après forme (ordre du SVG).
                    cr = cg = cb = ca = 0.0
                    for shape in shapes:
                        if covered(px, py, shape):
                            col, op = shape[5], shape[6]
                            cr = col[0] * op + cr * (1 - op)
                            cg = col[1] * op + cg * (1 - op)
                            cb = col[2] * op + cb * (1 - op)
                            ca = op + ca * (1 - op)
                    r += cr
                    g += cg
                    b += cb
                    a += ca
            k = n * n
            alpha = a / k
            if alpha > 0:
                # couleurs prémultipliées → couleurs normales
                row.append((round(r / k / alpha), round(g / k / alpha), round(b / k / alpha), round(alpha * 255)))
            else:
                row.append((0, 0, 0, 0))
        rows.append(row)
    return rows


def png_bytes(rows):
    size = len(rows)
    raw = b"".join(b"\x00" + bytes(c for px in row for c in px) for row in rows)

    def chunk(kind, data):
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data) & 0xFFFFFFFF)

    return (b"\x89PNG\r\n\x1a\n"
            + chunk(b"IHDR", struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0))
            + chunk(b"IDAT", zlib.compress(raw, 9))
            + chunk(b"IEND", b""))


def bmp_bytes(rows):
    """Image d'icône au format DIB : en-tête BITMAPINFOHEADER (hauteur doublée), BGRA de bas en haut, masque AND."""
    size = len(rows)
    header = struct.pack("<IiiHHIIiiII", 40, size, size * 2, 1, 32, 0, 0, 0, 0, 0, 0)
    pixels = b"".join(bytes((px[2], px[1], px[0], px[3])) for row in reversed(rows) for px in row)
    mask_row = ((size + 31) // 32) * 4
    mask = b"\x00" * (mask_row * size)  # la transparence vient du canal alpha
    return header + pixels + mask


def ico_bytes(images):
    """images : liste de (taille, données)."""
    out = struct.pack("<HHH", 0, 1, len(images))
    offset = 6 + 16 * len(images)
    entries = b""
    for size, data in images:
        dim = 0 if size >= 256 else size
        entries += struct.pack("<BBBBHHII", dim, dim, 0, 0, 1, 32, len(data), offset)
        offset += len(data)
    return out + entries + b"".join(data for _, data in images)


def icon_image(size):
    rows = render(size, with_outline=size > 48)
    return size, (png_bytes(rows) if size >= 256 else bmp_bytes(rows))


def main():
    app_ico = ROOT / "src" / "Unpark" / "Assets" / "Unpark.ico"
    app_ico.parent.mkdir(parents=True, exist_ok=True)
    app_ico.write_bytes(ico_bytes([icon_image(s) for s in (16, 32, 48, 64, 256)]))
    print(f"✓ {app_ico.relative_to(ROOT)}")

    site = ROOT / "site"
    site.mkdir(exist_ok=True)
    (site / "favicon.ico").write_bytes(ico_bytes([icon_image(s) for s in (16, 32, 48)]))
    (site / "apple-touch-icon.png").write_bytes(png_bytes(render(180, with_outline=True)))
    (site / "logo.svg").write_text(LOGO_SVG + "\n", encoding="utf-8")
    # Favicon vectoriel : sans le contour (lisible en tout petit).
    favicon = LOGO_SVG.replace(LOGO_SVG[LOGO_SVG.index('<rect x="55"'):LOGO_SVG.index('<rect x="16" y="54"')], "")
    (site / "favicon.svg").write_text(favicon + "\n", encoding="utf-8")
    print("✓ site/favicon.ico, site/apple-touch-icon.png, site/logo.svg, site/favicon.svg")


if __name__ == "__main__":
    main()
