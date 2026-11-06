"""Tiny painter's-algorithm renderer for baked triangle soups (Unity mesh space, Y up)."""
import numpy as np
from PIL import Image, ImageDraw

LIGHT = np.array([-0.533, 0.775, 0.339])


def load(path):
    data = np.loadtxt(path)
    tris = data[:, :9].reshape(-1, 3, 3)
    colors = data[:, 12:15]
    flags = data[:, 15:18]
    return tris, colors, flags


def view_matrix(yaw_deg, pitch_deg):
    """Camera looking at the model: yaw around Y (0 = looking at the face, from +Z), pitch down."""
    y, p = np.radians(yaw_deg), np.radians(pitch_deg)
    ry = np.array([[np.cos(y), 0, -np.sin(y)], [0, 1, 0], [np.sin(y), 0, np.cos(y)]])
    rx = np.array([[1, 0, 0], [0, np.cos(p), -np.sin(p)], [0, np.sin(p), np.cos(p)]])
    return rx @ ry


def render(tris, colors, yaw=0, pitch=0, size=420, scale=300, center=(0, 0.6, 0), grid=False, image=None, offset=(0, 0), lines=()):
    m = view_matrix(yaw, pitch)
    pts = (tris - np.array(center)) @ m.T  # camera space: x right, y up, z towards viewer
    normals = np.cross(tris[:, 1] - tris[:, 0], tris[:, 2] - tris[:, 0])
    normals /= np.linalg.norm(normals, axis=1, keepdims=True) + 1e-12
    light = np.clip(normals @ LIGHT, 0, 1)
    band = np.where(light > 0.72, 1.0, np.where(light > 0.35, 0.8, 0.58))
    img = image if image is not None else Image.new("RGB", (size, size), (70, 92, 60))
    draw = ImageDraw.Draw(img)
    ox, oy = offset
    if grid:
        for v in np.arange(-1.0, 1.01, 0.1):
            gx = ox + size / 2 + v * scale
            draw.line([(gx, oy), (gx, oy + size)], fill=(90, 110, 80) if abs(v) > 1e-6 else (140, 140, 90))
        for v in np.arange(-0.6, 1.61, 0.1):
            gy = oy + size / 2 - (v - center[1]) * scale
            draw.line([(ox, gy), (ox + size, gy)], fill=(90, 110, 80) if abs(v) > 1e-6 else (140, 140, 90))
    order = np.argsort(pts[:, :, 2].mean(1))  # far first
    facing = (np.cross(pts[:, 1] - pts[:, 0], pts[:, 2] - pts[:, 0])[:, 2])
    for i in order:
        c = np.clip(colors[i] * band[i], 0, 1)
        poly = [(ox + size / 2 + p[0] * scale, oy + size / 2 - p[1] * scale) for p in pts[i]]
        draw.polygon(poly, fill=tuple(int(v * 255) for v in c))
    for a, b, col in lines:
        pa = (np.array(a) - center) @ m.T
        pb = (np.array(b) - center) @ m.T
        draw.line([(ox + size / 2 + pa[0] * scale, oy + size / 2 - pa[1] * scale), (ox + size / 2 + pb[0] * scale, oy + size / 2 - pb[1] * scale)], fill=col, width=2)
    return img
