// 角色帧：照搬桌面版 sprites.py 的帧序与数学，只是不预先烘焙，每帧现画。
// 帧序：待机 → 按键 → 点击 → 拖动；脚底锚在画布底部居中。
export const IDLE_N = 24, PRESS_N = 6, CLICK_N = 6, DRAG_N = 8;
const BOTTOM_PAD = 6;
const PRESS = [[1, 1], [1.06, 0.93], [1.10, 0.86], [1.08, 0.85], [1.03, 0.95], [1, 1]];
const CLICK = [[1, 1], [1.09, 0.88], [1.15, 0.81], [1.11, 0.84], [1.03, 0.96], [1, 1]];
const CLICK_DY = [0, -2, -5, -4, -1, 0];
const DRAG_ANGLE = [-5, -3, 0, 3, 5, 3, 0, -3];
const DRAG_DX = [6, 3, 0, -3, -6, -3, 0, 3];
const DRAG_LIFT = 14, DRAG_SHRINK = 0.95;

export const canvasW = H => Math.round(H * 320 / 300);
export const canvasH = H => Math.round(H * 324 / 300);

// 大图一步缩到很小会出锯齿：先对半缩，最后一步交给高质量插值
function downscale(src, w, h) {
  let cur = src, cw = src.naturalWidth || src.width, ch = src.naturalHeight || src.height;
  while (cw > w * 2 && ch > h * 2) {
    const nw = Math.max(w, Math.round(cw / 2)), nh = Math.max(h, Math.round(ch / 2));
    const c = document.createElement('canvas');
    c.width = nw; c.height = nh;
    const g = c.getContext('2d');
    g.imageSmoothingQuality = 'high';
    g.drawImage(cur, 0, 0, nw, nh);
    cur = c; cw = nw; ch = nh;
  }
  const out = document.createElement('canvas');
  out.width = Math.max(1, w); out.height = Math.max(1, h);
  const g = out.getContext('2d');
  g.imageSmoothingQuality = 'high';
  g.drawImage(cur, 0, 0, out.width, out.height);
  return out;
}

export class Sprite {
  // custom: { idle, press, click, drag } → ImageBitmap[] | null
  constructor(img, H, amps = {}, custom = {}, res = 1) {
    this.H = H;
    this.W = canvasW(H);
    this.CH = canvasH(H);
    const iw = img.naturalWidth || img.width, ih = img.naturalHeight || img.height;
    this.cw = Math.max(1, Math.round(iw * H / ih));
    this.res = res;
    this.base = downscale(img, Math.round(this.cw * res), Math.round(H * res));
    this.a = {
      idle: Math.max(0, +amps.idle || 0), press: Math.max(0, +amps.press || 0),
      click: Math.max(0, +amps.click || 0), drag: Math.max(0, +amps.drag || 0),
    };
    this.custom = {};
    for (const k of ['idle', 'press', 'click', 'drag']) {
      let f = custom[k];
      if (f && f.length) {
        // 按压 / 点击要有「压到最扁再回弹」的余量，帧太少就补最后一帧
        if ((k === 'press' || k === 'click') && f.length < 4) f = f.concat(Array(4 - f.length).fill(f[f.length - 1]));
        this.custom[k] = f;
      }
    }
    this.n = {
      idle: this.custom.idle ? this.custom.idle.length : IDLE_N,
      press: this.custom.press ? this.custom.press.length : PRESS_N,
      click: this.custom.click ? this.custom.click.length : CLICK_N,
      drag: this.custom.drag ? this.custom.drag.length : DRAG_N,
    };
    this.hit = null;
    this.hitIdx = -1;
  }

  get pressStart() { return this.n.idle; }
  get clickStart() { return this.n.idle + this.n.press; }
  get dragStart() { return this.n.idle + this.n.press + this.n.click; }
  get total() { return this.dragStart + this.n.drag; }

  seg(i) {
    const n = this.n;
    if (i < n.idle) return ['idle', i];
    i -= n.idle; if (i < n.press) return ['press', i];
    i -= n.press; if (i < n.click) return ['click', i];
    i -= n.click; return ['drag', Math.min(i, n.drag - 1)];
  }

  // 在 ctx 上画第 i 帧；k = 每个 CSS 像素对应的画布像素
  draw(ctx, i, k = 1) {
    const [kind, j] = this.seg(i);
    const W = this.W, CH = this.CH, H = this.H, cw = this.cw;
    const custom = this.custom[kind];
    ctx.save();
    ctx.imageSmoothingQuality = 'high';
    if (custom) {
      const f = custom[j % custom.length];
      const fw = f.width, fh = f.height;
      const s = Math.min(W / fw, (CH - BOTTOM_PAD) / fh);
      const dw = fw * s, dh = fh * s;
      ctx.drawImage(f, ((W - dw) / 2) * k, (CH - dh - BOTTOM_PAD) * k, dw * k, dh * k);
      ctx.restore();
      return;
    }
    let sx = 1, sy = 1, dx = 0, dy = 0, ang = 0, shrink = 1;
    if (kind === 'idle') {
      const t = 2 * Math.PI * j / IDLE_N, a = this.a.idle;
      sy = 1 + 0.010 * a * (0.5 + 0.5 * Math.sin(t));
      dy = 2.5 * a * Math.sin(t);
    } else if (kind === 'press') {
      const a = this.a.press;
      sx = 1 + (PRESS[j][0] - 1) * a; sy = 1 + (PRESS[j][1] - 1) * a;
    } else if (kind === 'click') {
      const a = this.a.click;
      sx = 1 + (CLICK[j][0] - 1) * a; sy = 1 + (CLICK[j][1] - 1) * a;
      dy = CLICK_DY[j] * a;
    } else {
      const a = this.a.drag;
      shrink = DRAG_SHRINK; ang = DRAG_ANGLE[j] * a; dx = DRAG_DX[j] * a; dy = -DRAG_LIFT * a;
    }
    const w = Math.max(1, Math.round(cw * sx * shrink)), h = Math.max(1, Math.round(H * sy * shrink));
    const x = Math.floor((W - w) / 2) + dx, y = CH - h + dy - BOTTOM_PAD;
    if (ang) {
      // PIL 的 rotate 正角度是逆时针，canvas 正角度是顺时针
      ctx.translate((x + w / 2) * k, (y + h / 2) * k);
      ctx.rotate(-ang * Math.PI / 180);
      ctx.drawImage(this.base, (-w / 2) * k, (-h / 2) * k, w * k, h * k);
    } else {
      ctx.drawImage(this.base, x * k, y * k, w * k, h * k);
    }
    ctx.restore();
  }

  // 命中测试：透明处（alpha < 24）点击穿透
  alphaAt(i, x, y) {
    if (x < 0 || y < 0 || x >= this.W || y >= this.CH) return 0;
    if (!this.hit) {
      this.hit = document.createElement('canvas');
      this.hit.width = this.W; this.hit.height = this.CH;
      this.hitCtx = this.hit.getContext('2d', { willReadFrequently: true });
    }
    if (this.hitIdx !== i) {
      this.hitCtx.clearRect(0, 0, this.W, this.CH);
      this.draw(this.hitCtx, i, 1);
      this.hitIdx = i;
    }
    return this.hitCtx.getImageData(x | 0, y | 0, 1, 1).data[3];
  }
}
