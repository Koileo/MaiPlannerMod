import { $, NEEDS, api, busy, chain, config, polling, setBusy, setChain, setNote, setPolling } from "./core.js";
import { loadStats } from "./stats.js";
import { loadPool, loadSongs } from "./songlist.js";

// 用途：数据维护页：选包、链选择、跑构建、进度（从 app.js 按领域拆出，classic script，index.html 按顺序加载）
// 依赖：全局函数/变量在调用时解析，不要求定义顺序
// mod 版：config.chains 恒为空对象，下面所有 find* 都返回 null，由 refresh* 显示占位。
function chainWithNeed(need) {
  if (!config || !config.chains) return null;
  for (const [name, info] of Object.entries(config.chains)) {
    if (!info || !Array.isArray(info.steps)) continue;
    if (info.steps.some((s) => (s.needs || []).includes(need))) return name;
  }
  return null;
}

/* 别名那块单独对应「needs 里有 als」的那条链（不写死链名） */
function alsChainName() {
  return chainWithNeed("als");
}

/* 数据小更新那条链（步骤里带 packageDir），它有自己的块 */
function smallChainName() {
  return chainWithNeed("packageDir");
}

/* 成绩导入那条链（步骤里带 scores） */
function scoresChainName() {
  return chainWithNeed("scores");
}


/* 页面切换：tab 的 data-page 对上 section.page 的 data-page；选中记进 #hash，刷新不丢 */
function showPage(name) {
  const verbox = document.getElementById("verbox");
  if (verbox) verbox.hidden = true;
  const pages = [...document.querySelectorAll("section.page")];
  const names = pages.map((p) => p.dataset.page);
  const want = names.includes(name) ? name : "songs";
  pages.forEach((p) => { p.hidden = p.dataset.page !== want; });
  document.querySelectorAll(".tab").forEach((t) => {
    t.classList.toggle("active", t.dataset.page === want);
    if (t.dataset.page === want) t.setAttribute("aria-current", "page");
    else t.removeAttribute("aria-current");
  });
  const headings = {
    songs: ["歌曲点歌台", "找到想打的曲目，选择难度后发送到机台。"],
    assistant: ["打歌助手", "搭配你的练习卡池，让下一首更有方向。"],
    pool: ["卡池预览", "按目标挑选曲目，安排下一轮练习。"],
    scores: ["玩家与成绩", "查看机台玩家、游玩记录与个人进步。"],
  };
  const title = document.getElementById("page-title");
  const description = document.getElementById("page-description");
  if (title) title.textContent = headings[want][0];
  if (description) description.textContent = headings[want][1];
  if (location.hash.slice(1) !== want) history.replaceState(null, "", "#" + want);
  if (want === "songs") loadSongs();           // 进歌曲页就拉曲库（loadSongs 自己会缓存）
  if (want === "scores") loadStats();          // 进成绩页就激活统计（每次进入都算一遍）
  if (want === "pool") loadPool();             // 卡池预览：读 analysis.json
  window.dispatchEvent(new CustomEvent("planner:page", { detail: { page: want } }));
}

document.querySelectorAll(".tab").forEach((t) => {
  t.onclick = () => {
    const page = t.dataset.page;
    showPage(page);
    if (page === "songs") loadSongs();
  };
});
window.addEventListener("hashchange", () => { showPage(location.hash.slice(1)); if (location.hash.slice(1) === "songs") loadSongs(); });

/* 建库那块的链列表：自己有块的（别名、数据小更新）不在这里重复列；无链时也照样能渲染占位 */
function buildChainNames() {
  if (!config || !config.chains) return [];
  const own = [alsChainName(), smallChainName(), scoresChainName()];
  return Object.keys(config.chains).filter((name) => !own.includes(name));
}

let showSteps = false;                     // 「显示流程」开关，默认关

function renderChains() {
  const box = $("chains");
  if (!box) return;
  const toggle = $("steps-toggle");
  if (toggle) toggle.textContent = showSteps ? "隐藏流程" : "显示流程";
  box.innerHTML = "";
  if (!config || !config.chains) return;
  for (const name of buildChainNames()) {
    const info = config.chains[name];
    if (!info) continue;
    const label = document.createElement("label");
    label.className = "chain";
    const radio = document.createElement("input");
    radio.type = "radio";
    radio.name = "chain";
    radio.value = name;
    radio.checked = name === chain;
    radio.onchange = () => { setChain(name); renderChains(); refreshBuildButton(); };
    const text = document.createElement("span");
    const needs = [...new Set((info.steps || []).flatMap((s) => s.needs || []))].filter((n) => n !== "proxy");
    // 只有「联网更新数据」这条链需要看数据有多旧
    const age = name === "dxratingUpdate" ? (config.chainAges || {})[name] : null;
    const ageText = age ? (age.ageDays < 1 ? "今天更新" : `${Math.floor(age.ageDays)} 天前`) : "";
    const ageColor = !age ? "" : (age.ageDays <= 3 ? "var(--ok)" : age.ageDays <= 7 ? "#e2a93b" : "var(--bad)");
    text.innerHTML = `<b>${info.title || name}</b>` +
      (needs.length ? `<small>${needs.map((n) => NEEDS[n] || n).join("・")}</small>` : "") +
      (ageText ? `<small class="chain-age" style="color:${ageColor}">${ageText}</small>` : "") +
      (showSteps ? `<small class="chain-steps">${(info.steps || []).map((s) => s.script).join(" → ")}</small>` : "");
    label.append(radio, text);
    box.append(label);
  }
}

function currentChain() {
  return config && chain && config.chains ? config.chains[chain] || null : null;
}

function refreshBuildButton() {
  const info = currentChain();
  const needsPkg = info ? (info.steps || []).some((s) => (s.needs || []).includes("gamePackage")) : false;
  const needsNet = info ? (info.steps || []).some((s) => (s.needs || []).includes("network")) : false;
  const pkg = $("pkg");
  const note = !info ? "" : (needsPkg && !(pkg && pkg.value) ? "未选择游戏目录" : (needsNet ? "需要联网" : ""));
  if ($("build-note")) $("build-note").textContent = note;
  if ($("build")) $("build").disabled = busy || !info || (needsPkg && !(pkg && pkg.value));
}

function refreshAlsButton() {
  const name = alsChainName();
  const info = name && config && config.chains ? config.chains[name] : null;
  if (!info) {
    if ($("als-build")) $("als-build").disabled = true;
    setNote("als-note2", "常用别名已随曲库提供，可直接用于搜索。");
    return;
  }
  const als = $("als");
  if ($("als-build")) $("als-build").disabled = busy || !(als && als.value);
  setNote("als-note2", als && als.value ? "" : "先选那份 JSON。");
}

function stepLine(step) {
  const mark = {pending: "·", running: "…", done: "✓", failed: "✗"}[step.state] || "·";
  const cls = step.state || "pending";
  const secs = step.seconds ? ` ${step.seconds}s` : "";
  const why = step.state === "failed" && step.reason ? ` —— ${step.reason}` : "";
  const counts = step.products && step.products[0] && step.products[0].counts
    ? " " + JSON.stringify(step.products[0].counts).replace(/["{}]/g, "").slice(0, 90) : "";
  return `<li class="${cls}"><span class="mark">${mark}</span> <b>${step.script}</b>${secs}${why}<small>${counts}</small></li>`;
}

function renderRun(state) {
  if (!$("steps") || !$("log") || !$("result")) return;
  if (state.steps && state.steps.length) {
    const list = $("steps");
    list.innerHTML = state.steps.map(stepLine).join("");
    // 歌词式：把当前步骤滚到中间（5 行窗口）
    const cur = list.querySelector("li.running") || list.querySelector("li.pending") || list.lastElementChild;
    if (cur) {
      const top = cur.offsetTop - (list.clientHeight - cur.offsetHeight) / 2;
      list.scrollTo({ top: Math.max(0, top), behavior: "smooth" });
    }
  }
  const logEl = $("log");
  // 新内容来了自动跟到最下面；用户手动往上翻时不抢（翻回底部后自动恢复）
  const stick = logEl.scrollHeight - logEl.scrollTop - logEl.clientHeight < 24;
  logEl.textContent = (state.log || []).join("\n");
  if (stick) logEl.scrollTop = logEl.scrollHeight;
  if (state.running) $("logbox").open = true;

  const result = $("result");
  if (state.running) {
    result.className = "result running";
    result.textContent = `正在跑：${state.chain} · 批次 ${state.runId} · ${state.seconds}s`;
  } else if (state.result) {
    result.className = "result " + (state.result.ok ? "ok" : "bad");
    result.textContent = state.result.ok
      ? `完成 ✓ 全部成功（批次 ${state.runId}）`
      : `失败 ✗ ${state.result.reason || ""}`;
  } else {
    result.className = "result";
    result.textContent = "";
  }
  $("stop").disabled = !state.running;
  setBusy(!!state.running);
  refreshBuildButton();
  refreshAlsButton();
}
async function startRun(body, noteEl) {
  try {
    await api("/api/build", {
      method: "POST",
      headers: {"Content-Type": "application/json"},
      body: JSON.stringify(body),
    });
    $("steps").innerHTML = '<li class="idle">正在启动…</li>';
    setBusy(true);
    refreshBuildButton();
    refreshAlsButton();
    watch();
  } catch (err) {
    noteEl.textContent = "启动失败：" + err.message;
  }
}

function watch() {
  clearInterval(polling);
  let wasRunning = false;
  setPolling(setInterval(async () => {
    let state;
    try {
      state = await api("/api/run");
    } catch (_) {
      clearInterval(polling);
      return;
    }
    renderRun(state);
    if (wasRunning && !state.running) {
      try {
        const fresh = await api("/api/config");
        config.chains = fresh.chains || {};
        config.chainAges = fresh.chainAges || {};
        renderChains();
      } catch (_) { /* 刷新失败不影响本次运行结果 */ }
    }
    wasRunning = !!state.running;
    if (!state.running) clearInterval(polling);
  }, 700));
}

/* 下面这些都按「元素在不在」挂，少一个不影响整个页面 */
function on(id, handler) {
  const el = $(id);
  if (el) el.onclick = handler;
}

on("pick", async () => {
  setNote("pkg-note", "等待选取中…");
  try {
    const data = await api("/api/pick-folder", {method: "POST"});
    if ($("pkg")) $("pkg").value = data.path || "";
    setNote("pkg-note", data.path ? (data.warning || "完成") : (data.note || "已取消"));
  } catch (err) {
    setNote("pkg-note", "选目录失败：" + err.message);
  }
  refreshBuildButton();
});

const stepsToggle = $("steps-toggle");
if (stepsToggle) stepsToggle.onclick = () => { showSteps = !showSteps; renderChains(); };

on("build", () => startRun({chain, gamePackage: ($("pkg") && $("pkg").value) || null}, $("build-note")));

on("pick-als", async () => {
  setNote("als-note", "等待选取中…");
  try {
    const data = await api("/api/pick-file", {method: "POST"});
    if ($("als")) $("als").value = data.path || "";
    setNote("als-note", data.path ? (data.warning || "完成") : (data.note || "已取消"));
  } catch (err) {
    setNote("als-note", "选文件失败：" + err.message);
  }
  refreshAlsButton();
});

on("als-build", () =>
  startRun({chain: alsChainName(), als: ($("als") && $("als").value) || null}, $("als-note2")));

/* 玩家下拉：数据来自 /api/users（机台玩家，id = String(userIndex)）。成绩页和导入块共用一份。 */


export { alsChainName, buildChainNames, currentChain, refreshAlsButton, refreshBuildButton, renderChains, renderRun, scoresChainName, showPage, smallChainName, startRun, stepLine, watch };
