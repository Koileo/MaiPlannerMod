// 用途：点歌台 —— 选中谱面后 POST /api/jump、卡片上的直点按钮、/api/status 轮询（inGame 时禁用）、
//       以及「手机扫码点歌」二维码（用 web/qr.js 现画，不引 CDN）。
// 状态：现行。只读 DOM，不改 songcard 的卡片结构：难度行上的点歌按钮是运行时挂上去的。
// 契约：POST /api/jump {internalId, chartid, type}；/api/status 的 inGame 为真时按钮禁用。
import { $, api } from "./core.js";

const JUMP_TYPE = { std: "std", dx: "dx" };     // 宴谱按 dx 走（chartid 用 0）
const STATUS_POLL_MS = 1500;

let selected = null;        // {internalId, chartid, type, title, diff, level}
let status = { connected: false, inGame: false, inSelect: false, ready: false, lan: true };
let requesting = false;
let refreshing = false;
let pollTimer = 0;
let scanUrls = [];
let scanSig = "";
let msgTimer = 0;

function esc(text) {
  return String(text == null ? "" : text)
    .replace(/[&<>"]/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" }[c]));
}

/* ── 提示文案：成功/失败都用同一个位置，失败不改布局 ── */
function setMsg(text, kind) {
  const el = $("jump-msg");
  if (!el) return;
  el.textContent = text || "";
  el.className = "jumpmsg" + (kind ? " " + kind : "");
  const assistMessage = $("assist-jump-message");
  if (assistMessage) {
    assistMessage.textContent = text || "";
    assistMessage.className = "jumpmsg" + (kind ? " " + kind : "");
  }
  el.classList.remove("note-fade");
  clearTimeout(msgTimer);
  if (!text) return;
  msgTimer = setTimeout(() => {
    el.classList.add("note-fade");
    setTimeout(() => {
      if (el.classList.contains("note-fade")) { el.textContent = ""; el.classList.remove("note-fade"); }
    }, 700);
  }, kind === "bad" ? 9000 : 5000);
}

/* ── 状态（inGame 时按钮禁用） ── */
function applyStatus(next) {
  status = next || { connected: false };
  const sel = $("jump-sel");
  const go = $("jump-go");
  const stateEl = $("jump-state");
  const bar = $("jumpbar");
  if (bar) bar.classList.toggle("in-game", !!status.inGame);

  let why = "";
  if (!status.connected) why = "尚未连接机台";
  else if (status.inGame) why = "正在游玩，先结束这一局";
  else if (status.ready === false) why = "游戏数据还在加载";
  else if (!status.inSelect) why = "等待机台进入选曲界面";
  else if (status.busy || requesting) why = "正在切换曲目…";
  if (stateEl) stateEl.textContent = why;
  const connection = $("connection-state");
  if (connection) {
    connection.textContent = !status.connected ? "未连接机台" : why || "机台已连接";
    connection.dataset.state = !status.connected ? "offline" : why ? "pending" : "ready";
  }
  const total = $("library-total");
  if (total && status.songs != null) total.textContent = status.songs > 0 ? status.songs + " 首曲目 · 游戏曲库" : "等待游戏载入曲库";
  document.querySelectorAll(".sc-jump").forEach(button => { button.disabled = !!why; });

  if (go) {
    go.disabled = !selected || !!why;
    go.title = why || "把选中的谱面推到机台";
  }
  const clear = $("jump-clear");
  if (clear) clear.hidden = !selected;
  if (sel) {
    sel.textContent = selected
      ? selected.title + " · " + selected.diff + (selected.level == null ? "" : " " + selected.level)
      : "选择一张谱面，准备下一首";
    sel.classList.toggle("on", !!selected);
  }
}

async function refreshStatus(force) {
  if (refreshing) return;
  refreshing = true;
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), 8000);
  try {
    const data = await api("/api/status", { signal: controller.signal });
    applyStatus({ connected: true, inGame: !!data.inGame, inSelect: !!data.inSelect,
      ready: data.ready !== false, busy: !!data.busy, songs: data.songs, lan: data.lan !== false });
    if (Array.isArray(data.scanUrls)) renderQr(data.scanUrls);
  } catch (err) {
    applyStatus({ ...status, connected: false });
    if (force) setMsg("状态读取失败：" + err.message, "bad");
  } finally {
    clearTimeout(timer);
    refreshing = false;
  }
}

function startPoll() {
  clearInterval(pollTimer);
  pollTimer = setInterval(() => {
    if (document.hidden) return;
    refreshStatus(false);
  }, STATUS_POLL_MS);
}

/* ── 选中谱面 ── */
function selectRow(row) {
  const card = row.closest(".songcard");
  if (!card || !card.__song) return;
  const song = card.__song;
  const sheet = (song.sheets || []).find((x) => String(x.chartid) === row.dataset.chartid);
  if (!sheet) return;
  const level = song.family === "utage" || song.family === "utage2p"
    ? (sheet.levelText || sheet.level) : sheet.level;
  selected = {
    internalId: song.internalId,
    chartid: Number(sheet.chartid),
    type: JUMP_TYPE[song.family] || "dx",
    title: song.title || "",
    diff: String(sheet.difficulty || "").toUpperCase(),
    level: level == null ? null : level,
  };
  // 高亮：新旧卡片一起清（翻页后旧卡片已经不在了，所以按整个列表清）
  document.querySelectorAll("#song-cards .sc-row.sel, #pool-cards .sc-row.sel, #assist-preview .sc-row.sel")
    .forEach((el) => el.classList.remove("sel"));
  row.classList.add("sel");
  setMsg("", "");
  applyStatus(status);
  refreshStatus(true);
}

function clearSelection() {
  selected = null;
  document.querySelectorAll(".sc-row.sel").forEach((el) => el.classList.remove("sel"));
  setMsg("", "");
  applyStatus(status);
}

/* ── 点歌 ── */
async function doJump() {
  if (!selected || requesting || !status.connected || !status.inSelect || !status.ready || status.inGame || status.busy) return;
  requesting = true;
  const body = { internalId: selected.internalId, chartid: selected.chartid, type: selected.type };
  const go = $("jump-go");
  if (go) go.disabled = true;
  applyStatus(status);
  setMsg("正在点歌…", "");
  try {
    const data = await api("/api/jump", {
      method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(body),
    });
    setMsg(data.msg || "已发送", data.superseded ? "" : "ok");
    if (navigator.vibrate) { try { navigator.vibrate(18); } catch (_) { /* 忽略 */ } }
  } catch (err) {
    // 契约里的失败码：409 不在选曲界面、503 数据没加载完、404 没这首
    const why = err.status === 409 ? (err.message || "现在不在选曲界面")
      : err.status === 503 ? "游戏数据还在加载"
        : err.status === 404 ? "曲库里没有这首" : (err.message || "点歌失败");
    setMsg(why, "bad");
  } finally {
    requesting = false;
    applyStatus(status);
    await refreshStatus(true);
  }
}

/* ── 卡片上的按钮（不改 songcard 结构，运行时追加到难度行里） ── */
function attachCardButtons() {
  if (!window.MutationObserver) return;
  const decorateRow = (row) => {
    if (row.querySelector(".sc-jump")) return;
    const jump = document.createElement("button");
    jump.type = "button";
    jump.className = "sc-jump";
    jump.textContent = "点歌";
    jump.title = "把这张谱推到机台";
    jump.disabled = !status.connected || !status.inSelect || !status.ready || status.inGame || status.busy || requesting;
    jump.setAttribute("aria-label", "点歌：" + (row.closest(".songcard")?.dataset.title || "") + " " + row.dataset.diff);
    jump.addEventListener("click", (e) => {
      e.stopPropagation();                 // 别冒到「打开谱面详情」的点击上
      selectRow(row);
      doJump();
    });
    const detail = document.createElement("button");
    detail.type = "button";
    detail.className = "sc-detail";
    detail.textContent = "详情";
    detail.title = "看这张谱的详情";
    detail.setAttribute("aria-label", "查看 " + row.dataset.diff + " 谱面详情");
    row.appendChild(jump);
    row.appendChild(detail);
  };
  const decorate = (card) => {
    if (card.dataset.jumpReady) return;
    card.dataset.jumpReady = "1";
    card.querySelectorAll(".sc-row[data-chartid]").forEach((row) => {
      if (row.classList.contains("sc-row-empty") || row.classList.contains("sc-type-row")) return;
      decorateRow(row);
    });
  };
  const watch = (container) => {
    if (!container) return;
    const obs = new MutationObserver(() => {
      container.querySelectorAll(".songcard").forEach(decorate);
    });
    obs.observe(container, { childList: true, subtree: true });
    container.querySelectorAll(".songcard").forEach(decorate);
  };
  watch($("song-cards"));
  watch($("pool-cards"));
  watch($("assist-preview"));
}

/* ── 二维码 ── */
async function renderQr(urls) {
  const list = (urls || []).filter(Boolean);
  const sig = list.join("|");
  if (sig === scanSig) return;
  scanSig = sig;
  scanUrls = list;
  const box = $("scan-qr");
  const urlEl = $("scan-url");
  const note = $("scan-note");
  if (!box) return;
  box.innerHTML = "";
  if (urlEl) urlEl.textContent = "";
  if (!list.length) {
    if (note) note.textContent = status.lan === false
      ? "局域网被配置关掉了：打开配置里的「局域网」才扫得到。"
      : "还没拿到局域网地址（确认配置里「局域网」是开的，且机台在 Wi-Fi 上）。";
    box.innerHTML = '<p class="hint">暂时没有可扫的地址</p>';
    return;
  }
  if (note) note.textContent = "";
  const url = list[0];
  if (urlEl) urlEl.textContent = url;
  try {
    // 动态 import：qr.js 只在这一块用到，页面其它地方不为它阻塞
    const qr = await import("./qr.js");
    const canvas = document.createElement("canvas");
    canvas.className = "scanqr-canvas";
    canvas.width = 220; canvas.height = 220;
    canvas.setAttribute("role", "img");
    canvas.setAttribute("aria-label", "手机扫码点歌的二维码：" + url);
    box.appendChild(canvas);
    qr.qrDrawCanvas(url, canvas, { size: 220, quiet: 4 });
    if (list.length > 1) {
      const pick = document.createElement("select");
      pick.className = "scanqr-pick";
      list.forEach((item, i) => {
        const opt = document.createElement("option");
        opt.value = item;
        opt.textContent = item;
        if (i === 0) opt.selected = true;
        pick.appendChild(opt);
      });
      pick.addEventListener("change", () => {
        if (urlEl) urlEl.textContent = pick.value;
        qr.qrDrawCanvas(pick.value, canvas, { size: 220, quiet: 4 });
      });
      box.appendChild(pick);
    }
  } catch (err) {
    box.innerHTML = '<p class="hint">二维码生成失败：' + esc(err.message) + "</p>";
  }
}

/* ── 绑定 ── */
export function bindJump() {
  const bar = $("jumpbar");
  if (!bar) return;

  if ($("jump-go")) $("jump-go").onclick = () => { doJump(); };
  if ($("jump-clear")) $("jump-clear").onclick = () => { clearSelection(); };

  // 点难度行 = 选中（不点歌）；点行里的「点歌」按钮 = 选中并立刻点歌；「详情」交给 songcard.js
  document.addEventListener("click", (e) => {
    const row = e.target.closest("#song-cards .sc-row[data-chartid], #pool-cards .sc-row[data-chartid], #assist-preview .sc-row[data-chartid]");
    if (!row) return;
    if (row.classList.contains("sc-row-empty") || row.classList.contains("sc-type-row")) return;
    if (e.target.closest(".sc-jump")) return;      // 交给按钮自己处理
    selectRow(row);
  });

  if ($("scan-refresh")) $("scan-refresh").onclick = () => { scanSig = ""; refreshStatus(true); };
  if ($("scan-copy")) $("scan-copy").onclick = async () => {
    const value = $("scan-url")?.textContent;
    if (!value) return;
    try {
      if (navigator.clipboard && window.isSecureContext) await navigator.clipboard.writeText(value);
      else {
        const input = document.createElement("textarea");
        input.value = value;
        input.style.cssText = "position:fixed;opacity:0";
        document.body.appendChild(input);
        input.select();
        const copied = document.execCommand("copy");
        input.remove();
        if (!copied) throw new Error("copy unavailable");
      }
      if ($("scan-note")) $("scan-note").textContent = "地址已复制，发到手机即可打开。";
    } catch {
      if ($("scan-note")) $("scan-note").textContent = "请长按或选中上方地址进行复制。";
    }
  };
  document.querySelectorAll("[data-open-scan]").forEach(button => button.addEventListener("click", () => {
    document.querySelector('.tab[data-page="songs"]')?.click();
    const box = $("scanbox");
    if (box) { box.open = true; box.scrollIntoView({ block: "nearest" }); }
  }));
  document.addEventListener("keydown", event => {
    if (event.target.matches(".chart-row") && (event.key === "Enter" || event.key === " ")) {
      event.preventDefault();
      selectRow(event.target);
    }
  });
  document.addEventListener("visibilitychange", () => { if (!document.hidden) refreshStatus(false); });

  // 进歌曲页才轮询（省事，也避免在没渲染卡片时算状态）
  document.addEventListener("click", (e) => {
    const tab = e.target.closest('.tab');
    if (tab) { refreshStatus(true); startPoll(); }
  });
  window.addEventListener("hashchange", () => {
    if (location.hash.slice(1) === "songs") { refreshStatus(true); startPoll(); }
  });

  applyStatus(status);
  refreshStatus(true);
  startPoll();
  attachCardButtons();
}

export { clearSelection, doJump, refreshStatus, selectRow };
