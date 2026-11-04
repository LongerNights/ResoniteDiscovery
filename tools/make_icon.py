#!/usr/bin/env python3
"""Generates the ResoniteDiscovery dash-tab icon: a two-armed spiral galaxy in the mod's purples.

Output: src/Discover.Resonite/Assets/galaxy-icon.png (256x256 RGBA, transparent background), embedded in the
mod DLL and imported into the local asset DB at runtime (see Ui/IconLoader.cs).

Colours match RadiantUI_Constants: Hero.PURPLE #ba64f2 and MidLight.PURPLE #824aab, with a pale core.
Drawn at 4x and downsampled for smooth edges. Requires Pillow.

Usage:  python tools/make_icon.py
"""
import math
import os

from PIL import Image, ImageDraw, ImageFilter

SIZE = 256
SCALE = 4
S = SIZE * SCALE
C = S / 2

CORE = (246, 232, 255)      # pale lilac core
HERO = (186, 100, 242)      # RadiantUI_Constants.Hero.PURPLE
MIDLIGHT = (130, 74, 171)   # RadiantUI_Constants.MidLight.PURPLE


def lerp(a, b, t):
    return tuple(round(x + (y - x) * t) for x, y in zip(a, b))


def colour_at(t):
    """Core colour to Hero purple over the inner third, then to MidLight purple."""
    if t < 0.33:
        return lerp(CORE, HERO, t / 0.33)
    return lerp(HERO, MIDLIGHT, (t - 0.33) / 0.67)


def main():
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))

    # Soft halo behind the arms.
    halo = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    hd = ImageDraw.Draw(halo)
    hr = S * 0.30
    hd.ellipse([C - hr, C - hr, C + hr, C + hr], fill=HERO + (90,))
    halo = halo.filter(ImageFilter.GaussianBlur(S * 0.06))
    img.alpha_composite(halo)

    d = ImageDraw.Draw(img)

    # Two spiral arms, tapering outward. Thick enough to read at ~64 px on the dash.
    steps = 900
    turns = 1.15
    for arm in range(2):
        base = arm * math.pi
        for i in range(steps):
            t = i / (steps - 1)
            angle = base + t * turns * 2 * math.pi
            radius = S * (0.05 + 0.38 * t)
            width = S * (0.075 - 0.058 * t)
            x = C + radius * math.cos(angle)
            y = C + radius * math.sin(angle)
            d.ellipse([x - width, y - width, x + width, y + width], fill=colour_at(t) + (255,))

    # Bright core.
    cr = S * 0.11
    d.ellipse([C - cr, C - cr, C + cr, C + cr], fill=CORE + (255,))

    # A few four-point sparkles.
    for sx, sy, size in ((0.80, 0.20, 0.060), (0.17, 0.78, 0.045), (0.84, 0.74, 0.030)):
        x, y, r = sx * S, sy * S, size * S
        w = r * 0.28
        d.polygon([(x, y - r), (x + w, y - w), (x + r, y), (x + w, y + w),
                   (x, y + r), (x - w, y + w), (x - r, y), (x - w, y - w)], fill=CORE + (255,))

    out = img.resize((SIZE, SIZE), Image.LANCZOS)
    path = os.path.join(os.path.dirname(__file__), "..", "src", "Discover.Resonite", "Assets", "galaxy-icon.png")
    path = os.path.normpath(path)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    out.save(path, optimize=True)
    print(path)


if __name__ == "__main__":
    main()
