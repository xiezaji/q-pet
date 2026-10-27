import { loadCfg, loadChars, chars, on } from './store.js';
import { Pet } from './pet.js';
import { Settings } from './settings.js';
import * as stats from './stats.js';
import { applyTheme, menu } from './ui.js';

const $ = id => document.getElementById(id);
const desk = $('desk');
const deskSize = () => ({ w: desk.clientWidth, h: desk.clientHeight });

async function boot() {
  const cfg = loadCfg();
  applyTheme(cfg.themeColor);
  await loadChars();
  if (!chars.has(cfg.charId)) cfg.charId = 'furina';

  let settings = null;
  const pet = new Pet($('pet-layer'), {
    deskSize,
    onMenu: (x, y) => menu(x, y, [
      { label: '打开设置', onClick: () => settings.show() },
      { label: '隐藏桌宠', onClick: () => settings.setLive('showPet', false) },
      '-',
      { label: '退出桌宠', danger: true, onClick: () => pet.setRunning(false) },
    ]),
  });
  await pet.apply(cfg);
  stats.start(() => (pet.visible && pet.ch ? pet.ch.id : null));

  settings = new Settings({
    pet, cfg, layer: $('win-layer'), deskSize, taskBtn: $('tb-app'),
    onQuit: () => pet.setRunning(false),
  });

  $('tb-start').addEventListener('click', () => settings.show());
  $('tb-app').addEventListener('click', () => settings.taskClick());

  const tray = $('tb-tray');
  const paintTray = () => tray.classList.toggle('off', !pet.visible);
  on('petstate', paintTray);
  paintTray();
  tray.addEventListener('dblclick', () => settings.show());
  tray.addEventListener('contextmenu', e => {
    e.preventDefault();
    const r = tray.getBoundingClientRect();
    const shown = pet.running && settings.saved.showPet;
    menu(r.left, r.top - 4, [
      { label: '打开设置', onClick: () => settings.show() },
      pet.running
        ? { label: shown ? '隐藏桌宠' : '显示桌宠', onClick: () => settings.setLive('showPet', !shown) }
        : { label: '启动桌宠', onClick: () => { pet.setRunning(true); if (!settings.saved.showPet) settings.setLive('showPet', true); } },
      { label: '回到右下角', disabled: !pet.visible, onClick: () => pet.resetPos() },
      '-',
      pet.running
        ? { label: '退出桌宠', danger: true, onClick: () => pet.setRunning(false) }
        : { label: '退出桌宠', disabled: true },
    ]);
  });
  // 单击托盘：她退出了就叫回来
  tray.addEventListener('click', e => {
    if (e.detail > 1) return;
    if (!pet.running) { pet.setRunning(true); if (!settings.saved.showPet) settings.setLive('showPet', true); }
  });

  const clock = $('tb-clock');
  const tickClock = () => {
    const d = new Date();
    const p = n => String(n).padStart(2, '0');
    clock.innerHTML = `${p(d.getHours())}:${p(d.getMinutes())}<br>${d.getFullYear()}/${d.getMonth() + 1}/${d.getDate()}`;
  };
  tickClock();
  setInterval(tickClock, 1000);
  document.addEventListener('contextmenu', e => { if (!e.target.closest('input, textarea')) e.preventDefault(); });
}

boot().catch(e => {
  document.body.append(Object.assign(document.createElement('pre'), {
    textContent: '启动失败：' + (e && e.message || e),
    style: 'position:fixed;left:16px;top:16px;color:#fff;background:#B3261E;padding:10px 14px;font:13px monospace;z-index:99',
  }));
});
