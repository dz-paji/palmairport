# M7 PRD 覆盖与验收边界

2026-10-04，M7 本机证据更新；不声明全部验收完成。需求依据为 `../02-PRD.html` 正文，以及 [P0 计划](P0-HIGH-FIDELITY-PLAN.md)、[M0 规则](M0-BASELINE.md)、[M3](M3-TASKS-NET.md)、[M4](M4-SHIFT-COOP.md)、[M6](M6-RESULTS-SHARING-PLAN.md) 与 `README.md`。F0–F21 在当前 PRD 中均有定义；编号依据正文逐条核对，不按模块顺序推断。

**本轮按用户最新边界仅开发与本机验收：没有 Android 真机，不部署云服务、不修改 OAuth 配置，M5 / F21 延期。** 没有公开下载地址；邀请去除占位链接，改为获取同版本安装包及同网组队说明。原文中的下载链接 / WhatsApp 预览卡片要求保留为后续事项。旧计划的两台 Android 验收目标不代表本轮已有设备。

表中实现路径默认以 `Assets/Scripts/` 为根。`C:<名称>` 指 [M7 console 最终证据目录](../Evidence/m7-console/20261004T133115.367716Z-27351/summary.json) 中相应 `.log` / `.exitcode`：12 项串行门禁全部 exit 0，执行时间为 2026-10-04 13:31 UTC；未启动 Editor、真实监听端口、ADB 或云服务。额外 [M7 身份检查](../Evidence/m7-identity-review.txt)与 [M4 UDP 最终回归](../Evidence/m4-netplay/summary.txt)已通过。身份检查使用显式 FakeAuth / 内存 socket / 领域 fixture；UDP 使用隔离 Unity 进程 / FakeAuth，不代表真实账号或双物理设备。其他历史报告仅证明各自归档版本和 fixture 范围。

| PRD | 实现位置与覆盖内容 | 已有验证证据 | 当前状态与未验收部分 |
|---|---|---|---|
| F0 飞机大厅 | `Gameplay/LobbyApp.cs`、`AppState.cs`；`Presentation/LobbyCanvas.cs`；`Assets/Scenes/CabinLobby.unity`：游客、衣柜、bot、房间列表 | [M2 大厅最终回归](../Evidence/m2-lobby-review.txt)，Fake 登录、衣柜、往返与两种比例；[M7 身份检查](../Evidence/m7-identity-review.txt) | 已实现并本机验证；刷新保留房间 UI / ready、失效离房与重新组队通过；标准 Mac `.app` 已显示游客大厅，完整独立 player 自动化受启动工具阻塞，未通过 |
| F1 计时与航班流转 | `Core/AirportSimulation.cs`、`Core/Shift/ShiftSim.cs`；`Gameplay/AirportGame.cs`：300 秒、五架错峰、归零冻结、完成结果保留 | `C:simulation`、`C:shift`；[M4 UDP 最终回归](../Evidence/m4-netplay/summary.txt)，四组八进程通过 | 规则、300 秒 / 归零 / 回放与重试本机通过；首周至少送走两架待真人验证 |
| F2 站点投入、等待、取出 | `Core/Shift/ShiftSim.cs`；`Gameplay/AirportGame.cs`：餐食下单、等待、出货阻塞、装车与交付 | `C:shift`；[餐食双端证据](../Evidence/m3-meal-captures.txt)，[M3 UDP 历史汇总](../Evidence/netplay-summary.txt) | 已实现，规则与历史本机 UDP 通过；无需教学即理解的目标待真人验证 |
| F3 共享车辆与复用 | `Core/Shift/ShiftSim.cs`、`Core/Net/RoomManager.cs`；`Gameplay/AirportGame.cs`：唯一占用、载货归属、卸货后空车复用、到达行李回运 | `C:shift`、`C:net`；[行李双端证据](../Evidence/m3-baggage-captures.txt) | 已实现，规则与历史双端检查通过；自发车辆分工对话未验证 |
| F4 燃油接力与漫油 | `Core/Shift/ShiftSim.cs`；`Gameplay/AirportGame.cs`、`Presentation/AirportWorld.cs`：M3.3r 全点按流程：拿枪、插车、开阀、关阀自动归位、运输、取车载油管、接管自动加注、断管续加、满溢 2 秒宽限漫油、清油渍 | `C:shift`；[真实 bot / 输入检查](../Evidence/m3-fuel-playtest.txt)、[燃油双端证据](../Evidence/m3-fuel-captures.txt) | 已实现，浮点边界与取消保留等本机通过；脚本耗时对照不能替代真人接力收益和漫油体验评审 |
| F5 移动与情境交互 | `Gameplay/AirportGame.cs`；`Presentation/AirportHudCanvas.cs`：摇杆 / 键盘、点按 / 按住、对象占用与取消，无投掷 | `C:shift`、`C:net`；[M1 画面检查](../Evidence/m1-review.txt)、[燃油输入检查](../Evidence/m3-fuel-playtest.txt) | 已实现，有本机逻辑与画面证据；Android 触控与非玩家 10 分钟掌握未验收 |
| F6 航班板与屏外搭档 | `Presentation/AirportHudCanvas.cs`、`AirportWorld.cs`：顶部航班号 / 倒计时 / 四任务状态、边缘搭档与 bot 标记 | [合作检查](../Evidence/m4-coop-review.txt)：16:9 / 19.5:9、进出屏幕、bot 标记、安全区 fixture | 已实现，已有本机检查；手机真实可读性与无需语音判断优先级待验收 |
| F7 LAN / solo + bot | `Core/Net/RoomManager.cs` 及同目录传输；`Core/Auth/BotMemory.cs`；`Gameplay/LobbyApp.cs`、`AirportGame.cs`：发现 / 手动 IP、权威裁定、接管、记忆 | `C:net`、`C:bot-memory`；[M4 合作检查](../Evidence/m4-coop-review.txt)、[M4 UDP 最终回归](../Evidence/m4-netplay/summary.txt)、[M7 身份检查](../Evidence/m7-identity-review.txt) | 已实现并本机验证；失效终止 LAN / 局中返回大厅、checkpoint 迁移后席位 1 的姓名 / 颜色及重试映射通过。完整独立 Mac 自动化受启动工具阻塞，未通过；双物理设备及 bot “刚好不如真人”未验收 |
| F8 邀请与入房 | `Gameplay/ShareService.cs`、`LobbyApp.cs`；`Presentation/LobbyCanvas.cs`：岗位邀请、桌面复制 / Android chooser、本地事件、同网说明 | [M2 大厅最终回归](../Evidence/m2-lobby-review.txt)、[16:9 邀请画面](../Evidence/m2-lobby-invite.png)、[19.5:9 邀请画面](../Evidence/m2-lobby-invite-19_5x9.png) | 去占位链接、获取同版本安装包 / 同 Wi-Fi 说明与屏内完整呈现已本机验证；没有下载地址 / 预览卡片 / WhatsApp 专用集成，打开分享入口不等于发送；含登录 ≤3 步及邀请转化未验证 |
| F9 星级、自动回放、四按钮与分享 | `Gameplay/AirportGame.Results.cs`、`AirportGame.cs`；`Presentation/AirportHudCanvas.cs`；`Core/Results/`、`Core/Progress/`；`Sharing/SilentVideoEncoder.cs`；`Assets/Plugins/Android/{SilentVideoEncoder,ReplayVideoProvider,ReplayVideoShare}.java`；macOS 分享插件 | `C:settlement`、`C:progress`、`C:m6-video`；[M6 结果检查](../Evidence/m6-results-review.txt)、[实际 MP4](../Evidence/m6-replay.mp4)；[M7 Android](../Evidence/m7-android-build.log) / [Mac 构建](../Evidence/m7-macos-build.log)通过 | 客户端已实现，1 / 2 / 4 架对应 1 / 2 / 3 星、12 倍自动回放停帧、四按钮本机通过；下一关提示未开放。完整独立 Mac smoke 受启动工具阻塞，未通过；真机编码、分享接收应用、真实云端进度未验收；金币及主动回放控制属于 F9 完整版，范围外 |
| F10 送错后果 | `Core/Shift/ShiftSim.cs`、`ShiftEvents.cs`；`Gameplay/AirportGame.cs`：误接收航班行李永久失败，无额外扣分，事件进入回放 | `C:shift`；[行李双端证据](../Evidence/m3-baggage-captures.txt)、[错送回放](../Evidence/m3-baggage-replay.txt) | 已实现，规则 / 可见提示 / 回放本机通过；“可归因且好笑”的真人目标未验证 |
| F11 登机与道路占用 | `Core/Shift/ShiftSim.cs`、`ShiftSnapshot.cs`；`Gameplay/AirportGame.cs`、`Presentation/AirportWorld.cs`：左侧登机口、放行 / 关门、保留旅客 ID 与进度、实体阻车 | `C:shift`、`C:net`；[登机双端同态证据](../Evidence/m3-boarding-captures.txt)与三份冻结快照 | 已实现，规则与历史本机 UDP 通过；拥堵是否可预判、可干预且不烦躁待真人评审 |
| F12 转机行李 | 无本轮实现；当前到达 / 出发行李不含转机分拣链 | 无 F12 验收证据 | PRD P1，范围外、未实现 |
| F13 跨局共同记录三种表达 A/B | 无完整 F13 实现；`Core/Auth/BotMemory.cs` 的本机合作统计用于 bot 分工，不等于关系对三层进度 / A/B 系统 | `C:bot-memory` 只验证本机记忆 | PRD P1，范围外；三种表达及关系资产假设未验证 |
| F14 赛季承接 | 无本轮实现 | 无 | PRD P1，范围外；通行证与商业化在留存验证之后 |
| F15 引导车与机位决策 | 无本轮实现；当前自动机位安排不等于玩家引导接机 | 无 F15 验收证据 | PRD P2，范围外、未实现 |
| F16 每日挑战班岗 | 无本轮实现；固定首关航班表不等于每日全球统一挑战 | 无 | PRD P2 候选，范围外、未实现 |
| F17 废弃飞机居住地 | 无本轮修理 / 收益 / 家园资产实现；机舱大厅不等于 Meta 居住地 | 无 | PRD 局外 Meta，范围外、未实现 |
| F18 纪念品与图鉴 | 无本轮实现 | 无 | PRD P2 / Meta，范围外、未实现 |
| F19 居住地无尽模式 | 无完整 F19 实现；`Core/Shift/PracticeFlightScheduler.cs` 与 `StartSandbox()` 仅为任务练习，没有居住地入口或确定的生存失败制 | `C:shift` 验证练习循环 / 有界历史，不验证 F19 | PRD Meta，范围外；不得以练习模式通过宣称 F19 完成 |
| F20 登录、年龄与结算资格 | `Identity/FirebaseRestAuth.cs`、`OAuthSecurity.cs`、`AgeVerificationStore.cs`；`Gameplay/AppState.cs`、`LobbyApp.cs`；`Core/Results/SettlementEligibility.cs`、`Core/Progress/`：登录门禁、13 岁、按身份保存资格、退出不记 bot 代打 | `C:identity`、`C:auth-protocol`、`C:android-manifest`、`C:settlement`、`C:progress`；[M7 身份检查](../Evidence/m7-identity-review.txt)、[标准 Mac UI 检查](../Evidence/m7-standard-ui-review.txt) | 刷新保房间 / ready、失效离房 / 局中回大厅、无关失败保有效身份本机通过；正式无配置 player 显示游客与 Google 未配置提示，不回退 Fake。OAuth loopback 因 bind 权限拒绝未通过；真实 Google 登录、Android 回跳、年龄拦截日志后台归档和真实 Firestore 写入未验收 |
| F21 语音与管理 | 仅有 HUD 麦克风占位；RTC、静音 / 屏蔽、权限恢复、举报后台未实现 | 无 F21 功能通过证据；无声视频不等于语音实现 | 用户批准 M5 延期，不计入本轮验收，不标完成 |

**已验证与阻塞**：最终 console 12/12、M7 身份 / 迁移 / 重试、大厅邀请与 M4 四组八进程 UDP 均通过，报告无 runtime errors。M7 Android / Mac 包构建通过；[APK 元数据](../Evidence/m7-apk-metadata.txt)为 1.0.7 / code 7、ARM64、minSdk 24 / targetSdk 35，[打包 manifest](../Evidence/m7-apk-manifest.txt)含非导出的只读视频 provider、无麦克风权限。

[Firestore 规则尝试](../Evidence/m7-rules-attempt.txt)因 TCP `Operation not permitted`、[OAuth loopback](../Evidence/m7-loopback-oauth.txt)因 `Socket.Bind: Access denied` 未通过：是测试启动 / 通信受执行环境限制，尚未证明受测规则或回调功能通过，也不据此认定业务实现失败。独立 CLI / batch 两次 smoke 在 macOS 原生 `RegisterApplication` 阶段 abort；标准系统 `.app` 入口已显示游客大厅与 Google 未配置提示，[UI 观察](../Evidence/m7-standard-ui-review.txt)与 [player 日志](../Evidence/m7-standard-player.log)无 Error / Exception。专用 UI 包首次因 Bundle ID 存档路径被隔离校验拒绝，校验已修正；后续工具启动超时且未产生 managed 报告，见 [UI 尝试](../Evidence/m7-ui-launch-attempts.txt)。完整独立 player 自动化状态 **BLOCKED / 未通过**；未宣称完整手动试玩或系统分享验收完成。

**外部待验收（本轮不执行）**：

- 两台 Android 安装 / 联机、触控与安全区、性能、原生 MediaCodec 编码、分享接收应用读取视频和取消；无设备时保留待验收。
- 真实 Google OAuth、Android 回跳、登录到同网首局的完整路径；本轮不修改 OAuth 配置。
- Firestore 模拟器规则执行、数据库启用 / 生产部署、真实账号写入与断网恢复 / 跨账号拒绝 / 防重。客户端纯逻辑通过不能证明 Security Rules 已执行；本轮不部署云端。
- 邀请下载地址 / WhatsApp 预览卡片、真实目标应用分享，以及邀请到双人首局转化；当前本地 JSONL 不能证明 GA4 已接入或内容已发送。
- 真人上手、首局完成架数、分工与燃油接力收益、堵车 / 送错的可归因体验、重试 / 分享意愿及留存。所有 MDA 目标仍为待验证设计意图。
- F21 随 M5 延期；F9 完整版、F12–F19、PRD F22 清洁 / F23 公网深链及未编号的跨区协作 / 投掷均不属于本轮交付范围。
