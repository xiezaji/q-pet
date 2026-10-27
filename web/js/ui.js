// 设置窗口用到的控件：开关、滑块、下拉、按钮、对话框、取色、菜单、色块、树图
import { el, parseHex, hexOf, css, darkenTo, tint, shade, relLum, signalInk } from './util.js';

const $menu = () => document.getElementById('menu-layer');
const $dlg = () => document.getElementById('dlg-layer');

// ---------------- 主题 ----------------
export function applyTheme(hex) {
  const c = parseHex(hex, [0x00, 0x5F, 0xB8]);
  const white = [255, 255, 255], desk = [0xE1, 0xE5, 0xEA];
  const solid = darkenTo(c, white, 4.5);
  const deep = relLum(solid) < 0.05;
  const s = document.documentElement.style;
  s.setProperty('--accent', css(c));
  s.setProperty('--accent-solid', css(solid));
  s.setProperty('--accent-edge', css(darkenTo(c, white, 8.5)));
  s.setProperty('--accent-ink', css(darkenTo(c, desk, 4.5)));
  s.setProperty('--accent-soft', css(tint(c, 0.90)));
  s.setProperty('--accent-hover', css(shade(solid, deep ? 0.15 : -0.18)));
  s.setProperty('--accent-press', css(shade(solid, deep ? 0.28 : -0.34)));
  s.setProperty('--on-accent', css(signalInk(c)));
  return solid;
}
export const accentSolid = () => parseHex(getComputedStyle(document.documentElement).getPropertyValue('--accent-solid'), [0x00, 0x5F, 0xB8]);

// ---------------- 小控件 ----------------
export const button = (text, kind = 'secondary', onclick, attrs = {}) =>
  el('button', Object.assign({ class: `btn ${kind}`, type: 'button', onclick }, attrs), text);

export function toggle(label, checked, onChange) {
  const inp = el('input', { type: 'checkbox' });
  inp.checked = !!checked;
  const root = el('label', { class: 'tg' }, inp, el('span', { class: 'tr' }), label ? el('span', { text: label }) : null);
  inp.addEventListener('change', () => onChange && onChange(inp.checked));
  return {
    el: root,
    get value() { return inp.checked; },
    set value(v) { inp.checked = !!v; },
    set disabled(d) { inp.disabled = !!d; root.classList.toggle('disabled', !!d); },
  };
}

export function slider({ min, max, step = 1, value, width, onInput, onChange }) {
  const s = el('input', { type: 'range', class: 'sl', min, max, step });
  if (width) s.style.width = width + 'px';
  const paint = () => s.style.setProperty('--p', ((s.value - min) / (max - min) * 100) + '%');
  s.set = v => { s.value = v; paint(); };
  s.set(value);
  s.addEventListener('input', () => { paint(); onInput && onInput(+s.value); });
  s.addEventListener('change', () => onChange && onChange(+s.value));
  return s;
}

const CHEVRON = '<svg width="10.4" height="6.2" viewBox="-1 -1 10.4 6.2"><polyline points="0,0 4.2,4.2 8.4,0" fill="none" stroke="currentColor" stroke-width="1.4" stroke-linejoin="round"/></svg>';

// ---------------- 弹出层（下拉 / 右键菜单共用，一次只开一个） ----------------
let openPop = null;
export function closePop() {
  if (!openPop) return;
  const p = openPop;
  openPop = null;
  p.node.remove();
  p.onClose && p.onClose();
}
function mountPop(node, onClose) {
  closePop();
  $menu().append(node);
  openPop = { node, onClose };
}
addEventListener('pointerdown', e => { if (openPop && !openPop.node.contains(e.target)) closePop(); }, true);
addEventListener('keydown', e => { if (e.key === 'Escape' && openPop) { e.stopPropagation(); closePop(); } }, true);
addEventListener('resize', closePop);
addEventListener('blur', closePop);

function placeNear(node, x, y, alignW) {
  const r = node.getBoundingClientRect();
  if (x + r.width > innerWidth - 4) x = Math.max(4, (alignW != null ? alignW : innerWidth - 4) - r.width);
  if (y + r.height > innerHeight - 4) y = Math.max(4, y - r.height);
  node.style.left = x + 'px';
  node.style.top = y + 'px';
}

export function menu(x, y, items) {
  const m = el('div', { class: 'menu', role: 'menu' });
  for (const it of items) {
    if (it === '-') { m.append(el('div', { class: 'menu-sep' })); continue; }
    m.append(el('div', {
      class: 'menu-item' + (it.danger ? ' danger' : '') + (it.disabled ? ' disabled' : ''), role: 'menuitem',
      onclick: () => { closePop(); it.onClick && it.onClick(); },
    }, it.label));
  }
  m.style.left = x + 'px'; m.style.top = y + 'px';
  m.addEventListener('contextmenu', e => e.preventDefault());
  mountPop(m);
  placeNear(m, x, y);
  return m;
}

// items: [{ value, label, disabled, font }]
export function combo(items, value, onChange, { width, editable = false, cls = '', title } = {}) {
  let cur = value, list = items, closedAt = 0;
  const arr = el('span', { class: 'arr', html: CHEVRON });
  const tx = editable ? el('input', { class: 'tx', type: 'text' }) : el('span', { class: 'tx' });
  const box = el(editable ? 'div' : 'button', { class: 'combo ' + cls, type: editable ? null : 'button', title }, tx, el('span', { class: 'sep' }), arr);
  if (width) box.style.width = width + 'px';
  const labelOf = v => { const it = list.find(i => String(i.value) === String(v)); return it ? (it.display || it.label) : String(v ?? ''); };
  const paint = () => {
    if (editable) tx.value = labelOf(cur);
    else {
      tx.textContent = labelOf(cur);
      const it = list.find(i => String(i.value) === String(cur));
      tx.style.fontFamily = it && it.font ? it.font : '';
    }
  };
  paint();
  const pick = v => {
    const changed = String(v) !== String(cur);
    cur = v; paint();
    if (changed && onChange) onChange(v);
  };
  const open = () => {
    if (performance.now() - closedAt < 250) return;
    const r = box.getBoundingClientRect();
    const pop = el('div', { class: 'pop', role: 'listbox' });
    pop.style.minWidth = r.width + 'px';
    let selNode = null;
    for (const it of list) {
      const n = el('div', {
        class: 'pop-item' + (String(it.value) === String(cur) ? ' sel' : '') + (it.disabled ? ' disabled' : ''),
        onclick: () => { closePop(); pick(it.value); },
      }, it.label);
      if (it.font) n.style.fontFamily = it.font;
      if (String(it.value) === String(cur)) selNode = n;
      pop.append(n);
    }
    pop.style.left = r.left + 'px'; pop.style.top = (r.bottom + 3) + 'px';
    box.classList.add('open');
    mountPop(pop, () => { box.classList.remove('open'); closedAt = performance.now(); });
    const pr = pop.getBoundingClientRect();
    if (pr.bottom > innerHeight - 4) pop.style.top = Math.max(4, r.top - 3 - pr.height) + 'px';
    if (pr.right > innerWidth - 4) pop.style.left = Math.max(4, innerWidth - 4 - pr.width) + 'px';
    if (selNode) selNode.scrollIntoView({ block: 'nearest' });
  };
  if (editable) {
    arr.addEventListener('click', open);
    arr.style.cursor = 'pointer';
    const commit = () => pick(tx.value.trim());
    tx.addEventListener('change', commit);
    tx.addEventListener('keydown', e => { if (e.key === 'Enter') { commit(); tx.blur(); } });
  } else {
    box.addEventListener('click', open);
    box.addEventListener('keydown', e => {
      if (e.key !== 'ArrowDown' && e.key !== 'ArrowUp') return;
      e.preventDefault();
      const en = list.filter(i => !i.disabled);
      let i = en.findIndex(x => String(x.value) === String(cur));
      i = Math.max(0, Math.min(en.length - 1, i + (e.key === 'ArrowDown' ? 1 : -1)));
      if (en[i]) pick(en[i].value);
    });
  }
  return {
    el: box,
    get value() { return cur; },
    set value(v) { cur = v; paint(); },
    setItems(it) { list = it; paint(); },
    set disabled(d) { box.classList.toggle('disabled', !!d); if (!editable) box.disabled = !!d; else tx.disabled = !!d; },
  };
}

// ---------------- 对话框 ----------------
export function ask({ title, body = '', ok = '好', cancel = null, danger = false }) {
  return new Promise(res => {
    const prev = document.activeElement;
    const mask = el('div', { class: 'dlg-mask' });
    const done = v => { mask.remove(); removeEventListener('keydown', onKey, true); if (prev && prev.focus) prev.focus(); res(v); };
    const okBtn = button(ok, danger ? 'danger' : 'primary', () => done(true));
    const cBtn = cancel ? button(cancel, 'secondary', () => done(false)) : null;
    const card = el('div', { class: 'dlg', role: 'dialog', 'aria-modal': 'true' },
      el('div', { class: 'hd' },
        el('div', { class: 'badge' + (danger ? ' danger' : '') }, danger ? '!' : 'i'),
        el('div', null, el('div', { class: 'tt' }, title), body ? el('div', { class: 'bd' }, body) : null)),
      el('div', { class: 'ft' }, cBtn, okBtn));
    mask.append(card);
    const onKey = e => {
      if (e.key === 'Escape') { e.preventDefault(); e.stopPropagation(); done(false); }
      else if (e.key === 'Enter' && !danger && !(e.target instanceof HTMLButtonElement)) { e.preventDefault(); done(true); }
      else e.stopPropagation();
    };
    addEventListener('keydown', onKey, true);
    $dlg().append(mask);
    (danger && cBtn ? cBtn : okBtn).focus();
  });
}
export const info = (title, body) => ask({ title, body, ok: '好' });

export function colorDlg(hex, title = '选择颜色') {
  return new Promise(res => {
    let c = parseHex(hex, [0x00, 0x5F, 0xB8]);
    const prev = el('div', { class: 'cprev' });
    const hexIn = el('input', { class: 'input', maxlength: 6, spellcheck: 'false' });
    const rows = [], sls = [];
    const paint = (fromHex) => {
      prev.style.background = css(c);
      sls.forEach((s, i) => { s.set(c[i]); rows[i].textContent = c[i]; });
      if (!fromHex) hexIn.value = hexOf(c);
    };
    const lines = ['R 红', 'G 绿', 'B 蓝'].map((lb, i) => {
      const v = el('span', { class: 'v' });
      const s = slider({ min: 0, max: 255, value: c[i], onInput: x => { c[i] = x; paint(); } });
      rows.push(v); sls.push(s);
      return el('div', { class: 'crow' }, el('span', null, lb), s, v);
    });
    hexIn.addEventListener('input', () => {
      const t = hexIn.value.replace(/[^0-9a-fA-F]/g, '').slice(0, 6);
      if (t !== hexIn.value) hexIn.value = t;
      if (t.length === 6) { c = parseHex(t); paint(true); }
    });
    const mask = el('div', { class: 'dlg-mask' });
    const done = v => { mask.remove(); removeEventListener('keydown', onKey, true); res(v); };
    mask.append(el('div', { class: 'dlg cdlg', role: 'dialog' },
      el('div', { class: 'tt' }, title),
      prev, ...lines,
      el('div', { class: 'chex' }, el('span', { style: { fontWeight: 700 } }, '#'), hexIn),
      el('div', { class: 'ft' }, button('取消', 'secondary', () => done(null)), button('确定', 'primary', () => done(hexOf(c))))));
    const onKey = e => {
      if (e.key === 'Escape') { e.preventDefault(); done(null); }
      else if (e.key === 'Enter') { e.preventDefault(); done(hexOf(c)); }
      e.stopPropagation();
    };
    addEventListener('keydown', onKey, true);
    $dlg().append(mask);
    paint();
    hexIn.focus(); hexIn.select();
  });
}

// ---------------- 色块 ----------------
export const PALETTE = [
  ['8FA6D8', '默认蓝'], ['005FB8', '群青'], ['3B9BE0', '天蓝'], ['2FA8A0', '青绿'],
  ['5FA855', '草绿'], ['C8A227', '芥黄'], ['E08A3C', '落日'], ['E06C8B', '玫粉'],
  ['9B7BD8', '紫罗兰'], ['6B7280', '石墨'], ['3F4A5A', '深灰蓝'], ['2F3542', '墨黑'],
];
export const INKS = [
  ['auto', '自动'], ['1A1A1A', '纯黑'], ['FFFFFF', '纯白'], ['203170', '深蓝'],
  ['C2426B', '玫粉'], ['8A6D1F', '芥黄'], ['2E6B4F', '墨绿'], ['6B4E8A', '紫褐'],
];
export function swatches(list, value, onPick) {
  const wrap = el('div', { class: 'sw-wrap' });
  const set = v => {
    for (const b of wrap.children) b.classList.toggle('sel', String(b.dataset.v).toUpperCase() === String(v).toUpperCase());
  };
  for (const [hex, name] of list) {
    const chip = el('i');
    chip.style.background = hex === 'auto' ? 'linear-gradient(135deg, #FFFFFF 0 50%, #333333 50% 100%)' : '#' + hex;
    wrap.append(el('button', {
      class: 'sw', type: 'button', 'data-v': hex, title: hex === 'auto' ? name : `${name} #${hex}`,
      onclick: () => { set(hex); onPick(hex); },
    }, chip, el('span', null, name)));
  }
  set(value);
  return { el: wrap, set };
}

// ---------------- 文件选择 ----------------
export function pickFiles({ accept = '', multiple = false, dir = false } = {}) {
  return new Promise(res => {
    const inp = el('input', { type: 'file', accept, multiple: multiple || dir || null });
    if (dir) inp.webkitdirectory = true;
    inp.style.display = 'none';
    document.body.append(inp);
    const done = v => { inp.remove(); res(v); };
    inp.addEventListener('change', () => done([...inp.files]));
    inp.addEventListener('cancel', () => done([]));
    inp.click();
  });
}

// ---------------- 树图（squarify） ----------------
export function squarify(vals, x, y, w, h) {
  const out = vals.map(() => ({ x, y, w: 0, h: 0 }));
  const total = vals.reduce((a, b) => a + b, 0);
  if (!total || w <= 0 || h <= 0) return out;
  const scale = w * h / total;
  let rest = vals.map((_, i) => i).filter(i => vals[i] > 0).sort((a, b) => vals[b] - vals[a]);
  while (rest.length) {
    const side = Math.min(w, h);
    const worst = r => {
      let s = 0, mx = 0, mn = Infinity;
      for (const j of r) { const a = vals[j] * scale; s += a; mx = Math.max(mx, a); mn = Math.min(mn, a); }
      return Math.max(side * side * mx / (s * s), (s * s) / (side * side * mn));
    };
    let row = [rest[0]], best = worst(row);
    for (let i = 1; i < rest.length; i++) {
      const r2 = row.concat(rest[i]), w2 = worst(r2);
      if (w2 > best) break;
      row = r2; best = w2;
    }
    const sum = row.reduce((a, j) => a + vals[j] * scale, 0);
    if (w >= h) {
      const cw = sum / h; let cy = y;
      for (const j of row) { const ch = vals[j] * scale / cw; out[j] = { x, y: cy, w: cw, h: ch }; cy += ch; }
      x += cw; w -= cw;
    } else {
      const rh = sum / w; let cx = x;
      for (const j of row) { const cw = vals[j] * scale / rh; out[j] = { x: cx, y, w: cw, h: rh }; cx += cw; }
      y += rh; h -= rh;
    }
    rest = rest.slice(row.length);
  }
  return out;
}
