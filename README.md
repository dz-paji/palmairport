# PALM BAY · 小岛机场

基于 `../02-PRD.html` 的 Unity 合作玩法 demo。产品入口是机舱大厅；本地 solo / 同机双人和正式 LAN 房间均进入 300 秒、五架错峰航班的完整班岗，单人可与 bot 合作。M3.0–M3.5 的四类任务已完成验收；M4 的有限 LAN 班岗、断线续玩、共同回放和合作记忆已实现，开发与本机自动化验收已完成。M6 结算、无声 MP4 导出、系统分享入口和账号进度客户端已实现；云端规则部署、真实账号和真机分享仍待验收。

## 打开试玩

1. 在 Unity Hub 中添加本目录 `game`。目标版本 **Unity 2022.3 LTS，3D Built-in Render Pipeline**；代码也使用本机团结 2022.3.61t14 的程序集完成编译检查。
2. 打开 `Assets/Scenes/CabinLobby.unity`，点击 **Play**。大厅可选择本地 solo / 同机双人并开始完整班岗，也可创建或加入同一局域网内的房间。
3. 直接打开 `Assets/Scenes/PalmBay.unity` 仍可 Play 完整班岗，未带大厅启动配置时默认 solo。Game 视图推荐 **1600 × 1000** 或 16:10；点一下 Game 视图让键盘输入获得焦点。

关卡布局与道路分组已按 2026-09-22 Figma 同步，两个场景共用 `Level1Map`。原始矢量参考及同步说明见 [Docs/LEVEL1-FIGMA-SYNC.md](Docs/LEVEL1-FIGMA-SYNC.md)。MainLoop 的保存场景可直接预览，运行时也会检查布局版本。

Build Settings 顺序为 `[CabinLobby, PalmBay, MainLoop]`：机舱大厅是产品入口，PalmBay 是完整本地班岗，MainLoop 是单航班调试场景。场景重建菜单见 **Palm Bay** 下的重建项。重建会替换当前场景，编辑其他场景时请先保存。

### 完整本地班岗操作

| 操作 | 玩家 1 | 玩家 2 |
|---|---|---|
| 移动 / 驾驶 | WASD（或左下虚拟摇杆） | 方向键 |
| 点按情境交互 | E（或右下情境按钮） | 右 Shift |
| 按住持续动作（交付、清理油渍等；燃油全部为点按） | 按住 E（或按住情境按钮） | 按住右 Shift |
| 切换目标航班 | Q（或点按底栏目标 chip） | Enter |
| 暂停 / 地勤手册 | Esc / F1（或右上齿轮） | 共用 |

触屏控件为运行时程序化 uGUI（无第三方字体资产），与键盘可混用：触屏操作 P1 的同时键盘仍可操作 P2。HUD 五区为左上班岗倒计时、顶部航班表、右上麦克风（占位）与设置、左下摇杆、右下情境动作钮（文案随情境变化）；安全区自动适配。

- 靠近车辆点按接管，离开站点后点按放开。车辆与货物留在原地，可交给搭档。
- **餐食（M3 已迁移）**：在餐食站下单 → 等 5 秒 → 餐车装载 → 机位交付 2.5 秒。中断会清零读条，餐食仍留在车上。
- **行李（M3 已迁移）**：空车先到飞机卸下到达行李并运回行李站 → 按所选目标航班装载出发行李并保留原属航班号 → 到实际接收机位交付 2.5 秒。中断清零读条但留货；错送完成后接收航班行李任务永久失败且不额外扣分，原属航班不受影响、可重新装货。
- **燃油（M3.3r 简化流程，2026-10-06 验收完成）**：全部为单次点按，无长按。油车停在站边：点油站拿枪 → 点油车插枪 → 点阀门开阀，油车自动注油 → 再点阀门关阀，插在车上的油枪自动归位到油站（无需回车边收枪）。站内油枪永不随车离站；油车自带车载油管专用于机位加注。驾驶到可加注机位：点油车取车载油管 → 点飞机接管并自动注油 → 再点飞机断开，油管自动收回油车；手持油管时点油车即收回。机位上该航班仍需燃油且油车有油时点油车取管，否则上车驾驶。阀门开着、站内油枪插在车上或车载油管不在车上时油车不能驾驶；驾驶时不能操作油枪或阀门。站内资源无限，中断保留油车储量与航班进度；阀门开着而油枪未插车立即漫油，油车满箱仍开阀或飞机加满仍接管持续 2 秒后漫油。路面油渍使油车减速，玩家需徒步按住清理，漫油不消耗储量、不导致任务失败。全量复验见 [Evidence/m3r-unity-regression.txt](Evidence/m3r-unity-regression.txt) 与 [Docs/M3-TASKS-NET.md](Docs/M3-TASKS-NET.md)。
- **登机（M3.4 验收完成）**：选航班可见队伍与路线；餐食与燃油完成后，到登机口点按放行 / 关闭。关闭仅停止新放行，已出发旅客继续；重开保留进度与旅客序号，旅客穿过道路时车辆需要让行，客户端镜像相同阻车事实。
- 装货前确认底栏中的目标航班。出发行李错送不会纠正后继续交付：它会消耗实物并锁定接收航班的行李任务。

正式本地和 LAN 五分钟班岗中，四项完成后自动起飞；完成任务每项 50 分，起飞另加 150 分及剩余时间奖励，送走 **1 / 2 / 4 架**分别得到 **1 / 2 / 3 星**。300 秒归零即结算并停止移动与作业，保留已完成结果；完成时间恰好等于截止时间仍计入完成。LAN 的重试由当前权威端启动并同步新局身份；客户端等待房主重开。M3 的循环航班练习仍通过显式测试入口保留，六组旧剧本继续回归。

## 实现范围

包括程序化 3D 场景、情境交互、共享车辆、餐食生产、到达/出发行李、燃油储备、实体旅客阻车、5 航班与三机位、bot、顶部任务板、暂停帮助、星级结算和本地状态回放。四类任务现共用纯 Core 规则与 client 只读镜像；M3.0–M3.5 在编辑器 / 本机 UDP 原型范围内均已验收。

本地 solo / 同机双人班岗离线可玩，无需登录。正式 LAN 采用同网 UDP 房间发现、单一权威输入裁定与快照镜像，餐食、行李、燃油和登机使用相同 Core 规则。手动输入 IP 可作为广播发现失败时的加入方式。客户端退出或超时后 bot 接管；房主主动退出或突然失联后，剩余客户端从最后收到的权威检查点接任，继续原局并保留 `LocalSeat=1`，原房主席位变为 bot。检测失联的等待时间不补扣到班岗时间；最后检查点之后未收到的状态无法恢复。旧权威与旧局消息不能覆盖接管或重试后的状态。显式 `CloseRoom()` 仍终止房间。此模式面向可信 LAN 原型，不承诺任意网络分区下无双权威，也未实现后端认证裁定或反作弊。M2 移动车辆 sandbox 与 M3 无限任务练习的描述属于历史范围。

Google 登录通过浏览器 OAuth PKCE 与 Firebase Auth REST API 接入，不使用原生 Google 登录 SDK。编辑器缺少 Desktop OAuth client ID 时会明确使用 Fake 登录，不产生 Firebase 网络请求；正式玩家包缺配置时明确提示登录不可用，不再自动使用 Fake 账号，游客离线仍可玩。真实 Google 登录与 Android 真机回跳尚待验证。年龄门槛为 13 岁：本地只保存按 Firebase UID 关联的年龄验证状态，不保存生日。refresh token 通过 `PlayerPrefs` 保存在本机明文偏好存储中，不属于安全凭据存储。

邀请使用 Android 系统分享 chooser，桌面端复制到剪贴板。打开 chooser 或复制文案不代表内容已发送；目前没有 WhatsApp 专用集成。M7 已按用户确认移除未部署的邀请链接；邀请文案提供获取同版安装包、同一 Wi-Fi、登录验证及建房 / 手动 IP 加入说明，当前没有公开下载地址。事件只追加到本地 JSONL；GA4 上报尚未接入。本地与 LAN 回放记录实际状态和任务事件，自动加速播放并在结束处停帧；归因事件只显示一次。LAN 按约 0.5 秒采样、最多 602 槽保存历史，客户端补取丢失样本并使用权威播放时间；接管时无法从离线房主补回的历史段明确显示缺失，不生成虚构帧。M6 从这些真实历史状态导出无声 MP4，不采集麦克风；缺段会披露。结算提供分享、再试一次、下一关和返回大厅，下一关暂未开放。Android 原生编码并打开系统分享面板，Mac 通过本机 ffmpeg 编码并打开原生分享面板；打开面板不代表发布成功。合作记忆保存本机 JSON：共同局数、送走架数、真人实际完成任务和上次同玩日期，bot 优先避开真人常做任务；同局结算只写一次，去重身份保留最近 256 局，累计计数继续保留。语音与交流管理属于 M5，用户于 2026-10-04 确认本轮跳过并延期，不计入本轮 P0 验收。

详细设计、PRD 覆盖和真人试玩清单见 [Docs/DEMO-DESIGN.md](Docs/DEMO-DESIGN.md)；M1 高保真样板的决策与验收对照见 [Docs/M1-HIFIDELITY-SAMPLE.md](Docs/M1-HIFIDELITY-SAMPLE.md)；M2 大厅、身份、联机范围与证据索引见 [Docs/M2-CABIN-IDENTITY-NET.md](Docs/M2-CABIN-IDENTITY-NET.md)；M3 批准计划与当前验收分别见 [Docs/M3-TASKS-PLAN.md](Docs/M3-TASKS-PLAN.md) 和 [Docs/M3-TASKS-NET.md](Docs/M3-TASKS-NET.md)；M4 实施约束和当前实现、证据分别见 [Docs/M4-SHIFT-COOP-PLAN.md](Docs/M4-SHIFT-COOP-PLAN.md) 与 [Docs/M4-SHIFT-COOP.md](Docs/M4-SHIFT-COOP.md)。

## 当前 M4 实现与检查（2026-10-04，开发与本机自动化验收完成）

六项纯 C# 套件（simulation、single-cycle、network、identity、shift、bot-memory）共 **101 组 / 6573 断言，0 失败**；加上 auth protocol 48 与 manifest 7，共 **6628 断言**、103 个有报告组数的用例组，完整 runtime / editor 编译 PASS，见 [Evidence/m4-final-console.txt](Evidence/m4-final-console.txt)。auth protocol 不报告组数。

[M4 四场景真实 UDP](Evidence/m4-netplay/summary.txt) 最终全量 PASS：完整班岗 / 共同结果回放 / 权威重试、客户端异常断线、房主主动离开、房主异常失联；八个隔离 Unity 进程退出码均 0、运行时错误均 0，七张 1920×1080 图齐全。两种房主退出后均能继续原局、自动回放到终帧，实际结算界面会披露无法恢复的历史缺段，文字断言和截图均已通过。测试使用 FakeAuth 和明确领域 fixture，不代表真人把五架航班全部完成。

[合作检查](Evidence/m4-coop-review.txt) 最终本地复验 PASS，覆盖真实 bot 历史偏好、本机记忆去重 / 重新加载、16:9 和 19.5:9 屏外搭档提示、两轮有限本地班岗与自动回放，设备原记忆文件保留。场景 layout 14,430、MainLoop 三轮 / 三个 ramp 与第四航班、M1 四图、M2 17 图、M3 失败回放一图、合作三图及燃油 bot / 输入七项 Unity 门禁全部 exit 0、零运行时错误，见 [m4-unity-regression.txt](Evidence/m4-unity-regression.txt)。额外 localhost OAuth loopback 2 组 / 9 断言 PASS，不计入纯 console 汇总；最后源码修改后静态编译再次 PASS。M3 六组 UDP 的登机测试反射参数不匹配已修复，原断言保留；修复后[六组全量重跑](Evidence/netplay-summary.txt) PASS，12 个进程 exit 0、运行时错误 0。失败记录保存在 [m4-m3-regression-attempt1](Evidence/m4-m3-regression-attempt1/netplay-summary.txt)。开发自动化门禁全部完成，详见 [M4 实现与验收记录](Docs/M4-SHIFT-COOP.md)。

M4 尚未完成真人上手 / 分工评审、两台 Android、真实 Google OAuth 与独立安装包运行验收。M5 语音与交流管理本轮跳过（延期、未实现）；M6 已进入实现与验收，详见 [M6 记录](Docs/M6-RESULTS-SHARING-PLAN.md)。跳过 M5 不代表上述 M4 待验收项已通过。

## 当前 M7 开发与交付（2026-10-04）

本轮按用户确认先完成开发和本机验收，不部署云端或修改 OAuth，两台 Android 与真人评审后续进行；M5 仍延期。已修复正式包缺配置自动 Fake、凭据续期后房间面板丢失、凭据失效仍可开局 / 留在 LAN，以及迁移后重试 seat1 档案错位；邀请去掉占位链接并提供同网说明。

新增 `Tools/test-m7-console.py` 一键本地门禁、`M7IdentityReview.Run` 身份恢复回归、独立隔离的 Mac player smoke 与交付打包脚本。身份恢复及迁移重试 Unity 验收 PASS，运行时错误 0；邀请两种宽高比与完整大厅回归 PASS。本轮 12 项 console、身份 / 大厅回归、四组真实 UDP 均通过；正式 Mac 包已观察到游客大厅。完整独立包自动化、Firestore 规则和 OAuth loopback 仍受本机环境 / 工具阻塞，不能标为通过。当前证据与限制见 [M7 计划及交付记录](Docs/M7-DELIVERY-PLAN.md)、[运行说明](Docs/M7-RUNBOOK.md)、[PRD 覆盖表](Docs/M7-PRD-COVERAGE.md)。开发版号为 **1.0.7 / Android code 7**，最终构建状态以交付记录为准。

## 当前 M6 实现与检查（2026-10-04）

补充实现及验证索引：[M6 实现记录](Docs/M6-RESULTS-SHARING.md)。后续 M7 记录和交付产物继续保留。

结算采用送走 1 / 2 / 4 架对应 1 / 2 / 3 星；全机场俯视回放自动 12 倍速并停帧。Unity 本地 300 秒实际模拟验收通过，覆盖四按钮、16:9 / 19.5:9、安全区、导出/取消恢复、重试与返回大厅；生成的 H.264 MP4 为 960×540、24 fps、624 帧、26 秒、无音轨，见 [验收报告](Evidence/m6-results-review.txt) 和 [回放视频](Evidence/m6-replay.mp4)。

账号进度仅为开局与结算身份一致且仍参与的真实登录账号保存；游客和 FakeAuth 不上传。客户端持久化待提交记录、隔离账号、失败重试并以不可变结算记录防重复，服务端配置见 [Backend/progress/README.md](Backend/progress/README.md)。客户端实现不等于云端已启用：Firestore 数据库、规则部署及真实账号读写仍待验证。

## M3 历史验收（2026-10-04，M3 完成）

燃油数值修复后五项纯 C# 回归为 **82 组 / 6056 断言，0 失败**，完整 runtime / editor 静态编译 PASS；auth protocol 48 与 manifest 7 合计使 console 达 **6111 断言**，见 [Evidence/m3-final-console.txt](Evidence/m3-final-console.txt)。编排端额外 localhost OAuth loopback 2 组 / 9 断言 PASS。

最终数值修复后的 [M3FuelPlaytest](Evidence/m3-fuel-playtest.txt)、场景布局 14,430、MainLoop 三个 ramp / 第四航班、M1 四图、M2 17 图和本地回放均 PASS、零运行时错误。[完整六组 UDP 联机](Evidence/netplay-summary.txt) exit 0，12 个 host / client 进程退出码均 0、报告均 Final PASS / runtime errors 0；四类任务 [餐食](Evidence/m3-meal-captures.txt)、[行李](Evidence/m3-baggage-captures.txt)、[燃油](Evidence/m3-fuel-captures.txt)、[登机](Evidence/m3-boarding-captures.txt) 共 22 张 1920×1080 图及三份登机冻结快照齐全。燃油图为实时顺序拍摄，登机图为冻结同态对照。最新全量产物位于 `Evidence/` 根目录，独立子集和失败档案另存；范围与修复历史见 [Docs/M3-TASKS-NET.md](Docs/M3-TASKS-NET.md)。

MainLoop 是独立的旧调试场景；其 PASS 只验证该场景自己的移动与交互循环，不承载 M3 新任务规则，不能替代 ShiftSim 测试或 LAN 任务剧本。本轮联机验收来自本机隔离 Unity 进程和空间 fixture，未验证真实 Google OAuth、Android OAuth 回跳、两台 Android 设备或 standalone 玩家包，详见 [Docs/M2-TODO.md](Docs/M2-TODO.md)。

## 构建与检查

编辑器内选择 **Palm Bay → Build macOS demo**，输出 `Builds/Palm Bay.app` 并以 CabinLobby 为首场景；**Palm Bay → Build Android demo** 输出 `Builds/PalmBay.apk`（包名 `com.palmbay.islandairport`、横屏、MinSdk 24、IL2CPP+ARM64）。2026-10-04 M6 已完成 Android 与 macOS 重建，日志见 `Evidence/m6-android-build.log`、`Evidence/m6-macos-build.log`；当前 APK 元数据见 `Evidence/m6-apk-metadata.txt`。构建通过不代表独立包运行验收：APK 尚未在 Android 真机安装运行，Google OAuth 回跳和真机编码/分享未验收。其他平台用 Unity Build Settings 切换平台后构建。

```sh
# 在 game 目录下运行；产物自动写入临时目录并清理
./Tools/test-simulation.sh
./Tools/test-single-cycle.sh
./Tools/test-net.sh
./Tools/test-identity.sh
./Tools/test-shift.sh
./Tools/test-bot-memory.sh
./Tools/test-progress.sh
./Tools/test-settlement.sh
./Tools/test-m6-video.sh
./Tools/test-auth-protocol.sh
./Tools/test-loopback-oauth.sh
./Tools/test-android-manifest.sh
./Tools/test-netplay.sh
python3 Tools/test-m4-netplay.py
python3 Tools/check-unity-compile.py

# 已安装 Unity 编辑器且授权正常时
"/path/to/Unity" -batchmode -quit -projectPath "$PWD" \
  -executeMethod DemoBuilder.BuildMac -logFile /tmp/palmbay-build.log
```

`test-simulation.sh` 可通过 `MONO_BIN` 指定 Mono 可执行文件。`check-unity-compile.py` 可通过 `UNITY_CONTENTS` 指定编辑器的 `Contents` 目录。

`test-netplay.sh` 使用隔离临时项目启动 Unity host/client 进程，通过本机真实 UDP socket、输入和明确 fixture 验证 M2 移动 / 占用边界及 M3 四类任务剧本。完整六组与 `NETPLAY_FUEL_ONLY=1` / `NETPLAY_BOARDING_ONLY=1` 独立子集分别留证；子集 PASS 不代表全量 PASS。它不是两台物理设备或独立 macOS 玩家包测试；证据写入 `Evidence/netplay-summary.txt`、`Evidence/net-host-session*.txt`、`Evidence/net-client-session*.txt`。`M2LobbyCapture.Run`、M3 与 M4 联机脚本使用同一固定 UDP 端口，编辑器 capture 与联机集成应串行运行。

`python3 Tools/test-m4-netplay.py` 运行四组 M4 双进程场景；`--case shift|clientdrop|hostleave|hostloss` 可单独复验。脚本隔离项目 / 偏好目录并过滤真实登录配置，证据写入 `Evidence/m4-netplay/`，子集写入 `Evidence/m4-netplay-<case>/`。共同回放验证样本数与逐帧哈希；退出场景检查恢复身份、已完成分数、物资与继续操作。fixture 范围、最后检查点边界与当前状态见 [Docs/M4-SHIFT-COOP.md](Docs/M4-SHIFT-COOP.md)。

### 历史验证情况（截至 2026-09-23，M2 本机门禁）

- pure C# 回归：simulation **11 组 / 209 条断言**、single-cycle **10 / 127**、network **15 / 262**、identity **9 / 109**、auth protocol **48 条断言**，合计 **755 条断言**且均通过；OAuth localhost loopback **2 个场景通过**；Android manifest XML **2 个用例 / 7 条断言通过**。
- 完整 C# 编译通过；最终三场景校验与 layout **14,656 条断言**通过，`-buildTarget StandaloneOSX DemoBuilder.ValidateScenes` exit 0，marker `LEVEL_LAYOUT_VALIDATED assertions=14656`；日志见 `/tmp/palmbay-m2-final-scenes.log`。
- M2 capture **17 张 PNG**、运行时错误为 0；验证 Fake 登录取消/失败重试、DOB 边界、待建房/待输入 IP 恢复、登出、岗位邀请与剪贴板/事件语义、房间状态 fixture，以及本地四模式往返。16:9 与 19.5:9 的手动 IP 布局断言通过，结果见 `Evidence/m2-lobby-review.txt` 和 `Evidence/m2-*.png`。
- M1 capture **4 张 PNG**通过、运行时错误为 0，见 `Evidence/m1-review.txt`。
- LAN 集成：两对独立 Unity 进程运行真实 UDP，两个 session 的 host/client 共 **4 个退出码均为 0**。已验证移动、唯一车辆 owner、Pressed 抢放边沿只消费一次、无 leave 包的 client socket 中断后 bot 接管，以及第二轮 host 关闭房间后 client 回 CabinLobby。测试注入 FakeAuth，不是 Google 登录、双物理设备或 standalone 包验收；见 `Evidence/netplay-summary.txt` 和 host/client session 日志。
- macOS 构建通过：`Builds/Palm Bay.app`（112 MB），日志 marker 为 `PALM_BAY_BUILD_READY entry=CabinLobby scenes=3`。尚未手动运行 standalone 包。
- Android BuildAndroid 通过，marker `PALM_BAY_ANDROID_READY path=Builds/PalmBay.apk scenes=3`；产物约 12 MB。`aapt` 确认 package `com.palmbay.islandairport`、MinSdk 24、targetSdk 35、`UnityPlayerActivity`、`arm64-v8a`。日志见 `/tmp/palmbay-m2-final-build-android-theme.log`；真机安装、运行及 OAuth 回跳仍待验证。
- **编辑器当时可运行**：本机团结 2022.3.61t14（arm64）batchmode 启动、导入、编译、场景重建均正常；早先"授权客户端架构不兼容"问题已不存在。ULF 授权有效期至 2026-09-28 是当时的历史记录；2026-10-01 本轮 editor 自动化授权检查为绿，当前有效期没有新证据。
- 截至该历史记录，三场景与 layout **14,656 条断言**、M1 四图、M2 四模式本地 smoke 均已通过；当时的 MainLoopPlaytest 回归例外已在 2026-09-28 修复。2026-10-01 新一轮 `MainLoopPlaytest.Run` 也通过。
- OAuth 真 Google 登录、Android 自定义 scheme 回跳、APK 真机运行和两台 Android 真机联机仍待设备验证；Android APK 构建本身已通过。
- M0 历史修复记录（.meta GUID 断裂、停车位交互半径重叠等）见 [Docs/M0-BASELINE.md](Docs/M0-BASELINE.md)。

## 代码结构

- `Assets/Scripts/Core/AirportSimulation.cs`：不依赖 Unity 的航班、时序、任务前置、计分。
- `Assets/Scripts/Gameplay/AirportGame.cs`：输入、角色与车辆、站点、bot、旅客、回放、有限 LAN 班岗和 practice / remote 玩法分支；`ExternalControl` + `Step(dt)` 供编辑器工具固定步长驱动。
- `Assets/Scripts/Gameplay/AppState.cs`、`LobbyApp.cs`：跨场景档案、启动模式和机舱大厅流程。
- `Assets/Scripts/Core/Net/`、`Assets/Scripts/Identity/`：LAN UDP Ver=3 协议、局身份 / 权威代次与迁移、年龄状态、Fake 与 Firebase REST OAuth；`Core/Auth/BotMemory.cs` 保存本机合作记忆。
- `Assets/Scripts/Presentation/AirportWorld.cs`：程序化场景与模型；调色板/材质/字体等视觉事实源在 `AirportStyle.cs`。
- `Assets/Scripts/Presentation/AirportHudCanvas.cs`、`LobbyCanvas.cs`：机场 HUD 与机舱大厅程序化 uGUI。
- `Assets/Editor/DemoBuilder.cs`：场景生成与 macOS / Android 构建入口；`M1ReviewCapture.cs`、`M2LobbyCapture.cs` 为截图工具。
- `Tests/`、`Tools/test-*.sh`：状态机、联机核心、身份协议、OAuth loopback 与 Android manifest 检查。

目标渲染管线为 Built-in；不要直接切换 URP/HDRP，当前程序化材质使用 Standard shader。

### Figma 关卡同步历史记录（2026-09-22；不代表 2026-09-23 复验）

- 两个场景已重建保存，MainLoop 重载后的脚本、网格和材质引用检查通过。
- 道路联合边界、凹角、连续移动、三机位车辆/旅客路径及飞机倒车对齐：14,627 条断言通过。
- 核心玩法：20 组测试通过，合计 326 条断言。
- 历史 MainLoop Play Mode（2026-09-22）：三个 ramp 的完整移动与交互循环通过，运行时错误为 0；这不是 2026-09-23 的复验结果。
- 自动驾驶会绕行停放的其他车辆；获取车辆时角色定位到车辆上，避免从路外进入驾驶后卡住。

2026-09-23 当时记录的 MainLoop 回归曾在第二架航班中间机位移车时超时；该例外随后已修复，2026-10-01 新验收通过。2026-09-22 的历史 Figma 验证不覆盖后续回归。

结果见 `Evidence/level-layout-validation.txt`、`Evidence/main-loop-playtest.txt`；保存场景预览见 `Evidence/main-loop-scene.png`。

## 1.0.8 画面精修（2026-10-04）

用户选择后续优先改善画面。本轮更新局内光照、海岸、设施与棕榈细节，重建机舱的座椅/舷窗/行李架，并精修共享 UI。局内与大厅两种横屏比例回归通过，运行时错误 0。当前开发版本 **1.0.8 / Android code 8**；构建、截图和验收边界见 [画面精修交付记录](Docs/VISUAL-POLISH-1.0.8.md)。历史 M7 1.0.7 安装包与证据保留。
