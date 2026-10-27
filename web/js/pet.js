// 桌宠本体：状态机、拖动、气泡、点击台词。逻辑照搬 pet.cs。
import { Sprite, canvasW, canvasH } from './frames.js';
import { buildSkin, drawBubble, fontCss, ptToPx, analyzeFrame } from './bubble.js';
import * as audio from './audio.js';
import { chars, ensureImage, heightOf, ampOf, customAnims, getFrame, loadPos, savePos, emit, on } from './store.js';
import { parseHex, el } from './util.js';

const FRAME_MS = 33, HOLD_MS = 110, LOOP_MS = 46.8;
const speedOf = v => Math.max(0.5, Math.min(2, (+v || 100) / 100));

export class Pet {
  constructor(layer, hooks) {
    this.layer = layer;
    this.hooks = hooks;           // { onMenu(x, y), deskSize() → {w, h} }
    this.cfg = null;
    this.ch = null;
    this.sprite = null;
    this.spriteKey = '';
    this.running = true;          // 「退出桌宠」后为 false，托盘可以再叫回来
    this.fsHidden = false;
    this.phase = 'idle'; this.phaseStart = 0; this.lastInputAt = 0; this.pressKind = 'press';
    this.idlePhase = 0; this.dragPhase = 0; this.frame = 0; this.drawn = -1;
    this.bubble = { text: '', until: 0, shownAt: 0, scroll: 0, maxScroll: 0, need: false, dragging: false, lastY: 0 };
    this.lastAuto = performance.now();
    this.seq = 0; this.lastLine = -1; this.lineOnDown = false; this.lineUrl = '';
    this.drag = null;
    this.skin = null; this.skinKey = ''; this.skinData = null;
    this.frameInfo = new Map();

    this.root = el('div', { class: 'pet', style: { display: 'none' } });
    this.bc = el('canvas', { class: 'pet-bubble' });
    this.cc = el('canvas', { class: 'pet-body' });
    this.root.append(this.bc, this.cc);
    layer.append(this.root);
    this.ctx = this.cc.getContext('2d');
    this.bctx = this.bc.getContext('2d');
    this.pos = loadPos();

    this.bind();
    on('anims', ({ charId }) => { if (this.ch && charId === this.ch.id) { this.spriteKey = ''; this.rebuild(); } });
    addEventListener('resize', () => { this.layout(); this.clampPos(); this.place(); });
    setInterval(() => this.tick(), FRAME_MS);
    setInterval(() => this.checkFullscreen(), 1000);
    document.addEventListener('fullscreenchange', () => this.checkFullscreen());
  }

  // ---------------- 配置 ----------------
  async apply(cfg) {
    this.cfg = cfg;
    const ch = chars.get(cfg.charId) || chars.get('furina') || chars.values().next().value;
    if (ch !== this.ch) { this.seq = 0; this.lastLine = -1; }
    this.ch = ch;
    await this.rebuild();
    this.updateVisible();
  }

  async rebuild() {
    const cfg = this.cfg, ch = this.ch;
    if (!cfg || !ch) return;
    const H = heightOf(cfg, ch), amps = ampOf(cfg, ch), k = devicePixelRatio || 1;
    const key = [ch.id, H, JSON.stringify(amps), k].join('|');
    if (key === this.spriteKey && this.sprite) { this.layout(); return; }
    this.spriteKey = key;
    const img = await ensureImage(ch);
    const custom = await customAnims(ch.id);
    if (key !== this.spriteKey) return;
    this.sprite = new Sprite(img, H, amps, custom, k);
    this.phase = 'idle'; this.idlePhase = 0; this.dragPhase = 0; this.frame = 0;
    this.drawn = -1;
    this.layout();
  }

  layout() {
    if (!this.sprite || !this.cfg) return;
    const k = devicePixelRatio || 1, W = this.sprite.W, CH = this.sprite.CH, bh = Math.max(0, this.cfg.bubbleH | 0);
    this.k = k;
    for (const [c, h] of [[this.bc, bh], [this.cc, CH]]) {
      const pw = Math.max(1, Math.round(W * k)), ph = Math.max(1, Math.round(h * k));
      if (c.width !== pw || c.height !== ph) { c.width = pw; c.height = ph; }
      c.style.width = W + 'px'; c.style.height = h + 'px';
    }
    this.bc.style.display = bh > 0 ? '' : 'none';
    this.root.style.width = W + 'px';
    this.drawn = -1;
    this.skinKey = '';
    if (!this.pos) {
      const d = this.hooks.deskSize();
      this.pos = { x: d.w - W - 40, y: d.h - CH - 24 };
    }
    this.clampPos();
    this.place();
  }

  clampPos() {
    if (!this.sprite || !this.pos) return;
    const d = this.hooks.deskSize(), W = this.sprite.W, CH = this.sprite.CH;
    const bh = Math.max(0, this.cfg.bubbleH | 0);
    this.pos.x = Math.round(Math.max(0, Math.min(this.pos.x, d.w - W)));
    this.pos.y = Math.round(Math.max(Math.min(bh, Math.max(0, d.h - CH)), Math.min(this.pos.y, d.h - CH)));
  }

  place() {
    if (!this.pos || !this.cfg) return;
    const bh = Math.max(0, this.cfg.bubbleH | 0);
    this.root.style.transform = `translate(${this.pos.x}px, ${this.pos.y - bh}px)`;
  }

  get visible() { return !!(this.running && this.cfg && this.cfg.showPet && !this.fsHidden); }
  updateVisible() {
    const v = this.visible;
    this.root.style.display = v ? '' : 'none';
    if (!v) { this.root.style.pointerEvents = 'none'; this.bubble.until = 0; }
    emit('petstate', null);
  }
  setRunning(r) { this.running = r; if (!r) audio.stop(); this.updateVisible(); }

  checkFullscreen() {
    const fs = !!document.fullscreenElement || (innerWidth >= screen.width && innerHeight >= screen.height && !matchMedia('(pointer: coarse)').matches);
    const h = !!(this.cfg && this.cfg.hideFullscreen && fs);
    if (h !== this.fsHidden) { this.fsHidden = h; this.updateVisible(); }
  }

  // ---------------- 状态机 ----------------
  triggerPress(fromClick) {
    this.lastInputAt = performance.now();
    const c = this.cfg;
    if (!c) return;
    if (fromClick) { if (!c.animClick) return; this.pressKind = 'click'; }
    else { if (!c.animPress) return; this.pressKind = 'press'; }
    if (this.phase === 'idle' || this.phase === 'rebound') { this.phase = 'squash'; this.phaseStart = performance.now(); }
  }

  advance(t, dt) {
    const s = this.sprite, c = this.cfg;
    if (this.drag && this.drag.moved && c.animDrag && s.n.drag > 0) {
      this.dragPhase += speedOf(c.speedDrag) * dt / LOOP_MS;
      if (this.dragPhase >= s.n.drag * 1000) this.dragPhase = 0;
      this.frame = s.dragStart + (Math.floor(this.dragPhase) % s.n.drag);
      return;
    }
    const isClick = this.pressKind === 'click' && s.n.click > 0;
    const base = isClick ? s.clickStart : s.pressStart;
    const count = isClick ? s.n.click : s.n.press;
    const speed = speedOf(isClick ? c.speedClick : c.speedPress);
    const deep = count - 3;
    if (this.phase === 'squash') {
      let idx = 1 + Math.floor((t - this.phaseStart) * speed / FRAME_MS);
      if (idx >= deep) { idx = deep; this.phase = 'hold'; this.phaseStart = t; }
      this.frame = base + Math.min(idx, count - 1);
    } else if (this.phase === 'hold') {
      this.frame = base + deep;
      if (t - this.lastInputAt > HOLD_MS) { this.phase = 'rebound'; this.phaseStart = t; }
    } else if (this.phase === 'rebound') {
      const idx = deep + 1 + Math.floor((t - this.phaseStart) * speed / FRAME_MS);
      if (idx >= count - 1) { this.frame = base + count - 1; this.phase = 'idle'; } else this.frame = base + idx;
    } else if (!c.animIdle) {
      this.frame = 0;
    } else {
      this.idlePhase += speedOf(c.speedIdle) * dt / LOOP_MS;
      if (this.idlePhase >= s.n.idle * 1000) this.idlePhase = 0;
      this.frame = Math.floor(this.idlePhase) % s.n.idle;
    }
    if (this.frame < 0 || this.frame >= s.total) this.frame = 0;
  }

  tick() {
    const t = performance.now(), dt = Math.min(200, t - (this.lastTick || t));
    this.lastTick = t;
    if (!this.sprite || !this.visible) return;
    this.advance(t, dt);
    const c = this.cfg, bh = c.bubbleH | 0;
    if (bh > 0 && c.bubbleEveryMs > 0 && this.bubble.until < t && t - this.lastAuto > c.bubbleEveryMs) {
      this.lastAuto = t;
      this.showBubble(this.randomLine(), 7000);
    }
    if (this.frame !== this.drawn) {
      const k = this.k;
      this.ctx.clearRect(0, 0, this.cc.width, this.cc.height);
      this.sprite.draw(this.ctx, this.frame, k);
      this.drawn = this.frame;
    }
    this.drawBubble(t);
  }

  // ---------------- 气泡 ----------------
  voices() {
    return this.ch ? this.ch.voices : [];
  }
  randomLine() {
    const v = this.voices();
    if (v.length) return v[Math.floor(Math.random() * v.length)].text;
    const l = this.ch ? this.ch.lines : [];
    return l.length ? l[Math.floor(Math.random() * l.length)] : '';
  }

  showBubble(text, ms) {
    if (!this.cfg || (this.cfg.bubbleH | 0) <= 0 || !text) return;
    const t = performance.now();
    Object.assign(this.bubble, { text, until: t + ms, shownAt: t, scroll: 0, dragging: false });
  }

  // 点她：气泡 + 这句自己的语音；返回是否播了台词
  clickLine() {
    const v = this.voices(), c = this.cfg;
    if (!v.length) { this.showBubble(this.randomLine(), 5000); return false; }
    let li;
    if (c.clickLineMode === 'seq') { li = this.seq % v.length; this.seq = (li + 1) % v.length; }
    else {
      li = Math.floor(Math.random() * v.length);
      if (v.length > 1 && li === this.lastLine) li = (li + 1) % v.length;
    }
    this.lastLine = li;
    const line = v[li];
    if (!c.clickVoice) { this.showBubble(line.text, 6000); return false; }
    const shown = performance.now();
    this.lineUrl = line.url;
    this.showBubble(line.text, Math.max(6000, audio.knownDur(line.url) + 1500));
    audio.play(line.url, c.volume).then(d => {
      if (d && this.bubble.shownAt === shown) this.bubble.until = shown + Math.max(6000, d + 1500);
    });
    return true;
  }

  sfx() {
    const c = this.cfg;
    const src = c.sfxGroup && c.sfxGroup !== 'auto' && chars.get(c.sfxGroup) ? chars.get(c.sfxGroup) : this.ch;
    return { press: src.press, release: src.release };
  }

  async getSkin(W, bh) {
    const c = this.cfg, k = this.k;
    const style = c.bubbleStyle || 'card';
    const key = [W, bh, style, c.bubbleColor, c.bubbleFrame, c.bubbleInk, k].join('|');
    if (key === this.skinKey) return this.skin;
    this.skinKey = key;
    let frameImg = null, frameInfo = null;
    if (style === 'custom' && c.bubbleFrame) {
      frameImg = await getFrame(c.bubbleFrame);
      if (frameImg) {
        if (!this.frameInfo.has(c.bubbleFrame)) this.frameInfo.set(c.bubbleFrame, analyzeFrame(frameImg));
        frameInfo = this.frameInfo.get(c.bubbleFrame);
      }
    }
    if (key !== this.skinKey) return this.skin;
    const inkFix = c.bubbleInk && c.bubbleInk !== 'auto' ? parseHex(c.bubbleInk, [0x1A, 0x1A, 0x1A]) : null;
    this.skin = buildSkin({ w: W, bh, style, accent: parseHex(c.bubbleColor, [0x2F, 0x35, 0x42]), frameImg, frameInfo, inkFix, k });
    this.skinData = this.skin.canvas.getContext('2d').getImageData(0, 0, this.skin.canvas.width, this.skin.canvas.height);
    this.bubbleDirty = true;
    return this.skin;
  }

  drawBubble(t) {
    const b = this.bubble, c = this.cfg, bh = c.bubbleH | 0, W = this.sprite.W;
    const live = bh > 0 && b.text && t < b.until;
    if (!live) {
      if (this.bubbleShown) { this.bctx.clearRect(0, 0, this.bc.width, this.bc.height); this.bubbleShown = false; }
      return;
    }
    if (this.skinKey !== [W, bh, c.bubbleStyle || 'card', c.bubbleColor, c.bubbleFrame, c.bubbleInk, this.k].join('|')) {
      this.getSkin(W, bh);
      if (!this.skin || this.skin.w !== W || this.skin.bh !== bh) return;
    }
    if (!this.skin) return;
    const appear = Math.min(1, (t - b.shownAt) / 170);
    const left = b.until - t;
    const fade = left < 240 ? Math.max(0, left / 240) : 1;
    const alpha = Math.min(1, appear * 1.6) * fade;
    const g = this.bctx;
    g.setTransform(1, 0, 0, 1, 0, 0);
    g.clearRect(0, 0, this.bc.width, this.bc.height);
    this.bubbleShown = true;
    if (alpha <= 0.02) return;
    const ease = 1 - (1 - appear) * (1 - appear);
    g.setTransform(this.k, 0, 0, this.k, 0, 0);
    const px = ptToPx(+c.bubbleFontSize || 10.5);
    const r = drawBubble(g, this.skin, b.text, { font: fontCss(c.bubbleFont, px), fontPx: px, alpha, scale: 0.94 + 0.06 * ease, scroll: b.scroll });
    b.need = r.need; b.maxScroll = r.maxScroll; b.scroll = r.scroll;
  }

  bubbleLive() { const b = this.bubble; return (this.cfg.bubbleH | 0) > 0 && b.text && performance.now() < b.until; }

  // ---------------- 输入 ----------------
  hitAt(cx, cy) {
    if (!this.visible || !this.sprite || !this.pos) return null;
    const bh = this.cfg.bubbleH | 0, W = this.sprite.W;
    const x = cx - this.pos.x, y = cy - this.pos.y;
    if (x < 0 || x >= W) return null;
    if (y >= 0 && y < this.sprite.CH) return this.sprite.alphaAt(this.frame, x, y) >= 24 ? 'body' : null;
    if (y < 0 && y >= -bh && this.bubbleLive() && this.skinData) {
      const k = this.k, px = Math.floor(x * k), py = Math.floor((y + bh) * k), d = this.skinData;
      if (px < d.width && py < d.height && d.data[(py * d.width + px) * 4 + 3] >= 24) return 'bubble';
    }
    return null;
  }

  bind() {
    const root = this.root;
    const desk = () => root.parentElement.getBoundingClientRect();
    const local = e => { const r = desk(); return [e.clientX - r.left, e.clientY - r.top]; };

    // 透明处点击穿透：鼠标不在不透明像素上时让事件落到下面
    addEventListener('pointermove', e => {
      if (this.drag || this.bubble.dragging) return;
      const [x, y] = local(e);
      root.style.pointerEvents = this.hitAt(x, y) ? 'auto' : 'none';
    }, { capture: true, passive: true });

    // 全局按键 / 别处点击 → 按压帧
    addEventListener('keydown', () => this.triggerPress(false), true);
    addEventListener('pointerdown', e => { if (!root.contains(e.target)) this.triggerPress(false); }, true);

    root.addEventListener('contextmenu', e => { e.preventDefault(); this.hooks.onMenu(e.clientX, e.clientY); });

    root.addEventListener('pointerdown', e => {
      if (e.button !== 0) return;
      const [x, y] = local(e);
      const where = this.hitAt(x, y);
      if (!where) return;
      e.preventDefault();
      root.setPointerCapture(e.pointerId);
      if (where === 'bubble' && this.bubble.need) {
        this.bubble.dragging = true; this.bubble.lastY = e.clientY;
        return;
      }
      this.triggerPress(true);
      const played = this.clickLine();
      this.lineOnDown = played;
      if (!played && this.cfg.clickVoice) {
        const s = this.sfx();
        audio.playPair(s.press, s.release, this.cfg.volume);
      }
      this.drag = { ox: e.clientX, oy: e.clientY, px: this.pos.x, py: this.pos.y, moved: false, savedAt: 0 };
    });

    root.addEventListener('pointermove', e => {
      if (this.bubble.dragging) {
        this.bubble.scroll = Math.max(0, this.bubble.scroll + (this.bubble.lastY - e.clientY));
        this.bubble.lastY = e.clientY;
        return;
      }
      const d = this.drag;
      if (!d) return;
      const dx = e.clientX - d.ox, dy = e.clientY - d.oy;
      if (!d.moved && (Math.abs(dx) > 4 || Math.abs(dy) > 4)) d.moved = true;
      if (!d.moved) return;
      this.pos.x = d.px + dx; this.pos.y = d.py + dy;
      this.clampPos(); this.place();
      const t = performance.now();
      if (t - d.savedAt > 150) { d.savedAt = t; savePos(this.pos); emit('pos', this.pos); }
    });

    const up = () => {
      if (this.bubble.dragging) { this.bubble.dragging = false; this.bubble.scroll = Math.min(this.bubble.scroll, this.bubble.maxScroll); return; }
      const d = this.drag;
      if (!d) return;
      this.drag = null;
      if (d.moved) { savePos(this.pos); emit('pos', this.pos); }
      else if (!this.lineOnDown && this.cfg.clickVoice) audio.releaseNow();
      this.lineOnDown = false;
    };
    root.addEventListener('pointerup', up);
    root.addEventListener('pointercancel', up);

    root.addEventListener('wheel', e => {
      if (!this.bubble.need) return;
      const [x, y] = local(e);
      if (this.hitAt(x, y) !== 'bubble' && y - this.pos.y >= 0) return;
      e.preventDefault();
      this.bubble.scroll = Math.max(0, Math.min(this.bubble.maxScroll, this.bubble.scroll + (e.deltaY > 0 ? 18 : -18)));
    }, { passive: false });
  }

  resetPos() {
    this.pos = null;
    this.layout();
    savePos(this.pos);
    emit('pos', this.pos);
  }
}
