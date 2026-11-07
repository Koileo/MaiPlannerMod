// 前端验收：契约行为 + 交互 + 布局 + 无异常 + 无外部 CDN。用法 node verify/web-accept.mjs
import fs from "node:fs";
import { spawn } from "node:child_process";
import path from "node:path";
import { fileURLToPath } from "node:url";

const CHROME = process.env.CHROME_PATH || "C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe";
const PORT = 9350;
const BASE = "http://127.0.0.1:18795";
const HERE = path.dirname(fileURLToPath(import.meta.url));
const OUT = path.join(HERE, "_run");
fs.rmSync(OUT, { recursive: true, force: true });
fs.mkdirSync(OUT, { recursive: true });

const srv = spawn(process.execPath, [path.join(HERE, "web-serve.mjs")], { stdio: "ignore", windowsHide: true });
const chrome = spawn(CHROME, ["--headless=new", "--disable-gpu", "--no-sandbox", "--no-first-run",
  "--remote-debugging-port=" + PORT, "--user-data-dir=" + path.join(OUT, "prof"), "about:blank"], { stdio: "ignore", windowsHide: true });
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

async function getWs() {
  for (let i = 0; i < 40; i++) {
    try {
      const l = await (await fetch(`http://127.0.0.1:${PORT}/json/list`)).json();
      const p = l.find((t) => t.type === "page");
      if (p) return p.webSocketDebuggerUrl;
    } catch (_) { /* retry */ }
    await sleep(250);
  }
  throw new Error("chrome 起不来");
}

let id = 0; const pending = new Map(); let ws; const exc = []; const httpErr = [];
const send = (m, p) => new Promise((res, rej) => { const n = ++id; pending.set(n, { res, rej }); ws.send(JSON.stringify({ id: n, method: m, params: p })); });

const CHECKS = [];
function check(name, value, expect) {
  const ok = typeof expect === "function" ? expect(value) : value === expect;
  CHECKS.push({ name, ok, value });
}

async function main() {
  ws = new WebSocket(await getWs());
  await new Promise((r) => { ws.onopen = r; });
  ws.onmessage = (e) => {
    const m = JSON.parse(e.data);
    if (m.id && pending.has(m.id)) {
      const p = pending.get(m.id);
      pending.delete(m.id);
      m.error ? p.rej(new Error(JSON.stringify(m.error))) : p.res(m.result);
      return;
    }
    if (m.method === "Runtime.exceptionThrown") { const d = m.params.exceptionDetails; exc.push((d.exception && (d.exception.description || d.exception.value)) || d.text); }
    if (m.method === "Network.responseReceived" && m.params.response.status >= 400 && !m.params.response.url.includes("favicon")) {
      httpErr.push(m.params.response.status + " " + m.params.response.url);
    }
  };
  await send("Page.enable"); await send("Runtime.enable"); await send("Network.enable");
  const ev = async (x) => {
    const r = await send("Runtime.evaluate", { expression: x, awaitPromise: true, returnByValue: true });
    if (r.exceptionDetails) throw new Error(JSON.stringify(r.exceptionDetails));
    return r.result.value;
  };
  const go = async (p) => { await send("Page.navigate", { url: BASE + p }); await sleep(2700); };
  const until = async (expression) => {
    for (let attempt = 0; attempt < 30; attempt++) {
      if (await ev(expression)) return true;
      await sleep(200);
    }
    return false;
  };
  const clickAt = async (x, y) => { for (const t of ["mousePressed", "mouseReleased"]) await send("Input.dispatchMouseEvent", { type: t, x, y, button: "left", clickCount: 1 }); };

  await send("Emulation.setDeviceMetricsOverride", { width: 1440, height: 950, deviceScaleFactor: 1, mobile: false });
  await go("/#songs");

  check("页面标题", await ev("document.title"), "打歌计划器");
  check("卡片渲染", await ev("document.querySelectorAll('#song-cards .songcard').length"), (v) => v > 0);
  check("每张卡都有点歌按钮", await ev("(() => { const c = document.querySelectorAll('#song-cards .songcard').length; const b = document.querySelectorAll('#song-cards .sc-jump').length; return c > 0 && b >= c; })()"), true);
  check("二维码已画", await ev("!!document.querySelector('#scan-qr canvas')"), true);
  check("二维码地址来自 scanUrls", await ev("document.getElementById('scan-url').textContent"), (v) => /^http:\/\/192\.168\.1\.9:18795\/scan$/.test(v));
  check("无外部 CDN", await ev("[...document.querySelectorAll('script[src],link[href]')].map(e => e.src || e.href).filter(u => /^https?:\\/\\//.test(u) && !/127\\.0\\.0\\.1|192\\.168/.test(u))"), (v) => v.length === 0);
  check("缺曲绘用占位块", await ev("document.querySelectorAll('#song-cards span.sc-jacket').length"), (v) => v > 0);
  check("桌面无横向溢出", await ev("document.documentElement.scrollWidth - document.documentElement.clientWidth"), 0);
  check("合并版不再显示数据同步页", await ev("document.querySelectorAll('.tab').length === 4 && !document.querySelector('section.page[data-page=\"db\"]')"), true);
  check("曲库数量与连接状态在顶部可见", await ev("document.getElementById('library-total').textContent.includes('130') && document.getElementById('connection-state').dataset.state === 'ready'"), true);
  check("桌面曲卡保持可读字号和点歌触控尺寸", await ev("(() => { const b = document.querySelector('.sc-jump'); const t = document.querySelector('.sc-title span'); return b.getBoundingClientRect().height >= 30 && parseFloat(getComputedStyle(t).fontSize) >= 16; })()"), true);
  await ev("document.getElementById('song-quick-search').value = '曲目 130'; document.getElementById('song-quick-form').requestSubmit()");
  await sleep(200);
  check("常驻搜索可直接过滤曲库", await ev("document.querySelectorAll('#song-cards .songcard').length"), 1);
  await ev("document.getElementById('song-searchbtn').click()");
  await sleep(150);

  // 点难度行 = 选中，不弹详情
  const rowBox = await ev("(() => { const r = document.querySelectorAll('#song-cards .sc-row[data-chartid]')[2].getBoundingClientRect(); return { x: Math.round(r.left + r.width / 2), y: Math.round(r.top + r.height / 2) }; })()");
  await clickAt(rowBox.x, rowBox.y);
  await sleep(400);
  check("点行只选中 1 行", await ev("document.querySelectorAll('.sc-row.sel').length"), 1);
  check("点行不弹详情", await ev("document.getElementById('songmodal').hidden"), true);

  // 点曲绘 → 曲目详情页
  const baseBox = await ev("(() => { const r = document.querySelector('#song-cards .sc-base').getBoundingClientRect(); return { x: Math.round(r.left + r.width / 2), y: Math.round(r.top + r.height / 2) }; })()");
  await clickAt(baseBox.x, baseBox.y);
  await sleep(1000);
  check("点曲绘开曲目页", await ev("!document.getElementById('songfull').hidden"), true);
  check("曲目页有大图位", await ev("!!document.querySelector('#sf2-body .sf2-jacket')"), true);
  await ev("document.querySelector('#songfull .sf2-diff') && document.querySelector('#songfull .sf2-diff').click()");
  await sleep(700);
  check("曲目页难度开单谱页", await ev("!document.getElementById('songmodal').hidden"), true);
  check("弹层锁滚动", await ev("document.documentElement.classList.contains('no-scroll')"), true);
  await ev("document.getElementById('sm-close').click()");
  await ev("document.getElementById('sf2-close').click()");
  await sleep(300);
  check("关弹层解锁滚动", await ev("document.documentElement.classList.contains('no-scroll')"), false);

  // 点歌
  await ev("document.querySelector('#song-cards .sc-jump').click()");
  await sleep(900);
  check("点歌成功提示", await ev("document.getElementById('jump-msg').textContent"), (v) => /^已跳转: 曲目 \d+$/.test(v));
  check("成功提示样式", await ev("document.getElementById('jump-msg').className"), "jumpmsg ok");

  // inGame 禁用 + 409 文案
  await fetch(BASE + "/__ing?on=1");
  await ev("document.querySelector('#song-cards .sc-jump').click()");
  await sleep(1800);
  check("inGame 时点歌按钮禁用", await ev("document.getElementById('jump-go').disabled"), true);
  check("inGame 有说明", await ev("document.getElementById('jump-state').textContent"), (v) => v.length > 0);
  await fetch(BASE + "/__ing?on=0");
  await ev("document.getElementById('jump-clear').click()");
  await sleep(300);
  check("取消选择清高亮", await ev("document.querySelectorAll('.sc-row.sel').length"), 0);

  // config 500 仍能进页面
  await fetch(BASE + "/__cfg?on=1");
  await go("/#db");
  check("旧数据页地址自动返回点歌台", await ev("location.hash === '#songs' && !document.querySelector('section.page[data-page=\"songs\"]').hidden"), true);
  check("config 失败仍能浏览曲库", await ev("document.querySelectorAll('#song-cards .songcard').length"), (v) => v > 0);
  check("config 失败不报异常", exc.length, 0);
  await fetch(BASE + "/__cfg?on=0");
  await go("/#assistant");
  check("直接进入助手页会加载牌桌", await until("document.querySelectorAll('#assist-table .assist-card').length === 3"), true);
  check("助手三首预览显示谱面卡片", await until("document.querySelectorAll('#assist-preview .songcard').length === 3"), true);
  check("预览只展示抽中的难度", await ev("[...document.querySelectorAll('#assist-preview .songcard')].every(c => c.querySelectorAll('.sc-row[data-chartid]').length === 1)"), true);
  check("开始打歌无需填写机台 IP 或端口", await ev("!document.querySelector('#assist-host, #assist-mod-port, #assist-callback-port')"), true);
  await ev("document.getElementById('assist-start').click()");
  check("无需连接参数即可启动助手", await until("document.getElementById('assist-status').textContent.includes('运行中')"), true);
  await ev("document.getElementById('assist-stop').click()");
  check("停止助手", await until("document.getElementById('assist-status').textContent.includes('待启动')"), true);
  await fetch(BASE + "/__users?mode=none");
  check("退出登录后清除旧玩家", await until("document.querySelectorAll('#user-cards .usercard').length === 0 && document.getElementById('assist-table').textContent.includes('登录')"), true);
  await fetch(BASE + "/__users?mode=2p");
  check("仅 2P 登录自动识别玩家 ID", await until("document.querySelector('#user-cards .usercard.selected')?.dataset.id === '1'"), true);
  check("登录后自动恢复助手预览", await until("document.querySelectorAll('#assist-preview .songcard').length === 3"), true);
  check("空的 1P 槽位不会作为玩家显示", await ev("document.querySelectorAll('#user-cards .usercard').length === 1"), true);
  await go("/#scores");
  check("成绩页显示 2P 玩家", await until("document.getElementById('now-name').textContent === '玩家乙'"), true);
  await fetch(BASE + "/__users?mode=1p");
  check("玩家变化后自动更新成绩页", await until("document.getElementById('now-name').textContent === '玩家甲'"), true);
  await go("/#assistant");
  await ev("document.querySelector('[data-open-scan]').click()");
  check("全局手机入口会展开二维码", await until("document.getElementById('scanbox').open && !document.querySelector('section.page[data-page=\"songs\"]').hidden"), true);

  // 手机扫码页
  await send("Emulation.setDeviceMetricsOverride", { width: 390, height: 844, deviceScaleFactor: 2, mobile: true });
  await go("/scan");
  check("扫码页标题", await ev("document.title"), "手机点歌台");
  check("扫码页有回首页链接", await ev("document.querySelector('.back').getAttribute('href')"), "/");
  check("扫码页手机无横向溢出", await ev("document.documentElement.scrollWidth - document.documentElement.clientWidth"), 0);
  check("扫码页顶部搜索固定", await ev("getComputedStyle(document.querySelector('.top')).position"), "sticky");
  check("扫码页单列", await ev("getComputedStyle(document.querySelector('.song')).gridTemplateColumns"), (v) => v.split(" ").length === 2);
  check("难度按钮高度 ≥38", await ev("Math.round(document.querySelector('.song .diff').getBoundingClientRect().height)"), (v) => v >= 38);
  // 难度只在可用时出现：3..5 且都有
  check("难度枚举止于 3..5", await ev("(() => { const n = [...document.querySelectorAll('.song')].map(c => c.querySelectorAll('.diff').length); return [Math.min(...n), Math.max(...n)]; })()"), (v) => v[0] >= 1 && v[1] <= 5);
  // 搜索：四个字段都能命中，且不发请求
  const before = (await (await fetch(BASE + "/__stats")).json()).songs;
  for (const [field, text] of [["曲名", "曲目 1"], ["作曲", "作曲者甲"], ["谱师", "谱师乙"], ["别名", "别名"]]) {
    await ev(`(() => { const i = document.getElementById('q'); i.value = ${JSON.stringify(text)}; i.dispatchEvent(new Event('input', { bubbles: true })); })()`);
    await sleep(320);
    check("搜索命中-" + field, await ev("document.querySelectorAll('.song').length"), (v) => v > 0);
  }
  await sleep(400);
  check("搜索不重复请求 /api/songs", (await (await fetch(BASE + "/__stats")).json()).songs - before, 0);
  // 点难度 → toast
  await ev("document.querySelector('.song .diff').click()");
  await sleep(800);
  check("扫码页点歌有 toast", await ev("document.getElementById('toast').textContent"), (v) => /^已跳转: /.test(v));
  check("扫码页不加载桌面模块", await ev("performance.getEntriesByType('resource').some(r => /\\/(core|stats|db|songlist)\\.js/.test(r.name))"), false);

  await fetch(BASE + "/__ing?on=1");
  check("手机游玩中禁用全部点歌按钮", await until("[...document.querySelectorAll('.diff')].every(b => b.disabled)"), true);
  check("手机提示返回选曲且不声称排队", await ev("document.getElementById('state').textContent"), (v) => v.includes("返回选曲") && !v.includes("排队"));
  await fetch(BASE + "/__ing?on=0");
  check("返回选曲自动恢复点歌", await until("!!document.querySelector('.diff:not(:disabled)')"), true);

  await fetch(BASE + "/__offline?on=1");
  check("断线后禁用点歌并显示连接状态", await until("document.getElementById('state').textContent === '连不上机台' && [...document.querySelectorAll('.diff')].every(b => b.disabled)"), true);
  await fetch(BASE + "/__offline?on=0");
  await ev("window.dispatchEvent(new Event('online'))");
  check("网络恢复后无需刷新页面", await until("!!document.querySelector('.diff:not(:disabled)')"), true);

  // 先打开扫码页，再让游戏曲库就绪。
  await fetch(BASE + "/__library?mode=empty");
  await go("/scan");
  check("启动空曲库提供等待说明和重试", await ev("document.getElementById('list').textContent.includes('自动载入') && !!document.querySelector('#more button')"), true);
  await fetch(BASE + "/__library?mode=ready");
  check("游戏曲库就绪后自动补载", await until("document.querySelectorAll('.song').length === 60"), true);

  // 首次曲库请求失败也应自动恢复。
  await fetch(BASE + "/__library?mode=fail");
  await go("/scan");
  check("曲库失败提供重试按钮", await ev("document.getElementById('list').textContent.includes('曲库读取失败') && !!document.querySelector('#more button')"), true);
  await fetch(BASE + "/__library?mode=ready");
  check("曲库请求失败后自动恢复", await until("document.querySelectorAll('.song').length === 60"), true);

  await ev("(() => { const i = document.getElementById('q'); i.value = '不会命中'; i.dispatchEvent(new Event('input')); document.getElementById('q-clear').click(); })()");
  await sleep(200);
  check("清空搜索不会被旧防抖任务覆盖", await ev("document.querySelectorAll('.song').length"), 60);
  check("全程无 JS 异常", exc, (v) => v.length === 0);

  console.log("");
  let failed = 0;
  for (const c of CHECKS) {
    if (!c.ok) failed += 1;
    console.log((c.ok ? "PASS " : "FAIL ") + c.name + (c.ok ? "" : "  -> " + JSON.stringify(c.value)));
  }
  console.log("\n" + (CHECKS.length - failed) + "/" + CHECKS.length + " 通过" + (failed ? "，" + failed + " 失败" : ""));
  if (exc.length) console.log("异常：", exc);
  const unexpected = httpErr.filter((e) => !/404 http:.*\/api\/run$|409 http:.*\/api\/jump$|500 http:.*\/api\/config$|503 http:.*\/api\/(songs|status)(\?.*)?$/.test(e));
  if (unexpected.length) console.log("意外 HTTP 错误：", unexpected);
  process.exitCode = failed ? 1 : 0;
}

main().catch((e) => { console.log("致命：", String(e), exc); process.exitCode = 1; })
  .finally(async () => { await sleep(150); try { ws && ws.close(); } catch (_) {} chrome.kill(); srv.kill(); });
