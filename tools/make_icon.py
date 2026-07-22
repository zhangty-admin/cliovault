# -*- coding: utf-8 -*-
"""
ClipVault 应用图标生成器（numpy 渲染版）
设计：玻璃拟态 Glassmorphism + 层叠卡片（立体景深）
品牌色：靛蓝 #4C6EF5 → 紫罗兰

输出：
  src/ClipVault/Assets/app_icon_1024.png  高清主图（1024）
  src/ClipVault/Assets/app.png            应用内资源（512）
  src/ClipVault/Assets/app-icon.png       预览图（1024）
  src/ClipVault/Assets/app.ico            多分辨率图标（256/128/64/48/32/24/16）
"""
import os
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

S = 1024  # 主图尺寸


# ---------------------------------------------------------------- 基础工具

def grids(h, w):
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
    return yy, xx


def to_pil(arr):
    """(h,w,4) float 0..1 -> RGBA PIL"""
    a = np.clip(arr, 0, 1)
    return Image.fromarray((a * 255.0 + 0.5).astype(np.uint8), "RGBA")


def from_pil(img):
    return np.asarray(img.convert("RGBA")).astype(np.float32) / 255.0


def over(dst, src):
    """Porter-Duff 'over'，dst/src 为 (h,w,4) float 0..1（任意尺寸相同）"""
    sa = src[..., 3:4]
    da = dst[..., 3:4]
    out_a = sa + da * (1 - sa)
    out_rgb = (src[..., :3] * sa + dst[..., :3] * da * (1 - sa)) / np.clip(out_a, 1e-6, None)
    return np.concatenate([out_rgb, out_a], -1)


def rounded_mask(h, w, r, inset=0):
    m = Image.new("L", (w, h), 0)
    d = ImageDraw.Draw(m)
    d.rounded_rectangle([inset, inset, w - 1 - inset, h - 1 - inset],
                        radius=max(1, r - inset), fill=255)
    return np.asarray(m).astype(np.float32) / 255.0


def blur_alpha(layer, radius):
    return from_pil(to_pil(layer).filter(ImageFilter.GaussianBlur(radius)))


def place_on_canvas(canvas_h, canvas_w, small_arr, angle, cx, cy):
    """把小图层旋转后居中放置到全尺寸透明画布 (h,w,4)"""
    img = to_pil(small_arr)
    rot = img.rotate(angle, expand=True, resample=Image.BICUBIC)
    layer = np.zeros((canvas_h, canvas_w, 4), np.float32)
    rw, rh = rot.size
    px = int(round(cx - rw / 2))
    py = int(round(cy - rh / 2))
    # 裁剪到画布范围
    x0 = max(0, px); y0 = max(0, py)
    x1 = min(canvas_w, px + rw); y1 = min(canvas_h, py + rh)
    if x1 <= x0 or y1 <= y0:
        return layer
    sx0 = x0 - px; sy0 = y0 - py
    sx1 = sx0 + (x1 - x0); sy1 = sy0 + (y1 - y0)
    arr = from_pil(rot)
    layer[y0:y1, x0:x1] = arr[sy0:sy1, sx0:sx1]
    return layer


# ---------------------------------------------------------------- 背景

def render_background(s):
    h = w = s
    yy, xx = grids(h, w)

    # 对角渐变：深靛蓝 -> 近黑
    t = (xx / w + yy / h) / 2.0
    c1 = np.array([26, 30, 58], np.float32) / 255.0     # 深靛蓝
    c2 = np.array([7, 7, 14], np.float32) / 255.0       # 近黑
    rgb = c1 * (1 - t)[..., None] + c2 * t[..., None]

    # 环境辉光：靛蓝（左上偏中）+ 紫罗兰（右下）
    def glow(cx, cy, r, color, strength):
        d = np.sqrt((xx - cx) ** 2 + (yy - cy) ** 2)
        a = np.clip(1 - d / r, 0, 1) ** 2.2
        rgb[:] += (np.array(color, np.float32) / 255.0) * a[..., None] * strength

    glow(w * 0.34, h * 0.38, w * 0.58, [76, 110, 245], 0.55)
    glow(w * 0.74, h * 0.74, w * 0.52, [150, 90, 255], 0.42)

    rgb = np.clip(rgb, 0, 1)
    sq = rounded_mask(h, w, int(s * 0.225))
    alpha = sq
    return np.concatenate([rgb, alpha[..., None]], -1)


# ---------------------------------------------------------------- 卡片

def render_card(cw, ch, r, c_top, c_bot, base_a, frost_str,
                edge_col, content=False):
    """渲染单张玻璃卡片 (ch,cw,4) float 0..1"""
    yy, xx = grids(ch, cw)
    R = rounded_mask(ch, cw, r)

    # 底色对角渐变
    t = (xx / cw + yy / ch) / 2.0
    ct = np.array(c_top, np.float32) / 255.0
    cb = np.array(c_bot, np.float32) / 255.0
    rgb = ct * (1 - t)[..., None] + cb * t[..., None]

    # 磨砂高光：顶部白色淡入
    fade = np.clip(1 - yy / (ch * 0.60), 0, 1) ** 2.0
    white = np.array([1, 1, 1], np.float32)
    rgb = rgb * (1 - frost_str * fade[..., None]) + white * (frost_str * fade[..., None])

    # 左上斜向反光带
    # 用一条对角线性衰减模拟
    sheen = np.clip(1 - (xx / cw * 0.6 + yy / ch * 0.9) / 1.0, 0, 1) ** 3
    rgb = np.clip(rgb + white * sheen[..., None] * 0.22, 0, 1)

    # 边缘霓虹高光（rim band）
    inner = rounded_mask(ch, cw, r, inset=int(r * 0.18))
    rim = np.clip(R - inner, 0, 1)
    ec = np.array(edge_col, np.float32) / 255.0
    rgb = np.clip(rgb * (1 - rim[..., None] * 0.5) + ec * (rim[..., None] * 0.9), 0, 1)

    # 透明度：基底半透明 + rim 处更实
    alpha = base_a * R + 0.5 * rim
    alpha = np.clip(alpha, 0, 1)

    # 内容线条 + 剪贴板夹子（仅前卡）
    if content:
        lines_layer = np.zeros((ch, cw, 4), np.float32)
        ld = ImageDraw.Draw(to_pil(lines_layer))
        lx0 = int(cw * 0.18)
        for i, frac in enumerate([0.64, 0.78, 0.5]):
            x1 = int(cw * (0.18 + frac))
            ly = int(ch * 0.30) + i * int(ch * 0.075)
            lh = int(ch * 0.022)
            ld.rounded_rectangle([lx0, ly, x1, ly + lh], radius=lh // 2,
                                 fill=(235, 240, 255, 120))
        # 夹子
        clipw = int(cw * 0.22)
        cliph = int(ch * 0.052)
        cx0 = (cw - clipw) // 2
        cy0 = int(ch * 0.075)
        ld.rounded_rectangle([cx0, cy0, cx0 + clipw, cy0 + cliph],
                             radius=cliph // 2, fill=(255, 255, 255, 150))
        ld.rounded_rectangle([cx0 + 5, cy0 + 5, cx0 + clipw - 5, cy0 + cliph - 5],
                             radius=(cliph - 10) // 2, fill=(110, 150, 255, 200))
        lines_layer = from_pil(to_pil(lines_layer))
        lines_layer[..., 3] *= R
        # 合到卡片（不透明叠加）
        card = np.concatenate([rgb, alpha[..., None]], -1)
        card = over(card, lines_layer)
        rgb, alpha = card[..., :3], card[..., 3]

    return np.concatenate([rgb, alpha[..., None]], -1)


def soft_shadow(canvas_s, cw, ch, r, blur, alpha, dx, dy):
    """投影：黑柔影"""
    h = w = canvas_s
    layer = np.zeros((h, w, 4), np.float32)
    yy, xx = grids(h, w)
    # 用一个填充圆角矩形（模糊前）近似
    m = Image.new("L", (cw, ch), 0)
    ImageDraw.Draw(m).rounded_rectangle([0, 0, cw - 1, ch - 1], radius=r, fill=255)
    big = Image.new("L", (w, h), 0)
    big.paste(m, ((w - cw) // 2 + dx, (h - ch) // 2 + dy))
    big = big.filter(ImageFilter.GaussianBlur(blur))
    a = np.asarray(big).astype(np.float32) / 255.0 * alpha
    layer[..., 3] = a
    return layer


# ---------------------------------------------------------------- 主流程

def make_icon(s=1024):
    canvas = render_background(s)

    # 投影
    cw = int(s * 0.46)
    ch = int(s * 0.58)
    r = int(s * 0.07)
    sh = soft_shadow(s, cw, ch, r, int(s * 0.035), 0.55, 0, int(s * 0.045))
    canvas = over(canvas, sh)

    cards = [
        # angle, scale, c_top, c_bot, base_a, frost, edge, content
        (-16, 0.86, (96, 70, 200), (58, 40, 140), 0.50, 0.26, (150, 120, 255), False),
        (12, 0.93, (70, 110, 235), (48, 72, 200), 0.58, 0.30, (120, 200, 255), False),
        (-3, 1.00, (95, 135, 255), (130, 95, 250), 0.72, 0.34, (190, 225, 255), True),
    ]
    cx = s * 0.5
    for (ang, sc, ct, cb, ba, fr, ec, content) in cards:
        W = int(cw * sc)
        H = int(ch * sc)
        R = int(r * sc)
        card = render_card(W, H, R, ct, cb, ba, fr, ec, content)
        oy = -int(s * 0.02) if sc < 0.9 else int(s * 0.015)
        layer = place_on_canvas(s, s, card, ang, cx, s * 0.51 + oy)
        canvas = over(canvas, layer)

    # 整体顶部镜面高光（裁到 squircle）
    yy, xx = grids(s, s)
    spec = np.clip(1 - (xx / s * 0.5 + yy / s * 1.1), 0, 1) ** 3.0
    white = np.array([1, 1, 1], np.float32)
    spec_rgb = np.zeros((s, s, 3), np.float32) + white * spec[..., None] * 0.16
    sq = rounded_mask(s, s, int(s * 0.225))
    spec_layer = np.concatenate([spec_rgb, (spec * 0.5 * sq)[..., None]], -1)
    canvas = over(canvas, spec_layer)

    # 外圈细描边
    rim_mask = rounded_mask(s, s, int(s * 0.225))
    inner = rounded_mask(s, s, int(s * 0.225), inset=2)
    rim = np.clip(rim_mask - inner, 0, 1)
    rim_layer = np.concatenate([np.zeros((s, s, 3), np.float32) + 1.0,
                                (rim * 0.12)[..., None]], -1)
    canvas = over(canvas, rim_layer)

    return to_pil(canvas)


def main():
    here = os.path.dirname(os.path.abspath(__file__))
    assets = os.path.abspath(os.path.join(here, "..", "src", "ClipVault", "Assets"))
    os.makedirs(assets, exist_ok=True)

    master = make_icon(1024)

    p1024 = os.path.join(assets, "app_icon_1024.png")
    master.save(p1024)
    print("saved", p1024)

    p512 = os.path.join(assets, "app.png")
    master.resize((512, 512), Image.LANCZOS).save(p512)
    print("saved", p512)

    p_preview = os.path.join(assets, "app-icon.png")
    master.save(p_preview)
    print("saved", p_preview)

    ico_path = os.path.join(assets, "app.ico")
    sizes = [(256, 256), (128, 128), (64, 64), (48, 48), (32, 32), (24, 24), (16, 16)]
    master.save(ico_path, format="ICO", sizes=sizes)
    print("saved", ico_path)


if __name__ == "__main__":
    main()
