// 气泡：照搬桌面版 BuildBubbleSkin / DrawBubbleInto 的几何与配色
import { lighten, darken, inkFor, rgba } from './util.js';

const SHADOW = [0x2A, 0x3A, 0x66];

function bubblePath(g, l, t, r, b, rad, tailL, tailR, tipX, tipY) {
  rad = Math.max(0.5, Math.min(rad, (r - l) / 2, (b - t) / 2));
  g.beginPath();
  g.arc(l + rad, t + rad, rad, Math.PI, Math.PI * 1.5);
  g.arc(r - rad, t + rad, rad, Math.PI * 1.5, Math.PI * 2);
  g.arc(r - rad, b - rad, rad, 0, Math.PI * 0.5);
  g.lineTo(tailR, b);
  g.lineTo(tipX, tipY);
  g.lineTo(tailL, b);
  g.arc(l + rad, b - rad, rad, Math.PI * 0.5, Math.PI);
  g.closePath();
}

function roundRect(g, l, t, r, b, rad) {
  g.beginPath();
  g.roundRect(l, t, Math.max(1, r - l), Math.max(1, b - t), Math.min(rad, (r - l) / 2, (b - t) / 2));
}

function nineSlice(g, img, x0, y0, w, h, insX, insY) {
  const W = img.naturalWidth || img.width, H = img.naturalHeight || img.height;
  const scale = Math.min(w / W, h / H);
  const cw = Math.max(1, Math.round(Math.min(insX * W * scale, w * 0.5 - 1)));
  const ch = Math.max(1, Math.round(Math.min(insY * H * scale, h * 0.5 - 1)));
  const scw = Math.max(1, Math.round(insX * W)), sch = Math.max(1, Math.round(insY * H));
  const dxs = [x0, x0 + cw, x0 + w - cw, x0 + w], dys = [y0, y0 + ch, y0 + h - ch, y0 + h];
  const sxs = [0, scw, W - scw, W], sys = [0, sch, H - sch, H];
  for (let j = 0; j < 3; j++) for (let i = 0; i < 3; i++) {
    const dw = dxs[i + 1] - dxs[i], dh = dys[j + 1] - dys[j], sw = sxs[i + 1] - sxs[i], sh = sys[j + 1] - sys[j];
    if (dw > 0 && dh > 0 && sw > 0 && sh > 0) g.drawImage(img, sxs[i], sys[j], sw, sh, dxs[i], dys[j], dw, dh);
  }
}

// 自定义外框：取中间 40% 区域看透明度与亮度，决定字色和要不要垫底
export function analyzeFrame(img) {
  const W = img.naturalWidth || img.width, H = img.naturalHeight || img.height;
  const c = document.createElement('canvas');
  c.width = W; c.height = H;
  const g = c.getContext('2d', { willReadFrequently: true });
  g.drawImage(img, 0, 0);
  const x0 = Math.floor(W * 3 / 10), x1 = Math.floor(W * 7 / 10), y0 = Math.floor(H * 3 / 10), y1 = Math.floor(H * 7 / 10);
  const d = g.getImageData(0, 0, W, H).data;
  let L = 0, A = 0, n = 0;
  for (let y = y0; y < y1; y += 2) for (let x = x0; x < x1; x += 2) {
    const p = (y * W + x) * 4, a = d[p + 3] / 255;
    L += (0.299 * d[p] + 0.587 * d[p + 1] + 0.114 * d[p + 2]) * a; A += a; n++;
  }
  const centerAlpha = n === 0 ? 255 : Math.round(A / n * 255);
  const avgL = A < 0.5 ? 255 : L / A;
  return { centerAlpha, ink: avgL > 132 ? [0x1A, 0x1A, 0x1A] : [0xF7, 0xF9, 0xFF] };
}

// 外框只在尺寸 / 样式 / 颜色变化时画一次
export function buildSkin({ w, bh, style, accent, frameImg, frameInfo, inkFix, k = 1 }) {
  if (style === 'custom' && !frameImg) style = 'card';
  const c = document.createElement('canvas');
  c.width = Math.max(1, Math.round(w * k)); c.height = Math.max(1, Math.round(bh * k));
  const g = c.getContext('2d');
  g.scale(k, k);
  g.imageSmoothingQuality = 'high';

  const sw = Math.max(1.4, w * 4.6 / 1026);
  const pad = Math.max(9, w * 0.052);
  const tailH = Math.min(15, bh * 0.15);
  const bodyL = sw + 1.5, bodyR = w - sw - 1.5, bodyT = sw + 1.5, bodyB = bh - tailH - sw - 1.5;
  const radius = Math.max(8, Math.min(15, (bodyB - bodyT) * 0.32));
  const cx = (bodyL + bodyR) / 2;
  const tipY = bodyB + tailH, tailL = cx - 15, tailR = cx + 7, tipX = cx - 5;
  let ink = inkFor(accent);
  let tr = { x: bodyL + pad, y: bodyT + pad * 0.72, w: Math.max(20, bodyR - bodyL - pad * 2), h: Math.max(16, bodyB - bodyT - pad * 1.45) };
  const path = (rad) => bubblePath(g, bodyL, bodyT, bodyR, bodyB, rad, tailL, tailR, tipX, tipY);

  if (style === 'custom') {
    nineSlice(g, frameImg, 0, 0, Math.round(w), Math.round(bh), 0.26, 0.30);
    const insX = pad * 1.15, insT = pad * 0.95, insB = Math.max(pad * 1.2, bh * 0.16);
    tr = { x: insX, y: insT, w: Math.max(20, w - insX * 2), h: Math.max(16, bh - insT - insB) };
    if (frameInfo && frameInfo.centerAlpha < 110) {
      g.fillStyle = 'rgba(255,255,255,0.62)';
      roundRect(g, tr.x - 5, tr.y - 5, tr.x + tr.w + 5, tr.y + tr.h + 5, 10);
      g.fill();
    }
    ink = frameInfo ? frameInfo.ink : [0x1A, 0x1A, 0x1A];
  } else if (style === 'classic') {
    const rx = (bodyR - bodyL) / 2, ry = (bodyB - bodyT) / 2, ecy = (bodyT + bodyB) / 2;
    const csw = Math.max(2.4, sw * 2.1);
    const r1 = w * 0.048, x1 = cx - rx * 0.74, y1 = bh - r1 * 1.25;
    const r2 = w * 0.031, x2 = cx - rx * 0.33, y2 = bh - r2 * 0.85;
    const ell = (x, y, ex, ey) => { g.beginPath(); g.ellipse(x, y, ex, ey, 0, 0, Math.PI * 2); };
    const shapes = [[cx, ecy, rx, ry], [x1, y1, r1, r1 * 0.7], [x2, y2, r2, r2 * 0.7]];
    g.fillStyle = rgba(darken(accent, 0.45), 22 / 255);
    for (const [x, y, ex, ey] of shapes) { ell(x, y + 2 + (ex === rx ? 1 : 0), ex, ey); g.fill(); }
    g.lineWidth = csw;
    g.strokeStyle = rgba(darken(accent, 0.30));
    for (const [x, y, ex, ey] of shapes) { ell(x, y, ex, ey); g.fillStyle = '#fff'; g.fill(); g.stroke(); }
    ink = darken(accent, 0.55);
    tr = { x: cx - rx * 0.80, y: ecy - ry * 0.74, w: rx * 1.60, h: ry * 1.48 };
  } else if (style === 'comic') {
    path(4);
    g.save(); g.translate(5, 5); g.fillStyle = rgba(darken(accent, 0.62), 40 / 255); g.fill(); g.restore();
    g.fillStyle = '#fff'; g.fill();
    g.lineWidth = Math.max(2.4, sw * 1.7); g.strokeStyle = rgba(darken(accent, 0.55)); g.stroke();
    ink = darken(accent, 0.78);
  } else if (style === 'glass') {
    path(radius);
    g.fillStyle = rgba(SHADOW, 20 / 255);
    for (const d of [3, 6, 9]) { g.save(); g.translate(0, d); g.fill(); g.restore(); }
    const lg = g.createLinearGradient(0, bodyT, 0, tipY);
    lg.addColorStop(0, 'rgba(255,255,255,' + 236 / 255 + ')');
    lg.addColorStop(1, rgba(lighten(accent, 0.55), 196 / 255));
    g.fillStyle = lg; g.fill();
    g.lineWidth = Math.max(1.6, sw * 1.35); g.strokeStyle = 'rgba(255,255,255,' + 238 / 255 + ')'; g.stroke();
    const inset = radius + 5;
    if (bodyR - bodyL > inset * 2) {
      g.beginPath(); g.moveTo(bodyL + inset, bodyT + sw * 1.3); g.lineTo(bodyR - inset, bodyT + sw * 1.3);
      g.lineWidth = Math.max(1, sw * 0.8); g.strokeStyle = 'rgba(255,255,255,' + 150 / 255 + ')'; g.stroke();
    }
    ink = darken(accent, 0.72);
  } else {
    path(radius);
    g.save(); g.translate(0, 2.5); g.fillStyle = rgba(SHADOW, 24 / 255); g.fill(); g.restore();
    g.save(); g.translate(0, 5); g.fillStyle = rgba(SHADOW, 15 / 255); g.fill(); g.restore();
    const lg = g.createLinearGradient(0, bodyT, 0, tipY);
    lg.addColorStop(0, '#fff');
    lg.addColorStop(1, rgba(lighten(accent, 0.78)));
    g.fillStyle = lg; g.fill();
    g.lineWidth = sw; g.strokeStyle = rgba(accent, 215 / 255); g.stroke();
    const inset = radius + 4;
    if (bodyR - bodyL > inset * 2) {
      g.beginPath(); g.moveTo(bodyL + inset, bodyT + sw * 1.2); g.lineTo(bodyR - inset, bodyT + sw * 1.2);
      g.lineWidth = Math.max(1, sw * 0.7); g.strokeStyle = 'rgba(255,255,255,' + 130 / 255 + ')'; g.stroke();
    }
  }
  if (inkFix) ink = inkFix;
  return { canvas: c, tr, ink, w, bh };
}

// 字体：点数按 96 DPI 折算成像素，与桌面版同大
export const FONTS = ['YouYuan', 'Noto Sans SC Medium', 'Noto Sans SC', 'DengXian', 'Microsoft YaHei UI', 'KaiTi', 'SimHei', 'FangSong'];
const FALLBACK = '"Microsoft YaHei", "PingFang SC", "Hiragino Sans GB", "Noto Sans SC", sans-serif';
export function fontCss(name, px) {
  const fam = {
    YouYuan: '"YouYuan", "幼圆"', 'Noto Sans SC Medium': '"Noto Sans SC Medium", "Noto Sans SC"',
    'Noto Sans SC': '"Noto Sans SC"', DengXian: '"DengXian", "等线"', 'Microsoft YaHei UI': '"Microsoft YaHei UI"',
    KaiTi: '"KaiTi", "楷体", "STKaiti"', SimHei: '"SimHei", "黑体"', FangSong: '"FangSong", "仿宋", "STFangsong"',
  }[name] || `"${name}"`;
  const wt = name === 'Noto Sans SC Medium' ? '500 ' : '';
  return `${wt}${px}px ${fam}, ${FALLBACK}`;
}
export const ptToPx = pt => pt * 96 / 72;

const NO_START = '，。！？、；：,.!?;:）)」』】》〉…—~～';
export function wrapText(g, text, maxW) {
  const out = [];
  for (const para of String(text).split('\n')) {
    const toks = para.match(/[A-Za-z0-9'’\-]+|\s+|./gu) || [''];
    let line = '';
    for (let t of toks) {
      const test = line + t;
      if (g.measureText(test).width <= maxW || line === '') {
        if (g.measureText(t).width > maxW && line === '') {
          for (const ch of t) {
            if (g.measureText(line + ch).width > maxW && line) { out.push(line); line = ''; }
            line += ch;
          }
        } else line = test;
        continue;
      }
      if (NO_START.includes(t[0])) { line += t; continue; }
      out.push(line.replace(/\s+$/, ''));
      line = /^\s+$/.test(t) ? '' : t;
    }
    out.push(line);
  }
  return out;
}

// 在 ctx（已按 k 缩放到 CSS 像素）上画一帧气泡；返回滚动信息
export function drawBubble(g, skin, text, { font, fontPx, alpha, scale, scroll }) {
  const { w, bh, tr, ink } = skin;
  g.save();
  g.translate(w / 2, bh); g.scale(scale, scale); g.translate(-w / 2, -bh);
  g.globalAlpha = alpha;
  g.drawImage(skin.canvas, 0, 0, w, bh);
  g.font = font;
  g.textBaseline = 'top';
  g.textAlign = 'center';
  const lines = wrapText(g, text, Math.max(12, tr.w));
  const lh = Math.round(fontPx * 1.36);
  const total = lines.length * lh + 6;
  const need = total > tr.h;
  let maxScroll = 0;
  g.fillStyle = rgba(ink);
  if (need) {
    maxScroll = Math.max(0, total - tr.h + 8);
    scroll = Math.max(0, Math.min(scroll, maxScroll));
    g.save();
    g.beginPath(); g.rect(tr.x, tr.y - 1, tr.w, tr.h + 2); g.clip();
    lines.forEach((ln, i) => g.fillText(ln, tr.x + tr.w / 2, tr.y - scroll + 3 + i * lh));
    g.restore();
    const tx = tr.x + tr.w - 2.5, ty = tr.y, th = tr.h;
    g.fillStyle = rgba(ink, 46 / 255);
    g.fillRect(tx, ty, 2, th);
    const ratio = tr.h / total;
    if (ratio > 0.12 && ratio < 1) {
      const thumb = Math.max(18, th * ratio);
      const avail = Math.max(1, total - tr.h + 8);
      g.fillStyle = rgba(ink, 165 / 255);
      g.fillRect(tx, ty + (th - thumb) * (scroll / avail), 2, thumb);
    }
  } else {
    const top = tr.y - tr.h * 0.05 + (tr.h - (total - 6)) / 2;
    lines.forEach((ln, i) => g.fillText(ln, tr.x + tr.w / 2, top + i * lh));
  }
  g.restore();
  return { need, maxScroll, scroll };
}
