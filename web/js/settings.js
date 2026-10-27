// 设置窗口外壳：标题栏 / 左摘要 / 中枢纽 / 右八页入口 / 页眉 / 底栏 / 保存与未保存
import { el, svg, pad2 } from './util.js';
import { chars, clone, saveCfg, heightOf, on, VERSION } from './store.js';
import { IDLE_N, PRESS_N, CLICK_N, DRAG_N } from './frames.js';
import { applyTheme, ask, button, closePop } from './ui.js';
import { PAGES, META } from './pages.js';

const ICONS = [
  'M2.5,3.5 H13.5 V11 H8.5 L5.5,13.5 V11 H2.5 Z',
  'M2.6,5.5 H13.4 M2.6,10.5 H13.4 M4.6,3.6 H8.2 V7.4 H4.6 Z M7.8,8.6 H11.4 V12.4 H7.8 Z',
  'M8,7.5 A2.4,2.4 0 1,1 8,2.7 A2.4,2.4 0 1,1 8,7.5 Z M3.8,13.5 C3.8,10.9 5.6,9.4 8,9.4 C10.4,9.4 12.2,10.9 12.2,13.5',
  'M2.5,4.5 H13.5 V11.5 H2.5 Z M5.5,4.5 V11.5 M10.5,4.5 V11.5',
  'M6,4.6 A2,2 0 0,1 10,4.6 V8.4 A2,2 0 0,1 6,8.4 Z M8,10.4 V13 M5.5,13 H10.5',
  'M8,2.6 L13.2,5.4 V10.6 L8,13.4 L2.8,10.6 V5.4 Z M2.8,5.4 L8,8.2 L13.2,5.4 M8,8.2 V13.4',
  'M4,12.4 V7.4 M8,12.4 V3.8 M12,12.4 V9.4 M2.5,14 H13.5',
  'M8,2.2 A5.8,5.8 0 1,1 8,13.8 A5.8,5.8 0 1,1 8,2.2 Z M8,7.2 V11 M8,5 V5.2',
];
const TILES = [
  ['bubble', '气泡', '字体 │ 颜色 │ 频率'],
  ['ui', '通用设置', '显示 │ 全屏 │ 主题色'],
  ['char', '形象与大小', '角色 │ 高度 │ 立绘'],
  ['anim', '动画', '待机 │ 点击 │ 拖动'],
  ['voice', '语音', '音量 │ 试听 │ 台词'],
  ['import', '导入', '角色包 │ 素材'],
  ['stats', '使用统计', '陪伴时长 │ 点击'],
  ['about', '关于', '版本 │ 许可'],
];
const ARROW = '<svg width="6.5" height="11" viewBox="-1 -1 6.5 11"><path d="M0,0 L4,4.5 L0,9" fill="none" stroke="currentColor" stroke-width="1.5"/></svg>';
const CAP_ICONS = {
  min: 'M3.5,8 H12.5',
  max: 'M3.5,3.5 H12.5 V12.5 H3.5 Z',
  restore: 'M3.5,5.5 H10.5 V12.5 H3.5 Z M5.5,5.5 V3.5 H12.5 V10.5 H10.5',
  close: 'M4,4 L12,12 M12,4 L4,12',
};
const LIVE_KEYS = ['showPet', 'hideFullscreen'];

function stable(o) {
  if (Array.isArray(o)) return '[' + o.map(stable).join(',') + ']';
  if (o && typeof o === 'object') return '{' + Object.keys(o).sort().map(k => JSON.stringify(k) + ':' + stable(o[k])).join(',') + '}';
  return JSON.stringify(o);
}
const strip = c => { const o = clone(c); LIVE_KEYS.forEach(k => delete o[k]); return o; };

export class Settings {
  constructor({ pet, cfg, layer, deskSize, taskBtn, onQuit }) {
    this.pet = pet;
    this.saved = clone(cfg);
    this.draft = clone(cfg);
    this.layer = layer;
    this.deskSize = deskSize;
    this.taskBtn = taskBtn;
    this.onQuit = onQuit;
    this.page = null;
    this.open = false;
    this.min = false;
    this.max = false;
    this.rect = null;
    this.z = 1; this.panX = 0; this.panY = 0;
    this.lastBubH = cfg.bubbleH > 0 ? cfg.bubbleH : 110;
    this.build();
    on('pos', () => this.open && this.refreshPet());
    on('petstate', () => this.open && this.refreshPet());
    addEventListener('resize', () => { if (this.open) this.place(); });
    setInterval(() => { if (this.open && !this.min) this.refreshDirty(); }, 600);
  }

  // ---------------- 供页面使用 ----------------
  ch() { return chars.get(this.draft.charId) || chars.get('furina') || chars.values().next().value; }
  H() { return heightOf(this.draft, this.ch()); }
  frameCount() {
    const p = this.pet;
    if (p.sprite && p.ch && p.ch.id === this.ch().id) return p.sprite.total;
    return IDLE_N + PRESS_N + CLICK_N + DRAG_N;
  }
  changed(key) {
    if (key === 'themeColor') applyTheme(this.draft.themeColor);
    if (key === 'bubbleH' && this.draft.bubbleH > 0) this.lastBubH = this.draft.bubbleH;
    this.refresh();
  }
  setLive(key, v) {
    this.draft[key] = v;
    this.saved[key] = v;
    saveCfg(this.saved);
    if (this.pet.cfg) this.pet.cfg[key] = v;
    if (key === 'showPet') { if (v && !this.pet.running) this.pet.setRunning(true); else this.pet.updateVisible(); }
    if (key === 'hideFullscreen') this.pet.checkFullscreen();
    this.refresh();
  }
  // 导入 / 删除角色这类「已经落盘」的操作：直接写进已保存配置并应用
  async commit(patch) {
    Object.assign(this.saved, clone(patch));
    Object.assign(this.draft, clone(patch));
    saveCfg(this.saved);
    await this.pet.apply(clone(this.saved));
    this.refresh();
  }
  rerender() { if (this.page) this.renderPage(true); }

  // ---------------- 构建 ----------------
  build() {
    const capBtn = (kind, fn, title) => {
      const b = el('button', { class: 'cap-btn' + (kind === 'close' ? ' close' : ''), type: 'button', title, html: svg(CAP_ICONS[kind], 14, 1), onclick: fn });
      return b;
    };
    this.capDot = el('i', { class: 'cap-dot' });
    this.capRun = el('span', { class: 'eyebrow', style: { marginLeft: '7px' } });
    this.capPos = el('span', { class: 'cap-val', style: { marginLeft: '9px' } });
    this.capChar = el('span', { class: 'cap-val' });
    this.capVoice = el('span', { class: 'cap-val' });
    this.back = el('button', { class: 'back-link', type: 'button', onclick: () => this.go(null) }, '角色枢纽 ›');
    this.maxBtn = capBtn('max', () => this.toggleMax(), '最大化');
    const cap = el('div', { class: 'cap' },
      el('img', { src: 'assets/icon.png', alt: '' }),
      el('span', { class: 'eyebrow', style: { marginLeft: '10px' } }, '// Q PET'),
      el('i', { class: 'vd s' }),
      el('span', { class: 'cap-title' }, '设置'),
      el('div', { class: 'cap-info' },
        el('i', { class: 'vd' }), this.capDot, this.capRun, this.capPos,
        el('i', { class: 'vd' }), el('span', { class: 'eyebrow' }, 'CHAR'), this.capChar,
        el('i', { class: 'vd' }), el('span', { class: 'eyebrow' }, 'VOICE'), this.capVoice),
      el('div', { class: 'cap-fill' }),
      this.back,
      capBtn('min', () => this.minimize(), '最小化'), this.maxBtn, capBtn('close', () => this.close(), '关闭'));
    this.bindCaption(cap);

    this.win = el('div', { class: 'win', hidden: true },
      cap,
      el('div', { class: 'wbody' }, this.buildSummary(), el('div', { class: 'mid' }, this.buildHub(), this.buildContent()), this.buildTiles()));
    this.win.addEventListener('pointerdown', () => { if (this.layer.lastElementChild !== this.win) this.layer.append(this.win); });
    this.layer.append(this.win);
  }

  bindCaption(cap) {
    cap.addEventListener('dblclick', e => { if (!e.target.closest('button')) this.toggleMax(); });
    cap.addEventListener('pointerdown', e => {
      if (e.button !== 0 || e.target.closest('button') || this.max || !this.rect) return;
      const sx = e.clientX, sy = e.clientY, ox = this.rect.x, oy = this.rect.y;
      cap.setPointerCapture(e.pointerId);
      const mv = ev => { this.rect.x = ox + ev.clientX - sx; this.rect.y = oy + ev.clientY - sy; this.clampRect(); this.applyRect(); };
      const up = () => { cap.removeEventListener('pointermove', mv); cap.removeEventListener('pointerup', up); cap.removeEventListener('pointercancel', up); };
      cap.addEventListener('pointermove', mv);
      cap.addEventListener('pointerup', up);
      cap.addEventListener('pointercancel', up);
    });
  }

  buildSummary() {
    const spec = [['01', '当前角色', 'CHAR'], ['02', '形象高度', 'PX', 'PX'], ['03', '气泡', 'ENABLED', 'PX'], ['04', '音量', 'VOL', '%'], ['05', '主题色', 'ACCENT']];
    this.sum = spec.map(([no, name, code, unit], i) => {
      const v = el('span', { class: i === 4 ? 'sum-v sum-hex' : 'sum-v' }, '—');
      const u = unit ? el('span', { class: 'sum-u' }, unit) : null;
      const note = el('div', { class: 'sum-note' });
      const gauge = i < 4 ? el('div', { class: 'gauge' }, Array.from({ length: 6 }, () => el('i'))) : null;
      const val = i === 4 ? el('div', { class: 'sum-val' }, el('i', { class: 'sum-sw' }), v) : el('div', { class: 'sum-val' }, v, u);
      const node = el('div', { class: 'sum-row' },
        el('div', { class: 'sum-top' }, el('span', { class: 'sum-no' }, no), el('span', { class: 'sum-name' }, name), el('span', { class: 'eyebrow sum-code' }, code)),
        val, note, gauge);
      return { node, v, u, note, gauge };
    });
    if (this.sum[4]) this.sum[4].note.textContent = '当前项 │ 主按钮 │ 滑块';
    this.sum[1].note.textContent = '屏幕上从头顶到脚底';
    return el('div', { class: 'sum' },
      el('div', { class: 'colhead' }, el('i', { class: 'bar' }), el('span', { class: 'eyebrow' }, 'SUMMARY'), el('span', { class: 'eyebrow r' }, '05')),
      el('div', { class: 'sum-list' }, this.sum.map(r => r.node)),
      el('div', { class: 'sum-foot' }, el('span', { class: 'eyebrow', style: { opacity: 0.6 } }, 'Q PET ' + VERSION), el('span', { class: 'eyebrow', style: { opacity: 0.4 } }, 'LOCAL ONLY')));
  }

  buildHub() {
    this.hubImg = el('img', { alt: '', draggable: 'false' });
    this.plate = el('div', { class: 'hub-plate' }, this.hubImg);
    this.vp = el('div', { class: 'hub-vp', title: '滚轮缩放立绘，按住左键拖动，双击复位' }, this.plate);
    this.zoomLbl = el('span', { class: 'hub-zoom' }, '×1.0');
    this.hubRun = el('b');
    this.hubPos = el('b');
    this.hubShow = button('在桌面隐藏', 'primary', () => this.toggleShow());
    const hub = el('div', { class: 'hub' }, this.vp,
      el('div', { class: 'hub-top' }, el('span', { class: 'eyebrow' }, '// CURRENT CHARACTER'), this.zoomLbl),
      el('div', { class: 'hub-bot' },
        el('span', { class: 'hub-state', style: { whiteSpace: 'pre' } }, '状态 ', this.hubRun, '    位置 ', this.hubPos),
        this.hubShow,
        button('查看原图', 'secondary', () => window.open(this.ch().imageUrl, '_blank', 'noopener'))));

    const vp = this.vp;
    vp.addEventListener('wheel', e => {
      e.preventDefault();
      let z = this.z * (e.deltaY < 0 ? 1.1 : 1 / 1.1);
      z = Math.max(0.4, Math.min(3, z));
      if (Math.abs(z - 1) < 0.06) z = 1;
      this.z = z;
      this.clampPan();
      this.applyZoom(false);
    }, { passive: false });
    vp.addEventListener('pointerdown', e => {
      if (e.button !== 0 || this.z <= 1) return;
      const sx = e.clientX, sy = e.clientY, ox = this.panX, oy = this.panY;
      vp.setPointerCapture(e.pointerId);
      vp.classList.add('drag');
      const mv = ev => { this.panX = ox + ev.clientX - sx; this.panY = oy + ev.clientY - sy; this.clampPan(); this.applyZoom(false); };
      const up = () => { vp.classList.remove('drag'); vp.removeEventListener('pointermove', mv); vp.removeEventListener('pointerup', up); };
      vp.addEventListener('pointermove', mv);
      vp.addEventListener('pointerup', up);
    });
    vp.addEventListener('dblclick', () => { this.z = 1; this.panX = 0; this.panY = 0; this.applyZoom(true); });
    return hub;
  }
  clampPan() {
    if (this.z <= 1) { this.panX = 0; this.panY = 0; return; }
    const r = this.vp.getBoundingClientRect();
    const mx = r.width * (this.z - 1) / 2, my = r.height * (this.z - 1) / 2;
    this.panX = Math.max(-mx, Math.min(mx, this.panX));
    this.panY = Math.max(-my, Math.min(my, this.panY));
  }
  applyZoom(anim) {
    this.plate.style.transition = anim ? 'transform 200ms cubic-bezier(.2,.8,.2,1)' : 'none';
    this.plate.style.transform = `translate(${this.panX}px, ${this.panY}px) scale(${this.z})`;
    this.zoomLbl.textContent = '×' + this.z.toFixed(1);
  }

  buildContent() {
    this.pbNo = el('span', { class: 'pb-no' });
    this.pbName = el('span', { class: 'pb-name' });
    this.pbChar = el('span', { class: 'pb-v' });
    this.pbSize = el('span', { class: 'pb-v' });
    this.pbFrames = el('span', { class: 'pb-v' });
    this.pbStateTx = el('span');
    this.pbState = el('span', { class: 'pb-state' }, this.pbStateTx, el('i'));
    const vd = () => el('i', { class: 'vd' });
    const pbar = el('div', { class: 'pbar' },
      el('span', { class: 'eyebrow' }, 'SECTION'), this.pbNo, this.pbName, vd(),
      el('span', { class: 'eyebrow' }, 'CHAR'), this.pbChar, vd(),
      el('span', { class: 'eyebrow' }, 'H'), this.pbSize, vd(),
      el('span', { class: 'eyebrow' }, 'FRAMES'), this.pbFrames,
      this.pbState);
    this.scroll = el('div', { class: 'scroll' });
    this.chipDirty = el('span', { class: 'chip-dirty', hidden: true }, 'UNSAVED');
    this.chipSaved = el('span', { class: 'chip-saved', hidden: true });
    this.saveBtn = button('保存更改', 'primary', () => this.save());
    const bbar = el('div', { class: 'bbar' },
      button('退出桌宠', 'ghost', () => this.quit()),
      this.chipDirty, this.chipSaved,
      el('div', { class: 'r' }, button('取消', 'secondary', () => this.close()), this.saveBtn));
    return el('div', { class: 'content' }, pbar, this.scroll, bbar);
  }

  buildTiles() {
    this.tileBtns = TILES.map(([key, t1, t2], i) => el('button', {
      class: 'tile', type: 'button', 'data-key': key,
      onclick: () => this.go(this.page === key ? null : key),
    }, el('span', { class: 'in' },
      el('span', { class: 'no' }, pad2(i + 1)),
      el('span', { class: 'ic', html: svg(ICONS[i], 16, 1.3) }),
      el('span', { class: 'tt' }, el('div', { class: 't1' }, t1), el('div', { class: 't2' }, t2)),
      el('span', { class: 'ar', html: ARROW }))));
    return el('div', { class: 'tiles' },
      el('div', { class: 'colhead' }, el('span', { class: 'eyebrow' }, 'SECTIONS'), el('span', { class: 'eyebrow r' }, '08')),
      el('div', { class: 'tile-list' }, this.tileBtns));
  }

  // ---------------- 窗口 ----------------
  show(page) {
    if (!this.open) {
      this.open = true;
      this.draft = clone(this.saved);
      this.lastBubH = this.saved.bubbleH > 0 ? this.saved.bubbleH : this.lastBubH;
      applyTheme(this.saved.themeColor);
      this.z = 1; this.panX = 0; this.panY = 0; this.applyZoom(false);
      this.chipSaved.hidden = true;
      this.win.hidden = false;
      this.place();
      this.go(page || null);
    } else {
      this.win.hidden = false;
      if (page !== undefined && page !== this.page) this.go(page);
    }
    this.min = false;
    this.layer.append(this.win);
    this.taskBtn.hidden = false;
    this.taskBtn.classList.add('on');
    this.syncCover();
  }
  // 手机上窗口铺满，桌宠会挡住设置，这时让窗口压在桌宠上面
  syncCover() { document.body.classList.toggle('win-cover', this.open && !this.min && this.win.classList.contains('phone')); }
  hide() {
    closePop();
    if (this.pageCtl && this.pageCtl.dispose) this.pageCtl.dispose();
    this.pageCtl = null;
    this.open = false;
    this.min = false;
    this.win.hidden = true;
    this.taskBtn.hidden = true;
    this.taskBtn.classList.remove('on');
    this.syncCover();
  }
  minimize() {
    closePop();
    this.min = true;
    this.win.hidden = true;
    this.taskBtn.classList.remove('on');
    this.syncCover();
  }
  taskClick() {
    if (!this.open) this.show();
    else if (this.min) this.show();
    else this.minimize();
  }
  toggleMax() {
    this.max = !this.max;
    this.maxBtn.innerHTML = svg(this.max ? CAP_ICONS.restore : CAP_ICONS.max, 14, 1);
    this.maxBtn.title = this.max ? '还原' : '最大化';
    this.place();
  }
  place() {
    const d = this.deskSize();
    const full = this.max || d.w < 900 || d.h < 560;
    this.win.classList.toggle('max', full);
    const ww = full ? d.w : Math.min(1180, d.w - 24);
    this.win.classList.toggle('narrow', ww < 1000);
    this.win.classList.toggle('phone', ww < 700);
    this.syncCover();
    if (full) {
      Object.assign(this.win.style, { left: '0px', top: '0px', width: d.w + 'px', height: d.h + 'px' });
      this.win.classList.toggle('dense', d.h < 660);
      return;
    }
    const w = Math.min(1180, d.w - 24), h = Math.min(780, d.h - 24);
    if (!this.rect) this.rect = { x: Math.round((d.w - w) / 2), y: Math.max(12, Math.round((d.h - h) / 2)) };
    this.rect.w = w; this.rect.h = h;
    this.clampRect();
    this.applyRect();
    this.win.classList.toggle('dense', h < 660);
  }
  clampRect() {
    const d = this.deskSize(), r = this.rect;
    r.x = Math.max(-(r.w - 160), Math.min(d.w - 160, r.x));
    r.y = Math.max(0, Math.min(d.h - 52, r.y));
  }
  applyRect() {
    const r = this.rect;
    Object.assign(this.win.style, { left: r.x + 'px', top: r.y + 'px', width: r.w + 'px', height: r.h + 'px' });
  }

  isDirty() { return stable(strip(this.draft)) !== stable(strip(this.saved)); }

  async close() {
    closePop();
    if (this.isDirty()) {
      const discard = await ask({
        title: '还有没保存的更改',
        body: '你改了设置但还没点「保存更改」。现在关掉的话，这些改动不会生效。',
        ok: '不保存退出', cancel: '继续编辑', danger: true,
      });
      if (!discard) return;
    }
    this.draft = clone(this.saved);
    applyTheme(this.saved.themeColor);
    this.hide();
  }

  async quit() {
    const ok = await ask({
      title: '让她先休息一下？',
      body: '桌宠会从桌面上消失，没保存的更改也不会生效。\n点右下角托盘图标，可以随时叫她回来。',
      ok: '退出桌宠', cancel: '再陪一会儿', danger: true,
    });
    if (!ok) return;
    this.draft = clone(this.saved);
    applyTheme(this.saved.themeColor);
    this.hide();
    this.onQuit && this.onQuit();
  }

  toggleShow() {
    const shown = this.pet.running && this.draft.showPet;
    if (!this.pet.running) { this.pet.setRunning(true); this.setLive('showPet', true); return; }
    this.setLive('showPet', !shown);
  }

  async save() {
    this.saveBtn.textContent = '应用中…';
    this.saveBtn.disabled = true;
    this.saved = clone(this.draft);
    saveCfg(this.saved);
    try { await this.pet.apply(clone(this.saved)); } finally {
      this.saveBtn.textContent = '保存更改';
      this.saveBtn.disabled = false;
    }
    const ch = this.ch();
    let note = `已保存并应用  │  ${ch.name}  │  ${this.H()}px  │  音量 ${this.saved.volume}%`;
    if (!this.pet.visible) note += '  │  桌宠当前隐藏';
    this.chipSaved.textContent = note;
    this.chipSaved.hidden = false;
    clearTimeout(this.savedTimer);
    this.savedTimer = setTimeout(() => { this.chipSaved.hidden = true; }, 6000);
    this.refresh();
  }

  // ---------------- 导航 ----------------
  go(key) {
    closePop();
    this.page = key;
    this.win.classList.toggle('paged', !!key);
    for (const b of this.tileBtns) b.classList.toggle('on', b.dataset.key === key);
    this.back.hidden = !key;
    if (key) this.renderPage(false);
    else if (this.pageCtl) { this.pageCtl.dispose && this.pageCtl.dispose(); this.pageCtl = null; this.scroll.replaceChildren(); }
    this.refresh();
  }

  renderPage(keepScroll) {
    if (this.pageCtl && this.pageCtl.dispose) this.pageCtl.dispose();
    const m = META[this.page];
    const top = this.scroll.scrollTop;
    const ctl = PAGES[this.page](this);
    this.pageCtl = ctl;
    const body = el('div', { class: 'body' }, ctl.el);
    [...body.children].forEach((c, i) => { c.style.animationDelay = (Math.min(i, 6) * 45) + 'ms'; });
    const page = el('div', { class: 'page' },
      el('div', { class: 'ph' }, el('span', { class: 'h1' }, m.h1), el('span', { class: 'eyebrow' }, m.eyebrow)),
      el('div', { class: 'psub' }, m.sub),
      el('div', { class: 'prule' }),
      body);
    if (keepScroll) { page.style.animation = 'none'; [...body.children].forEach(c => { c.style.animation = 'none'; }); }
    this.scroll.replaceChildren(page);
    this.scroll.scrollTop = keepScroll ? top : 0;
    if (ctl.mounted) ctl.mounted();
  }

  // ---------------- 刷新 ----------------
  refresh() {
    if (!this.open) return;
    const d = this.draft, ch = this.ch(), H = this.H();
    const voices = ch.voices.length, frames = this.frameCount();
    this.setVal(this.sum[0], ch.name);
    this.sum[0].note.textContent = `${voices} 条语音 │ ${frames} 帧动画 │ 立绘 1`;
    this.gauge(this.sum[0], ((voices > 0 ? 2 : 0) + (frames ? 2 : 0) + 2) / 6);
    this.setVal(this.sum[1], String(H));
    this.gauge(this.sum[1], (H - 180) / 220);
    const bh = d.bubbleH | 0;
    this.setVal(this.sum[2], bh > 0 ? String(bh) : '已关闭');
    this.sum[2].u.hidden = bh <= 0;
    this.sum[2].note.textContent = '字体 │ 颜色 │ 频率';
    this.gauge(this.sum[2], bh > 0 ? bh / 220 : 0);
    this.setVal(this.sum[3], String(d.volume));
    this.sum[3].note.textContent = d.volume > 0 ? '台词与音效' : '静音：只做动画';
    this.gauge(this.sum[3], d.volume / 100);
    this.setVal(this.sum[4], '#' + String(d.themeColor).toUpperCase());

    if (this.hubImg.dataset.src !== ch.imageUrl) { this.hubImg.src = ch.imageUrl; this.hubImg.dataset.src = ch.imageUrl; }
    this.capChar.textContent = ch.name;
    this.capVoice.textContent = String(voices);

    if (this.page) {
      const i = TILES.findIndex(t => t[0] === this.page);
      this.pbNo.textContent = pad2(i + 1);
      this.pbName.textContent = TILES[i][1];
      this.pbChar.textContent = ch.name;
      this.pbSize.textContent = `${H} px`;
      this.pbFrames.textContent = String(frames);
    }
    this.refreshPet();
    this.refreshDirty();
  }
  refreshPet() {
    const p = this.pet, vis = p.visible;
    this.capDot.classList.toggle('run', vis);
    this.capRun.textContent = vis ? 'RUN' : 'HIDDEN';
    const pos = p.pos ? `${p.pos.x} │ ${p.pos.y}` : '—';
    this.capPos.textContent = pos;
    this.hubRun.textContent = vis ? 'RUNNING' : '已隐藏';
    this.hubPos.textContent = pos;
    this.hubShow.textContent = p.running && this.draft.showPet ? '在桌面隐藏' : '在桌面显示';
    if (this.pageCtl && this.pageCtl.petChanged) this.pageCtl.petChanged();
  }
  refreshDirty() {
    const dirty = this.isDirty();
    this.chipDirty.hidden = !dirty;
    if (dirty) this.chipSaved.hidden = true;
    this.pbState.classList.toggle('dirty', dirty);
    this.pbStateTx.textContent = dirty ? '未保存' : '已保存';
  }
  setVal(r, text) {
    if (r.v.textContent === text) return;
    r.v.textContent = text;
    r.v.classList.remove('bump');
    void r.v.offsetWidth;
    r.v.classList.add('bump');
  }
  gauge(r, pct) {
    pct = Math.max(0, Math.min(1, pct || 0));
    [...r.gauge.children].forEach((c, i) => {
      const cover = Math.max(0, Math.min(1, pct * 6 - i));
      c.style.transitionDelay = (i * 26) + 'ms';
      c.style.background = cover > 0.02 ? 'var(--accent)' : '';
      c.style.opacity = cover > 0.02 && cover < 1 ? String(0.32 + 0.6 * cover) : '1';
    });
  }
}
