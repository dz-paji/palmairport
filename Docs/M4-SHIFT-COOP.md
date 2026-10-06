# M4 有限班岗、断线续玩与合作体验

日期：2026-10-04。状态：开发与本机自动化验收已完成。**尚未完成全部 M4 验收**，真人合作体验和目标设备验证仍待执行。

范围依据见 [M4 实施计划](M4-SHIFT-COOP-PLAN.md)、[P0 计划](P0-HIGH-FIDELITY-PLAN.md)及 [M0 已定规则](M0-BASELINE.md)。[M3 验收记录](M3-TASKS-NET.md)保留为历史依据，不自动证明当前 M4 树回归通过。

## 当前实现

正式大厅 LAN 入口启动 300 秒班岗。五架航班在 0 / 35 / 85 / 140 / 195 秒错峰到达，使用当前三机位关卡。餐食、到达 / 出发行李、燃油和登机继续共用纯 Core 规则：房主裁定输入、占用、计时和任务；客户端只读镜像状态。

- 快照包括时间、分数、完成 / 错过架数、任务完成数、星级和结束状态。归零停止移动与作业，完成时刻等于截止时刻仍有效；归零后的重复输入不会增加计时、分数或任务结果。
- 星级仍为送走 1 / 2 / 4 架对应 1 / 2 / 3 星。结果冻结后播放实际历史，播放到最后一帧停住；表现回放不推进权威业务状态。
- 当前权威端才能重试。可靠控制、roster 与快照传播新局身份；双方清空结果、输入边沿和回放历史。重复 / 乱序的 start、输入、快照与回放补帧不能重置当前局或重复触发结算。
- M3 `StartSandbox()` 是无限循环任务练习，保留给六组历史 UDP 剧本；正式大厅使用 `StartShift()`。显式 `CloseRoom()` 仍终止房间，区别于局内房主主动离开时的交接。

Core 不引用 Unity，业务时间仅来自 `Pump(dt)` / `Step(dt)`。本机记录日期是展示元数据，不用于班岗计时。

## 退出与权威接管

客户端主动离开或心跳超时后，房主保留原局并将该席交给 bot，清除旧输入。房主主动离开发送当前检查点；房主突然失联时，客户端从最近收到并保留的权威检查点接任。

接任后的 `LocalSeat` 仍为 1；原房主席位 0 变为 bot，不重新编号角色、车辆驾驶者或货物归属。恢复内容包括航班 / 机位、任务与永久失败状态、部分餐食读条、车辆 / 货物、油量 / 油枪 / 阀门 / 油渍、关闭登机口及等待 / 已放行旅客、计分和已完成结果。检测失联的等待时间不补扣到班岗时间；剩余玩家使用正常输入继续控制原席位。

UDP Ver=3 消息带 `MatchId`、`RoundId`、`AuthorityEpoch`。接任提升权威代次，重试生成新局身份；旧权威 / 旧局输入、结果、start 和回放消息不能覆盖当前状态。退出席位的后续 bot 行为不计入退出真人的任务贡献，退出设备不会收到 bot 代打后的结算写入。

恢复边界是**最后收到的权威状态**。断线前未收到的增量无法从离线房主补回，客户端也不将失联期间自行推演的状态与原房主合并。本实现面向可信 LAN 原型，不声称零数据丢失、任意网络分区下绝无两个模拟，或具备后端身份认证 / 反作弊能力。

## 有界共同回放

LAN 以约 0.5 秒间隔记录实际权威状态，最多 602 个索引槽。快照携带采样索引与预期数量；客户端按索引去重，按需请求缺少的单帧，房主从保留历史回复。每个 UDP 包只携带一份状态，不一次发送整段历史。最终常规双人剧本同时验证帧数和每一帧的序列化哈希。

客户端历史补齐后发出可靠 ready 控制；当前权威端同步自动播放时间，双方加速重演并结束停帧。ready 控制保持单条待确认消息，持续重试直至 ack、失联、迁移或重试清理，避免仅控制包长期丢失时停在等待状态。纯测试覆盖选择性丢 ready 35 秒、随后丢 ack 2 秒，心跳仍正常时恢复一次播放事件并停止重发。

接管时保留客户端已有样本。离线原房主掌握、客户端从未收到的样本列入 `ReplayMissing`，显示历史缺段；不伪造样本，也不无限请求无法恢复的帧。结果计时、任务与计分恢复不依赖回放历史完整。产品回放自动播放，不提供拖动、暂停或倍速控件。

这仍是状态回放，不是视频录制。视频导出、系统分享及云端进度属于 M6；语音与交流管理属于 M5，当前未实现；2026-10-04 用户确认本轮跳过并延期，下一开发阶段为 M6。

## 合作提示与本机记忆

HUD 按本机稳定席位选角色，显示共同班岗、bot 接管和结果；屏外搭档箭头夹在安全区内，回到屏内后隐藏。16:9 与 19.5:9 检查使用人工 camera crop、位置和安全区 fixture，不代表真机异形屏认证。

`BotMemory` 在本机 JSON 保存共同局数、送走架数、真人实际完成的四类任务次数及上次同玩日期。bot 在多个可执行任务之间优先选择真人历史较少承担的任务，并继续使用相同输入、路径、占用和物资规则。已出发旅客保留放行席位，关门 / 换席重开后的完成归因不会默认落到席位 0。

同局结束调用有一次性保护，落盘账本按局身份去重，保留**最近 256 局身份**；累计局数和任务统计继续保留。该账本不提供超过 256 局历史身份的无限期防重，也不是账号云端进度或退出账号的后端奖励凭证。不记录语音、聊天或生日。

## 已有证据与最终回归状态

以下为最终版本的本机自动化归档；真人和设备验收单独列示。

| 检查 | 已归档结果 | 最终状态 / 边界 |
|---|---|---|
| 六项纯 C# 套件 | 101 组 / 6573 断言，0 失败 | [m4-final-console.txt](../Evidence/m4-final-console.txt)；含 simulation、single-cycle、network、identity、shift、bot-memory |
| auth protocol / Android manifest | 48 条断言 / 2 组 7 条断言，全部 PASS | 合计 6628 断言；103 个报告组，auth 不报告组数 |
| runtime / editor 编译 | PASS；最后源码修改后再次 PASS | `python3 Tools/check-unity-compile.py`；对本机已安装 Unity assemblies 静态编译 |
| M4 四场景真实 UDP | 最终完整复验全部 PASS，八个进程 exit 0、runtime errors 0、七张 1920×1080 图 | [summary.txt](../Evidence/m4-netplay/summary.txt)；接管后终帧、缺段状态与实际结算提示文字均通过，截图已复核 |
| M4 本地合作检查 | 最终本地复验 PASS、exit 0、runtime errors False，三图齐全 | [m4-coop-review.txt](../Evidence/m4-coop-review.txt)；16:9 / 19.5:9 安全区、真实 bot 偏好、记忆文件保存恢复均通过 |
| M3 燃油 bot / 输入 | 本轮 PASS、exit 0、runtime errors 0 | [m3-fuel-playtest.txt](../Evidence/m3-fuel-playtest.txt)；含实际 bot、余油 remount 与打滑比较；该报告现已被 2026-10-06 M3.3r 全点按流程复验覆盖，M4 四场景同日复验 PASS，见 [m3r-unity-regression.txt](../Evidence/m3r-unity-regression.txt) |
| M3 六组 UDP | 本轮修复后全量 PASS；12 个进程 exit 0、runtime errors 0 | [netplay-summary.txt](../Evidence/netplay-summary.txt)；四类任务 22 张截图及三份登机冻结快照齐全 |
| 场景校验 | 本轮最终 PASS、exit 0、layout 14,430 断言 | 日志 `/tmp/palmbay-m4-final-scenes.log` |
| MainLoop | 本轮最终 PASS、exit 0，三个 ramp / 三轮完成与第四航班到达 | [main-loop-playtest.txt](../Evidence/main-loop-playtest.txt)，日志 `/tmp/palmbay-m4-final-mainloop.log`；仅验证独立调试场景 |
| M1 capture | 本轮最终 PASS、exit 0、四张 PNG、runtime errors False | [m1-review.txt](../Evidence/m1-review.txt)，日志 `/tmp/palmbay-m4-final-m1.log` |
| M3 本地失败回放 | 本轮最终 PASS、exit 0、一张 PNG、runtime errors False | [m3-baggage-replay.txt](../Evidence/m3-baggage-replay.txt)，日志 `/tmp/palmbay-m4-final-replay.log` |
| M2 capture | 本轮 PASS、exit 0、17 张 PNG、runtime errors False | [m2-lobby-review.txt](../Evidence/m2-lobby-review.txt)，日志 `/tmp/palmbay-m4-final-m2.log`；两种比例手动 IP 布局均 PASS |
| localhost OAuth loopback | 本轮 PASS、2 组 / 9 断言 | `sh Tools/test-loopback-oauth.sh`；单独执行的真实本地 socket 测试，不纳入纯 console 的 6628 汇总 |
| 真人上手 / 分工 / 重试意愿 | 未验证 | 需真人 playtest，不能由程序脚本推断 |
| 两台 Android / 真 Google OAuth / 本轮独立安装包 | 未验证 | [M2 设备与认证待办](M2-TODO.md)继续适用；M2 历史 build 不是 M4 build |

七项 Unity 门禁与额外 localhost loopback 的命令 / 日志 / 报告索引见 [m4-unity-regression.txt](../Evidence/m4-unity-regression.txt)。M3 六组 UDP 最终重跑与 M4 四场景（含结算 HUD 缺段可见性）均已通过，开发自动化门禁全部完成。

最近完整四场景 UDP 归档的范围：

- `shift`：完整 300 秒班岗、双端分数 / 完成架数 / 星级 / 结果、归零停止输入、共同回放逐帧相同与终帧停留、权威重试一次、显式关闭回大厅。一个航班通过领域 API 完成作为非零结果 fixture，其余时刻表与全部时间正常推进；不声称真人完成任务或五架全部离港。
- `clientdrop`：关闭真实客户端 socket 且不发送 leave，房主在同局检测失联并配置 bot，继续到 300 秒结果。
- `hostleave`：房主正常离开大厅链路，剩余客户端恢复原检查点、保持席位 1 正常操作并结束原局；保留 589 帧、状态报告明确记录 11 段缺失，自动回放达到终帧，`ReplayFraction=1`。
- `hostloss`：关闭真实房主 socket 且不发送交接消息，客户端自行超时接任并结束原局；保留 589 帧、状态报告明确记录 11 段缺失，自动回放达到终帧，`ReplayFraction=1`。

退出场景由领域 API 准备部分餐食、部分油量 / 关阀归位的站内油枪与 seat 1 手持的车载油管（M3.3r）、载货且房主占用的行李车、关闭登机口和稳定旅客作为恢复 fixture；不手工替换客户端快照或接任状态。所有 LAN 场景使用隔离项目和 FakeAuth，不复制真实凭据或用户偏好；不等同双物理设备、真实账号或 standalone 运行。

本地合作最终日志为 `/tmp/palmbay-m4-final-coop.log`。检查使用隔离 JSON store，备份并恢复设备原文件；历史任务次数、领域任务完成和屏外搭档位置为明确 fixture。它验证实际 `AirportGame.BotInput` 的选岗 / 抢车、真人任务归因、两轮 300 秒 / 回放 / 去重与 JSON reload，不测量真人上手或分工收益。

## 结算 HUD 缺段披露复验

截图检查曾发现 HUD 仅在没有可用历史时展示 `ReplayStatus`，已有回放时缺段说明被普通回放提示覆盖。已修复提示优先级，并扩展迁移剧本：等待暂时事件提示消失后，断言实际结算提示 `VisibleResultsHint == ReplayStatus`。修复后的四场景全量复验 PASS；主动退出和突然失联两组报告均包含 `PASS Results HUD visibly discloses unrecoverable replay segments`，截图已确认文字可见。缺段数量取决于实际收到的样本，本轮两组均保留 589 帧并披露 11 段缺失，不是固定业务常量。

## 本轮 M3 回归失败与修复记录

首次 M4 树上的 M3 六组全量，sessions 1–5 两端均 exit 0；session 6 两端 exit 1，complete 两图及期望快照缺失。房主测试反射调用 `BotGeneralInput` 时，未传入新增的可选 `preferred` 参数，触发 `TargetParameterCountException`；这次全量确实 FAIL，不以之前 M3 或前五组 PASS 替代。

修复在测试调用处显式传入 `-1`，保持自动选岗语义与原断言，编译复验 PASS；没有删除登机完成后的 bot 路由检查。首轮失败资料完整保留在 [m4-m3-regression-attempt1](../Evidence/m4-m3-regression-attempt1/netplay-summary.txt)。修复后的六组全量重跑已 PASS，12 个进程 exit 0、runtime errors 0；包括原先失败的完整登机断言。

## 复验入口

在 `game` 目录执行：

```sh
sh Tools/test-simulation.sh
sh Tools/test-single-cycle.sh
sh Tools/test-net.sh
sh Tools/test-identity.sh
sh Tools/test-shift.sh
sh Tools/test-bot-memory.sh
sh Tools/test-auth-protocol.sh
sh Tools/test-android-manifest.sh
python3 Tools/check-unity-compile.py
sh Tools/test-netplay.sh
python3 Tools/test-m4-netplay.py
```

M4 可用 `--case shift|clientdrop|hostleave|hostloss` 单独检查，子集 PASS 不代替四场景全量。主证据目录为 `Evidence/m4-netplay/`，单场景目录为 `Evidence/m4-netplay-<case>/`。失败尝试 `m4-netplay-shift-attempt1/` 保留为历史，不与当前结果合并。

编辑器本地检查入口为 `M4CoopCapture.Run`；场景、MainLoop、M1 / M2、M3 燃油和本地回放沿用各模块工具。M2 capture 和两个 LAN 脚本争用固定 UDP 端口，必须串行运行。视频 / 云端 / 语音功能不在本轮检查范围。
