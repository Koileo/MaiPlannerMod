import { showPage } from "./db.js";
import { currentUser, renderAvatar, selectUser } from "./stats.js";

// 用途：全局工具（$ / api / showPage / 页面路由）+ 用户表与用户切换（从 app.js 按领域拆出，classic script，index.html 按顺序加载）
// 依赖：全局函数/变量在调用时解析，不要求定义顺序
/* 打歌计划器 · 页面：建库（可折叠）+ 曲别名（Chiffonmai）+ 进度 */

function $(id) { return document.getElementById(id); }   // 函数声明：循环 import 时也能在模块求值前用
const NEEDS = {network: "联网", proxy: "走系统代理", gamePackage: "游戏目录", packageDir: "包目录", als: "别名文件", scores: "成绩文件", user: "用户"};

let config = null;
let chain = null;
let polling = null;
let busy = false;

function setPickMask(on) {
  const mask = document.getElementById("pickmask");
  if (mask) mask.hidden = !on;
}

async function api(path, options) {
  const isPick = /\/api\/(pick-|user-avatar$)/.test(path);   // 弹系统对话框的请求
  if (isPick) setPickMask(true);
  try {
    const res = await fetch(path, options);
    const data = await res.json().catch(() => ({}));
    // 契约：错误靠 ok:false 与 HTTP 码一起表达；这里把两者都挂到 Error 上，调用方按需取。
    // 两个例外：superseded 是「被后一次点歌顶掉」的正常结果；started 是脚本已受理。
    const soft = data.superseded === true || data.started === true;
    if (!soft && (!res.ok || data.ok === false)) {
      const err = new Error(data.error || data.info || res.statusText || ("HTTP " + res.status));
      err.status = res.status;
      err.data = data;
      throw err;
    }
    return data;
  } finally {
    if (isPick) setPickMask(false);
  }
}

/* 别名那块单独对应「needs 里有 als」的那条链（不写死链名） */

/* ── 用户 ── */
/* mod 版：/api/users 给的是**机台玩家**（userIndex 0..3、fromGame），不是原 uuid 用户表。
   这里统一成 {id: String(userIndex), userIndex, name, avatar:null, ...}，
   让下游（成绩页 / 收藏 / 分析）继续按 user.id 走的原逻辑不用改。 */
let usersById = {};
let usersSignature = null;
let usersLoading = false;

function normalizeUser(raw) {
  const index = raw.userIndex != null ? Number(raw.userIndex) : Number(raw.id);
  const id = String(raw.id != null ? raw.id : index);
  return {
    id,
    userIndex: Number.isFinite(index) ? index : 0,
    name: raw.name || ("玩家 " + (Number.isFinite(index) ? index + 1 : 1)),
    rating: raw.rating,
    isEntry: !!raw.isEntry,
    fromGame: raw.fromGame !== false,     // 契约里机台玩家恒为 true
    hasScores: !!raw.hasScores,
    avatar: raw.avatar || null,           // 目前恒为 null → 前端用默认占位
    summary: raw.summary || null,
    updatedAt: raw.updatedAt || null,
  };
}

async function loadUsers() {
  if (usersLoading) return;
  usersLoading = true;
  try {
    const data = await api("/api/users");
    const list = (data.users || []).map(normalizeUser).filter(u => u.isEntry);
    const signature = JSON.stringify(list);
    if (signature === usersSignature) return;
    usersSignature = signature;
    usersById = Object.fromEntries(list.map((u) => [u.id, u]));
    renderAvatar();
  } catch (err) {
    console.warn("读玩家表失败：" + err.message);
  } finally {
    usersLoading = false;
  }
}



/* 一次性提示：写进 .note 元素，5s 后淡出并清空（重复调用会重置计时） */
const noteTimers = new Map();
export function setNote(id, text, holdMs = 5000) {
  const el = typeof id === "string" ? $(id) : id;
  if (!el) return;
  const key = el.id || id;
  const prev = noteTimers.get(key);
  if (prev) { clearTimeout(prev); noteTimers.delete(key); }
  el.textContent = text == null ? "" : String(text);
  el.classList.remove("note-fade");
  if (!el.textContent) return;
  const t = setTimeout(() => {
    el.classList.add("note-fade");
    setTimeout(() => {
      if (el.classList.contains("note-fade")) { el.textContent = ""; el.classList.remove("note-fade"); }
    }, 700);
  }, holdMs);
  noteTimers.set(key, t);
}

export { $, NEEDS, api, busy, chain, config, loadUsers, polling, setPickMask, usersById };

/* 所有弹层：任何一个可见时锁住页面滚动（弹层内部自己滚） */
const OVERLAY_IDS = ["songmodal", "songfull", "songfilter", "songsearch", "bldlg",
                     "newmodal", "delmodal", "cropbox", "pickmask", "verbox"];

export function installScrollLock() {
  const els = OVERLAY_IDS.map((id) => $(id)).filter(Boolean);
  const sync = () => {
    const any = els.some((el) => !el.hidden);
    document.documentElement.classList.toggle("no-scroll", any);
  };
  const obs = new MutationObserver(sync);
  els.forEach((el) => obs.observe(el, { attributes: true, attributeFilter: ["hidden"] }));
  sync();
}

export function setBusy(value) {
  busy = !!value;
}

export function setPolling(timer) {
  polling = timer;
}

export function setChain(name) {
  chain = name;
}

/* 启动：读取配置与玩家。无效或旧页签地址统一回到歌曲点歌台。 */
export async function boot() {
  try {
    config = await api("/api/config");
  } catch (err) {
    console.warn("读取配置失败，使用默认设置：" + err.message);
    config = { chains: {}, chainAges: {}, ratingVersions: [] };
  }
  await loadUsers();
  if (!currentUser) {
    const first = Object.values(usersById)[0];
    if (first) selectUser(first.id);
  }
  showPage(location.hash.slice(1));
  setInterval(() => { if (!document.hidden) loadUsers(); }, 3000);
  document.addEventListener("visibilitychange", () => { if (!document.hidden) loadUsers(); });
}
