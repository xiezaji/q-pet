// 配置（localStorage）+ 角色包（内置 + IndexedDB 导入）+ 自定义动画 / 气泡外框
import { idbGet, idbPut, idbDel, idbKeys, loadImage } from './util.js';

export const VERSION = '2.0.0';
export const BUILD = '2026-10-27';

export const DEFAULTS = {
  charId: 'furina',
  heights: {},
  amps: {},
  showPet: true,
  hideFullscreen: true,
  themeColor: '005FB8',
  bubbleH: 110,
  bubbleEveryMs: 600000,
  bubbleFont: 'YouYuan',
  bubbleColor: '2F3542',
  bubbleInk: 'auto',
  bubbleFontSize: 10.5,
  bubbleStyle: 'comic',
  bubbleFrame: '',
  volume: 80,
  clickVoice: true,
  clickLineMode: 'random',
  sfxGroup: 'auto',
  animIdle: true, animPress: true, animClick: true, animDrag: true,
  speedIdle: 100, speedPress: 100, speedClick: 100, speedDrag: 100,
};

const KEY = 'qpet.cfg.v1';
export const clone = o => JSON.parse(JSON.stringify(o));

export function loadCfg() {
  let c = {};
  try { c = JSON.parse(localStorage.getItem(KEY) || '{}'); } catch (_) { c = {}; }
  return Object.assign(clone(DEFAULTS), c);
}
export function saveCfg(c) {
  try { localStorage.setItem(KEY, JSON.stringify(c)); } catch (_) { /* 隐私模式写不进去就算了 */ }
}

export const POS_KEY = 'qpet.pos.v1';
export function loadPos() {
  try { const p = JSON.parse(localStorage.getItem(POS_KEY) || 'null'); if (p && isFinite(p.x) && isFinite(p.y)) return p; } catch (_) {}
  return null;
}
export function savePos(p) { try { localStorage.setItem(POS_KEY, JSON.stringify(p)); } catch (_) {} }

export const bus = new EventTarget();
export const emit = (name, detail) => bus.dispatchEvent(new CustomEvent(name, { detail }));
export const on = (name, fn) => bus.addEventListener(name, e => fn(e.detail));

// ---------------- 角色 ----------------
export const chars = new Map();

function fromBuiltin(id, j) {
  const base = `assets/chars/${id}/`;
  return {
    id, name: j.name, builtin: true,
    imageUrl: base + j.image, imageW: j.imageW, imageH: j.imageH,
    charH: j.charH || 219, amp: j.amp || { idle: 1, press: 1, click: 1, drag: 1 },
    press: j.press ? base + j.press : '', release: j.release ? base + j.release : '',
    voices: (j.voices || []).map(v => ({ text: v.text, url: base + v.file })),
    lines: j.lines || [], img: null,
  };
}

function fromRecord(id, r) {
  const voices = (r.voices || []).map(v => ({ text: v.text, url: URL.createObjectURL(v.blob), blob: v.blob }));
  return {
    id, name: r.name, builtin: false,
    imageUrl: URL.createObjectURL(r.image), imageW: r.imageW, imageH: r.imageH,
    charH: r.charH || 219, amp: { idle: 1, press: 1, click: 1, drag: 1 },
    press: voices[0] ? voices[0].url : '', release: voices[1] ? voices[1].url : '',
    voices, lines: voices.map(v => v.text), img: null, created: r.created || 0,
  };
}

export async function loadChars() {
  const idx = await fetch('assets/chars/index.json').then(r => r.json());
  const list = await Promise.all(idx.map(c => fetch(`assets/chars/${c.id}/char.json`).then(r => r.json())));
  list.forEach((j, i) => chars.set(idx[i].id, fromBuiltin(idx[i].id, j)));
  try {
    for (const k of await idbKeys('chars')) {
      const r = await idbGet('chars', k);
      if (r) chars.set(k, fromRecord(k, r));
    }
  } catch (_) { /* 没有 IndexedDB 就只用内置角色 */ }
  for (const [id, ch] of chars) {
    const v = await loadVoiceOverride(id);
    if (v) ch.voices = v;
  }
}

// ---------------- 台词库 ----------------
// 内置角色的语音是资源文件，改台词 / 删条目 / 新增语音都写一份副本到 IndexedDB，
// 原文件不动；副本里的条目要么引用资源路径，要么带着新导入的 blob。
async function loadVoiceOverride(charId) {
  let rec = null;
  try { rec = await idbGet('voices', charId); } catch (_) { return null; }
  if (!Array.isArray(rec)) return null;
  return rec.map(v => ({ text: v.text || '', url: v.blob ? URL.createObjectURL(v.blob) : v.url, blob: v.blob }));
}

export async function saveVoices(charId, voices) {
  const rec = voices.map(v => (v.blob ? { text: v.text, blob: v.blob } : { text: v.text, url: v.url }));
  await idbPut('voices', charId, rec);
}

export async function ensureImage(ch) {
  if (!ch.img) ch.img = await loadImage(ch.imageUrl);
  return ch.img;
}

export async function addChar(name, imageFile, voiceFiles) {
  const img = await loadImage(URL.createObjectURL(imageFile));
  const voices = voiceFiles
    .filter(f => /\.(wav|mp3|ogg|m4a|flac|aac|opus)$/i.test(f.name))
    .sort((a, b) => a.name.localeCompare(b.name, 'zh'))
    .map(f => ({ text: f.name.replace(/\.[^.]+$/, ''), blob: f }));
  const id = 'u' + Date.now().toString(36);
  const rec = { name, image: imageFile, imageW: img.naturalWidth, imageH: img.naturalHeight, charH: 219, voices, created: Date.now() };
  await idbPut('chars', id, rec);
  const ch = fromRecord(id, rec);
  ch.img = img;
  chars.set(id, ch);
  return ch;
}

export async function deleteChar(id) {
  await idbDel('chars', id);
  for (const s of ['idle', 'press', 'click', 'drag']) await idbDel('anims', `${id}|${s}`).catch(() => {});
  await idbDel('voices', id).catch(() => {});
  chars.delete(id);
}

export const heightOf = (cfg, ch) => (cfg.heights && cfg.heights[ch.id]) || ch.charH;
export const ampOf = (cfg, ch) => Object.assign({}, ch.amp, (cfg.amps && cfg.amps[ch.id]) || {});

// ---------------- 自定义动画 ----------------
const animCache = new Map();
export async function getCustomAnim(charId, slot) {
  const k = `${charId}|${slot}`;
  if (animCache.has(k)) return animCache.get(k);
  let frames = null;
  try {
    const blobs = await idbGet('anims', k);
    if (blobs && blobs.length) frames = await Promise.all(blobs.map(b => createImageBitmap(b)));
  } catch (_) { frames = null; }
  animCache.set(k, frames);
  return frames;
}
export async function setCustomAnim(charId, slot, blobs) {
  const k = `${charId}|${slot}`;
  if (blobs) await idbPut('anims', k, blobs); else await idbDel('anims', k);
  animCache.delete(k);
  emit('anims', { charId, slot });
}
export async function customAnims(charId) {
  const out = {};
  for (const s of ['idle', 'press', 'click', 'drag']) out[s] = await getCustomAnim(charId, s);
  return out;
}

// ---------------- 自定义气泡外框 ----------------
const frameCache = new Map();
export async function listFrames() {
  try {
    const keys = await idbKeys('frames');
    const out = [];
    for (const k of keys) {
      const r = await idbGet('frames', k);
      out.push({ id: k, name: (r && r.name) || '', img: await getFrame(k) });
    }
    return out.filter(f => f.img);
  } catch (_) { return []; }
}
export async function getFrame(id) {
  if (!id) return null;
  if (frameCache.has(id)) return frameCache.get(id);
  let img = null;
  try { const r = await idbGet('frames', id); if (r) img = await loadImage(URL.createObjectURL(r.blob)); } catch (_) {}
  frameCache.set(id, img);
  return img;
}
export async function addFrame(file) {
  const id = 'f' + Date.now().toString(36);
  await idbPut('frames', id, { name: file.name, blob: file });
  return id;
}
export async function deleteFrame(id) { await idbDel('frames', id); frameCache.delete(id); }
