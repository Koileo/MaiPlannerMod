# MaiPlannerMod 接口契约

**本文件是前后端与各实现模块的唯一契约**，改接口先改这里。

原则：**路径与载荷尽量和原「打歌计划器」保持一致**，这样 planner 的前端（web/）可以按原样搬进来，只换后端实现。新增的能力（跳曲、扫码、机台玩家）走新路径。

共用一个端口（默认 8790）。响应一律 `application/json; charset=utf-8` 或图片/HTML。
无鉴权；`127.0.0.1` 永远允许，局域网按配置放开。

## 页面

| 方法 | 路径 | 说明 |
|---|---|---|
| GET | `/` | 点歌台（PC 宽屏：曲库/成绩/助手/卡池 + 点歌） |
| GET | `/scan` | 手机扫码点歌页（窄屏，搜索 + 大按钮） |

静态资源：`/style.css`、`/main.js`、`/core.js`、`/db.js`、`/stats.js`、`/songlist.js`、
`/songcard.js`、`/songlist-view.js`、`/assistant.js`、`/pageup.js`、`/vendor/*`、`/ui/*`、`/fonts/*`、`/scan/*`。
全部是 DLL 内嵌资源，按路径名直出。

## 状态

### GET /api/status（新增）

```json
{"ok":true,"ready":true,"version":"1.0.0","songs":1934,"inSelect":true,"busy":false,
 "inGame":false,"lan":true,"scanUrls":["http://192.168.1.9:8790/scan"]}
```

### GET /api/config（planner 同形）

```json
{"chains":{},"chainAges":{},"python":"","projectDir":"...","ratingVersions":["MAGiCAL"]}
```

mod 里没有外部脚本链，`chains` 给空对象。前端没有数据同步/建库页，曲库数量和游戏连接状态显示在顶部；旧的 `#db` 地址回到点歌台。

## 跳曲（新增）

### POST /api/jump

`application/json`：`{"internalId":10146,"chartid":3,"type":"dx"}`

```json
{"ok":true,"superseded":false,"msg":"已跳转: 曲名 [DX MASTER]"}
```

`chartid` 是难度 0..4。连发只有最新一次生效，前一次 `superseded: true`。
不在选曲界面 409，数据没加载完 503，没这首 404。

### GET /api/probe

只读诊断（沿用原 README 的 `/probe`：scoreType/realId/difficulty/cursorId 等）。

## 曲库

### GET /api/songs?user=U（planner 同形）

```json
{"songs":[{"internalId":10146,"family":"dx","type":"dx","title":"...","artist":"...",
  "version":"CiRCLE","dataid":146,"hasRemaster":true,"aliasesTag":[],
  "jacket":"/jacket?dataid=146",
  "sheets":[{"chartid":3,"difficulty":"master","level":13.7,"levelText":"13+",
             "noteDesigner":"...","noteCounts":897,"rank":null,"achievement":null,
             "combo":null,"sync":null}]}],"versions":["maimai","..."],"count":1934}
```

- `achievement` **×10000 整数**（1010000 = 101.0000%）。
- `sheets[].chartid` 用 planner 语义：**0..4 = 难度槽位**。
- `family` 取 `std` / `dx` / `utage` / `utage2p`。

### GET /api/song?internalId=N&user=U（planner 同形）

单选详情：`{"song":{...同上, "sheets":[...含 tag/fit/favorite/blacklisted/achievement/playCount...], "fitModel":{}}}`

### GET /jacket?dataid=N → PNG

- 非法编号返回 400；没有对应资源包且未能从游戏缓存取得贴图，返回 404。
- 资源包存在但加载暂不可用或主线程请求超时，返回 503 和 `Retry-After: 10`，失败响应不缓存。
- 成功的 PNG 可缓存一周。优先读取游戏现有贴图，不销毁游戏持有的对象；压缩或不可读贴图通过临时渲染纹理转换。
- 主线程排队任务超时后不再执行；单帧已用 4ms 后，余下任务让到下一帧。已开始的单个 Unity 操作不能抢占。

### GET /api/plates → `{"plates":[]}`

## 用户与成绩

| 方法 | 路径 | 说明 |
|---|---|---|
| GET | `/api/users` | 机台玩家（userIndex 0..3） |
| POST | `/api/user-create` `{name}` | 新建本地用户 |
| POST | `/api/user-rename` `{user,name}` | 改名 |
| POST | `/api/user-delete` `{user}` | 删除 |
| POST | `/api/user-order` `{order}` | 排序 |
| POST | `/api/pick-folder` | 选目录（mod 里返回提示） |
| GET | `/api/scores/dxrating?user=U&version=V` | 该玩家某版本 DX rating |
| POST | `/api/scores/dxrating` `{user,version}` | 重算 |
| POST | `/api/scores/stats` `{user}` | 重算统计 |
| POST | `/api/score/edit` `{user,internalId,chartid,achievement,...}` | 改单条成绩 |

### GET /api/users

```json
{"users":[{"id":"0","userIndex":0,"name":"玩家名","rating":14500,"isEntry":true,
  "fromGame":true,"hasScores":true,"avatar":null,"summary":null}]}
```

`fromGame: true` 表示机台槽位，`isEntry: true` 才表示该槽位有登录玩家。`id` 是字符串形式的槽位索引（1P 为 `"0"`、2P 为 `"1"`），不是 Aime 卡号或服务器账号 ID。网页每 3 秒刷新玩家列表，只显示已登录玩家，退出登录后清除旧选择。

## 分析 / 助手 / 卡池（planner 同形）

| 方法 | 路径 |
|---|---|
| GET/POST | `/api/analysis?user=U[&run=1]` |
| GET | `/api/favorites?user=U` · `POST /api/favorite` · `POST /api/favorite/remove` |
| GET | `/api/blacklist?user=U` · `POST /api/blacklist` · `POST /api/blacklist/remove` |
| GET | `/api/assistant/state?user=U[&counts=1]` |
| POST | `/api/assistant/cards` · `/redraw` · `/skip` · `/start` · `/stop` · `/push` · `/repush` · `/settings` |
| GET | `/api/assistant/log` · `/api/assistant/bridge-next` |
| POST | `/api/assistant/bridge-score` · `/api/assistant/refill` |

成绩回传入口（游戏 → 自身）：`POST /api/assistant/bridge-score` 与 `/api/score`（原始成绩）。

助手内置于 Mod，网页启动时不需要机台 IP、Mod 端口或回传端口。`POST /api/assistant/start` 请求体为 `{"user":"0","mode":"push"}`；只接收成绩用 `mode:"score"`。旧 `/settings` 路由保留兼容。

`start` 要求游戏数据就绪且玩家已登录，推歌模式还要求游戏处于选曲界面并且首曲跳转成功；失败返回 HTTP 409 及 `error` 或 `info` 原因，同时写入助手日志。启动成功返回 `running:true` 和 `mode`，停止成功返回 `running:false`，网页可直接更新状态。

`repush` 请求体为 `{user}`，重新推送当前运行玩家的队首；需已启动推歌模式。`push` 使用与 `/api/jump` 一致的校验和实际跳转结果。

助手状态包含 `profile.rating`（当前登录玩家的游戏 Rating）、`profile.config` 和 `profile.eval`（各为 `{tag,value}` 数组，来自分析计算），并保留卡片 `settings` 和筛选用的 `versions`。玩家列表仅返回 `isEntry:true` 的登录槽位。

`state`、`cards`、`redraw` 和 `skip` 返回的每个 `preview` 条目都包含 `song`：结构与 `/api/song` 的 `song` 相同，但 `sheets` 仅包含抽中的难度，成绩、收藏和分析字段按请求玩家读取。旧队列也在响应时补齐；曲目不存在或难度不可用时 `song` 为 `null`，不会借用 STD/DX 另一张谱面。

## 设置

MelonPreferences 落盘，`Package/UserData/MelonPreferences.cfg`：

| 配置项 | 默认 | 说明 |
|---|---|---|
| 启用 | true | 整体开关 |
| 接口端口 | 8790 | |
| 局域网 | true | 手机扫码要开 |
| 详细日志 | false | |
| 跳到难度页 | true | 跳曲后停在难度选择页 |
| 回传地址 | 空 | 留空不对外回传 |
| 回传端口 | 20010 | |
