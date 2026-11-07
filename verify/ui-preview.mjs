// 生成桌面、手机布局预览。使用本地假数据，不操作已有浏览器会话。
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { spawn } from "node:child_process";

const here = path.dirname(fileURLToPath(import.meta.url));
const label = process.argv[2] || "after";
if (!/^[a-z0-9-]+$/.test(label)) throw new Error("Invalid preview label");
const out = path.join(here, "ui-preview", label);
fs.mkdirSync(out, { recursive: true });
const server = spawn(process.execPath, [path.join(here, "web-serve.mjs")], { stdio: "ignore", windowsHide: true });
const chrome = spawn(process.env.CHROME_PATH || "C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe", [
  "--headless=new", "--disable-gpu", "--no-first-run", "--remote-debugging-port=9351",
  "--user-data-dir=" + path.join(out, "profile"), "about:blank",
], { stdio: "ignore", windowsHide: true });
const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));
let ws, nextId = 0;
const pending = new Map();
const errors = [];
const send = (method, params = {}) => new Promise((resolve, reject) => {
  const id = ++nextId;
  pending.set(id, { resolve, reject });
  ws.send(JSON.stringify({ id, method, params }));
});
try {
  for (let attempt = 0; attempt < 50; attempt++) {
    try {
      const list = await (await fetch("http://127.0.0.1:9351/json/list")).json();
      const page = list.find(item => item.type === "page");
      if (page) { ws = new WebSocket(page.webSocketDebuggerUrl); break; }
    } catch {}
    await sleep(200);
  }
  if (!ws) throw new Error("Chrome unavailable");
  await new Promise(resolve => { ws.onopen = resolve; });
  ws.onmessage = event => {
    const item = JSON.parse(event.data);
    if (item.id && pending.has(item.id)) {
      const waiter = pending.get(item.id);
      pending.delete(item.id);
      item.error ? waiter.reject(new Error(item.error.message)) : waiter.resolve(item.result);
    }
    if (item.method === "Runtime.exceptionThrown") errors.push(item.params.exceptionDetails.text);
  };
  await send("Page.enable");
  await send("Runtime.enable");
  for (const [name, route, width, height] of [
    ["desktop", "/#songs", 1440, 1000],
    ["assistant", "/#assistant", 1440, 1000],
    ["scores", "/#scores", 1440, 1000],
    ["mobile", "/scan", 390, 844],
    ["mobile-narrow", "/scan", 320, 812],
    ["tablet", "/#songs", 768, 1024],
    ["desktop-narrow", "/#songs", 390, 1000],
  ]) {
    await send("Emulation.setDeviceMetricsOverride", { width, height, deviceScaleFactor: 1, mobile: width <= 390 });
    await send("Page.navigate", { url: "http://127.0.0.1:18795" + route });
    await sleep(2200);
    await send("Runtime.evaluate", { expression: "document.fonts.ready", awaitPromise: true });
    const { data } = await send("Page.captureScreenshot", { format: "png" });
    fs.writeFileSync(path.join(out, name + ".png"), Buffer.from(data, "base64"));
    const result = await send("Runtime.evaluate", { expression: "JSON.stringify({width:innerWidth,overflow:document.documentElement.scrollWidth-document.documentElement.clientWidth})", returnByValue: true });
    console.log(name, result.result.value, path.join(out, name + ".png"));
  }
  if (errors.length) throw new Error(errors.join("; "));
} finally {
  ws?.close();
  chrome.kill();
  server.kill();
}
