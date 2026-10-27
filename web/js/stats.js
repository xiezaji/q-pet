// 使用统计：只记这个页面里的时长与按键，存在 localStorage，每 4 秒落一次
import { dayKey } from './util.js';
import { emit } from './store.js';

const PREFIX = 'qpet.stats.';
const ACTIVE_MS = 300000;
let today = dayKey();
let cur = read(today);
let lastInput = Date.now();
let savedAt = 0;
let dirty = false;

function blank() { return { screen: 0, active: 0, idle: 0, keys: {}, chars: {} }; }
function read(day) {
  try {
    const o = JSON.parse(localStorage.getItem(PREFIX + day) || 'null');
    if (o) return Object.assign(blank(), o);
  } catch (_) {}
  return blank();
}
function flush() {
  if (!dirty) return;
  try { localStorage.setItem(PREFIX + today, JSON.stringify(cur)); } catch (_) {}
  dirty = false;
  savedAt = Date.now();
  emit('stats', null);
}

export function days() {
  const out = new Set([today]);
  for (let i = 0; i < localStorage.length; i++) {
    const k = localStorage.key(i);
    if (k && k.startsWith(PREFIX)) out.add(k.slice(PREFIX.length));
  }
  return [...out].sort().reverse();
}
export const load = day => (day === today ? cur : read(day));
export const lastSaved = () => savedAt;
export const touch = () => { lastInput = Date.now(); };

export function start(getCharId) {
  addEventListener('keydown', e => {
    touch();
    if (e.repeat) return;
    const k = e.code || e.key;
    cur.keys[k] = (cur.keys[k] || 0) + 1;
    dirty = true;
  }, true);
  for (const ev of ['pointerdown', 'pointermove', 'wheel']) addEventListener(ev, touch, { capture: true, passive: true });

  setInterval(() => {
    const d = dayKey();
    if (d !== today) { flush(); today = d; cur = read(d); }
    if (document.visibilityState !== 'visible') return;
    cur.screen++;
    if (Date.now() - lastInput < ACTIVE_MS) cur.active++; else cur.idle++;
    const id = getCharId();
    if (id) cur.chars[id] = (cur.chars[id] || 0) + 1;
    dirty = true;
  }, 1000);
  setInterval(flush, 4000);
  addEventListener('pagehide', flush);
  document.addEventListener('visibilitychange', flush);
}

export function clearDay() {
  try { localStorage.removeItem(PREFIX + today); } catch (_) {}
  cur = blank();
  dirty = false;
  emit('stats', null);
}

export function clearAll() {
  const ks = [];
  for (let i = 0; i < localStorage.length; i++) { const k = localStorage.key(i); if (k && k.startsWith(PREFIX)) ks.push(k); }
  ks.forEach(k => localStorage.removeItem(k));
  cur = blank();
  dirty = false;
}
