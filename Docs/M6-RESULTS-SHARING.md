# M6 结算、回放、分享与进度

2026-10-05。开发实现已落地；本机检查通过项与外部待验收项分列。依据 F9、F20 和用户确认的 M5 跳过 / Firebase 方案，见 [实施计划](M6-RESULTS-SHARING-PLAN.md)。

## 当前实现

- 结算顶部轻量星级/得分/回放提示，中央完整机场视图，底部按“分享 / 再试一次 / 下一关 / 返回大厅”排列。下一关未开放，保留点击提示；结果终帧和操作栏停留。星级阈值：送走 1/2/4 架对应 1/2/3 星，得分不改变星级。
- 视频为真实历史状态重演：960×540、24 fps、12 倍速，完整班岗 25 秒加 1 秒终帧。Android MediaCodec/MediaMuxer；Mac 使用本机 ffmpeg。工作线程编码、三帧有界队列，单次输出上限 128 MB；导出期间正常更新联机，逐帧渲染后恢复当前表现，不改变模拟分数或共同回放时钟。重开/换场景取消导出并释放资源。临时目录仅保留最近三份完成视频。
- 不采集或混入任何音轨。Android 使用只读 content URI 和专用 provider；Mac 使用 AppKit 原生分享面板。用户自行选择接收应用/发布；返回 chooser-open 状态不代表发送成功。目标应用是否接收横屏视频以真机应用验证为准。
- 缺失采样只保留已有画面并明确标注，不生成缺失事件；开场缺失标注首个可用采样。当前状态回放涵盖地勤、车辆、载货、旅客、油渍和服务中飞机，未记录飞机完整滑行/起飞动画轨迹。
- Firestore 每账号每局一份不可变结算凭据，无客户端累加计数；已存在时读取并逐字段比对后确认，冲突不覆盖。账号班岗数/完成架数/最佳星级从去重记录派生，分页未完整读完不展示不完整累计。
- 开局捕获真实 UID、局身份、席位、认证会话版本；结算检查仍为相同真人、房间 Playing、席位可记账。退出宽限期不记账、同 UID 登出重登录也不恢复本局资格、token 正常刷新不影响资格；迁移不重抓身份。各端只提交本机账号，不替退出搭档补记。
- 待提交队列以原账号隔离并原子落盘，不保存令牌。断网、权限/后端未就绪和令牌失效有明确提示并重试；本机存储失败先重试落盘，未成功前不上传，退出进程可能丢失尚未落盘记录。安全规则验证 Firebase UID、字段和不可变性，比赛成绩仍属于可信 LAN 原型，未实现后端反作弊。

## 验证证据

| 检查 | 结果 / 证据 |
| --- | --- |
| Unity 本地完整班岗/四按钮/安全区/回放/取消/重开/大厅 | PASS，零 runtime errors；[报告](../Evidence/m6-results-review.txt)、[16:9](../Evidence/m6-results-16x9.png)、[19.5:9](../Evidence/m6-results-19_5x9.png) |
| 真实完整 MP4 | PASS，H.264、960×540、24 fps、624 帧、26 秒、无音轨，完整解码成功；[视频](../Evidence/m6-replay.mp4)、[流信息](../Evidence/m6-video-probe.json) |
| 结算资格及导出时间线 | 12 组 / 712 断言通过；[报告](../Evidence/m6-settlement-tests.txt) |
| 账号进度与网络/存储失败 | 64 断言通过；[报告](../Evidence/m6-progress-tests.txt) |
| 编码器/Android Java/YUV/失败和清理 | PASS；[报告](../Evidence/m6-video-tests.txt) |
| 四场景真实 UDP：完整结算/重试、客户端断线、房主主动退出、房主失联 | 八进程 exit 0，runtime errors 0，七张截图；[汇总](../Evidence/m6-netplay/summary.txt) |
| C# runtime/editor 编译 | PASS；[报告](../Evidence/m6-compile.txt) |
| 当前工程 Android / Mac 重建 | 两包 PASS；APK 内原生编码/分享类及 provider、Mac 原生分享 bundle 齐全；[打包检查](../Evidence/m6-build-summary.txt)、[Android 日志](../Evidence/m6-android-final-build.log)、[Mac 日志](../Evidence/m6-mac-build.log)。此重建包含工程已有的后续 M7 修改，不覆盖 M7 的独立交付记录 |
| Firestore 安全规则 | 未取得实际规则执行通过证据；现有本地测试入口保留，[现有检查](../Evidence/m6-rules-harness-check.txt)。未部署到生产 |

自动化使用 FakeAuth、隔离本机合作记忆和明确测试夹具；不代表真人完成航班、真实 Firebase 账号或两台物理设备已通过。首次 M6 脚本反射重载歧义已修正；失败报告保留在 `Evidence/m6-attempt1`，最终报告对应通过的完整复验。

## 外部待验收

1. 审阅现有项目规则后部署 Firestore 规则，启用/核对默认 Native 数据库；不得直接覆盖其他业务权限。详见 [部署说明](../Backend/progress/README.md)。当前未对线上 Firestore 执行部署或测试账号写入。
2. 两台 Android：真实 Google OAuth、MediaCodec 实际设备支持、导出中切后台/低内存、分享面板取消、接收应用未安装和 Shorts/Reels 接收效果。
3. 安装包运行与真人上手/合作体验；这些保留自 M4，不能因 M5 跳过或本机测试通过而视为通过。

复现入口：`M6ResultsCapture.Run`（Unity batchmode 不带 `-quit`）；`sh Tools/test-settlement.sh`、`sh Tools/test-progress.sh`、`sh Tools/test-m6-video.sh`。`python3 Tools/test-m4-netplay.py` 会重跑 LAN 四场景，不与其他固定端口的 LAN 工具并行。
