# PALM BAY 1.0.7 本地交付说明

本轮包含 M0–M4、M6、M7 的客户端实现；M5 语音延期。首关为五分钟机场班岗，支持游客 + bot、同机双人及登录后的同网双人。额外关卡尚未开放。

## 安装与开始

- **macOS**：解压交付包 `Palm-Bay-macOS.zip`，从 Finder 打开 `Palm Bay.app`；源码工程产物为 `Builds/Palm Bay.app`。使用系统 App 入口，不能用直接执行 `Contents/MacOS/...` 的 CLI 结果替代。Mac 视频导出需要本机 ffmpeg；缺少时游戏会提示。本轮标准 `.app` 启动已显示游客大厅与 Google 未配置提示，无 Error / Exception，但尚未完成整局人工试玩或分享面板验收。
- **Android**：把 `PalmBay-Android.apk` 传到设备，允许当前文件管理器安装此包。要求 Android 7.0/API 24 或以上、ARM64。包名为 `com.palmbay.islandairport`，版本 1.0.7 / code 7。与历史测试包签名一致时可直接覆盖；不要为安装随意卸载旧包，以免丢失本地记录。
- **离线游客**：大厅选择 bot 搭档并开始执勤，不需要登录。WASD / 触屏摇杆移动，E / 情境按钮交互，持续动作按住；Q 切换目标航班，Esc 打开设置，F1 查看帮助。可以选择同机搭档，用方向键和右 Shift 控制第二人。
- **任务**：先把到达行李卸回；准备餐食、燃油和出发行李，餐食/燃油完成后放行旅客。餐食和行李取消读条清零但保留货物；燃油和登机保留已完成进度。
- **结算**：送走 1 / 2 / 4 架获得 1 / 2 / 3 星。自动加速真实回放并停帧；可分享无声视频、重试、查看下一关未开放提示或返回大厅。打开分享面板不代表已发布。

## 同网组队与异常恢复

双方安装同一版本、连接同一个 Wi-Fi，登录并完成 13 岁年龄准入。一人创建房间，另一人从大厅加入；广播发现失败可输入房主的局域网 IP。房主 IP 可在设备当前 Wi-Fi 的网络详情中查看。访客 Wi-Fi 的客户端隔离可能导致彼此无法连接。当前没有公开下载链接或公网联机。

正式包缺少 OAuth 配置时会明确提示，不能用 Fake 账号替代真实登录。编辑器允许显式 Fake 测试。当前真实 Google OAuth、Android 回跳与生产 Firestore 尚未验收；本轮不部署云端。云端不可用时，符合资格的结算记录在本机排队，原账号恢复登录/连接后重试；游客不会上传。

客户端离开由 bot 接管；房主离开或失联后，剩余玩家从已收到的检查点继续，保持原席位，原房主席位变为 bot。迁移后的重试保留本机姓名 / 颜色及 bot 席位映射。收不到的最后一段状态无法恢复，回放会披露缺段。归零停止移动与交互；LAN 重试由当前房主发起。正常令牌刷新保留房间和准备状态；凭据失效终止真人组队，局中返回大厅，重新登录后可再组队。

## 验证与交付边界

`SHA256SUMS` / `manifest.json` 用于核对交付文件。源码工程中的 `Evidence/` 保存实际检查证据；`Docs/M7-PRD-COVERAGE.md` 为逐项覆盖。

当前已完成最终 console 12/12、身份 / 迁移 / 重试、大厅邀请与四场景真实 UDP 本机回归，Android / Mac 重建均通过。[APK 元数据](../Evidence/m7-apk-metadata.txt)确认版本 1.0.7 / code 7、ARM64、minSdk 24 / targetSdk 35；[manifest](../Evidence/m7-apk-manifest.txt)确认非导出视频 provider 与无麦克风权限。完整索引见 [M7 交付记录](M7-DELIVERY-PLAN.md)。

在 game 目录可重复运行：

```sh
python3 Tools/test-m7-console.py
python3 Tools/test-m4-netplay.py
sh Tools/test-progress-rules.sh
sh Tools/test-loopback-oauth.sh
```

规则检查需要先启动本地 Firestore 模拟器，详见 `Backend/progress/README.md`。本轮 [规则尝试](../Evidence/m7-rules-attempt.txt)因 TCP `Operation not permitted`、[OAuth loopback](../Evidence/m7-loopback-oauth.txt)因 `Socket.Bind: Access denied` 未通过；这是本地执行权限阻塞，尚未完成业务功能验证。无模拟器或无监听权限时不能以进度客户端单测或旧 loopback PASS 替代；本轮不部署生产云服务。

独立 CLI smoke 使用专用隔离包 `Builds/M7-Smoke.app`，与产品包分开；复验 CLI 为 `python3 Tools/test-m7-player.py --evidence Evidence/<新的独立目录>`。已有 [direct](../Evidence/m7-player-20261004/runner.json) / [batch](../Evidence/m7-player-20261004-batch/runner.json) 两次启动在 macOS 原生 `RegisterApplication` abort（exit -6），未到 managed 报告。它们保留为失败尝试，不能宣称独立整局通过，也不能推断标准 `.app` 无法打开；标准系统入口的 [UI 观察](../Evidence/m7-standard-ui-review.txt)已单独记录。

新的 UI smoke 由 `M7SmokeBuilder.BuildMacUiSmoke` 生成 GUID 命名的 `Builds/M7-UI-Smoke-<GUID>.app`（具体路径见 `Builds/m7-ui-smoke-build.json`），从标准系统 App 入口打开后自动进行 300 秒加速模拟，默认报告目录为 `Evidence/m7-ui-player/`。它使用独立 GUID 身份、空偏好并校验实际持久目录，不改 HOME；正式产品由构建门禁禁止携带 smoke 资源 / 身份，也不会自动运行测试。本轮首次 UI 包被存档目录校验拒绝，已兼容实际 Bundle ID 目录；后续系统入口工具超时、未产生业务报告，见 [UI 尝试](../Evidence/m7-ui-launch-attempts.txt)。完整独立 player 自动化为 **BLOCKED / 未通过**；合成输入 / 加速模拟通过也不会代替真人体验、系统分享与真实 OAuth。

本机自动化使用加速模拟与明确 fixture，不能证明真人体验、两台物理 Android 的联机性能或系统分享接收效果。用户本轮暂不提供真机；真实 OAuth、真机编码/分享、真人上手与合作评审保留为后续验收。包构建成功不等于这些项目通过。
