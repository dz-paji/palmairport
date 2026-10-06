# M3 任务练习持续验收记录

记录日期：2026-10-04；2026-10-06 追加 M3.3r 燃油流程简化验收。M3 实施事实源为 [Docs/M3-TASKS-PLAN.md](M3-TASKS-PLAN.md)。**M3.0–M3.5 全部完成，批准范围内的编辑器 / 本机 UDP 原型验收通过。** 最终数值修复后的纯规则、编译、场景、燃油 bot、MainLoop、M1 / M2 capture、回放和完整六组 UDP 均 PASS。

本记录区分任务规则测试、编辑器场景检查和双进程联机验收。全部联机证据来自隔离的 Unity host/client 进程，使用本机真实 UDP 输入和空间 fixture；这不代表两台 Android 设备、真实 OAuth 或 standalone 玩家包验收。

## 切片状态

| 切片 | 范围 | 状态 | 验收事实 |
|---|---|---|---|
| M3.0 状态提取与镜像通道 | 本地 / LAN 共用任务状态、快照镜像和 HUD 读取 | **完成** | Shift / Flight 状态与 client 镜像通道已落地；纯 C#、完整 C# 编译、场景与稳定树检查通过。 |
| M3.1 餐食 + 共享车辆 | 下单、出货口阻塞、装载、交付、取消与空车复用 | **完成** | 餐食 ready / deliver host-client 同态截图齐全且 runtime errors 为 0；UDP session 3 两端退出码均为 0。 |
| M3.2 行李 | 到达卸货回站、出发行李装载与交付、错送失败语义、同步与回放归因 | **完成并验收通过** | console 覆盖、双进程 UDP session 4、wrong / deliver 双端截图和本地回放结果提示验收均 PASS。 |
| M3.3 燃油 | 油枪 / 油车 / 阀门、漫油、bot 与点按 / 长按流程（玩家流程已由 M3.3r 替代） | **完成并验收通过（历史）** | 最终数值修复后 console / Unity bot、独立及全量 UDP session 5 PASS；两端退出码 0、runtime errors 0，八张 1920×1080 图齐全。 |
| M3.3r 燃油流程简化 | 两套油管、全点按、关阀自动归位、机位自动注油 / 再点断开、2 秒满溢宽限 | **完成并验收通过（2026-10-06）** | console 99 / 4370、Unity bot、独立与全量 session 5、六组 UDP、M4 四场景及其余 Unity 门禁全 PASS；见下文“M3.3r 燃油流程简化：验收”。 |
| M3.4 登机 | 关门 / 重开保留、稳定旅客队伍、道路阻车与镜像 | **完成并验收通过** | 纯规则、独立及全量 UDP session 6 PASS；两端退出码 0、runtime errors 0、六张同态图及三份冻结快照齐全。 |
| M3.5 总体验收 | 全量门禁、四类任务双进程剧本、证据归档 | **完成并验收通过** | 最终树 console / 编译、layout 14,430、燃油 bot、MainLoop、M1 / M2、回放和六组 UDP 全 PASS；12 个进程退出码均 0，22 张任务图及登机冻结快照齐全。 |

## 门禁记录（2026-10-04，M3.3r 之前）

> 2026-10-06 M3.3r 全量复验已覆盖本表中的 console、燃油 bot、session 5 / 全量 UDP 与各 Unity 门禁，当前数字见下文“M3.3r 燃油流程简化：验收”；本表保留为 M3.5 收尾时的历史记录。

最终 console 输出与执行时间见 [`Evidence/m3-final-console.txt`](../Evidence/m3-final-console.txt)。五项必需纯 C# 套件为 **82 组 / 6056 断言，0 失败**；加上 auth protocol 48 与 Android manifest 7，共 **6111 断言**。auth protocol 不报告用例组数；不能把断言数当作组数。额外 localhost OAuth loopback 2 组 / 9 断言由编排端在 sandbox 外执行并报告 PASS，不包含在该 console 文件或 6111 汇总内。

| 检查 | 本轮结果 |
|---|---|
| simulation | PASS，11 组 / 209 断言 |
| single-cycle | PASS，10 组 / 127 断言 |
| network | PASS，17 组 / 342 断言 |
| identity | PASS，9 组 / 109 断言 |
| shift（含四类任务规则） | PASS，35 组 / 5269 断言 |
| auth protocol / Android manifest | PASS，48 断言 / 2 组 7 断言 |
| 完整 C# 编译 | PASS，全部 runtime 与 editor C# 对已安装 Unity assemblies 编译 |
| `M3FuelPlaytest.Run` | 数值修复后 PASS、exit 0；实际 bot、点按 / 长按、余油再次上车和漫油移动检查，runtime errors 0。日志 `/tmp/palmbay-m3-final-fuel3.log`。 |
| 独立 UDP session 5 | PASS，两端退出码 0、runtime errors 0，fill / spill / transfer / deliver 八张 1920×1080；见 `Evidence/netplay-fuel-only/`。 |
| 独立 UDP session 6 | PASS，两端退出码 0、runtime errors 0；六张 1920×1080 与三份同态冻结快照 |
| 全量 UDP sessions 1–6 | **PASS**，完整脚本 exit 0、12 个 host / client 进程退出码全 0；12 份报告均 Final PASS、runtime errors 0。当前汇总 `Evidence/netplay-summary.txt`，四类任务 22 张 PNG 全为 1920×1080。 |
| `DemoBuilder.ValidateScenes` | PASS，exit 0，layout **14,430** 断言；日志 `/tmp/palmbay-m3-final-scenes2.log`。14,656 是历史版本计数。 |
| `MainLoopPlaytest.Run` | 修正停车 fixture 后 PASS、exit 0，三个 ramp 移动 / 作业完成且第四航班到达，保存场景模型 / 货物引用重载通过；runtime errors False。日志 `/tmp/palmbay-m3-final-mainloop3.log`。 |
| `M1ReviewCapture.Run` | 数值修复后 PASS、exit 0、四张 PNG、runtime errors False；日志 `/tmp/palmbay-m3-final-m1capture2.log`。 |
| `M2LobbyCapture.Run` | 最终树在完整 LAN 后串行复验 PASS、exit 0、17 张 PNG、runtime errors False；日志 `/tmp/palmbay-m3-final-m2capture2.log`。 |
| `M3ReplayFailureCapture.Run` | 最终树重复复验 PASS、exit 0、runtime errors False；单次归因、81 个固定步后消失和 Retry 清理均通过，日志 `/tmp/palmbay-m3-final-replay2.log`。 |

`M2LobbyCapture.Run` 与 `test-netplay.sh` 会争用固定 UDP 端口，应串行运行。MainLoop 是独立旧调试场景，仅覆盖它自身的移动与交互循环，不能替代 ShiftSim 新任务覆盖或 LAN 任务剧本。

历史快照仍保留：2026-10-01 的 [`m3-baggage-console.txt`](../Evidence/m3-baggage-console.txt) 为 70 组 / 1164 断言、静态编译 PASS；当时场景布局 14,656 断言、MainLoop、M1 / M2 capture、sessions 1–4 与行李回放均 PASS。2026-10-02 的 [`m3-fuel-console.txt`](../Evidence/m3-fuel-console.txt) 为 75 组 / 1586 断言、静态编译 PASS，早于后续收管 / 输入修复。这些历史结果不替代当前版本复验。

## M3.0 / M3.1 已验收范围

- 任务领域状态与事件进入共享 Core 流程；本地输入与 host 输入走相同规则，client 按快照只读镜像。
- 餐食链路包含下单、等待出货、出货口占用、餐车取货、机位交付和复用；交付读条为 2.5 秒。中断会清零本次交付读条，货物仍留在餐车上。
- 双端 ready 与 deliver 同刻画面索引见 [`Evidence/m3-meal-captures.txt`](../Evidence/m3-meal-captures.txt)：四张 host/client 截图均为 1920×1080，host/client runtime errors 为 0。
- UDP session 3 证据见 `Evidence/net-host-session3.txt`、`Evidence/net-client-session3.txt` 及对应 `.log`。2026-10-04 完整 sessions 1–6 PASS，其中 session 3 两端退出码 0、runtime errors 0。

## M3.2 行李：实现范围与边界

- 必须先把到达行李从飞机卸下并运回行李站，之后才能装载出发行李。行李站按航班无限供给。
- 按所选目标航班装载出发行李，并绑定、保留货物的原属航班号。目标接收航班和货物原属航班是两个独立身份；交付时按实际对接机位的接收航班裁定成功或错送。
- 交付读条为 2.5 秒。取消或切换接收目标会清零读条，但货物保留在车上。
- 错送在交付完成时成立：货物被接收航班吞下，接收航班行李任务永久失败且不额外扣分；底层任务推进与离港均拒绝越过失败项。原属航班不失败、进度不受影响，可以从行李站重装。
- 已完成行李任务的接收航班拒收错送，货物留在车上。失败状态在任务显示中可见；接收目标切换会清零本次交付读条。
- 双端 netplay capture 验证了 source `UBA826` 可在错送后重装并正常完成，receiver `QMR152` 行李任务永久 `Failed`。wrong / deliver host-client 同态截图均为 1920×1080，运行时错误为 0。
- 失败状态与任务事件进入有界快照、回放事件和本地 JSONL 埋点。错送归因包含原属航班和接收航班身份。
- 本地回放帧记录保存失败状态与事件；跨帧播放时只在结算回放结果提示区显示一次性归因，不按帧重建失败列表。LAN 回放同步仍属 M4。

### 行李与本地回放证据

- 双端联机图：[`Evidence/m3-baggage-captures.txt`](../Evidence/m3-baggage-captures.txt)、`Evidence/m3-baggage-wrong-host.png`、`Evidence/m3-baggage-wrong-client.png`、`Evidence/m3-baggage-deliver-host.png`、`Evidence/m3-baggage-deliver-client.png`。错送和正确交付两组 host/client 截图均通过，四张均为 1920×1080。
- 双端联机汇总：当前 [`Evidence/netplay-summary.txt`](../Evidence/netplay-summary.txt) 为完整 sessions 1–6 PASS；12 个进程退出码均 0。session 4 细节见 `Evidence/net-host-session4.txt`、`Evidence/net-client-session4.txt` 与对应 `.log`。
- 本地回放：[`Evidence/m3-baggage-replay.txt`](../Evidence/m3-baggage-replay.txt) 为 PASS，图片为 `Evidence/m3-baggage-replay.png`。两架 fixture 飞机停稳后，本地交互完成一次错送 `RX-318 <- SRC-724`；固定 `Step(0.05)` 推进到 300 秒结算，结果提示区可见“RX-318 收到了 SRC-724 航班的行李 · 行李任务失败”。提示仅触发一次，81 个固定步（每步 0.05 秒）后消失；Retry 清除提示并开始新班岗。
- Console 与编译：当前 [`Evidence/m3-final-console.txt`](../Evidence/m3-final-console.txt) 为五套 82 / 6056、附加协议 / manifest 共 6111 断言，完整编译 PASS；历史 [`m3-baggage-console.txt`](../Evidence/m3-baggage-console.txt) 的 70 / 1164 保留。

## M3.3 燃油：实现与复验

> 本节为 2026-10-04 M3.3 旧玩家流程（收纳枪随车、长按 / 按住注油）的历史记录，已被 M3.3r 替代；当前规则与证据见下文“M3.3r 燃油流程简化：验收”。根目录 session 5 与燃油图现为 M3.3r 产物，旧版产物归档于 `Evidence/pre-m3.3r-full/` 与 `Evidence/netplay-fuel-only-m3.3-pre-r/`。

- 站内：徒步拿唯一油枪 → 车边插枪 → 回站开阀自动注入油车 → 关阀 → 车边收纳。站内资源无限，油枪收纳在车上（`StowedOnTruck`）后可运输。
- 机位：驾驶到机位、下车。收纳状态在可加注飞机旁**短点取管，长按上车**；手持状态在该飞机旁**短点接管，长按收纳**。运输途中没有可接管飞机时，收纳状态普通点按驾驶。
- 管已接飞机时，持续按住超过 0.2 秒点按窗口后注油；松开仅暂停且保持连接、储量与航班进度。短点（可跨多个 Held 帧）在松开时收管。部分油量也可收纳、再次驾驶或继续加注。
- 驾驶中不能操作枪、阀门或清理；阀门开着、手持油枪、油管连接任一端时禁止驾驶。阀门与油渍操作范围重叠时点按先关阀，再按住清理。
- 阀门开且枪未插车 / 满箱后仍开阀产生油渍，实际油车移动减速、徒步速度不变；可徒步清理，不消耗站内储备、不导致任务失败。

### 当前证据

| 项目 | 当前结果与证据 |
|---|---|
| 规则与编译 | [`m3-final-console.txt`](../Evidence/m3-final-console.txt)：五套 82 / 6056、附加协议 / manifest 共 6111 断言，完整静态编译 PASS。 |
| Unity bot 与输入 | [`m3-fuel-playtest.txt`](../Evidence/m3-fuel-playtest.txt)：PASS、runtime errors 0；实际 bot 以 `Step(0.05)` 从拿枪到加注、收纳与放车，燃油进度初始未修改，餐食 / 行李及宽限截止是明确 fixture。输入附加 fixture 验证 150 ms 短点、持续按住、余油收纳 / 取管 / 接管 / 再上车与真实移动。 |
| 漫油移动 | 同报告：同点油车正常 0.180 m / 油渍 0.058 m，徒步均为 0.225 m。此对照明确使用位置和油渍 fixture，不冒充自然场景资源流程。 |
| LAN session 5 与双端图 | **PASS**：当前全量 [`netplay-summary.txt`](../Evidence/netplay-summary.txt)、`net-host-session5.txt` / `net-client-session5.txt` 均 PASS，host / client exit 0、runtime errors 0；当前 [`m3-fuel-captures.txt`](../Evidence/m3-fuel-captures.txt) 列出八张 1920×1080。验证唯一枪竞争、自动补油、油渍清理、真实驾驶交接、连接驾驶锁、暂停 / 续加、精确完成与收管可驾驶。独立复验另保留于 `Evidence/netplay-fuel-only/`。 |

燃油 fill / spill / transfer 双端图是**依次拍摄的实时画面，不是冻结后逐值相同的快照**；最终八张图已逐张校验实际 1920×1080，未见实质缺陷，历史 transfer 例中 host 显示 25%、client 显示 28%，符合拍摄间持续加注。不得把这些图描述为登机式的严格同态冻结对照。最终独立及全量复验均已补齐 deliver 两图，并由新 session 报告证明整条流程 PASS；实时图仍不作逐值冻结相等声明。

### 保留的失败历史

2026-10-03 首次 Unity bot 验收 FAIL（收管卡住）。本轮较早燃油 LAN 尝试也 FAIL，保存在 [`netplay-fuel-attempt1/`](../Evidence/netplay-fuel-attempt1/netplay-summary.txt)；修复握手后的 fuel-only 第二次 FAIL 保存在 [`netplay-fuel-attempt2/netplay-summary.txt`](../Evidence/netplay-fuel-attempt2/netplay-summary.txt)：已通过注满、漫油、清理、驾驶、机位接管、部分加注与取消保留，但 stage 12 等待完成超时，油车储量为 0、打印燃油进度 1.000。该打印值未达到精确完成：新纯回归复现实际进度 0.9999999、余油约 2.98e-8，最后转移小于进度浮点精度而停滞。修复只在耗尽油车且最终差值不超过 1e-6 时归一到精确 1，并把耗尽储量归零；8 个不规则 dt / 取消种子均精确完成、计分 / 完成事件一次，真正不足的 0.4 / 0.9999 载量仍保持未完成。两端退出码 1、runtime errors 0，最后 deliver 两图缺失。独立余油再驾驶回归初次暴露“real dock input stows the partly used hose”失败，保存在 [`m3-fuel-remount-attempt1.txt`](../Evidence/m3-fuel-remount-attempt1.txt)，修复后当前 bot / 输入报告 PASS。本轮首次六组全量也在 session 5 的清理确认握手竞态失败，保存在 [`netplay-full-attempt1/netplay-summary.txt`](../Evidence/netplay-full-attempt1/netplay-summary.txt)：sessions 1–4 两端退出码均 0，session 5 两端 1，session 6 未启动。两端 runtime errors 为 0 不等于场景剧本 PASS。数值修复后独立 session 5 与八图现已 PASS，M3.3 独立验收完成；失败文件保留用于追踪修复。

## M3.3r 燃油流程简化：验收（2026-10-06）

规则以 [Docs/M3-TASKS-PLAN.md](M3-TASKS-PLAN.md) “M3.3r 燃油流程简化”为准：站内油枪只在油站与站边油车之间使用、永不随车离站；油车自带车载油管专用于机位加注；全部为单次点按。站内点油站拿枪 → 点油车插枪 → 点阀门开阀自动注入油车 → 再点阀门关阀，插车油枪自动归位。机位点油车取车载油管 → 点飞机接管并按 `DockFuelSeconds` 自动转移 → 再点飞机断开、油管自动收回；手持油管时点油车即收回。阀门开着、油枪插车或车载油管不在车上时不可驾驶。阀门开而枪未插车立即漫油；油车满箱仍开阀或飞机满仍接管持续 2 秒宽限后漫油；油车空则停止转移、不漫油。

| 检查 | 结果与证据 |
|---|---|
| console 五套 + 静态编译 | PASS，[`m3r-console.txt`](../Evidence/m3r-console.txt)：simulation 11/209、single-cycle 10/127、network 26/521、identity 9/109、shift 43/3404，共 99 组 / 4370 断言、0 失败；完整 C# 编译 PASS。shift 覆盖关阀自动归位、满溢宽限前后、机位点按优先级、各不可驾驶条件、油车空停止转移、浮点精确完成与取消保留。 |
| `M3FuelPlaytest.Run` | PASS，exit 0，86 项 PASS / 0 FAIL，runtime errors 0；[`m3-fuel-playtest.txt`](../Evidence/m3-fuel-playtest.txt)。实际 bot 走完拿枪—插车—开阀—注满—关阀（自动归位）—驾驶—取管—接管—自动转移—断管，2 秒宽限内关阀 / 断管无漫油；餐食 / 行李进度为明确 fixture。漫油移动对照为 fixture：同点油车正常 0.180 m / 油渍 0.058 m。日志 `/tmp/palmbay-m3r-full-fuel.log`。 |
| 独立 UDP session 5 | PASS，两端 Final PASS；[`netplay-fuel-only/`](../Evidence/netplay-fuel-only/netplay-summary.txt)。 |
| 全量 UDP sessions 1–6 | PASS，脚本 exit 0，12 个 host / client 进程 exit 0、报告 Final PASS、runtime errors 0；[`netplay-summary.txt`](../Evidence/netplay-summary.txt)。session 5 由 client 真实 UDP 单次点按完成拿枪、插车、开阀、关阀自动归位、无回车收枪步骤、取车载油管、接管自动转移、0.33 时断开保留进度并续接、加满；站内满箱与机位满机的 2 秒宽限前无油渍、宽限后漫油，点飞机断开优先于清理，徒步清理两处油渍。八张燃油图 1920×1080，见 [`m3-fuel-captures.txt`](../Evidence/m3-fuel-captures.txt)；仍为实时依次拍摄，不作冻结同态声明。四类任务 22 张图及三份登机冻结快照齐全。 |
| M4 四场景 UDP | PASS，`python3 Tools/test-m4-netplay.py` exit 0，八进程 exit 0、runtime errors 0、七张 1920×1080；[`m4-netplay/summary.txt`](../Evidence/m4-netplay/summary.txt)。房主主动离开 / 失联接任保留部分油量、已归位站内油枪与 seat 1 手持的车载油管。 |
| 其余 Unity 门禁 | `DemoBuilder.ValidateScenes`（layout 14,430）、`MainLoopPlaytest.Run`、`M1ReviewCapture.Run`（四图）、`M2LobbyCapture.Run`（17 图）、`M3ReplayFailureCapture.Run`、`M4CoopCapture.Run`、`M6ResultsCapture.Run`（MP4 960×540 / 24 fps / 624 帧 / 26 秒）均 exit 0、runtime errors False。M4 合作检查有限 300 秒本地局得分由归档前的 150 / 0 星变为 466 / 1 星（回放帧数同为 1183），反映简化后 bot 燃油更快，断言仍 PASS。汇总见 [`m3r-unity-regression.txt`](../Evidence/m3r-unity-regression.txt)。 |

复验前根目录产物归档于 `Evidence/pre-m3.3r-full/`（含 `m4-netplay/` 与 2026-10-05 的燃油 bot 报告），旧版 session 5 子集归档于 `Evidence/netplay-fuel-only-m3.3-pre-r/`。本轮未运行 `M7IdentityReview.Run`、M7 玩家包冒烟、构建或 OAuth loopback（不涉燃油）。边界同上：本机隔离 Unity 进程、FakeAuth、真实 UDP 与明确 fixture，不代表 Android 双机、真实 OAuth、standalone 玩家包或真人体验验收。

## M3.4 登机：实现与验收

- 选航班即显示等待队伍和所选路线；餐食与燃油完成且机位停稳才可开门。首次放行建立固定 `(flightId, seq)` 队伍；已完成 / 失败任务拒绝再次放行。
- `Released=false` 为候机旅客，仅作排队显示；`Released=true` 为已出发旅客，沿参数化路线行进并参与房主道路阻车判定。客户端镜像相同状态和权威 `BlockedByTraffic`。
- 关门只暂停新放行与等待延迟；已出发者继续登机。取消只关闭本席开的门并保留已登机进度；重开沿用现有旅客序号和等待进度，不重复生成旅客。完成、错过或班岗结束回收队伍与开门集合。
- 旅客放行 / 到达走累计 dt 事件切片，跨段不丢剩余时间；大步与小步在截止前登机及离港计分一致。完成登机后 bot 继续剩余行李作业，不再反复尝试已完成登机口。
- 独立 session 6 的所有开 / 关 / 重开意图来自真实 client UDP；fixture 仅摆放角色 / 车辆及完成餐食 / 燃油前置，登机进度由真实旅客推进。验证前置拒绝、host 车辆被已放行旅客阻挡、关门后已放行的三位继续到 0.375、五位等待 ID 与延迟保留、重开完成到 1。

当前全量证据为 [`Evidence/netplay-summary.txt`](../Evidence/netplay-summary.txt)、`net-host-session6.txt` / `net-client-session6.txt`：**session 6 两端退出码 0、runtime errors 0、Final PASS**。当前 [`m3-boarding-captures.txt`](../Evidence/m3-boarding-captures.txt) 列出 blocked / closed / complete 六张 1920×1080 图片和三份冻结快照，文件均位于 `Evidence/` 根目录；client 对逐行旅客 ID、路点、Released、进度与延迟，以及门、任务和车辆状态匹配后截图。还验证客户端选航班 / fallback 只读、开门前路线和等待队伍可见，以及完成登机后 bot 的行李路由。早先独立 PASS 另保留于 [`netplay-boarding-only/`](../Evidence/netplay-boarding-only/netplay-summary.txt)，不覆盖最新全量产物。

初次 session 6 FAIL 保存在 [`netplay-boarding-attempt1/netplay-summary.txt`](../Evidence/netplay-boarding-attempt1/netplay-summary.txt)，由客户端所选航班镜像读取问题引发，修复后独立复验 PASS。独立 session 6 不替代全量回归；最终完整六组现已全部 PASS。

## 其他证据与边界

- 旧调试场景本轮首次路线失败保存在 [`main-loop-m3-attempt1.txt`](../Evidence/main-loop-m3-attempt1.txt)：`No clearance-safe route`，runtime errors False；修正 fixture 后实际驾驶油车挪开堵塞点，并保留九条 supply-to-ramp 可达断言；复验 exit 0、PASS 三个 ramp 和第四航班到达，见当前 `main-loop-playtest.txt`。
- 场景与旧调试场景：[`Evidence/level-layout-validation.txt`](../Evidence/level-layout-validation.txt)、[`Evidence/main-loop-playtest.txt`](../Evidence/main-loop-playtest.txt)、`Evidence/main-loop-scene.png`。MainLoop PASS 只表示独立调试场景自己的回归通过。
- 历史 M3.2 稳定树 editor 日志位于仓库外 `/tmp/palmbay-m32-{method}.log`，对应 `DemoBuilder.ValidateScenes`、`MainLoopPlaytest.Run`、`M1ReviewCapture.Run`、`M2LobbyCapture.Run`。Unity capture / netplay 应串行，避免固定 UDP 端口冲突。
- M2 macOS 与 Android build / APK 证据是早先 M2 的历史产物；本轮 M3 未执行 `BuildMac` 或 `BuildAndroid`，不能将历史构建结果视为 M3 构建验收。
- M2 真 Google OAuth、Android OAuth 回跳和两台物理 Android 设备发现 / 联机仍未验收，继续记录于 [Docs/M2-TODO.md](M2-TODO.md)。本地协议测试、隔离 Unity 进程 UDP 和截图不替代真机验收。

M3.0–M3.5 在批准的编辑器 / 本机 UDP 原型范围内全部完成。最终产物以 `Evidence/` 根目录的全量汇总、session 报告、四份任务 capture 清单、22 张任务图和三份登机冻结快照为准；独立子集及失败档案另存，不混作当前全量结果。M4 包含 300 秒 LAN 结算 / 星级、LAN 回放同步、host migration 与断线续玩完整验收；语音仍属 M5，按原计划暂缓。
