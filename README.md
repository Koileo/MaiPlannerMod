# MaiPlannerMod

舞萌 DX 的点歌台、手机扫码点歌和打歌助手，集成在一个 MelonLoader Mod 中。

网页、接口与助手都运行在游戏进程内。安装一个 DLL 即可使用，曲库、玩家成绩和曲绘直接读取游戏资源，无需 Python、外部数据库或单独启动计划器。

## 功能

- **歌曲点歌台**：搜索曲名、作曲、谱师和别名，按版本、定数、难度等条件筛选，选择谱面后跳转到游戏选曲界面。
- **手机扫码点歌**：手机与机台在同一局域网，扫描首页二维码进入 `/scan`，搜索后点击难度点歌。
- **玩家与成绩**：自动识别当前登录的机台玩家，展示达成率、RANK、连击、覆盖率和版本 Rating；支持切换玩家。
- **打歌助手**：配置曲池卡片，预览接下来的三首，启动后自动推歌，捕获成绩并顺延队列。
- **分析与卡池**：个人拟合、能力区间和推分候选；支持收藏、黑名单以及卡池预览。
- **可选成绩回传**：配置外部回调地址后，将一局成绩 POST 到指定地址。

## 环境与兼容性

- Windows 上的舞萌 DX / Sinmai，已安装可加载 .NET Framework Mod 的 MelonLoader。
- Mod 目标框架为 **.NET Framework 4.8**。游戏程序集通过 `GameDir` 引用，仓库不包含游戏程序或 MelonLoader 二进制。
- 当前适配以 SDEZ170 游戏程序集和对应整合环境为基础，其他版本需要编译和实机验证。
- 使用手机功能时，允许机台防火墙放行 TCP **8790**，并让手机与机台处于同一局域网。

## 安装

1. 编译获得 `MaiPlannerMod.dll`。
2. 退出游戏，将 DLL 放入 `Package/Mods/`。
3. 启动游戏，在浏览器打开 **[http://127.0.0.1:8790/](http://127.0.0.1:8790/)**。
4. 等待游戏曲库就绪，在机台登录玩家。网页会自动更新玩家和曲库。
5. 手机可扫描首页二维码，或访问 `http://<机台局域网IP>:8790/scan`。

网页资源在编译时嵌入 DLL，安装时无需复制 `web/` 目录。更新 DLL 后重启游戏并刷新网页。

## 打歌助手怎么用

1. 在机台登录玩家，进入网页的“打歌助手”。
2. 添加“待开荒”“推 b35”“收藏”等曲池卡片，按需设置筛选条件。
3. 查看“接下来三首”，确认曲目和难度。
4. 游戏处于选曲界面时点击“启动助手”。无需填写机台 IP、Mod 端口或成绩回传端口。
5. 一局结束后助手会匹配当前队列，顺延到下一首；“仅接收成绩”模式不主动推歌。

助手直接调用本机 Mod；配置中的外部成绩回传仅用于对接其他程序。

## 从源码编译

需要 [.NET SDK](https://dotnet.microsoft.com/download) 和 .NET Framework 4.8 目标包，以及本机游戏目录。`GameDir` 指向包含 `Sinmai_Data` 和 `MelonLoader` 的 **Package** 目录。

```powershell
git clone https://github.com/Koileo/MaiPlannerMod.git
cd MaiPlannerMod
dotnet build MaiPlannerMod.csproj -c Release -p:GameDir="<游戏目录>\Package"
```

产物为 `bin/Release/MaiPlannerMod.dll`。也可以使用脚本编译，添加 `-Install` 会复制到游戏 Mods 目录；安装前需先退出游戏：

```powershell
pwsh -File build/build.ps1 -GameDir "<游戏目录>\Package"
pwsh -File build/build.ps1 -GameDir "<游戏目录>\Package" -Install
```

## 配置与数据

首次启动在 `Package/UserData/MelonPreferences.cfg` 生成 `MaiPlanner` 配置项。

| 配置项 | 默认值 | 说明 |
| --- | --- | --- |
| 启用 | true | Mod 总开关 |
| 接口端口 | 8790 | 网页、扫码页与 API 的端口 |
| 局域网 | true | 允许局域网访问，手机扫码需要开启 |
| 详细日志 | false | 输出跳曲等调试信息 |
| 跳到难度页 | true | 跳曲后停在难度选择页 |
| 回传地址 | 空 | 可选的外部成绩回调地址 |
| 回传端口 | 20010 | 外部回传使用的默认端口 |

收藏、黑名单、助手配置和本地成绩覆盖保存在 `Package/UserData/MaiPlannerMod/data/`。网页修改成绩只影响 Mod 的覆盖表，不写入游戏存档。

玩家 `id` 为机台槽位索引的字符串：1P 是 `"0"`，2P 是 `"1"`，与服务器账号 ID、Aime 卡号不同。本地偏好目前也按槽位保存，换人登录相同槽位会共用该槽位的偏好。

接口没有鉴权，请仅在可信的本机或局域网使用。接口细节见 [API 文档](docs/API.md)。

## 常见问题

**玩家成绩页没有识别到玩家？**

先在机台登录。网页每 3 秒检查登录状态，只显示实际登录的玩家；仅 2P 登录时会自动选中 2P。若仍无成绩，检查游戏是否已完成玩家数据加载。

**助手预览显示“这一首暂时没有谱面数据”？**

1.0.1 修复了助手接口漏传 `song` 的问题。请更新 DLL、重启游戏并刷新网页。旧预览队列会在返回时补齐数据；若队列中的曲目已不在当前曲库，可重抽该条目。

**点歌按钮不可用？**

等待曲库加载完成并返回游戏选曲界面。游玩中或连接中断时，页面会暂停点歌，恢复后自动启用。

**点击“启动助手”没有反应？**

更新至 1.0.2，重启游戏并按 Ctrl+F5 刷新网页。按钮下方会立即显示启动进度和结果；若未登录、不在选曲界面、卡池为空或推歌失败，会显示具体原因。调整后可再次启动。

**手机打不开？**

确认使用机台的局域网 IP，检查两台设备的网络和防火墙。手机上的 `127.0.0.1` 指向手机自身，请使用首页提供的扫码地址。

## 开发与验证

目录结构：`src/` 为 C# Mod，`web/` 为内嵌网页，`verify/` 为回归检查，`docs/` 为接口与变更说明。

纯逻辑与运行时检查使用 .NET 8，游戏 / Unity 类型在这些检查中使用替身：

```powershell
dotnet run --project verify/VerifyRuntime.csproj -c Release
dotnet run --project verify/VerifyAssistant.csproj -c Release
dotnet run --project verify/VerifyLogic.csproj -c Release
dotnet run --project verify/VerifyStats.csproj -c Release
dotnet run --project verify/VerifyEndToEnd.csproj -c Release
dotnet run --project verify/VerifyAnalysis.csproj -c Release
```

编译正式 DLL 后，检查真实助手序列化代码，包括抽中难度、2P 成绩以及不存在的 STD/DX 谱面：

```powershell
pwsh -File verify/verify-assistant.ps1 -GameDir "<游戏目录>\Package"
```

网页验收需要 Node.js 22+ 和 Chrome，使用独立的临时浏览器配置及假后端：

```powershell
node verify/web-accept.mjs
node verify/ui-preview.mjs after
```

默认寻找 Windows 标准安装位置的 Chrome；其他位置可设置 `CHROME_PATH`。验收覆盖登录切换、仅 2P 登录、助手三首预览、无连接参数启动、扫码点歌以及断线恢复。

这些检查不替代真实游戏环境下的跳曲、结算时机和资源解码联调。目前自动结算采样以 1P 为主，双人各自结算与队列顺延仍需实机验证。社区谱面标签已内置，能力图按玩家成绩计算；上游未提供明确内部号的谱面不会猜测标签。水鱼差值数据尚未内置。

开发时可用 `node build/update-tags.mjs` 更新社区标签快照，也可传入已下载的 `/api/v1/dxdata` JSON 文件进行离线构建。`verify/verify-live-profile.ps1` 可从 `-BaseUrl` 读取真实机台的曲库与成绩，在临时目录中验证编译后的画像，不修改机台存档。

## 更新记录

见 [CHANGELOG.md](CHANGELOG.md)。

## 许可证与致谢

代码按 [MIT License](LICENSE) 发布；第三方代码、社区数据、字体和游戏 UI 素材按各自权利归属处理，见 [NOTICE](NOTICE)。

- [ZCM-X/maimai-song-request](https://github.com/ZCM-X/maimai-song-request)：跳曲状态机与主线程调度基础。
- 原打歌计划器（Copyright 2026 Akkarin）：分析算法、曲池规则与网页基础。
- [SortableJS](https://github.com/SortableJS/Sortable)：拖拽排序。
- SongSearch / MuNET 社区别名整理者与贡献者。
