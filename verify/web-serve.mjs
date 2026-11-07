// 假后端 + 最终回归（放 web/ 之外，不会被 web 清理带走）
import http from "node:http";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { spawn } from "node:child_process";

const ROOT = fileURLToPath(new URL("../web/", import.meta.url));
const PORT = 18795;
const DIFF = ["basic", "advanced", "expert", "master", "remaster"];
const SONGS = [];
for (let i = 1; i <= 130; i++) {
  const en = i % 7 === 0 ? [0, 1, 2] : (i % 11 === 0 ? [0, 1, 2, 3, 4] : [0, 1, 2, 3]);
  SONGS.push({
    internalId: 10000 + i, family: i % 13 === 0 ? "std" : "dx", type: i % 13 === 0 ? "std" : "dx",
    dataid: 200 + i, title: "曲目 " + i, artist: i % 5 === 0 ? "作曲者甲" : "artist " + i,
    version: "CiRCLE", jacket: i % 6 === 0 ? null : "/jacket?dataid=" + (200 + i),
    aliasesTag: i % 9 === 0 ? ["别名" + i] : [], hasRemaster: en.includes(4),
    sheets: en.map((d) => ({ chartid: d, difficulty: DIFF[d], level: 9 + d / 10, levelText: String(9 + d),
      noteDesigner: i % 4 === 0 ? "谱师乙" : "d", noteCounts: 1, rank: null, achievement: null, combo: null, sync: null })),
  });
}
const ST = { ok: true, ready: true, version: "1", songs: SONGS.length, inSelect: true, busy: false, inGame: false, lan: true,
  scanUrls: ["http://192.168.1.9:18795/scan"] };
let CFGFAIL = false;
let LIBMODE = "ready";
let OFFLINE = false;
let USERMODE = "1p";
let RUNNING = false;
let STARTFAIL = false;
const STATS = { songs: 0 };
const TYPES = { ".html": "text/html; charset=utf-8", ".css": "text/css; charset=utf-8", ".js": "application/javascript; charset=utf-8", ".png": "image/png" };
const json = (r, s, b) => { const u = Buffer.from(JSON.stringify(b)); r.writeHead(s, { "Content-Type": "application/json; charset=utf-8", "Cache-Control": "no-store" }); r.end(u); };
function send(r, rel) {
  const f = path.join(ROOT, rel);
  if (!fs.existsSync(f) || fs.statSync(f).isDirectory()) { r.writeHead(404); return r.end("{}"); }
  r.writeHead(200, { "Content-Type": TYPES[path.extname(f).toLowerCase()] || "application/octet-stream", "Cache-Control": "no-store" });
  r.end(fs.readFileSync(f));
}
http.createServer((req, res) => {
  const u = new URL(req.url, "http://x");
  const p = u.pathname;
  if (p === "/__ing") { ST.inGame = u.searchParams.get("on") === "1"; res.writeHead(200); return res.end("ok"); }
  if (p === "/__cfg") { CFGFAIL = u.searchParams.get("on") === "1"; res.writeHead(200); return res.end("ok"); }
  if (p === "/__library") { LIBMODE = u.searchParams.get("mode"); ST.songs = LIBMODE === "empty" ? 0 : SONGS.length; return json(res, 200, { ok: true }); }
  if (p === "/__offline") { OFFLINE = u.searchParams.get("on") === "1"; return json(res, 200, { ok: true }); }
  if (p === "/__users") { USERMODE = u.searchParams.get("mode"); return json(res, 200, { ok: true }); }
  if (p === "/__assistant") { STARTFAIL = u.searchParams.get("fail") === "1"; return json(res, 200, { ok: true }); }
  if (p === "/__stats") { const b = Buffer.from(JSON.stringify(STATS)); res.writeHead(200); return res.end(b); }
  if (p === "/" || p === "/index.html") return send(res, "index.html");
  if (p === "/scan" || p === "/scan/") return send(res, "scan/index.html");
  if (p === "/api/status") return OFFLINE ? json(res, 503, { ok: false, error: "模拟断线" }) : json(res, 200, ST);
  if (p === "/api/config") return CFGFAIL ? json(res, 500, { ok: false, error: "no" })
    : json(res, 200, { chains: {}, chainAges: {}, python: "", projectDir: "", ratingVersions: ["CiRCLE"] });
  if (p === "/api/run") return json(res, 404, { ok: false, error: "没有这个接口" });
  if (p === "/api/users") return json(res, 200, { ok: true, users: [0, 1].map(index => ({
    id: String(index), userIndex: index, name: index ? "玩家乙" : "玩家甲", rating: 15000,
    isEntry: USERMODE === (index ? "2p" : "1p"), fromGame: true, hasScores: true, avatar: null, summary: null,
  })) });
  if (p === "/api/songs") {
    STATS.songs += 1;
    return LIBMODE === "fail" ? json(res, 503, { ok: false, error: "模拟曲库失败" })
      : json(res, 200, { songs: LIBMODE === "empty" ? [] : SONGS, versions: ["CiRCLE"], count: ST.songs });
  }
  if (p === "/api/song") {
    const s = SONGS.find((x) => x.internalId === Number(u.searchParams.get("internalId")));
    return s ? json(res, 200, { ok: true, song: Object.assign({}, s, { fitModel: {} }) }) : json(res, 404, { ok: false });
  }
  if (p === "/api/plates") return json(res, 200, { plates: [] });
  if (p === "/api/favorites" || p === "/api/blacklist") return json(res, 200, { ok: true, entries: [] });
  if (p === "/api/analysis") return json(res, 200, { charts: [], fitModel: null });
  if (p === "/api/assistant/state") return json(res, 200, {
    ok: true, running: RUNNING, profile: { rating: 15000,
      config: [{tag:"转圈",value:0.07},{tag:"交互",value:-0.08},{tag:"错位",value:0.02}],
      eval: [{tag:"体力谱",value:0.06},{tag:"高物量",value:0.01},{tag:"底力谱",value:-0.09}] },
    cards: [{ pool: "practice", count: 68 }, { pool: "unplayed", count: 120 }, { pool: "favorite", count: 24 }],
    preview: SONGS.slice(0, 3).map(song => ({ internalId: song.internalId, chartid: 3, poolLabel: "待开荒", song: { ...song, sheets: song.sheets.filter(s => s.chartid === 3) } })), logs: [], settings: {},
  });
  if (p === "/api/assistant/log") return json(res, 200, { ok: true, running: RUNNING, log: "" });
  if (p === "/api/assistant/repush") return json(res, RUNNING ? 200 : 409, RUNNING
    ? {ok:true,info:"已跳转: 曲目 1 [DX MASTER]"} : {ok:false,error:"请先启动助手"});
  if (p === "/api/assistant/start" || p === "/api/assistant/stop") {
    let raw = "";
    req.on("data", chunk => raw += chunk);
    req.on("end", () => {
      const body = JSON.parse(raw || "{}");
      if (p.endsWith("/start") && !["push", "score"].includes(body.mode)) return json(res, 409, { ok: false, info: "mode 只能是 push 或 score" });
      setTimeout(() => {
        if (p.endsWith("/start") && STARTFAIL) return json(res, 409, { ok: false, info: "当前不在选曲界面，请回到选曲界面后启动助手" });
        RUNNING = p.endsWith("/start");
        return json(res, 200, { ok: true, running: RUNNING, info: "完成" });
      }, 500);
    });
    return;
  }
  if (p === "/api/scores/stats") return json(res, 200, { ok: true, stats: { totals: { played: 5, sheetsInLibrary: 100 }, rank: {}, versions: [{ version: "CiRCLE", sheets: 1 }], generatedAt: "2026-11-07T00:00:00" } });
  if (p === "/api/scores/dxrating") return json(res, 200, { ok: true, version: "CiRCLE", dxRating: 15000 });
  if (p === "/api/jump") {
    let raw = "";
    req.on("data", (c) => raw += c);
    req.on("end", () => {
      if (ST.inGame) return json(res, 409, { ok: false, error: "不在选曲界面" });
      const b = JSON.parse(raw || "{}");
      const s = SONGS.find((x) => x.internalId === Number(b.internalId));
      if (!s) return json(res, 404, { ok: false, error: "没有这首" });
      return json(res, 200, { ok: true, superseded: false, msg: "已跳转: " + s.title });
    });
    return;
  }
  if (p === "/jacket") { res.writeHead(200, { "Content-Type": "image/png" }); return res.end(fs.readFileSync(path.join(ROOT, "ui", "musicLib", "Base_DX.png"))); }
  return send(res, p.replace(/^\//, ""));
}).listen(PORT, "127.0.0.1", () => console.log("up"));
