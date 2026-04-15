"""
Procedurally generates the Elysium Wallpaper app icons.

Design: a stylized horizon at the bottom — dark mountain silhouette — with a
gradient sky transitioning from midnight blue (top) through twilight purple to
warm sunset orange near the horizon. A crescent moon and one star sit in the
upper sky. Matches the time-of-day theme of the app.

Renders ONE high-resolution master at 1024x1024, then downsamples with high-
quality LANCZOS resampling to every size Windows needs. Wide tile + splash get
their own renders (different aspect ratios), composed from the same elements.

Re-run any time the design changes — overwrites the existing PNGs.
"""

from __future__ import annotations

from pathlib import Path
from PIL import Image, ImageDraw, ImageFilter

REPO_ROOT = Path(__file__).resolve().parent.parent
ASSETS = REPO_ROOT / "ElysiumWallpaper.WinUI" / "Assets"
DOCS = REPO_ROOT / "docs"

# Sky gradient stops (top → bottom). Vertically lerp'd per pixel row.
SKY_TOP    = (8,   12,  48)    # midnight blue
SKY_MID    = (60,  35,  92)    # twilight purple
SKY_HORIZON = (235, 130, 60)   # warm sunset orange

MOUNTAIN_DARK  = (12, 18, 38)   # near-black silhouette
MOUNTAIN_EDGE  = (28, 38, 70)   # subtle highlight along ridge
MOON           = (250, 244, 220)
STAR           = (255, 250, 230)

# Outer rounded-square corner radius as fraction of canvas size.
CORNER_RADIUS_FRAC = 0.18


def _lerp(a: tuple[int, int, int], b: tuple[int, int, int], t: float) -> tuple[int, int, int]:
    return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(3))  # type: ignore[return-value]


def _sky_color(y: float, height: float) -> tuple[int, int, int]:
    """Three-stop vertical gradient: top→mid (0..0.6), mid→horizon (0.6..1)."""
    t = y / height
    if t < 0.6:
        return _lerp(SKY_TOP, SKY_MID, t / 0.6)
    return _lerp(SKY_MID, SKY_HORIZON, (t - 0.6) / 0.4)


def _draw_sky(img: Image.Image) -> None:
    draw = ImageDraw.Draw(img)
    w, h = img.size
    for y in range(h):
        draw.line([(0, y), (w, y)], fill=_sky_color(y, h))


def _draw_mountains(img: Image.Image, *, horizon_frac: float = 0.66) -> None:
    """Two-layer mountain silhouette at the bottom of the canvas."""
    draw = ImageDraw.Draw(img, "RGBA")
    w, h = img.size
    horizon = int(h * horizon_frac)

    # Back ridge (taller, slightly lighter).
    back = [
        (0, horizon + int(h * 0.04)),
        (int(w * 0.18), horizon - int(h * 0.06)),
        (int(w * 0.36), horizon + int(h * 0.02)),
        (int(w * 0.55), horizon - int(h * 0.10)),
        (int(w * 0.78), horizon - int(h * 0.02)),
        (w, horizon + int(h * 0.05)),
        (w, h),
        (0, h),
    ]
    draw.polygon(back, fill=MOUNTAIN_EDGE + (255,))

    # Front ridge (shorter, darker, full bleed to bottom).
    front = [
        (0, horizon + int(h * 0.08)),
        (int(w * 0.12), horizon + int(h * 0.02)),
        (int(w * 0.30), horizon + int(h * 0.10)),
        (int(w * 0.48), horizon - int(h * 0.02)),
        (int(w * 0.66), horizon + int(h * 0.07)),
        (int(w * 0.84), horizon + int(h * 0.01)),
        (w, horizon + int(h * 0.09)),
        (w, h),
        (0, h),
    ]
    draw.polygon(front, fill=MOUNTAIN_DARK + (255,))


def _draw_moon(img: Image.Image, *, cx_frac: float = 0.70, cy_frac: float = 0.28,
               radius_frac: float = 0.13) -> None:
    """Full moon disc with a soft outer glow.

    Earlier versions used a crescent, but the crescent shape carries strong religious
    connotations that we don't want to imply. A full moon is universally read as
    "nighttime sky" without any cultural baggage and reads cleanly at small sizes.
    """
    w, h = img.size
    cx, cy = int(w * cx_frac), int(h * cy_frac)
    r = int(min(w, h) * radius_frac)

    # Soft outer glow first (gives it presence at small sizes).
    glow = Image.new("RGBA", img.size, (0, 0, 0, 0))
    glow_draw = ImageDraw.Draw(glow)
    glow_draw.ellipse((cx - r * 2, cy - r * 2, cx + r * 2, cy + r * 2),
                      fill=(*MOON, 45))
    glow = glow.filter(ImageFilter.GaussianBlur(radius=r * 0.45))
    img.alpha_composite(glow)

    # Moon disc.
    moon_layer = Image.new("RGBA", img.size, (0, 0, 0, 0))
    md = ImageDraw.Draw(moon_layer)
    md.ellipse((cx - r, cy - r, cx + r, cy + r), fill=(*MOON, 255))
    img.alpha_composite(moon_layer)


def _draw_star(img: Image.Image, *, cx_frac: float, cy_frac: float, size_frac: float = 0.025) -> None:
    """Simple four-point sparkle star."""
    w, h = img.size
    cx, cy = int(w * cx_frac), int(h * cy_frac)
    s = max(2, int(min(w, h) * size_frac))

    star_layer = Image.new("RGBA", img.size, (0, 0, 0, 0))
    sd = ImageDraw.Draw(star_layer)
    # Diamond shape.
    sd.polygon([(cx, cy - s), (cx + s // 2, cy), (cx, cy + s), (cx - s // 2, cy)],
               fill=(*STAR, 255))
    sd.polygon([(cx - s, cy), (cx, cy - s // 2), (cx + s, cy), (cx, cy + s // 2)],
               fill=(*STAR, 255))
    img.alpha_composite(star_layer)


def _apply_rounded_corners(img: Image.Image, radius_frac: float = CORNER_RADIUS_FRAC) -> Image.Image:
    """Mask the image into a rounded square. Returns a new RGBA image."""
    w, h = img.size
    radius = int(min(w, h) * radius_frac)
    mask = Image.new("L", img.size, 0)
    mdraw = ImageDraw.Draw(mask)
    mdraw.rounded_rectangle((0, 0, w, h), radius=radius, fill=255)

    out = Image.new("RGBA", img.size, (0, 0, 0, 0))
    out.paste(img, (0, 0), mask)
    return out


def render_square(size: int, *, rounded: bool = True) -> Image.Image:
    """Renders the canonical square icon at the requested size."""
    img = Image.new("RGBA", (size, size), (0, 0, 0, 255))
    _draw_sky(img)
    _draw_mountains(img, horizon_frac=0.66)
    _draw_moon(img, cx_frac=0.72, cy_frac=0.26, radius_frac=0.13)
    _draw_star(img, cx_frac=0.30, cy_frac=0.18, size_frac=0.030)
    _draw_star(img, cx_frac=0.48, cy_frac=0.34, size_frac=0.018)
    _draw_star(img, cx_frac=0.20, cy_frac=0.40, size_frac=0.014)
    return _apply_rounded_corners(img) if rounded else img


def render_wide(width: int, height: int) -> Image.Image:
    """Wide tile / banner: same elements as the square icon, but composed for landscape.

    Earlier version pushed the moon to the far right and left the entire left half empty,
    which read as off-balance at banner sizes. This version positions the moon near the
    right golden-ratio third and distributes a small constellation across the full sky
    so the composition feels intentional rather than lopsided.
    """
    img = Image.new("RGBA", (width, height), (0, 0, 0, 255))
    _draw_sky(img)
    _draw_mountains(img, horizon_frac=0.62)
    # Moon at ~right-third, slightly smaller so it doesn't dominate.
    _draw_moon(img, cx_frac=0.72, cy_frac=0.32, radius_frac=0.13)
    # Constellation: a few brighter stars across the sky, with smaller ones filling
    # the empty zones. Sizes vary so the eye reads depth instead of a uniform pattern.
    _draw_star(img, cx_frac=0.10, cy_frac=0.22, size_frac=0.018)
    _draw_star(img, cx_frac=0.18, cy_frac=0.45, size_frac=0.012)
    _draw_star(img, cx_frac=0.28, cy_frac=0.18, size_frac=0.022)
    _draw_star(img, cx_frac=0.38, cy_frac=0.36, size_frac=0.014)
    _draw_star(img, cx_frac=0.48, cy_frac=0.20, size_frac=0.016)
    _draw_star(img, cx_frac=0.55, cy_frac=0.42, size_frac=0.012)
    _draw_star(img, cx_frac=0.88, cy_frac=0.50, size_frac=0.014)
    return img


def downsample(master: Image.Image, size: tuple[int, int]) -> Image.Image:
    """High-quality downsample for when target ≪ master size."""
    return master.resize(size, Image.Resampling.LANCZOS)


def save_ico(img: Image.Image, path: Path) -> None:
    """Multi-resolution ICO containing 16/24/32/48/64/128/256 variants."""
    sizes = [(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)]
    img.save(path, format="ICO", sizes=sizes)


def main() -> None:
    ASSETS.mkdir(parents=True, exist_ok=True)
    print(f"Rendering icons into {ASSETS}")

    # Master: render at very high resolution once, downsample for everything else.
    # This avoids the rasterization artifacts you get from re-rendering at 24x24.
    master = render_square(1024, rounded=True)
    master.save(ASSETS / "AppIcon.master.png")

    targets = {
        # Required by Package.appxmanifest defaults / referenced from csproj.
        "Square44x44Logo.scale-200.png":                            (88, 88),
        "Square44x44Logo.targetsize-24_altform-unplated.png":       (24, 24),
        "Square150x150Logo.scale-200.png":                          (300, 300),
        "StoreLogo.png":                                            (50, 50),
        "LockScreenLogo.scale-200.png":                             (48, 48),
    }

    for filename, size in targets.items():
        out = downsample(master, size)
        out.save(ASSETS / filename)
        print(f"  {filename:60s} {size[0]}x{size[1]}")

    # Wide + splash use a different aspect, so render them fresh.
    wide = render_wide(620, 300)
    wide.save(ASSETS / "Wide310x150Logo.scale-200.png")
    print(f"  {'Wide310x150Logo.scale-200.png':60s} 620x300")

    splash = render_wide(1240, 600)
    splash.save(ASSETS / "SplashScreen.scale-200.png")
    print(f"  {'SplashScreen.scale-200.png':60s} 1240x600")

    # ICO for the AppWindow.SetIcon hook.
    ico_path = ASSETS / "AppIcon.ico"
    save_ico(master, ico_path)
    print(f"  {'AppIcon.ico':60s} multi-res")

    # README hero image — committed (unlike the master, which is gitignored).
    # 512x512 logo as a fallback / favicon-grade asset.
    DOCS.mkdir(parents=True, exist_ok=True)
    logo = downsample(master, (512, 512))
    logo.save(DOCS / "logo.png")
    print(f"  {'docs/logo.png':60s} 512x512")

    # Wide README banner. 1280x640 (2:1) matches GitHub's OpenGraph / social-preview
    # ratio and renders cleanly across desktop (~760px content area, scaled down) and
    # mobile. Same elements as the icon, but composed for landscape so the gradient
    # sky and crescent get room to breathe.
    banner = render_wide(1280, 640)
    banner.save(DOCS / "banner.png")
    print(f"  {'docs/banner.png':60s} 1280x640")

    print("Done.")


if __name__ == "__main__":
    main()
