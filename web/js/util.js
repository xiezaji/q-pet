// 颜色、格式化、DOM、IndexedDB 小工具

export function parseHex(s, fb = [0, 0, 0]) {
  if (!s) return fb;
  s = String(s).trim().replace(/^#/, '');
  if (!/^[0-9a-fA-F]{6}$/.test(s)) return fb;
  return [parseInt(s.slice(0, 2), 16), parseInt(s.slice(2, 4), 16), parseInt(s.slice(4, 6), 16)];
}
export const hexOf = c => c.map(v => Math.round(v).toString(16).padStart(2, '0')).join('').toUpperCase();
export const rgba = (c, a = 1) => `rgba(${c[0] | 0},${c[1] | 0},${c[2] | 0},${a})`;
export const css = c => '#' + hexOf(c);

// 与桌面版 GDI 气泡同一套：按比例混向白 / 黑，截断取整
export function mix(a, b, t) {
  t = Math.max(0, Math.min(1, t));
  return [0, 1, 2].map(i => Math.trunc(a[i] + (b[i] - a[i]) * t));
}
export const lighten = (c, t) => mix(c, [255, 255, 255], t);
export const darken = (c, t) => mix(c, [0, 0, 0], t);
export const lum = c => (0.299 * c[0] + 0.587 * c[1] + 0.114 * c[2]) / 255;
export const inkFor = c => (lum(c) > 0.62 ? darken(c, 0.72) : lighten(c, 0.90));

// 设置面板主题：WCAG 对比度
const lin = v => { v /= 255; return v <= 0.03928 ? v / 12.92 : Math.pow((v + 0.055) / 1.055, 2.4); };
export const relLum = c => 0.2126 * lin(c[0]) + 0.7152 * lin(c[1]) + 0.0722 * lin(c[2]);
export function contrast(a, b) {
  let la = relLum(a), lb = relLum(b);
  if (la < lb) [la, lb] = [lb, la];
  return (la + 0.05) / (lb + 0.05);
}
export function darkenTo(c, bg, want) {
  for (let f = 1.0; f > 0.18; f -= 0.02) {
    const t = c.map(v => Math.round(v * f));
    if (contrast(t, bg) >= want) return t;
  }
  return [0x1A, 0x1D, 0x21];
}
export const tint = (c, a) => c.map(v => Math.round(v + (255 - v) * Math.max(0, Math.min(1, a))));
export const shade = (c, t) => (t >= 0 ? tint(c, t) : c.map(v => Math.round(v * (1 + t))));
export const signalInk = c => (0.299 * c[0] + 0.587 * c[1] + 0.114 * c[2] > 140 ? [0x16, 0x16, 0x16] : [255, 255, 255]);

export function fmtSec(s) {
  s = Math.max(0, Math.floor(s));
  if (s < 60) return s + ' 秒';
  if (s < 3600) return Math.floor(s / 60) + ' 分 ' + (s % 60) + ' 秒';
  return Math.floor(s / 3600) + ' 小时 ' + Math.floor((s % 3600) / 60) + ' 分';
}
export const pad2 = n => String(n).padStart(2, '0');
export const dayKey = (d = new Date()) => `${d.getFullYear()}-${pad2(d.getMonth() + 1)}-${pad2(d.getDate())}`;
export const now = () => performance.now();

export function el(tag, attrs, ...kids) {
  const e = document.createElement(tag);
  if (attrs) {
    for (const [k, v] of Object.entries(attrs)) {
      if (v == null || v === false) continue;
      if (k === 'class') e.className = v;
      else if (k === 'style' && typeof v === 'object') Object.assign(e.style, v);
      else if (k.startsWith('on')) e.addEventListener(k.slice(2), v);
      else if (k === 'text') e.textContent = v;
      else if (k === 'html') e.innerHTML = v;
      else e.setAttribute(k, v === true ? '' : v);
    }
  }
  for (const k of kids.flat()) if (k != null && k !== false) e.append(k.nodeType ? k : document.createTextNode(String(k)));
  return e;
}
export const svg = (d, size = 16, sw = 1.2) =>
  `<svg viewBox="0 0 16 16" width="${size}" height="${size}" fill="none" stroke="currentColor" stroke-width="${sw}" stroke-linejoin="round" stroke-linecap="round"><path d="${d}"/></svg>`;

export function loadImage(src) {
  return new Promise((res, rej) => {
    const im = new Image();
    im.decoding = 'async';
    im.onload = () => res(im);
    im.onerror = () => rej(new Error('图片读取失败'));
    im.src = src;
  });
}

// ---- IndexedDB ----
let _db;
function db() {
  if (_db) return _db;
  _db = new Promise((res, rej) => {
    const r = indexedDB.open('qpet', 2);
    r.onupgradeneeded = () => {
      for (const s of ['chars', 'anims', 'frames', 'voices']) if (!r.result.objectStoreNames.contains(s)) r.result.createObjectStore(s);
    };
    r.onsuccess = () => res(r.result);
    r.onerror = () => rej(r.error);
  });
  return _db;
}
async function tx(store, mode, fn) {
  const d = await db();
  return new Promise((res, rej) => {
    const t = d.transaction(store, mode);
    const out = fn(t.objectStore(store));
    t.oncomplete = () => res(out && 'result' in out ? out.result : undefined);
    t.onerror = () => rej(t.error);
  });
}
export const idbGet = (s, k) => tx(s, 'readonly', st => st.get(k));
export const idbPut = (s, k, v) => tx(s, 'readwrite', st => st.put(v, k));
export const idbDel = (s, k) => tx(s, 'readwrite', st => st.delete(k));
export const idbKeys = s => tx(s, 'readonly', st => st.getAllKeys());
export async function idbClear() {
  for (const s of ['chars', 'anims', 'frames', 'voices']) await tx(s, 'readwrite', st => st.clear());
}
