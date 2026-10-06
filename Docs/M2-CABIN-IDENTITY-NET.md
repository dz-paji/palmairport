# M2 机舱大厅、身份与局域网联机

日期：2026-09-23。状态：M2 本机原型功能及 console、capture、双进程真实 UDP、macOS 与 Android APK 构建门禁已完成；真实 Google/Android 设备验收仍待完成。M2 sandbox 不等于完整 P0 或生产级联机。详细状态与证据见 [M2-TODO.md](M2-TODO.md)。

## 入口与玩法边界

- 产品入口：`Assets/Scenes/CabinLobby.unity`。Build Settings 为 `[CabinLobby, PalmBay, MainLoop]`，macOS/Android 产品包以 CabinLobby 启动。
- 本地 solo / 同机 coop：完整五分钟班岗，包含机场任务与 bot；可离线游玩。直接 Play `Assets/Scenes/PalmBay.unity` 且没有大厅启动配置时默认进入 solo。
- LAN host / client：host 运行权威模拟，client 发送输入意图并接收 host 快照。M2 对局是自由练习 sandbox，只开放移动和抢车 / 放车；没有航班、计时、站点任务、结算或回放。这是验证同步骨架的切片，不是完整协作班岗。

## LAN 协议概览

传输只使用 UDP：

| 通道 | 端口 | 用途 |
|---|---:|---|
| 广播发现 | 47777 | 房间 beacon 与发现；Android 用多播锁，界面保留手动 IP 加入作为发现兜底 |
| 定向会话 | 47778 | join/roster/start、客户端 intent、快照、心跳与离开 |

host 是唯一权威端。client 以 **30 Hz** 发送全量输入意图；host 以 **15 Hz** 发状态快照，client 使用快照缓冲插值。Beacon 周期为 1 秒、房间记录 5 秒过期；协议计时由 `Pump(dt)` 推进。数据报丢失允许游戏态新包覆盖旧包；控制消息依靠幂等处理和重试。

连接状态与故障处理：

- 房间最多两席，加入时处理满员和已开局拒绝；联机真人加入需要已登录并通过 13 岁准入。
- client 掉线或离开后，host 将该席转交本地 bot 并继续 sandbox；host 掉线时 client 收到 host-lost 并回 CabinLobby。
- 不支持 host migration、公网 relay、NAT 穿透、端到端加密、服务端反作弊或不可信网络的身份防伪。LAN 测试结果不代表公网可用或可上线。

## Google 登录与年龄状态

登录流程不依赖原生 Google Sign-In SDK：浏览器 OAuth authorization code + PKCE，验证 `state`、`nonce` 与 ID token 后调用 Firebase `accounts:signInWithIdp`；续期调用 Firebase Secure Token REST endpoint。sign-in 响应使用 `idToken`、`refreshToken`、`localId` 等 camelCase 字段，refresh 响应使用 `id_token`、`refresh_token`、`user_id` 等 snake_case 字段。

- 桌面 loopback 流程由本机 HTTP listener 接回授权码。当前本地私有配置没有 `desktopClientId`，因此编辑器采用显式标记的 Fake 路径；Fake 不访问 Firebase，也不证明 Google 登录成功。
- Android 使用浏览器与应用自定义 scheme 回跳。Google OAuth 配置需显式启用 custom URI scheme；包名、Android OAuth client、签名和实机回跳尚待核验。参考 [Google API Console Advanced settings](https://support.google.com/googleapi/answer/6158849?hl=en) 与 [OAuth 2.0 for Mobile & Desktop Apps](https://developers.google.com/identity/protocols/oauth2/native-app)。协议/localhost 测试不替代 Android 设备验证。
- 13 岁是组队准入门槛。DOB 用于本地判定后丢弃；持久化只记录按 Firebase UID 关联的 `AgeVerified` 状态，不保存 DOB。
- refresh token 当前保存在 `PlayerPrefs`，为本机明文偏好存储，不等于 Keychain / Keystore 或加密凭据保险箱。正式发布前需改进安全存储与 token 失效处理。
- `Assets/Resources/palmbay-auth.json` 和 `Assets/google-services.json` 属本地私有配置，含其 Unity `.meta` 一并由 `.gitignore` 忽略；不要把凭据值写入文档、日志或版本控制。

## 分享与事件

邀请页先生成岗位文案，再由 Android 系统分享 chooser 交给用户选择应用；编辑器 / macOS 使用剪贴板并提示复制。打开 chooser 或复制文案不表示已发送，也没有 WhatsApp 专用 API 集成。`palmbay.app/invite?from=…&role=…` 只是占位地址，未部署、未配置通用链接，也不会自动连接局域网房间。

事件队列以 JSONL 写在本地。GA4/Firebase Analytics 上报尚未接入，当前没有远端分析事件。举报后端、游客进度迁移与关系资产云同步也未实现。

## 验证状态与证据

最近报告的纯 C# 测试：simulation 11 / 209、single-cycle 10 / 127、net 15 / 262、identity 9 / 109、auth protocol 48 断言，共 755 条断言，均 PASS。OAuth localhost loopback 为 2 个场景 PASS（不固定断言计数）；Android manifest XML 为 2 个用例 / 7 条断言 PASS。完整编译、最终三场景校验与 layout 14,656 断言、M1/M2 capture、双进程 UDP integration、macOS BuildMac 与 Android BuildAndroid 均通过。

M2 capture 生成 17 张图、无 runtime errors；覆盖 Fake 登录取消/失败重试、DOB 无效/未来/未满 13/刚满 13、待建房和待输入 IP 恢复、登出、邀请岗位/剪贴板/事件语义、host/client fixture 标签、四模式本地往返，以及 16:9 / 19.5:9 手动 IP 布局断言。M1 最终 capture 为 4 张图，runtime errors 为 0。

LAN 验收使用两对独立 Unity 进程和本机真实 UDP socket，两个 session 的 4 个 host/client 退出码均为 0。第一轮验证 host/client 双向移动、同一模拟帧车辆争抢只产生唯一 owner、单个 0.1s client Pressed 抢车/放车边沿只消费一次、无 leave 数据包的 client socket 中断后 bot 接管；第二轮验证 host `CloseRoom` 后 client 返回 CabinLobby。验收用显式 FakeAuth，不是 Google 真实登录、物理双机或 standalone macOS 包测试。

macOS `BuildMac` 已通过，生成 `Builds/Palm Bay.app`（112 MB），日志 marker `PALM_BAY_BUILD_READY entry=CabinLobby scenes=3`；standalone 包尚未手动运行。Android `BuildAndroid` 已通过，生成 `Builds/PalmBay.apk`（约 12 MB），marker `PALM_BAY_ANDROID_READY path=Builds/PalmBay.apk scenes=3`。`aapt` 确认 package `com.palmbay.islandairport`、MinSdk 24、targetSdk 35、`UnityPlayerActivity` 与 `arm64-v8a`。本轮依次修正 Android 构建后端为 IL2CPP+ARM64、显式声明内置 `com.unity.modules.androidjni:1.0.0`，并将主题名改为安装模板实际提供的 `@style/TuanjieThemeSelector`；设备安装运行仍未验收。

证据：`Evidence/m2-lobby-review.txt` 与 `Evidence/m2-*.png`（17 张）；`Evidence/m1-review.txt` 与 4 张验收图；`Evidence/netplay-summary.txt`、`Evidence/net-host-session1.txt`、`Evidence/net-client-session1.txt`、`Evidence/net-host-session2.txt`、`Evidence/net-client-session2.txt` 和对应日志；`Evidence/level-layout-validation.txt`、`Evidence/main-loop-scene.png`。构建/最终校验日志位于仓库外：`/tmp/palmbay-m2-final-build-mac.log`、`/tmp/palmbay-m2-final-build-android-theme.log`、`/tmp/palmbay-m2-final-scenes.log`。

原继承例外"第二架航班中间机位车辆移动超时"已于 2026-09-28 修复：`DriveRoute` 对稍微偏离侵蚀路网的端点做吸附规划而不是返回 NULL，playtest 也不再对 NULL 退回直线行驶；`Evidence/main-loop-playtest.txt` 现为 PASS（三个航班循环全过，无 runtime error）。

## 后续范围

后续：在两台物理 Android 设备上安装运行 APK，验收 LAN 与 Google custom scheme 回跳；macOS 包尚未手动运行。refresh token 安全存储、GA4、真实邀请深链、低端 Android 性能、游客进度迁移、举报、语音 RTC、原生登录 SDK 与 host migration 均待后续。M2 不应被表述为完整 P0 或生产级联机已验收。
