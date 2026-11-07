// 用途：手机扫码点歌页 —— 缓存曲库，本地过滤 / 排序 / 分页；断线或游戏就绪后自动补载。
// 状态：现行。窄屏单列 + 分页（每页 60 首，「加载更多」追加）。
// 契约：/api/jump 失败码 409（不在选曲界面）/ 503（没加载完）/ 404（没这首）；/api/status.inGame 为真时要提示。
// 手机页独立加载，避免引入桌面 planner 的整个模块依赖树。
async function api(path, options = {}) {
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), path === "/api/jump" ? 15000 : 10000);
  try {
    const response = await fetch(path, { ...options, signal: controller.signal });
    const data = await response.json();
    if (!response.ok || (data.ok === false && !data.superseded)) {
      const error = new Error(data.error || data.info || "请求失败");
      error.status = response.status;
      throw error;
    }
    return data;
  } catch (error) {
    if (error.name === "AbortError") {
      throw new Error(path === "/api/jump" ? "点歌响应超时，请确认机台状态" : "连接超时，请检查与机台的网络连接");
    }
    throw error;
  } finally {
    clearTimeout(timer);
  }
}

const PAGE_SIZE = 60;
const DIFF_NAMES = ["BAS", "ADV", "EXP", "MAS", "REM"];
const DIFF_CLASS = ["basic", "advanced", "expert", "master", "remaster"];
const MAX_QUERY = 200;

const state = {
  songs: [],
  loaded: false,
  loading: false,
  loadError: "",
  refreshing: false,
  connected: false,
  ready: false,
  inSelect: false,
  remoteBusy: false,
  q: "",
  sort: "dataid-desc",
  shown: 0,
  list: [],
  inGame: false,
  busy: false,
  lastRequest: null,
};

function $(id) { return document.getElementById(id); }

function esc(text) {
  return String(text == null ? "" : text)
    .replace(/[&<>"]/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" }[c]));
}

/* ── toast ── */
let toastTimer = 0;
function toast(text, kind) {
  const el = $("toast");
  if (!el) return;
  el.textContent = text;
  el.className = "toast" + (kind ? " " + kind : "");
  el.hidden = false;
  clearTimeout(toastTimer);
  toastTimer = setTimeout(() => { el.hidden = true; }, kind === "bad" ? 4000 : 2600);
}

function vibrate() {
  if (navigator.vibrate) {
    try { navigator.vibrate(18); } catch (_) { /* 有的浏览器会抛，忽略 */ }
  }
}

function canJump() {
  return state.connected && state.ready && state.inSelect && !state.inGame && !state.remoteBusy && !state.busy;
}

function updateButtons() {
  document.querySelectorAll(".diff").forEach((button) => { button.disabled = !canJump(); });
}

/* ── 状态与重连 ── */
async function refreshState() {
  if (state.refreshing) return;
  state.refreshing = true;
  const el = $("state");
  try {
    const data = await api("/api/status");
    state.connected = true;
    state.ready = !!data.ready;
    state.inSelect = !!data.inSelect;
    state.remoteBusy = !!data.busy;
    state.inGame = !!data.inGame;
    if (el) {
      el.className = "state" + (!canJump() ? " warn" : "");
      el.textContent = state.inGame ? "正在游玩，返回选曲后可点歌"
        : !state.ready ? "游戏数据加载中…"
          : !state.inSelect ? "等待机台进入选曲界面"
            : state.remoteBusy ? "机台正在切换曲目…" : "已连接 · 可以点歌";
    }
    // 正常搜索不会重取曲库；仅加载失败、启动时为空或曲库数量变化时补载。
    if (!state.loaded || state.loadError || (Number(data.songs) > 0 && Number(data.songs) !== state.songs.length)) {
      await loadSongs();
    }
  } catch (err) {
    state.connected = false;
    if (el) { el.className = "state bad"; el.textContent = "连不上机台"; }
  } finally {
    state.refreshing = false;
    updateButtons();
  }
}

/* ── 数据 ── */
function maxLevel(song) {
  let top = 0;
  (song.sheets || []).forEach((sh) => { const v = Number(sh.level); if (v > top) top = v; });
  return top;
}

function sorter(mode) {
  const byId = (a, b) => (b.dataid - a.dataid);
  if (mode === "level-desc") return (a, b) => (maxLevel(b) - maxLevel(a)) || byId(a, b);
  if (mode === "level-asc") return (a, b) => (maxLevel(a) - maxLevel(b)) || byId(a, b);
  if (mode === "title") return (a, b) => String(a.title || "").localeCompare(String(b.title || ""), "ja") || byId(a, b);
  return byId;
}

/* 搜索范围：曲名 / 作曲 / 谱师 / 别名（aliasesTag） */
function matches(song) {
  const needle = state.q;
  if (!needle) return true;
  if (String(song.title || "").toLowerCase().includes(needle)) return true;
  if (String(song.artist || "").toLowerCase().includes(needle)) return true;
  if ((song.sheets || []).some((sh) => String(sh.noteDesigner || "").toLowerCase().includes(needle))) return true;
  return (song.aliasesTag || []).some((a) => String(a).toLowerCase().includes(needle));
}

function recompute() {
  const list = state.songs.filter(matches);
  list.sort(sorter(state.sort));
  state.list = list;
  state.shown = 0;
  render();
}

/* ── 渲染 ── */
function renderCard(song) {
  const card = document.createElement("article");
  card.className = "song";
  card.dataset.songId = song.internalId;

  const jacket = document.createElement("div");
  if (song.jacket) {
    const img = document.createElement("img");
    img.className = "jacket";
    img.src = song.jacket;         // /jacket?dataid=N
    img.alt = "";
    img.loading = "lazy";
    img.decoding = "async";
    // 破图也退回占位块
    img.addEventListener("error", () => {
      const ph = document.createElement("div");
      ph.className = "jacket empty";
      ph.textContent = "无曲绘";
      img.replaceWith(ph);
    });
    jacket.replaceWith(img);
    card.appendChild(img);
  } else {
    jacket.className = "jacket empty";
    jacket.textContent = "无曲绘";
    card.appendChild(jacket);
  }

  const body = document.createElement("div");
  body.className = "body";
  const title = document.createElement("h2");
  title.className = "title";
  title.textContent = song.title || "";
  title.title = song.title || "";
  body.appendChild(title);
  const artist = document.createElement("p");
  artist.className = "artist";
  artist.textContent = song.artist || "";
  body.appendChild(artist);
  const meta = document.createElement("p");
  meta.className = "song-meta";
  const family = { dx: "DX", std: "STANDARD", utage: "宴", utage2p: "2P 宴" }[song.family] || "DX";
  meta.innerHTML = '<span class="family-label">' + family + '</span><span>' + esc(song.version || "") + '</span>';
  body.appendChild(meta);

  const diffs = document.createElement("div");
  diffs.className = "diffs";
  // sheets 只含游戏里启用的难度 → 天然「只在难度可用时显示」（chartid 0..4）
  for (const sheet of song.sheets || []) {
    const id = Number(sheet.chartid);
    if (!(id >= 0 && id <= 4)) continue;
    const btn = document.createElement("button");
    btn.type = "button";
    btn.className = "diff " + (DIFF_CLASS[id] || "master");
    btn.dataset.chartId = id;
    btn.setAttribute("aria-label", "点歌：" + song.title + " " + DIFF_NAMES[id]);
    const level = song.family === "utage" || song.family === "utage2p"
      ? (sheet.levelText || sheet.level) : sheet.level;
    btn.innerHTML = '<span class="diff-name">' + DIFF_NAMES[id] + '</span><span class="lv">' + esc(level == null ? "-" : level) + "</span>";
    btn.disabled = !canJump();
    btn.addEventListener("click", () => jump(song, sheet, btn));
    diffs.appendChild(btn);
  }
  card.appendChild(body);
  card.appendChild(diffs);
  diffs.style.setProperty("--diff-count", Math.max(1, (song.sheets || []).length));
  if (state.lastRequest?.id === song.internalId) {
    card.classList.add("requested");
    diffs.querySelector('[data-chart-id="' + state.lastRequest.chartId + '"]')?.classList.add("is-selected");
  }
  return card;
}

function render() {
  const box = $("list");
  const more = $("more");
  const count = $("count");
  if (!box) return;
  if (!state.loaded) {
    box.innerHTML = '<p class="loading">正在载入曲库…</p>';
    if (more) more.innerHTML = "";
    return;
  }
  if (state.loadError) {
    box.innerHTML = '<p class="empty">曲库读取失败：' + esc(state.loadError) + "</p>";
    if (count) count.textContent = "等待重试";
    renderRetry(more);
    return;
  }
  if (!state.list.length) {
    box.innerHTML = '<p class="empty">' + (state.songs.length ? "没有匹配的曲子。" : "等待游戏曲库，进入选曲界面后会自动载入。") + "</p>";
    if (more) more.innerHTML = "";
    if (count) count.textContent = "";
    if (!state.songs.length) renderRetry(more);
    return;
  }
  state.shown = Math.min(state.list.length, Math.max(state.shown || PAGE_SIZE, PAGE_SIZE));
  const slice = state.list.slice(0, state.shown);
  box.innerHTML = "";
  const frag = document.createDocumentFragment();
  for (const song of slice) frag.appendChild(renderCard(song));
  box.appendChild(frag);
  if (count) count.textContent = "命中 " + state.list.length + " 首 · 已显示 " + slice.length;
  if (more) {
    more.innerHTML = "";
    if (slice.length < state.list.length) {
      const btn = document.createElement("button");
      btn.type = "button";
      btn.textContent = "加载更多（还剩 " + (state.list.length - slice.length) + " 首）";
      btn.addEventListener("click", () => { state.shown += PAGE_SIZE; render(); });
      more.appendChild(btn);
    }
  }
}

function renderRetry(more) {
  if (!more) return;
  more.innerHTML = "";
  const button = document.createElement("button");
  button.type = "button";
  button.textContent = "重新载入曲库";
  button.addEventListener("click", () => loadSongs());
  more.appendChild(button);
}

/* ── 点歌 ── */
async function jump(song, sheet, btn) {
  if (!canJump()) return;
  const isUtage = song.family === "utage" || song.family === "utage2p";
  const body = {
    internalId: song.internalId,
    chartid: Number(sheet.chartid),
    type: isUtage ? "dx" : (song.family === "std" ? "std" : "dx"),
  };
  state.busy = true;
  updateButtons();
  toast("正在点歌…");
  try {
    const data = await api("/api/jump", {
      method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(body),
    });
    toast(data.msg || "已发送", data.superseded ? "" : "ok");
    if (!data.superseded) {
      state.lastRequest = { id: song.internalId, chartId: Number(sheet.chartid) };
      document.querySelectorAll(".song.requested, .diff.is-selected").forEach(element => element.classList.remove("requested", "is-selected"));
      btn.closest(".song")?.classList.add("requested");
      btn.classList.add("is-selected");
    }
    vibrate();
  } catch (err) {
    const why = err.status === 409 ? "机台不在选曲界面"
      : err.status === 503 ? "游戏数据还在加载"
        : err.status === 404 ? "曲库里没有这首" : (err.message || "点歌失败");
    toast(why, "bad");
  } finally {
    state.busy = false;
    // 响应后先确认机台已恢复选曲，避免切画面时又提交第二次。
    state.remoteBusy = true;
    updateButtons();
    await refreshState();
  }
}

/* ── 绑定 ── */
function bind() {
  const input = $("q");
  const clear = $("q-clear");
  let timer = 0;
  input.addEventListener("input", () => {
    const value = input.value.trim().toLowerCase();
    if (clear) clear.hidden = !value;
    // 输入防抖只在本地过滤，不发请求
    clearTimeout(timer);
    timer = setTimeout(() => {
      state.q = value.slice(0, MAX_QUERY);
      recompute();
    }, 90);
  });
  input.addEventListener("search", () => {
    clearTimeout(timer);
    state.q = input.value.trim().toLowerCase().slice(0, MAX_QUERY);
    recompute();
  });
  if (clear) clear.addEventListener("click", () => {
    clearTimeout(timer);
    input.value = "";
    clear.hidden = true;
    state.q = "";
    recompute();
    input.focus();
  });
  const sort = $("sort");
  if (sort) sort.addEventListener("change", () => { state.sort = sort.value; recompute(); });
}

/* ── 曲库：并发调用合并，读取失败时允许自动/手动重试 ── */
async function loadSongs() {
  if (state.loading) return;
  state.loading = true;
  state.loadError = "";
  try {
    const data = await api("/api/songs?user=");
    if (!Array.isArray(data.songs)) throw new Error("曲库数据格式错误");
    state.songs = data.songs;
    state.loaded = true;
    recompute();
  } catch (err) {
    state.loaded = true;
    state.loadError = err.message;
    render();
  } finally {
    state.loading = false;
  }
}

bind();
render();
refreshState();
loadSongs();
setInterval(() => { if (!document.hidden) refreshState(); }, 2500);
document.addEventListener("visibilitychange", () => { if (!document.hidden) refreshState(); });
window.addEventListener("online", () => refreshState());
