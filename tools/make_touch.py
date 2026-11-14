# Prepara los botones táctiles a partir del arte del usuario: quita los rótulos (GOLPE, SALTO,
# ESPECIAL) rehaciendo el borde del botón, y deja el fondo transparente.
import numpy as np, cv2
from PIL import Image
from scipy import ndimage

src = np.array(Image.open('src.jpg').convert('RGB')).astype(np.uint8)

def background_alpha(rgb):
    lum = rgb.astype(int).sum(2)
    dark = lum < 75
    lab, _ = ndimage.label(dark)
    border = set(np.unique(np.concatenate([lab[0], lab[-1], lab[:, 0], lab[:, -1]]))) - {0}
    bg = np.isin(lab, list(border))
    a = (~bg).astype(np.float32)
    a = cv2.GaussianBlur(a, (3, 3), 0.8)
    return (a * 255).astype(np.uint8)

def round_button(name, box, textbox, protect=None, extra=18):
    x0, y0, x1, y1 = box
    rgb = src[y0:y1, x0:x1].copy()
    h, w = rgb.shape[:2]
    lum = rgb.astype(int).sum(2)
    ys, xs = np.nonzero(lum > 90)
    cx = (xs.min() + xs.max()) / 2; R = (xs.max() - xs.min()) / 2
    cy = ys.max() - R
    yy, xx = np.mgrid[0:h, 0:w]
    r = np.hypot(xx - cx, yy - cy); th = np.arctan2(xx - cx, -(yy - cy))  # 0 = arriba
    tx0, ty0, tx1, ty1 = [v - o for v, o in zip(textbox, (x0, y0, x0, y0))]
    mask = (xx >= tx0) & (xx <= tx1) & (yy >= ty0) & (yy <= ty1 + extra)
    keep = np.zeros_like(mask)
    if protect is not None:
        pr = protect(rgb.astype(int)) & (r < 0.62 * R)
        lab, n = ndimage.label(pr)
        # solo las piezas del dibujo del centro (las que siguen por debajo del rótulo)
        below = np.unique(lab[(yy > ty1 + extra + 4) & pr])
        keep = np.isin(lab, below[below > 0]) & (yy > ty1 - 25)
        keep = ndimage.binary_dilation(keep, iterations=2)
    mask &= ~keep
    bad = mask | keep  # no se copia desde aquí
    out = rgb.copy()
    band = (r > 0.5 * R) & (r < 0.62 * R) & (np.abs(np.abs(th) - np.pi / 2) < 0.5)
    face = np.median(rgb[band].astype(int), axis=0)
    # Todo el disco es concéntrico: cada píxel tapado se copia del mismo radio, buscando el ángulo
    # libre más cercano hacia los lados (ni rótulo ni dibujo del centro).
    cands = [np.pi / 2 + d for d in np.linspace(0, 2.4, 90)] + [np.pi / 2 - d for d in np.linspace(0.05, 1.5, 40)]
    for y, x in zip(*np.nonzero(mask & (r <= R + 3))):
        sgn = 1 if th[y, x] >= 0 else -1
        rr = r[y, x]
        for t in cands:
            sx = int(round(cx + sgn * rr * np.sin(t))); sy = int(round(cy - rr * np.cos(t)))
            if 0 <= sx < w and 0 <= sy < h and not bad[sy, sx] and (rr >= 0.66 * R or np.abs(rgb[sy, sx].astype(int) - face).sum() < 90):
                out[y, x] = rgb[sy, sx]; break
    alpha = np.clip((R + 2.5 - r) * 255 / 2.5, 0, 255).astype(np.uint8)
    img = Image.fromarray(np.dstack([out, alpha]), 'RGBA')
    img = img.crop(img.getbbox())
    s = 256 / max(img.size); img = img.resize((round(img.size[0] * s), round(img.size[1] * s)), Image.LANCZOS)
    img.save(f'tc_{name}.png'); print(name, img.size, 'R', R)

def plain(name, box, size):
    x0, y0, x1, y1 = box
    rgb = src[y0:y1, x0:x1].copy()
    img = Image.fromarray(np.dstack([rgb, background_alpha(rgb)]), 'RGBA')
    img = img.crop(img.getbbox())
    s = size / max(img.size); img = img.resize((round(img.size[0] * s), round(img.size[1] * s)), Image.LANCZOS)
    img.save(f'tc_{name}.png'); print(name, img.size)

round_button('attack', (1230, 90, 1850, 700), (1318, 100, 1745, 282), protect=lambda c: (c[...,0] > 170) & (c[...,1] > 120) & (c[...,2] > 105), extra=12)
round_button('jump', (1880, 90, 2520, 700), (2000, 100, 2385, 255), protect=lambda c: (c[...,2] > 70) & (c[...,2] > c[...,0] + 40) & (c[...,2] < 170) & (c[...,1] < 90))
round_button('special', (1220, 730, 1850, 1345), (1270, 790, 1775, 918), protect=lambda c: ((c[...,0] > 150) & (c[...,1] < 130)) | ((c[...,0] > 90) & (c[...,1] < 45) & (c[...,0] > c[...,2] + 40)))
plain('start', (1920, 760, 2480, 1060), 256)
plain('back', (1920, 1080, 2480, 1380), 256)
plain('dpad', (100, 190, 1150, 1260), 384)
