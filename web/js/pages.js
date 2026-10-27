// 八个设置页。每页返回 { el, dispose?, mounted?, petChanged? }，改动写进 S.draft，再调 S.changed(key)
import { el, svg, parseHex, pad2, idbClear } from './util.js';
import {
  chars, VERSION, BUILD, addChar, deleteChar, ampOf, ensureImage, customAnims, setCustomAnim,
  listFrames, addFrame, deleteFrame,
} from './store.js';
import { Sprite, canvasW, canvasH } from './frames.js';
import { buildSkin, drawBubble, fontCss, ptToPx, FONTS, analyzeFrame } from './bubble.js';
import * as audio from './audio.js';
import { button, toggle, slider, combo, ask, colorDlg, menu, swatches, PALETTE, INKS, pickFiles } from './ui.js';
import { pageStats } from './pagestats.js';

const REPO = 'https://github.com/xiezaji/q-pet';
const IMG_EXT = /\.(png|webp|jpe?g|gif|bmp|avif)$/i;
const AUDIO_EXT = /\.(wav|mp3|ogg|m4a|flac|aac|opus)$/i;
const TRASH = 'M2.2,5.4 H15.8 M6.6,5.4 V2.6 H11.4 V5.4 M4.1,5.4 L5.3,15.4 H12.7 L13.9,5.4 M7.3,8 V12.9 M10.7,8 V12.9';

const card = (...kids) => el('div', { class: 'card' }, ...kids);
const boxed = (...kids) => el('div', { class: 'card boxed' }, ...kids);
const head = (title, sub, small) => el('div', { class: 'lbl' },
  el('div', { class: small ? 'h2 s' : 'h2' }, title), sub ? (sub.nodeType ? sub : el('div', { class: 'sub' }, sub)) : null);
const row = (title, sub, ...ctl) => el('div', { class: 'row' }, head(title, sub, true), el('div', { class: 'ctl' }, ...ctl));
const hr = () => el('div', { class: 'div' });
const clamp = (v, a, b) => Math.max(a, Math.min(b, v));
const dpr = () => devicePixelRatio || 1;
const sampleLine = ch => (ch.voices[0] && ch.voices[0].text) || ch.lines[0] || '你好呀';

function setAmp(d, ch, slot, val) {
  d.amps = d.amps || {};
  const o = Object.assign({}, d.amps[ch.id]);
  if (Math.abs(val - (ch.amp[slot] ?? 1)) < 0.005) delete o[slot]; else o[slot] = Math.round(val * 100) / 100;
  if (Object.keys(o).length) d.amps[ch.id] = o; else delete d.amps[ch.id];
}
function setHeight(d, ch, v) {
  d.heights = d.heights || {};
  if (v === ch.charH) delete d.heights[ch.id]; else d.heights[ch.id] = v;
}

// GIF 拆帧：浏览器支持 ImageDecoder 就逐帧转 PNG，否则整张当一帧
async function gifFrames(file) {
  if (!('ImageDecoder' in window) || !/gif$/i.test(file.type || file.name)) return [file];
  try {
    const dec = new ImageDecoder({ data: await file.arrayBuffer(), type: 'image/gif' });
    await dec.tracks.ready;
    const n = Math.min(120, dec.tracks.selectedTrack.frameCount);
    const out = [];
    for (let i = 0; i < n; i++) {
      const { image } = await dec.decode({ frameIndex: i });
      const c = document.createElement('canvas');
      c.width = image.displayWidth; c.height = image.displayHeight;
      c.getContext('2d').drawImage(image, 0, 0);
      image.close();
      out.push(await new Promise(r => c.toBlob(r, 'image/png')));
    }
    dec.close();
    return out.length ? out : [file];
  } catch (_) { return [file]; }
}

export const META = {
  bubble: { h1: '气泡', eyebrow: '// BUBBLE', sub: '她说话时那一小块地方的样子' },
  ui: { h1: '通用设置', eyebrow: '// GENERAL', sub: '桌面显示、全屏隐藏、界面主色' },
  char: { h1: '形象与大小', eyebrow: '// CHARACTER / SCALE', sub: '选择角色，并决定她在桌面上占多大' },
  anim: { h1: '动画', eyebrow: '// ANIMATION', sub: '每类动画单独调幅度与倍速，预览是真的帧' },
  voice: { h1: '语音', eyebrow: '// VOICE', sub: '点她时说什么、音量多大' },
  import: { h1: '新建角色', eyebrow: '// IMPORT', sub: '把立绘和语音装进一个新角色包' },
  stats: { h1: '使用统计', eyebrow: '// STATS', sub: '只存在这个浏览器里，不联网' },
  about: { h1: '关于', eyebrow: '// ABOUT', sub: '版本、数据与链接' },
};

// ================= 气泡 =================
const FONT_NAMES = {
  YouYuan: '幼圆', 'Noto Sans SC Medium': '思源黑体 Medium', 'Noto Sans SC': '思源黑体', DengXian: '等线',
  'Microsoft YaHei UI': '微软雅黑 UI', KaiTi: '楷体', SimHei: '黑体', FangSong: '仿宋',
};
const STYLES = [['card', '圆角卡片'], ['classic', '经典椭圆'], ['comic', '漫画描边'], ['glass', '半透明']];
const SIZES = [8, 9, 10, 10.5, 11, 12, 14, 16, 18, 20, 22, 24, 26, 28, 36];
const familyOf = f => fontCss(f, 13).replace(/^(\d+ )?[\d.]+px /, '');

function pageBubble(S) {
  const d = S.draft;
  let frames = [];
  const gated = [];
  const on = () => (d.bubbleH | 0) > 0;
  const gate = () => gated.forEach(n => n.classList.toggle('dis', !on()));
  const upd = key => { S.changed(key); paintStyles(); preview(); };

  const hRead = el('div', { class: 'readout' });
  const hSl = slider({
    min: 70, max: 220, value: on() ? d.bubbleH : S.lastBubH, width: 210,
    onInput: v => { d.bubbleH = v; hRead.textContent = v + ' px'; S.changed('bubbleH'); preview(); },
  });
  hRead.textContent = hSl.value + ' px';
  const tg = toggle('启用气泡', on(), v => {
    d.bubbleH = v ? S.lastBubH : 0;
    if (v) { hSl.set(d.bubbleH); hRead.textContent = d.bubbleH + ' px'; }
    S.changed('bubbleH'); gate(); preview();
  });

  const every = combo([
    { value: 0, label: '不主动冒泡' }, { value: 300000, label: '每 5 分钟' },
    { value: 600000, label: '每 10 分钟' }, { value: 1800000, label: '每 30 分钟' },
  ], d.bubbleEveryMs, v => { d.bubbleEveryMs = +v; S.changed('bubbleEveryMs'); }, { width: 180 });

  const font = combo(FONTS.map(f => ({ value: f, label: FONT_NAMES[f] || f, font: familyOf(f) })), d.bubbleFont,
    v => { d.bubbleFont = v; upd('bubbleFont'); }, { width: 220 });

  const color = swatches(PALETTE, d.bubbleColor, h => { d.bubbleColor = h; upd('bubbleColor'); });
  const colorBtn = button('自定义颜色…', 'secondary sm', async () => {
    const h = await colorDlg(d.bubbleColor, '气泡颜色');
    if (h) { d.bubbleColor = h; color.set(h); upd('bubbleColor'); }
  });
  const ink = swatches(INKS, d.bubbleInk, h => { d.bubbleInk = h; upd('bubbleInk'); });
  const inkBtn = button('自定义颜色…', 'secondary sm', async () => {
    const h = await colorDlg(d.bubbleInk === 'auto' ? '1A1A1A' : d.bubbleInk, '文字颜色');
    if (h) { d.bubbleInk = h; ink.set(h); upd('bubbleInk'); }
  });

  const size = combo(SIZES.map(v => ({ value: v, label: String(v) })), d.bubbleFontSize, v => {
    let n = parseFloat(v);
    if (!isFinite(n)) n = d.bubbleFontSize;
    n = clamp(Math.round(n * 2) / 2, 4, 72);
    d.bubbleFontSize = n; size.value = n;
    upd('bubbleFontSize');
  }, { width: 120, editable: true });

  const rFreq = row('主动冒泡', '她闲着时隔多久自己说一句', every.el);
  const rFont = row('字体', '下拉里每一项都用它自己的字体显示', font.el);
  const rColor = el('div', null, row('颜色', '外框与描边的主色', colorBtn), color.el);
  const rInk = el('div', null, row('文字颜色', '「自动」按外框深浅选黑或白', inkBtn), ink.el);
  const rSize = row('字号', '4 – 72 pt，可以直接输入', size.el, el('span', { class: 'note' }, 'pt'));
  const rH = row('气泡高度', '宽度跟着角色走，这里只调高度', el('div', { class: 'stack' }, hSl, hRead));
  gated.push(rH, rFreq, rFont, rColor, rInk, rSize);

  // 外框样式
  const styles = el('div', { class: 'styles', style: { marginTop: '10px' } });
  const hint = el('div', { class: 'note', style: { marginTop: '4px' } }, '自定义外框：四边画框效果最好，中间留白放字');
  const upload = async () => {
    const [f] = await pickFiles({ accept: 'image/png,image/webp,image/gif,image/jpeg' });
    if (!f) return;
    const id = await addFrame(f);
    await loadFrames();
    d.bubbleStyle = 'custom'; d.bubbleFrame = id;
    hint.textContent = `已添加外框：${f.name}。点「保存更改」后生效。`;
    upd('bubbleFrame');
  };
  const thumb = (style, fr) => {
    const k = dpr(), c = el('canvas');
    c.width = Math.round(150 * k); c.height = Math.round(58 * k);
    const g = c.getContext('2d');
    g.scale(k, k);
    const inkFix = d.bubbleInk && d.bubbleInk !== 'auto' ? parseHex(d.bubbleInk) : null;
    const skin = buildSkin({ w: 150, bh: 58, style, accent: parseHex(d.bubbleColor, [0x2F, 0x35, 0x42]), frameImg: fr && fr.img, frameInfo: fr && fr.info, inkFix, k });
    drawBubble(g, skin, '你好呀', { font: fontCss(d.bubbleFont, 13), fontPx: 13, alpha: 1, scale: 1, scroll: 0 });
    return c;
  };
  const tile = (style, label, fr) => {
    const sel = d.bubbleStyle === style && (style !== 'custom' || d.bubbleFrame === fr.id);
    const b = el('button', {
      class: 'stile' + (sel ? ' sel' : ''), type: 'button', title: fr ? fr.name : label,
      onclick: () => { d.bubbleStyle = style; if (fr) d.bubbleFrame = fr.id; upd('bubbleStyle'); },
    }, thumb(style, fr), el('span', null, label));
    if (fr) b.addEventListener('contextmenu', e => {
      e.preventDefault();
      menu(e.clientX, e.clientY, [{ label: '删除这个外框', danger: true, onClick: () => removeFrame(fr) }]);
    });
    return b;
  };
  const removeFrame = async fr => {
    if (!await ask({ title: '删除这个外框？', body: fr.name || '', ok: '删除', cancel: '算了', danger: true })) return;
    await deleteFrame(fr.id);
    if (S.saved.bubbleFrame === fr.id) {
      await S.commit({ bubbleFrame: '', bubbleStyle: S.saved.bubbleStyle === 'custom' ? 'card' : S.saved.bubbleStyle });
    }
    if (d.bubbleFrame === fr.id) { d.bubbleFrame = ''; if (d.bubbleStyle === 'custom') d.bubbleStyle = 'card'; }
    await loadFrames();
    upd('bubbleFrame');
  };
  function paintStyles() {
    styles.replaceChildren(
      ...STYLES.map(([s, l]) => tile(s, l)),
      ...frames.map((fr, i) => tile('custom', fr.name ? fr.name.replace(/\.[^.]+$/, '') : '自定义 ' + (i + 1), fr)),
      el('button', { class: 'stile add', type: 'button', onclick: upload }, '+ 上传外框'));
  }
  async function loadFrames() {
    frames = (await listFrames()).map(f => Object.assign(f, { info: analyzeFrame(f.img) }));
  }
  const rStyle = card(row('外框样式', '右键自定义外框可以删除', button('上传外框…', 'secondary sm', upload)), styles, hint);
  gated.push(rStyle);

  // 预览
  const cv = el('canvas');
  const cap = el('div', { class: 'note', style: { marginTop: '8px' } });
  let tok = 0;
  async function preview() {
    const my = ++tok;
    const ch = S.ch(), H = S.H(), bh = d.bubbleH | 0;
    const img = await ensureImage(ch);
    const custom = await customAnims(ch.id);
    if (my !== tok) return;
    const k = dpr(), sp = new Sprite(img, H, ampOf(d, ch), custom, k);
    const W = sp.W, CH = sp.CH, top = 10 + (bh > 0 ? bh + 10 : 0), total = top + CH + 10;
    cv.width = Math.round(W * k); cv.height = Math.round(total * k);
    const ch2 = Math.min(total, 278);
    cv.style.height = ch2 + 'px'; cv.style.width = (W * ch2 / total) + 'px';
    const g = cv.getContext('2d');
    g.setTransform(k, 0, 0, k, 0, 0);
    g.clearRect(0, 0, W, total);
    if (bh > 0) {
      const fr = d.bubbleStyle === 'custom' ? frames.find(f => f.id === d.bubbleFrame) : null;
      const inkFix = d.bubbleInk && d.bubbleInk !== 'auto' ? parseHex(d.bubbleInk) : null;
      const skin = buildSkin({ w: W, bh, style: fr ? 'custom' : (d.bubbleStyle === 'custom' ? 'card' : d.bubbleStyle), accent: parseHex(d.bubbleColor, [0x2F, 0x35, 0x42]), frameImg: fr && fr.img, frameInfo: fr && fr.info, inkFix, k });
      const px = ptToPx(+d.bubbleFontSize || 10.5);
      g.save(); g.translate(0, 10);
      drawBubble(g, skin, sampleLine(ch), { font: fontCss(d.bubbleFont, px), fontPx: px, alpha: 1, scale: 1, scroll: 0 });
      g.restore();
    }
    g.setTransform(1, 0, 0, 1, 0, Math.round(top * k));
    sp.draw(g, 6, k);
    g.setTransform(k, 0, 0, k, 0, top * k);
    g.setLineDash([4, 3]); g.strokeStyle = '#C9D2E0'; g.lineWidth = 1;
    g.beginPath(); g.moveTo(0, CH - 6 + 0.5); g.lineTo(W, CH - 6 + 0.5); g.stroke();
    cap.textContent = bh > 0 ? `气泡 ${bh} px ＋ 角色 ${CH} px（上下各留 10 px 间距）` : `气泡已关闭 ＋ 角色 ${CH} px`;
  }
  const box = el('div', { class: 'dashed', style: { height: '300px', background: '#FCFDFE' } }, cv);

  gate();
  paintStyles();
  return {
    el: [
      card(el('div', { class: 'row' }, head('气泡', '关掉后她只做动画，不说话'), el('div', { class: 'ctl' }, tg.el)), rH, rFreq, rFont, rSize, hr(), rColor, hr(), rInk),
      rStyle,
      card(head('预览', '按真实比例，1:1 显示'), el('div', { style: { marginTop: '10px' } }, box), cap),
    ],
    async mounted() { await loadFrames(); paintStyles(); preview(); },
    dispose() { tok++; },
  };
}

// ================= 通用 =================
function pageUi(S) {
  const d = S.draft;
  const tShow = toggle(null, d.showPet, v => S.setLive('showPet', v));
  const tFs = toggle(null, d.hideFullscreen, v => S.setLive('hideFullscreen', v));
  const theme = swatches(PALETTE, d.themeColor, h => { d.themeColor = h; S.changed('themeColor'); });
  const setTheme = h => { d.themeColor = h; theme.set(h); S.changed('themeColor'); };
  return {
    el: [
      card(
        row('在桌面显示桌宠', '切换立即生效', tShow.el),
        hr(),
        row('全屏时隐藏桌宠', '浏览器全屏（F11）时她自动躲开；切换立即生效', tFs.el)),
      card(
        row('主题色', '当前项、主按钮、滑块都用这个颜色；即时预览，保存后保留',
          button('自定义颜色…', 'secondary sm', async () => { const h = await colorDlg(d.themeColor, '主题色'); if (h) setTheme(h); }),
          button('恢复默认', 'ghost sm', () => setTheme('005FB8'))),
        theme.el),
      card(row('开机自启动', '网页版做不到；想让她常驻桌面，用 Windows 桌面版',
        el('a', { class: 'btn secondary sm', href: REPO + '/releases', target: '_blank', rel: 'noopener' }, '下载桌面版'))),
    ],
    petChanged() { tShow.value = S.draft.showPet; },
  };
}

// ================= 形象与大小 =================
const PRESETS = [180, 219, 260, 320, 400];
function pageChar(S) {
  const d = S.draft, ch = S.ch();
  const pick = combo([...chars.values()].map(c => ({ value: c.id, label: c.name })), ch.id,
    v => { d.charId = v; S.changed('charId'); S.rerender(); }, { width: 252, cls: 'pick' });

  const val = el('span', { style: { fontSize: '16px', fontWeight: 700 } });
  const pres = PRESETS.map(v => el('button', { class: 'pre', type: 'button', onclick: () => setH(v) }, String(v)));
  const img = el('img', { src: ch.imageUrl, alt: '' });
  const cl = el('span', { class: 'corner l' }), cap = el('div', { class: 'note', style: { marginTop: '8px' } });
  const sl = slider({ min: 180, max: 400, value: S.H(), width: 190, onInput: v => setH(v, true) });
  function paint() {
    const H = S.H(), w = canvasW(H), fh = canvasH(H), bub = Math.max(0, d.bubbleH | 0);
    val.textContent = H;
    pres.forEach((b, i) => b.classList.toggle('on', Math.abs(PRESETS[i] - H) <= 3));
    img.style.height = Math.round(212 * H / 400) + 'px';
    cl.textContent = `${w} × ${fh}`;
    cap.textContent = `角色 ${w} × ${fh} px　│　桌宠窗口 ${w} × ${fh + bub} px（含气泡 ${bub}）`;
  }
  function setH(v, fromSlider) {
    setHeight(d, ch, v);
    if (!fromSlider) sl.set(v);
    paint();
    S.changed('heights');
  }
  paint();
  return {
    el: [
      card(row('角色', '切换角色的立绘与台词，保存后生效', pick.el)),
      card(
        row('形象高度', '从头顶到脚底，单位是屏幕像素', el('div', { class: 'ctl' }, sl, val, el('span', { class: 'note' }, 'PX'))),
        el('div', { class: 'row' }, el('div', { class: 'lbl note' }, '常用尺寸'), el('div', { class: 'presets' }, pres)),
        el('div', { class: 'dashed', style: { height: '236px', marginTop: '6px' } }, cl, el('span', { class: 'corner r' }, 'PNG · ALPHA'), img),
        cap),
      card(
        row('立绘', `原图 ${ch.imageW} × ${ch.imageH} px`, button('查看原图', 'secondary sm', () => window.open(ch.imageUrl, '_blank', 'noopener'))),
        el('div', { class: 'dashed', style: { height: '150px' } }, el('img', { src: ch.imageUrl, alt: '', style: { height: '130px' } }))),
    ],
  };
}

// ================= 动画 =================
const SLOTS = [
  ['idle', '待机', 'Idle', '闲着时轻轻呼吸'],
  ['press', '按键', 'Press', '你敲键盘、点别处时压一下'],
  ['click', '点击', 'Click', '点她时压扁再回弹'],
  ['drag', '拖动', 'Drag', '拖着走时左右晃'],
];
function pageAnim(S) {
  const d = S.draft, ch = S.ch();
  const cards = [];
  let sprite = null, timer = 0, alive = true;

  const replace = async (slot, mode) => {
    let blobs;
    if (mode === 'dir') {
      const fs = (await pickFiles({ dir: true })).filter(f => IMG_EXT.test(f.name))
        .sort((a, b) => a.name.localeCompare(b.name, 'zh', { numeric: true }));
      blobs = fs.slice(0, 120);
    } else {
      const [f] = await pickFiles({ accept: 'image/gif,image/png,image/webp,image/jpeg' });
      blobs = f ? await gifFrames(f) : [];
    }
    if (!blobs.length) return;
    await setCustomAnim(ch.id, slot, blobs);
    if (alive) S.rerender();
    setTimeout(() => S.refresh(), 400);
  };
  const restore = async slot => {
    if (!await ask({ title: '恢复默认动画？', body: '自定义的帧会被删掉，换回按立绘生成的动画。', ok: '恢复', cancel: '算了' })) return;
    await setCustomAnim(ch.id, slot, null);
    if (alive) S.rerender();
    setTimeout(() => S.refresh(), 400);
  };

  for (const [slot, name, K, desc] of SLOTS) {
    const cv = el('canvas');
    const ampV = el('span', { class: 'v' }), spV = el('span', { class: 'v' });
    const amp = Math.round((ampOf(d, ch)[slot] ?? 1) * 100);
    const ampSl = slider({
      min: 20, max: 200, value: amp, onInput: v => {
        ampV.textContent = v + '%';
        setAmp(d, ch, slot, v / 100);
        if (sprite) sprite.a[slot] = v / 100;
        S.changed('amps');
      },
    });
    const spSl = slider({
      min: 50, max: 200, step: 10, value: d['speed' + K], onInput: v => {
        spV.textContent = '×' + (v / 100).toFixed(1);
        d['speed' + K] = v; S.changed('speed' + K);
      },
    });
    ampV.textContent = amp + '%';
    spV.textContent = '×' + (d['speed' + K] / 100).toFixed(1);
    const tag = el('span', { class: 'tag-ok', hidden: true }, '已自定义');
    const tg = toggle('启用', d['anim' + K], v => { d['anim' + K] = v; S.changed('anim' + K); });
    const more = button('自定义动画…', 'secondary sm', e => {
      const r = e.currentTarget.getBoundingClientRect();
      menu(r.left, r.bottom + 3, [
        { label: '用 GIF / 图片替换…', onClick: () => replace(slot, 'file') },
        { label: '用帧文件夹替换…', onClick: () => replace(slot, 'dir') },
        '-',
        { label: '恢复默认动画', onClick: () => restore(slot) },
      ]);
    });
    const node = el('div', { class: 'acard' },
      el('div', { class: 'top' },
        el('div', { class: 'pv' }, cv),
        el('div', { class: 'meta' },
          el('div', { class: 'cardhead' }, el('span', { class: 'h2 s' }, name), el('span', { class: 'eyebrow' }, K.toUpperCase()), tag),
          el('div', { class: 'sub', style: { marginTop: 0 } }, desc),
          tg.el)),
      el('div', { class: 'arow' }, el('span', null, '幅度'), ampSl, ampV),
      el('div', { class: 'arow' }, el('span', null, '倍速'), spSl, spV),
      el('div', { style: { marginTop: '10px' } }, more));
    cards.push({ slot, K, cv, tag, node, acc: 0, i: 0, pause: 0, drawn: -1 });
  }

  const draw = c => {
    if (!sprite) return;
    const s = sprite, n = s.n[c.slot], on = d['anim' + c.K];
    const start = { idle: 0, press: s.pressStart, click: s.clickStart, drag: s.dragStart }[c.slot];
    const f = !on ? 0 : (c.pause > 0 && c.slot !== 'idle' ? 0 : start + Math.min(c.i, n - 1));
    const g = c.cv.getContext('2d');
    g.setTransform(1, 0, 0, 1, 0, 0);
    g.clearRect(0, 0, c.cv.width, c.cv.height);
    s.draw(g, f, s.res);
  };
  const step = () => {
    for (const c of cards) {
      if (!d['anim' + c.K]) { draw(c); continue; }
      c.acc += d['speed' + c.K] / 100;
      while (c.acc >= 1) {
        c.acc -= 1;
        const n = sprite ? sprite.n[c.slot] : 1;
        if (c.slot === 'idle') c.i = (c.i + 1) % n;
        else if (c.pause > 0) c.pause--;
        else if (++c.i >= n) { c.i = 0; c.pause = 14; }
      }
      draw(c);
    }
  };

  return {
    el: [
      el('div', { class: 'anim-grid' }, cards.map(c => c.node)),
      el('div', { class: 'note', style: { marginTop: '12px' } }, '幅度和开关保存后对桌宠生效；预览按当前设置实时播放　│　自定义动画立即写入'),
    ],
    async mounted() {
      const img = await ensureImage(ch), custom = await customAnims(ch.id);
      if (!alive) return;
      const H = S.H(), res = 96 * dpr() / canvasH(H);
      sprite = new Sprite(img, H, ampOf(d, ch), custom, res);
      for (const c of cards) {
        c.cv.width = Math.round(sprite.W * res); c.cv.height = Math.round(sprite.CH * res);
        c.cv.style.width = (96 * sprite.W / sprite.CH) + 'px';
        c.tag.hidden = !custom[c.slot];
        draw(c);
      }
      timer = setInterval(step, 50);
    },
    dispose() { alive = false; clearInterval(timer); },
  };
}

// ================= 语音 =================
function pageVoice(S) {
  const d = S.draft, ch = S.ch(), voices = ch.voices;
  const timers = [];
  const sfx = combo([{ value: 'auto', label: '跟随角色（推荐）' },
    ...[...chars.values()].filter(c => c.press).map(c => ({ value: c.id, label: c.name }))],
  d.sfxGroup, v => { d.sfxGroup = v; S.changed('sfxGroup'); }, { width: 240 });
  const mode = combo([{ value: 'random', label: '随机一句' }, { value: 'seq', label: '顺序播放' }], d.clickLineMode,
    v => { d.clickLineMode = v; S.changed('clickLineMode'); }, { width: 180 });
  const tg = toggle(null, d.clickVoice, v => { d.clickVoice = v; S.changed('clickVoice'); });

  const volV = el('div', { class: 'readout' }), volHint = el('div', { class: 'sub' });
  const paintVol = () => {
    volV.textContent = d.volume + '%';
    volHint.textContent = d.volume > 0 ? '所有台词与音效都会按这个音量播放' : '当前静音：只做动画，不出声';
  };
  const vol = slider({ min: 0, max: 100, value: d.volume, width: 210, onInput: v => { d.volume = v; audio.setVolume(v); paintVol(); S.changed('volume'); } });
  paintVol();

  const tryMsg = el('span', { class: 'note' });
  const list = el('div', { class: 'lines' });
  const count = el('span', { class: 'note' });
  const q = el('input', { class: 'input', type: 'search', placeholder: '搜索台词…', style: { width: '220px' } });

  const playAt = i => {
    const v = voices[i];
    if (!v) return;
    tryMsg.textContent = '正在试听：' + v.text;
    list.querySelectorAll('.line.play').forEach(n => n.classList.remove('play'));
    const node = list.querySelector(`[data-i="${i}"]`);
    if (node) node.classList.add('play');
    audio.play(v.url, d.volume).then(ms => {
      timers.push(setTimeout(() => { if (node) node.classList.remove('play'); }, ms || 2000));
    });
  };
  const playRandom = () => {
    if (!voices.length) { tryMsg.textContent = '当前角色没有语音可试听'; return; }
    playAt(Math.floor(Math.random() * voices.length));
  };
  const paintList = () => {
    const kw = q.value.trim();
    const hits = voices.map((v, i) => [v, i]).filter(([v]) => !kw || v.text.includes(kw));
    count.textContent = kw ? `匹配 ${hits.length} / ${voices.length} 条` : `共 ${voices.length} 条`;
    list.style.height = clamp(hits.length * 46 + 10, 150, 260) + 'px';
    if (!voices.length) { list.replaceChildren(el('div', { class: 'empty' }, '当前角色还没有语音（建角色时带上语音文件夹即可）')); return; }
    if (!hits.length) { list.replaceChildren(el('div', { class: 'empty' }, '没有匹配的台词')); return; }
    list.replaceChildren(...hits.map(([v, i]) => el('div', { class: 'line', 'data-i': i, onclick: () => playAt(i) },
      el('span', { class: 'n' }, pad2(i + 1)), el('span', { class: 't' }, v.text), el('span', { class: 'p' }, '▶'))));
  };
  q.addEventListener('input', paintList);
  paintList();

  return {
    el: [
      card(
        row('点击她时播放语音', '关掉后点她只出气泡，不出声', tg.el),
        hr(),
        row('点击模式', '每次点她挑哪一句', mode.el),
        hr(),
        row('音效组', '没有台词时，按下 / 松开用哪套音效', sfx.el)),
      card(
        el('div', { class: 'row' }, el('div', { class: 'lbl' }, el('div', { class: 'h2 s' }, '音量'), volHint),
          el('div', { class: 'ctl' }, el('div', { class: 'stack' }, vol, volV))),
        el('div', { class: 'row' }, el('div', { class: 'lbl' }, tryMsg), el('div', { class: 'ctl' }, button('试听', 'secondary sm', playRandom)))),
      card(
        el('div', { class: 'row' }, el('div', { class: 'lbl' }, el('div', { class: 'h2 s' }, '台词库'), count),
          el('div', { class: 'ctl' }, q, button('随机来一句', 'secondary sm', playRandom))),
        list),
    ],
    dispose() { timers.forEach(clearTimeout); },
  };
}

// ================= 新建角色 =================
function pageImport(S) {
  let image = null, voiceFiles = [], busy = false;
  const name = el('input', { class: 'input', maxlength: 40, placeholder: '比如：芙宁娜', style: { width: '252px' } });
  const imgLbl = el('div', { class: 'sub' }, 'PNG / WebP，透明背景效果最好');
  const dirLbl = el('div', { class: 'sub' }, 'wav / mp3 / ogg，文件名就是台词');
  const msg = el('div', { class: 'note', style: { marginTop: '8px', minHeight: '18px' } });
  const thumb = el('img', { alt: '', style: { height: '130px' } });
  const box = el('div', { class: 'dashed', style: { height: '150px', marginTop: '6px' }, hidden: true }, thumb);

  const pickImg = async () => {
    const [f] = await pickFiles({ accept: 'image/png,image/webp,image/jpeg,image/gif,image/avif' });
    if (!f) return;
    image = f;
    imgLbl.textContent = '已选择：' + f.name;
    if (!name.value.trim()) name.value = f.name.replace(/\.[^.]+$/, '').slice(0, 40);
    if (thumb.src) URL.revokeObjectURL(thumb.src);
    thumb.src = URL.createObjectURL(f);
    box.hidden = false;
  };
  const pickDir = async () => {
    const fs = await pickFiles({ dir: true });
    if (!fs.length) return;
    voiceFiles = fs.filter(f => AUDIO_EXT.test(f.name));
    const dir = (fs[0].webkitRelativePath || '').split('/')[0] || '文件夹';
    dirLbl.textContent = `已选择：${dir}（${voiceFiles.length} 个音频）`;
  };
  const create = async () => {
    if (busy) return;
    const nm = name.value.trim();
    if (!nm) { msg.textContent = '请先填写角色名称。'; name.focus(); return; }
    if (!image) { msg.textContent = '请选择一张角色图片（每个角色包都需要）。'; return; }
    busy = true; msg.textContent = '正在导入…';
    try {
      const ch = await addChar(nm, image, voiceFiles);
      await S.commit({ charId: ch.id });
      S.flash = `已创建并应用：${ch.name}（语音 ${ch.voices.length} 条）`;
      S.rerender();
    } catch (e) {
      msg.textContent = '导入失败：' + (e && e.message || e);
    } finally { busy = false; }
  };

  const grid = el('div', { class: 'char-grid' });
  const cur = S.saved.charId;
  for (const c of chars.values()) {
    const del = c.builtin ? null : el('button', {
      class: 'del', type: 'button', title: '删除角色',
      html: svg(TRASH, 16, 1.3).replace('viewBox="0 0 16 16"', 'viewBox="0 0 18 18"'),
      onclick: async () => {
        if (!await ask({ title: `删除角色「${c.name}」？`, body: '立绘、语音和自定义动画都会从这个浏览器里删掉。', ok: '删除', cancel: '算了', danger: true })) return;
        const wasCur = S.saved.charId === c.id || S.draft.charId === c.id;
        await deleteChar(c.id);
        if (wasCur) await S.commit({ charId: 'furina' }); else S.refresh();
        S.rerender();
      },
    });
    grid.append(el('div', { class: 'ccard' + (c.id === cur ? ' cur' : '') },
      el('div', { class: 'pic' }, el('img', { src: c.imageUrl, alt: '', loading: 'lazy' })),
      el('div', { class: 'info' },
        el('div', null, el('div', { class: 'nm' }, c.name), el('div', { class: 'ds' }, `${(S.saved.heights || {})[c.id] || c.charH} px │ 语音 ${c.voices.length}`)),
        del)));
  }
  if (S.flash) { msg.textContent = S.flash; S.flash = ''; }

  return {
    el: [
      card(
        row('角色名称', '显示在设置和气泡里', name),
        hr(),
        row('立绘', imgLbl, button('选择图片…', 'secondary sm', pickImg)),
        box,
        hr(),
        row('语音（可选）', dirLbl, button('选择文件夹…', 'secondary sm', pickDir)),
        el('div', { class: 'row' }, el('div', { class: 'lbl' }, msg), el('div', { class: 'ctl' }, button('创建角色', 'primary', create)))),
      card(head('已有角色', '内置角色不能删除'), grid,
        el('div', { class: 'note', style: { marginTop: '10px' } }, '导入的角色存在这个浏览器里　│　换角色在「形象与大小」里选，保存后生效')),
    ],
  };
}

// ================= 关于 =================
function pageAbout() {
  const ua = navigator.userAgent;
  const br = (/Edg\/([\d.]+)/.exec(ua) && 'Edge ' + RegExp.$1.split('.')[0]) ||
    (/Firefox\/([\d.]+)/.exec(ua) && 'Firefox ' + RegExp.$1.split('.')[0]) ||
    (/Chrome\/([\d.]+)/.exec(ua) && 'Chrome ' + RegExp.$1.split('.')[0]) ||
    (/Version\/([\d.]+).*Safari/.exec(ua) && 'Safari ' + RegExp.$1.split('.')[0]) || '未知';
  const kv = pairs => el('div', { class: 'kv' }, pairs.flatMap(([k, v]) => [el('span', { class: 'k' }, k), el('span', { class: 'v' }, v)]));
  const link = (t, href) => el('a', { class: 'btn secondary sm', href, target: '_blank', rel: 'noopener' }, t);
  const wipe = async () => {
    if (!await ask({ title: '清除全部数据？', body: '设置、位置、统计、导入的角色、自定义动画和外框都会删掉，页面随后刷新。', ok: '清除', cancel: '算了', danger: true })) return;
    const ks = [];
    for (let i = 0; i < localStorage.length; i++) { const k = localStorage.key(i); if (k && k.startsWith('qpet.')) ks.push(k); }
    ks.forEach(k => localStorage.removeItem(k));
    try { await idbClear(); } catch (_) {}
    location.reload();
  };
  return {
    el: [
      card(head('版本'), kv([['版本', 'Q Pet ' + VERSION], ['构建', BUILD], ['运行方式', '网页版 │ 纯静态，无后端'], ['浏览器', br]])),
      card(
        el('div', { class: 'row', style: { padding: 0 } }, head('数据位置', '全部存在这个浏览器里，换浏览器或清缓存就没了'),
          el('div', { class: 'ctl' }, button('清除全部数据', 'danger sm', wipe))),
        kv([['设置', 'localStorage · qpet.cfg.v1'], ['位置', 'localStorage · qpet.pos.v1'], ['统计', 'localStorage · qpet.stats.日期'], ['角色 / 动画 / 外框', 'IndexedDB · qpet']])),
      card(head('链接'), el('div', { class: 'links' },
        link('GitHub 仓库', REPO), link('下载桌面版', REPO + '/releases'), link('查看许可证', REPO + '/blob/main/LICENSE'))),
      card(head('关于本程序'),
        el('div', { class: 'para' }, 'Q Pet 把你喜欢的角色放到桌面上：会呼吸、会被按扁、会说话，拖到哪就待在哪。'),
        el('div', { class: 'para' }, '网页版和 Windows 桌面版用同一套帧算法和气泡画法，设置项一一对应。开机自启、真正的透明窗口和全局按键统计只有桌面版能做。'),
        el('div', { class: 'para', style: { color: 'var(--ink2)' } }, '© 2026 Q Pet contributors　│　代码 MIT，角色素材版权归原权利方')),
    ],
  };
}

export const PAGES = {
  bubble: pageBubble, ui: pageUi, char: pageChar, anim: pageAnim,
  voice: pageVoice, import: pageImport, stats: pageStats, about: pageAbout,
};
