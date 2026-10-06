# M2 历史状态与待办快照（截至 2026-09-23）

> 本文记录 M2 范围与当时的验收状态，不描述当前 LAN 功能范围。当前 M3 进度见 [Docs/M3-TASKS-PLAN.md](M3-TASKS-PLAN.md) 和持续验收记录 [Docs/M3-TASKS-NET.md](M3-TASKS-NET.md)：截至 2026-10-04，LAN 已接入循环航班的餐食、行李、燃油与登机共享规则；M3.0–M3.5 已通过批准范围内的编辑器 / 本机 UDP 原型验收，完整六组 UDP 共 12 个 host / client 退出码均 0，四类任务截图与回放齐全。M2 当时的 console、capture、netplay、macOS / Android APK 构建仍为历史产物，本轮 M3 未重新构建包。Android 真机与真实 OAuth 验收仍待完成。

## 已实现范围

- **入口与本地流程**：`CabinLobby` 是产品入口；本地 solo / 同机双人进入完整班岗，直接 Play `PalmBay` 默认 solo。大厅档案、bot / 本地双人流程和跨场景返回已实现。
- **M2 LAN sandbox（历史范围）**：同一局域网内 host 权威；UDP beacon 发现与 UDP session，始终提供手动 IP 加入。**M2 时**游戏态仅同步移动、角色和车辆抢放，没有航班、倒计时、站点任务、结算或回放；该描述不适用于当前 M3 任务练习。客户端掉线后该席交由 host bot 接管；host 掉线后客户端回大厅。没有 host migration，也不是可用于不可信公网的生产网络方案。
- **身份与年龄**：Google 浏览器 OAuth 使用 PKCE、state、nonce，交换 Firebase Auth REST token；登录响应字段用 camelCase，refresh 响应用 snake_case。13 岁门槛按 UID 保存验证状态，不保存 DOB。编辑器缺少 `desktopClientId` 时显式走 Fake，Fake 不发 Firebase 请求。refresh token 目前写入 `PlayerPrefs`，属于明文本地存储。
- **分享与事件**：Android 使用系统分享 chooser，桌面端复制文案；事件写本地 JSONL，GA4 尚未接入。邀请 URL `palmbay.app/invite` 是未部署的文案占位，不是可用深链或 LAN 加入链接。
- **Android 配置**：manifest 含网络、Wi-Fi、多播权限和 OAuth intent-filter，并使用 Tuanjie 内置 `@style/TuanjieThemeSelector`；`com.unity.modules.androidjni:1.0.0` 已显式声明，`DemoBuilder.BuildAndroid` 设置 IL2CPP+ARM64 与 `forceInternetPermission`。APK 构建已通过，尚未在真机安装运行。

## 门禁状态

| 项目 | 最近报告结果 | 状态 |
|---|---|---|
| simulation / single-cycle / net / identity | 11 组 / 209 断言；10 / 127；15 / 262；9 / 109 | PASS |
| OAuth 协议与 loopback | auth protocol 48 断言；loopback 2 个场景，真实 localhost 回跳 | PASS |
| Android manifest XML | 2 个用例 / 7 断言 | PASS |
| Unity 编译 | 完整 M2 C# 编译 | PASS |
| 场景与布局 | 最终 `-buildTarget StandaloneOSX DemoBuilder.ValidateScenes` exit 0；全部 3 场景；Level layout 14,656 断言 | PASS |
| M2 capture | 17 张 PNG、runtime errors 0；Fake 登录取消/失败重试、年龄边界、待操作恢复、登出、邀请语义、房间 fixture、四模式往返、两比例手动 IP 布局 | PASS |
| M1 capture | 4 张 PNG、runtime errors 0 | PASS |
| LAN 集成 | 两对独立 Unity 进程、两组真实 UDP session；4 个 host/client 退出码均为 0 | PASS；不代表双物理设备或 standalone 玩家包 |
| macOS BuildMac | `Builds/Palm Bay.app`，112 MB；marker `entry=CabinLobby scenes=3` | PASS；尚未手动运行 standalone 包 |
| OAuth 真登录 / Android 深链 | 编辑器当前使用 Fake；Google/设备回跳尚未验证 | PENDING |
| Android BuildAndroid | `Builds/PalmBay.apk`，约 12 MB；marker `PALM_BAY_ANDROID_READY path=Builds/PalmBay.apk scenes=3`；aapt 确认 package `com.palmbay.islandairport`、MinSdk 24、targetSdk 35、`UnityPlayerActivity`、`arm64-v8a` | PASS；尚未真机运行 |
| 物理 Android 双机 | 尚未使用荣耀 Magic 8 / Galaxy S23 验收 | PENDING |

纯 C# 最近报告计数合计 755 条断言（209 + 127 + 262 + 109 + 48）；loopback 只记录 2 个场景通过，manifest 的 7 条断言单独列出，不混入总数。Android 自定义 URI scheme 需要在 OAuth 配置中显式启用；Google 文档也说明原生应用回调 URI 的安全约束。当前客户端配置和真机回跳仍待实测，不能将协议单测或成功打包视作真机登录验收：[Google API Console 的自定义 URI scheme 设置](https://support.google.com/googleapi/answer/6158849?hl=en)、[OAuth 2.0 for Mobile & Desktop Apps](https://developers.google.com/identity/protocols/oauth2/native-app)。

## 尚待完成

1. 在荣耀 Magic 8 与 Galaxy S23 上安装 APK，验证发现、手动 IP、移动/抢车一致性、掉线 bot 接管与 Android OAuth 深链。Google OAuth 控制台需显式启用 custom URI scheme，并核对 Android OAuth client、包名和签名。
2. macOS 包已生成但尚未手动运行；双进程 UDP 证据来自两个本机 Unity 进程，不是设备间或 standalone 包验收。
3. 真实 Google 登录、refresh token 安全存储、游客进度迁移、GA4、真实可解析邀请链接与举报处理方案仍待后续。低端 Android 性能、语音 RTC、原生 Google 登录 SDK 和 host migration 也属后续工作。
4. **编辑器授权记录**：M2 记录中“ULF 授权有效期至 2026-09-28”是历史到期日，不代表当前已过期。2026-10-01 本轮确认当前编辑器自动化授权为绿；当前有效期尚无新证据，不填写新日期。后续 editor/batchmode 验收开始前，确认当时授权仍可运行。

## 已知基线例外

原继承例外"第二架航班中间机位车辆移动超时"已于 2026-09-28 修复，不再是例外。根因：`Level1Map.FindGridRoute` 要求起终点精确通过 0.37 侵蚀路网的 `InDrivable`，而车辆停在站点交互半径内时 footprint 可能差几毫米不满足，规划器返回 NULL；playtest 又对 NULL 静默退回直线行驶，撞进行李车 0.92m 碰撞半径后整车步进取消、永久卡死（43% 的端点浮点扰动会触发）。修复：端点吸附到最近的可达网格节点（按 0.35 游戏裕量与车辆净空校验），吸附点作为首个路径点发出；playtest 对 NULL 改为显式报错。修复后 `MainLoopPlaytest.Run` 三个航班循环全过，Evidence/main-loop-playtest.txt 为 PASS。

## 证据索引

- M2 截图：`Evidence/m2-*.png`（17 张；具体尺寸和场景见 `Evidence/m2-lobby-review.txt`）。
- M1 截图：`Evidence/m1-review.txt` 列出的 4 张验收图。
- LAN：`Evidence/netplay-summary.txt`、`Evidence/net-host-session1.txt`、`Evidence/net-client-session1.txt`、`Evidence/net-host-session2.txt`、`Evidence/net-client-session2.txt`，及对应 `.log`。
- 场景/历史例外：`Evidence/level-layout-validation.txt`、`Evidence/main-loop-scene.png`、`Evidence/main-loop-playtest.txt`。
- macOS：本地 `/tmp/palmbay-m2-final-build-mac.log`；Android 成功日志 `/tmp/palmbay-m2-final-build-android-theme.log`。构建/验证日志位于仓库外。
- APK：`Builds/PalmBay.apk`（约 12 MB）；尚未生成 `Evidence/android/m2-*.png` 真机截图。
