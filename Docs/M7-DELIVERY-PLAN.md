# M7 联调与交付

2026-10-04：用户要求从 M7 持续开发至完成，已确认本轮先完成开发和本机验收；暂不提供两台 Android，不部署 Firestore 或修改云端 OAuth。没有公开下载地址，邀请移除无效链接并提供安装与同网组队说明。M5 语音继续延期。

## 执行顺序

1. 审计并修复大厅、开局、结算、重试、返回大厅、重新组队的端到端缺陷。
2. 正式 player 缺 OAuth 配置时明确失败，保留游客离线玩法；补齐邀请与服务异常说明。
3. 建立一键本地 console 门禁与独立 macOS player smoke，回归真实 UDP 双端、断线接管、归零和回放。证据标注 fixture、FakeAuth、加速模拟和真实交互的区别。
4. 本地验证 Firestore 实际规则；若环境无法运行模拟器，保留明确阻塞与可复现命令，不用 fake transport 替代规则执行。
5. 重建 Android / Mac 包，交付运行说明、PRD 覆盖表、版本校验与证据索引。

## 本轮边界

- 开发与可执行本机验收尽量完成；真机、真人合作、真实 OAuth 和生产 Firestore 不计作通过。
- 继续使用 Built-in、已有玩法和星级规则，不新增关卡或公网联机。
- 不自动发送邀请、不发布视频、不部署云服务。
- 当前执行环境限制本地 TCP：Firestore 规则访问 `Operation not permitted`，OAuth loopback `Socket.Bind: Access denied`。这两项未通过；不将其归因为业务断言失败，也不以历史 PASS 或 fake transport 替代。真实 UDP 四组已实际运行通过，不将 TCP 限制泛化为所有网络不可用。

## 本机结果与证据索引

| 项目 | 当前结果与范围 | 证据 |
|---|---|---|
| 最终 console | 12/12 PASS，总 exit 0；完整日志与各项退出码齐全 | [summary.json](../Evidence/m7-console/20261004T133115.367716Z-27351/summary.json) |
| 身份、房间与迁移重试 | PASS；刷新保留房间 / ready，失效离房、局中回大厅，checkpoint 接管并完成 300 秒 / Retry 后席位 1 的姓名 / 颜色保持一致；显式 FakeAuth、内存 socket，无后台调用 | [m7-identity-review.txt](../Evidence/m7-identity-review.txt) |
| 大厅与邀请最终回归 | PASS，零运行时错误；去掉未部署链接，保留岗位、获取同版本安装包 / 同 Wi-Fi 说明，16:9 / 19.5:9 邀请画面完整 | [m2-lobby-review.txt](../Evidence/m2-lobby-review.txt)、[邀请画面](../Evidence/m2-lobby-invite.png) |
| M4 最终真实 UDP | 四组 / 八进程 PASS，exit 0、零运行时错误；完整班岗 / 客户端掉线 / 房主离开 / 房主失联；隔离 Unity + FakeAuth + fixture | [summary.txt](../Evidence/m4-netplay/summary.txt) |
| Android / macOS 重建 | 两包 PASS；Android 1.0.7 / code 7、arm64-v8a、minSdk 24 / targetSdk 35，打包视频 provider 非导出、无麦克风权限 | [Android 日志](../Evidence/m7-android-build.log)、[Mac 日志](../Evidence/m7-macos-build.log)、[APK 元数据](../Evidence/m7-apk-metadata.txt)、[manifest](../Evidence/m7-apk-manifest.txt) |
| 标准 macOS `.app` 启动 | 已观察游客大厅和 Google 未配置提示，无 Error / Exception；正式包不以 Fake 替代缺配置登录。未完成完整指针试玩和系统分享验收 | [UI 观察](../Evidence/m7-standard-ui-review.txt)、[player 日志](../Evidence/m7-standard-player.log) |
| 独立 player 完整自动化 | **BLOCKED / 未通过**。CLI 两次原生注册 abort；专用 UI 包首次因存档目录布局校验被拒，已修正为同时接受精确 GUID Bundle ID 目录。后续系统工具启动超时且没有 managed 报告，不宣称整局通过 | [尝试记录](../Evidence/m7-ui-launch-attempts.txt)、[路径拒绝日志](../Evidence/m7-ui-player-path-attempt1.log)、[direct](../Evidence/m7-player-20261004/runner.json)、[batch](../Evidence/m7-player-20261004-batch/runner.json) |
| Firestore 实际规则 | **BLOCKED / 未通过**，本地 TCP 权限拒绝；没有规则执行通过证据 | [m7-rules-attempt.txt](../Evidence/m7-rules-attempt.txt) |
| OAuth loopback | **BLOCKED / 未通过**，两组 bind 均 Access denied，0 断言；不等于真实 Google OAuth 已验证 | [m7-loopback-oauth.txt](../Evidence/m7-loopback-oauth.txt) |

本机证据、安装说明和逐条状态分别见 [运行说明](M7-RUNBOOK.md)、[PRD 覆盖表](M7-PRD-COVERAGE.md)。独立 player 的实际尝试已记录为未通过，不能以编辑器结果替代。交付目录含安装包、说明和 SHA-256 清单。目标设备、真实账号与云端、分享接收应用、真人合作与留存均保留未验收；不声明所有 PRD 或全部验收完成。

UI smoke 使用独立 GUID company / product / bundle 与空偏好、实际持久目录校验，不改 HOME。正常产品包不携带生成的 smoke 资源，不会自动测试；`DemoBuilder.RequireProductBuild` 阻止遗留 smoke 资源 / 身份进入正式包。专用包的完整自动化尚未取得通过报告。重新验证时构建 `M7SmokeBuilder.BuildMacUiSmoke`，使用 `Builds/m7-ui-smoke-build.json` 记录的独立 GUID 应用路径；不可复用非空测试存档。

## 交付文件

最终交付目录：[M7 1.0.7](../Builds/M7-1.0.7-20261004T135139Z/)。包含 Android APK、macOS ZIP、独立安装说明、`manifest.json` 和 `SHA256SUMS`。包内字节长度、SHA-256 与 Mac ZIP CRC 均已复核，见 [打包检查](../Evidence/m7-package-review.txt)。这份客户端交付不等于所有外部依赖及整体验收已完成。
