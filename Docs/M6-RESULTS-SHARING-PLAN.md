# M6 结算、回放、分享与进度

2026-10-04：用户要求开发 M6，已确认沿用 Firebase 并新增 Firestore；M5 本轮跳过。M4 真人合作、两台 Android、真实 OAuth 和独立安装包待验收项继续保留。

## 范围

- F9：轻量顶部星级结果、全机场俯视真实历史回放、底部“分享 / 再试一次 / 下一关 / 返回大厅”。自动 12 倍速，结束停帧，无主动播放控制。首关之外仍未开放，点击下一关提示。
- 固定首关星级依据：送走 1 / 2 / 4 架分别获得 1 / 2 / 3 星；得分保留但不决定星级。
- 回放导出为无声 MP4：Android 使用原生编码和系统分享；Mac 开发入口使用本机 ffmpeg。只导出历史状态，缺段明确披露，不生成虚构历史；不采集麦克风。
- 账号进度：仅向本机实际参与结算、与开局身份一致的真实登录账号保存本局结果。游客和 FakeAuth 不上传；退出账号不补记 bot 代打；房主迁移保留本局身份。
- Firebase Firestore 保存按账号与局身份确定的不可变结算记录；本地持久化待提交队列、失败重试、账号隔离，重复提交不增加记录。客户端成绩适用于可信 LAN 原型，不声称后端反作弊。
- 不增加金币、解锁新关、实时语音或社交平台自动发布。打开分享面板不等于发布成功。

## 验证

纯逻辑验证身份资格、防重、持久化及失败重试；实际编码检查视频可解码、时长/尺寸、无音轨与取消清理；Unity 检查结算四按钮、星级、停帧、导出前后状态、重试与换场景；保留 LAN 双端与房主迁移回归。Firestore 规则/部署和 Android 真机编码/分享要有实际证据后才可标记通过。

## 2026-10-04 续作与验收记录

M6 客户端功能已实现，尚不能标记整体验收完成。当前证据如下：

| 项目 | 结果与证据 |
|---|---|
| 账号资格、持久化、重试、账号隔离和防重复 | 64 断言 PASS，`Evidence/m6-progress-tests.txt`；独立代码审查未发现新的具体缺陷 |
| 结算身份、房主迁移资格、导出时间线 | 12 组 / 712 断言 PASS，`Evidence/m6-settlement-tests.txt` |
| 视频编码与取消清理 | H.264 MP4 解码、方向、时长、无音轨、Java 编译与 YUV 转换 PASS，`Evidence/m6-video-tests.txt` |
| Unity 结算与回放 | PASS，`Evidence/m6-results-review.txt`；本地 300 秒真实模拟、四按钮、两种宽高比、自动停帧、导出/取消恢复、Retry、返回大厅；FakeAuth，不调用云端或发布分享 |
| 实际回放产物 | `Evidence/m6-replay.mp4`：960×540、24 fps、624 帧、26 秒、仅视频流；探测记录 `Evidence/m6-video-probe.json` |
| Android 构建 | PASS，`Evidence/m6-android-build.log`；`Builds/PalmBay.apk`，arm64-v8a、minSdk 24 / targetSdk 35，元数据 `Evidence/m6-apk-metadata.txt`；打包后的 manifest 确认视频 provider 不导出、授权只读 URI，且无麦克风权限，`Evidence/m6-apk-manifest.txt` |
| LAN 回归 | 四场景 / 八进程 PASS，运行时错误 0，`Evidence/m4-netplay/summary.txt`：完整班岗、客户端掉线、房主主动退出、房主突然失联；本机 FakeAuth，不代表两台真机 |
| macOS 构建 | PASS，`Evidence/m6-macos-build.log`；`Builds/Palm Bay.app` 包含 x86_64 / arm64 主程序与原生分享插件 |
| 全部 runtime / editor C# 静态编译 | PASS，`Evidence/m6-compile.txt`；未定义平台宏的检查产生两条编码器分支警告，实际平台构建成功 |

`README.md` 与 `CODELY.md` 已更新为当前 M6 状态，旧的“视频导出 / 云端进度尚未实现”不再代表现状。账号进度客户端已实现，云端启用与部署仍未验证。

新增规则验收入口 `sh Tools/test-progress-rules.sh`，直接加载受检的 `firestore.rules`，覆盖所有者、跨账号、未登录、不可修改/删除、重复提交及字段边界。Python 语法与本机端点限制检查通过，见 `Evidence/m6-rules-harness-check.txt`；本机缺少 Firebase CLI / Firestore 模拟器，规则执行仍未通过验收。运行步骤见 `Backend/progress/README.md`。

### 仍待验收

- Firestore 模拟器规则执行、数据库启用和生产规则部署；真实 Firebase 账号写入、断网恢复、跨账号拒绝与重复提交。当前纯逻辑 fake transport 测试不执行 Security Rules。
- Android 真机安装、原生 MediaCodec 编码、系统分享接收应用读取视频与取消流程。续作环境的 ADB 启动因本地监听 `Operation not permitted` 失败，无法据此判断是否连接了设备。
- macOS 独立包运行和系统分享面板人工验收；构建成功不等于交互已验收。
- M4 保留项：真人合作体验、两台 Android 联机和真实 OAuth。M5 仍延期。
