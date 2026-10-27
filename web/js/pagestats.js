// 使用统计页：日期 / 概览 / 键盘热力图 / 角色陪伴树图 / 导出
import { el, mix, contrast, fmtSec, pad2, dayKey } from './util.js';
import { chars, bus } from './store.js';
import * as stats from './stats.js';
import { button, combo, ask, accentSolid, squarify } from './ui.js';

const U = 4;
const KB = [
  [['Backquote', '`'], ...'1234567890'.split('').map(c => ['Digit' + c, c]), ['Minus', '-'], ['Equal', '='], ['Backspace', '←', 2]],
  [['Tab', 'Tab', 1.5], ...'QWERTYUIOP'.split('').map(c => ['Key' + c, c]), ['BracketLeft', '['], ['BracketRight', ']'], ['Backslash', '\\', 1.5]],
  [['CapsLock', 'Caps', 1.75], ...'ASDFGHJKL'.split('').map(c => ['Key' + c, c]), ['Semicolon', ';'], ['Quote', "'"], ['Enter', 'Enter', 2.25]],
  [['ShiftLeft', 'Shift', 2.25], ...'ZXCVBNM'.split('').map(c => ['Key' + c, c]), ['Comma', ','], ['Period', '.'], ['Slash', '/'], ['ShiftRight', 'Shift', 2.75]],
  [['ControlLeft', 'Ctrl', 1.25], ['MetaLeft', 'Win', 1.25], ['AltLeft', 'Alt', 1.25], ['Space', '', 6.25],
    ['AltRight', 'Alt', 1.25], ['MetaRight', 'Win', 1.25], ['ContextMenu', 'Menu', 1.25], ['ControlRight', 'Ctrl', 1.25]],
];
const BASE = [0xEF, 0xF2, 0xF6], DARK = [0x1A, 0x1D, 0x21], WHITE = [255, 255, 255];
const inkOn = c => (contrast(WHITE, c) >= contrast(DARK, c) ? '#fff' : '#1A1D21');
const rgb = c => `rgb(${c[0]},${c[1]},${c[2]})`;
function fmtShort(s) {
  s = Math.floor(s);
  if (s < 60) return s + ' 秒';
  if (s < 3600) return Math.floor(s / 60) + ' 分';
  return Math.floor(s / 3600) + ' 时 ' + Math.floor((s % 3600) / 60) + ' 分';
}
const cnDate = k => { const [y, m, d] = k.split('-').map(Number); return `${y}年${m}月${d}日`; };

export function pageStats() {
  let day = dayKey();
  const ov = [el('div', { class: 'v' }), el('div', { class: 'v' }), el('div', { class: 'v' })];
  const live = el('span', { class: 'r' });
  const upd = el('div', { class: 'note', style: { marginTop: '10px' } });
  const kb = el('div', { class: 'kb' });
  const keyNodes = new Map();
  for (const r of KB) for (const [code, label, u = 1] of r) {
    const n = el('div', { class: 'key', style: { gridColumn: `span ${Math.round(u * U)}` } }, label);
    keyNodes.set(code, n);
    kb.append(n);
  }
  const keyTotal = el('span', { class: 'r' });
  const legend = el('i');
  const tin = el('div', { class: 'in' });
  const tail = el('div', { class: 'tail' });
  const appTile = el('div', { class: 'in' });
  const dateNote = el('span', { class: 'note' });

  const dateItems = () => {
    const have = new Set(stats.days()), out = [], t = new Date();
    for (let i = 0; i < 30; i++) {
      const d = new Date(t.getFullYear(), t.getMonth(), t.getDate() - i), k = dayKey(d);
      const ok = i === 0 || have.has(k);
      const lb = (i === 0 ? '今天 │ ' : '') + cnDate(k);
      out.push({ value: k, label: lb + (ok ? '' : '  │  无记录'), display: lb, disabled: !ok });
    }
    const n = [...have].filter(k => k !== dayKey()).length;
    dateNote.textContent = n ? `共 ${n + 1} 天可查` : '今天还没有历史记录';
    return out;
  };
  const date = combo(dateItems(), day, v => { day = v; paint(); }, { width: 220, cls: 'datebtn' });

  function paintTree(data) {
    const r = tin.getBoundingClientRect();
    const all = Object.entries(data.chars || {}).filter(([, s]) => s > 0).sort((a, b) => b[1] - a[1]);
    const total = all.reduce((a, [, s]) => a + s, 0);
    if (!total) { tin.replaceChildren(el('div', { class: 'empty' }, '还没有陪伴记录')); tail.textContent = ''; return; }
    const shown = all.filter(([, s]) => s / total >= 0.012).slice(0, 8);
    const rest = all.length - shown.length;
    const solid = accentSolid(), top = shown[0][1];
    const rects = squarify(shown.map(([, s]) => s), 0, 0, r.width, r.height);
    tin.replaceChildren(...shown.map(([id, s], i) => {
      const q = rects[i], ch = chars.get(id);
      const bg = mix([0xF1, 0xF4, 0xF8], solid, 0.10 + 0.72 * Math.pow(s / top, 0.45));
      const big = q.w > 120 && q.h > 70;
      return el('div', {
        class: 'tm', title: `${ch ? ch.name : id} · ${fmtSec(s)}`,
        style: { left: (q.x + 1) + 'px', top: (q.y + 1) + 'px', width: Math.max(0, q.w - 2) + 'px', height: Math.max(0, q.h - 2) + 'px', background: rgb(bg), color: inkOn(bg) },
      }, big && ch ? el('img', { src: ch.imageUrl, alt: '' }) : null,
      q.w > 46 && q.h > 30 ? el('div', { class: 'nm' }, ch ? ch.name : '已删除的角色') : null,
      q.w > 70 && q.h > 46 ? el('div', { class: 'tm-v' }, `${fmtShort(s)} · ${Math.round(s / total * 100)}%`) : null);
    }));
    tail.textContent = `合计 ${fmtShort(total)}` + (rest > 0 ? `　│　另有 ${rest} 个角色不到 1.2%` : '');
  }

  function paint() {
    const data = stats.load(day), today = day === dayKey();
    ov[0].textContent = fmtSec(data.screen);
    ov[1].textContent = fmtSec(data.active);
    ov[2].textContent = fmtSec(data.idle);
    live.replaceChildren(today ? el('span', { class: 'live' }, el('i'), '每 4 秒刷新') : '历史记录（只读）');
    const t = stats.lastSaved();
    upd.textContent = today && t ? `更新于 ${new Date(t).toLocaleTimeString('zh-CN', { hour12: false })}` : '';

    const solid = accentSolid();
    const keys = data.keys || {};
    const max = Math.max(0, ...[...keyNodes.keys()].map(k => keys[k] || 0));
    let sum = 0;
    for (const v of Object.values(keys)) sum += v;
    for (const [code, n] of keyNodes) {
      const v = keys[code] || 0;
      if (!v) { n.style.background = ''; n.style.color = ''; n.style.borderColor = ''; n.title = `${n.textContent || 'Space'} · 0 次`; continue; }
      const c = mix(BASE, solid, 0.06 + 0.94 * Math.log(1 + v) / Math.log(1 + max));
      n.style.background = rgb(c); n.style.color = inkOn(c); n.style.borderColor = rgb(c);
      n.title = `${n.textContent || 'Space'} · ${v} 次`;
    }
    keyTotal.textContent = `共 ${sum} 次`;
    legend.style.background = `linear-gradient(90deg, ${rgb(mix(BASE, solid, 0.06))}, ${rgb(solid)})`;

    paintTree(data);
    appTile.replaceChildren(data.screen > 0
      ? el('div', { class: 'tm', style: { inset: '0', background: rgb(mix([0xF1, 0xF4, 0xF8], solid, 0.5)), color: inkOn(mix([0xF1, 0xF4, 0xF8], solid, 0.5)) } },
        el('div', { class: 'nm' }, 'Q Pet 网页版'), el('div', { class: 'tm-v' }, fmtShort(data.screen) + ' · 100%'))
      : el('div', { class: 'empty' }, '没有记录'));
  }

  const exportCsv = () => {
    const rows = [['日期', '屏幕使用(秒)', '活跃(秒)', '挂机(秒)', '按键次数', '角色陪伴']];
    for (const k of stats.days()) {
      const d = stats.load(k);
      const keys = Object.values(d.keys || {}).reduce((a, b) => a + b, 0);
      const cs = Object.entries(d.chars || {}).map(([id, s]) => `${(chars.get(id) || { name: id }).name}:${s}`).join('; ');
      rows.push([k, d.screen, d.active, d.idle, keys, cs]);
    }
    const csv = '\ufeff' + rows.map(r => r.map(v => /[",\n]/.test(String(v)) ? `"${String(v).replace(/"/g, '""')}"` : v).join(',')).join('\r\n');
    const a = el('a', { href: URL.createObjectURL(new Blob([csv], { type: 'text/csv' })), download: `qpet-stats-${dayKey()}.csv` });
    document.body.append(a); a.click(); a.remove();
    setTimeout(() => URL.revokeObjectURL(a.href), 2000);
  };
  const clear = async () => {
    if (!await ask({ title: '清空今天的统计？', body: '只清今天的，历史记录保留。', ok: '清空', cancel: '算了', danger: true })) return;
    stats.clearDay();
    day = dayKey(); date.setItems(dateItems()); date.value = day;
    paint();
  };

  const onStats = () => { if (day === dayKey()) paint(); };
  const onResize = () => paintTree(stats.load(day));
  const h2 = (t, sub) => el('div', null, el('div', { class: 'h2 s' }, t), sub ? el('div', { class: 'sub' }, sub) : null);

  return {
    el: [
      el('div', { class: 'card' }, el('div', { class: 'datebar' }, date.el, dateNote)),
      el('div', { class: 'card' },
        el('div', { class: 'cardhead' }, h2('概览', '页面开着并且在前台才算'), live),
        el('div', { class: 'ov' },
          el('div', null, el('div', { class: 'k' }, '屏幕使用'), ov[0]),
          el('div', null, el('div', { class: 'k' }, '活跃'), ov[1]),
          el('div', null, el('div', { class: 'k' }, '挂机（5 分钟没动）'), ov[2])),
        upd),
      el('div', { class: 'card' },
        el('div', { class: 'cardhead' }, h2('按键热力图', '只记这个页面有焦点时的按键，不记内容'), keyTotal),
        kb,
        el('div', { class: 'legend' }, '少', legend, '多')),
      el('div', { class: 'card' },
        h2('角色陪伴', '她在桌面上待了多久'),
        el('div', { class: 'tmap' }, tin), tail),
      el('div', { class: 'card' },
        h2('软件使用', '网页版只看得到这个页面本身；桌面版按前台程序统计'),
        el('div', { class: 'tmap', style: { height: '70px' } }, appTile)),
      el('div', { class: 'card' }, el('div', { class: 'btnrow' },
        button('立即刷新', 'secondary sm', () => { date.setItems(dateItems()); paint(); }),
        button('导出 CSV', 'secondary sm', exportCsv),
        button('清空统计', 'ghost sm', clear))),
    ],
    mounted() { paint(); bus.addEventListener('stats', onStats); addEventListener('resize', onResize); },
    dispose() { bus.removeEventListener('stats', onStats); removeEventListener('resize', onResize); },
  };
}
