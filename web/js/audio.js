// 单声道播放：和桌面版 PlaySound 一样，新的一条会掐断上一条
const el = new Audio();
el.preload = 'auto';
let token = 0;
let sched = 0;
const durCache = new Map();

export function stop() {
  token++;
  clearTimeout(sched);
  el.pause();
}

// 返回实际时长（毫秒）；拿不到就 0
export function play(url, vol) {
  if (!url) return Promise.resolve(0);
  const my = ++token;
  clearTimeout(sched);
  el.pause();
  el.src = url;
  el.volume = Math.max(0, Math.min(1, vol / 100));
  el.currentTime = 0;
  const p = el.play();
  if (p && p.catch) p.catch(() => {});
  return new Promise(res => {
    const done = () => {
      const d = isFinite(el.duration) ? Math.round(el.duration * 1000) : 0;
      if (my === token && d) durCache.set(url, d);
      res(d);
    };
    if (el.readyState >= 1) done();
    else {
      el.addEventListener('loadedmetadata', done, { once: true });
      el.addEventListener('error', () => res(0), { once: true });
    }
  });
}

export const playing = () => !el.paused && !el.ended;
export const knownDur = url => durCache.get(url) || 0;

// 按压音放完前 40ms 接上松开音；提前松手就立刻放
let pending = null;
export function playPair(press, release, vol) {
  const p = play(press, vol), my = token;
  pending = release ? { url: release, vol, my } : null;
  p.then(d => {
    if (release && d > 0 && pending && pending.my === token) sched = setTimeout(firePending, Math.max(0, d - 40));
  });
}
function firePending() {
  const q = pending;
  pending = null;
  clearTimeout(sched);
  if (q && q.my === token) play(q.url, q.vol);
}
export const releaseNow = () => firePending();

export function setVolume(vol) { el.volume = Math.max(0, Math.min(1, vol / 100)); }
