# M3 四类任务逐项实现 — 实施计划与当前进度

日期：2026-10-01。状态：**已批准**（2026-10-01 用户批准，§待定项 1–4 默认值全部生效）。对标 P0 计划 §4 M3 行（F2/F3/F4/F11/F5/F10）：四类任务逐项交付，双端一致、资源可循环、可取消、无重复领取与重复交付，错误有明确归因。

## Context

**历史起点快照**（计划获批时，2026-10-01；以下不是当前代码事实）：

- `Core/AirportSimulation.cs`（纯 C#）：航班、四项进度 `Progress[4]`、计分、截止/错过、`TryAdvance`/`ReturnArrivalBags` 意图 API。**不含**站点/车辆/货物/旅客领域状态。
- `Gameplay/AirportGame.cs`（MonoBehaviour，801 行）：任务机制全在 `Interact()`——`MealOrdered/MealProgress/MealReady`、`FuelReserve/fuelOperator`、`Cart.Load/Loaded/Arrival/Work`、`boarding` 集合、`Passengers`、`standReady`。Unity 耦合，只在 host/本地端存在。
- LAN（M2）：sandbox 只有移动+抢放车辆；快照只含 crew/cart 位置、owner、cargo 布尔、Labels、Toast；client `Sim=null` 纯渲染。
- 本地 solo/同机双人已是完整 5 分钟班岗（四类任务可玩），但规则是旧版（按住补油、送错可纠正）。

当前实现进度与验收状态见下方“当前实施状态”和持续验收记录 [Docs/M3-TASKS-NET.md](M3-TASKS-NET.md)。下列快照只解释 M3 开始前的代码基线。

**用户已定稿（2026-10-01，已入 M0 §3 / P0 §6）**：

1. **取消读条分项**：餐食、行李取消后读条清零不保留；燃油、登机取消后进度保留，可继续加注/放行。
2. **送错行李**："该航班"= 误接收航班，其行李任务永久失败、不额外扣分。原属航班任务与实物行李去向按本计划 §待定项 2 的已批准默认值实施。
3. **燃油流程**：站内储备 = 大资源库，油车 = 最后一公里载体。站内补油 = 加油站式：徒步拿加油枪 → 插到油车 → 站内开加注阀门；在油车上（驾驶中）无法操作油枪与阀门；单人可完成。机位侧沿用接管/加注/收管徒步模式。
4. **漫油**：纳入本轮，随 M3.3 交付；触发与处罚在 M3.3 设计定稿（§待定项 给候选）。

**铁律**（沿用 M0/M2，不得违反）：

- 单一权威端裁定任务/占用/航班/结算，client 只提交意图；稳定 ID（crew=座位、cart=0..2、flight=航班号、task=(flightId,ServiceKind)、passenger=(flightId,序号)）；网络与回放不存 Unity 对象引用。
- **Core 下任何文件不得 `using UnityEngine`**；几何/位置以纯 float 进出 Core（M2 的 `InputFrame` 先例）。
- **全部计时走 `Pump/Step(dt)` 累计**，禁 `DateTime`/`Time`（batchmode 停摆免疫）。
- 断网游客+bot 可玩；无第三方资产；Built-in 管线；不引入 TMP。
- 本地与 LAN 共用同一套任务规则实现——规则只写一遍，禁止 host/client 各写一份。

## 总体架构

**核心动作：把任务领域状态从 `AirportGame.Interact` 抽进 Core，client 以快照镜像 Core 状态。** 这样本地与 LAN host 跑同一份规则代码（双端一致的结构性保证），console 测试（mcs）直接覆盖全部任务规则。

### Core 新增（零 Unity 依赖，`Assets/Scripts/Core/Shift/`）

1. **`ShiftSim.cs`** — 任务领域编排器，组合现有 `AirportSimulation`（航班/进度/计分不动）。持有：
   - **餐食**：`MealState{Idle, Ordered(remaining), Ready, Blocked}`（出货口阻塞语义：Ready 未被餐车取走时不可再下单）。
   - **燃油**：`Reserve`（站内资源库，首关不设上限——§待定项 3）、`TruckTank[0..1]`（油车储量）、`Nozzle{AtStation, HeldBy(seat), AttachedToTruck}`、`ValveOpen`、加注进行度、漫油状态。
   - **车辆/货物**：每车 `CargoOwner=flightId`、`Arrival`、`DeliverWork[0..1]`（读条在 domain，不在 crew）、占用者 seat。
   - **登机**：每航班 `GateOpen`、旅客表 `Passenger{Id, FlightId, WaypointIndex, Progress01}`（沿人行道参数化进度，不含 Unity 坐标；实际坐标由表现层用 `Level1Map.PavementPath` 还原，快照只传参数）。
   - **任务失败**：`Failed[(flightId,ServiceKind)]` 永久失败集合；`Flight.Progress` 到达 1 前失败即锁死（`TryAdvance` 对该项拒绝）。
   - **意图 API**（本地、bot、remote 输入同一入口，返回事件供 toast/归因）：`OrderMeal(seat)`、`TakeMeal(seat, cartId)`、`LoadCart(seat, cartId, flightId)`、`Deliver(seat, dt)`、`TakeNozzle(seat)`、`AttachNozzle(seat, cartId)`、`SetValve(seat, open)`、`FuelAircraft(seat, dt)`、`OpenGate(seat, flightId)`、`CloseGate(seat, flightId)`、`Cancel(seat)`。
   - **取消规则**：`Cancel(seat)`/中断时——餐食/行李 `DeliverWork=0`；燃油/登机进度在航班/储量上天然累计保留。已装物品与已转移燃油保留；释放占用不复制资源（M0 §3）。
   - `Tick(dt)`：餐食倒计时、燃油加注速率、旅客推进、漫油计时；航班事件仍由 `AirportSimulation.Tick` 处理。
2. **`Shift/ShiftEvents.cs`** — 事件枚举+载荷（`meal_ready`、`delivered:flight:kind`、`wrong_delivery:receiver`、`task_failed:flight:kind`、`nozzle_*`、`valve_*`、`spill_started/cleared`、`gate_opened`…），host 转 toast/埋点，client 镜像后同样可播。
3. **`Shift/ShiftSnapshot.cs`** — ShiftSim 的可序列化镜像块（纯数据），并入 `NetProtocol.Snapshot`。

### 协议扩展（`NetProtocol` Ver→2）

- `Snapshot` 增加：`Shift` 块（餐食状态、燃油 reserve/tank/nozzle/valve/spill、每车 cargo owner flightId/arrival/deliverWork、登机集合、旅客参数表、失败集合）、`Flights` 块（id/status/stand/progress[4]/arrival/deadline）、`Score/Elapsed`。
- 输入不变（move/pressed/held/cycle 经情境解释已够用，意图在 host 侧由 Interact 映射到 ShiftSim API）。
- Ver=2 与 M2 设备不互通，可接受（原型阶段）；`Parse` 版本不符即拒。
- 快照体量估算：5 航班 + ≤24 旅客 + 任务块 ≈ 2–3KB/帧 @15Hz，LAN 可承受；不优化，留记录。

### client 镜像（`AirportGame` Remote 模式改造）

- client 从 `Sim=null` 改为持有**只读镜像**：本地 `AirportSimulation`+`ShiftSim` 实例，逐快照整块覆盖；HUD（`RefreshTopShift`、任务卡、世界提示）与 replay 采样读镜像即可复用现有代码，**client 永不调用意图 API**。
- 交互钮文案仍由 host 算好经 Labels 下发（M2 机制不动）；client 的 `Context()/ActionLabel()` 只读镜像用于提示，不做判定。
- 断线规则不变：client 掉线→host bot 接管；host 掉线→client 回大厅。host migration 仍属 M4。

### M3 LAN 对局形态（§待定项 1）

默认方案：**任务练习模式**——航班按时刻表循环到场（沿用 `AirportSimulation` + 新航班生成器，Endless），四类任务完整可做、双端一致，**无 300s 结算与星级**（那是 M4）。本地 solo/coop 仍是完整 5 分钟班岗，且同步切换到新规则。

### AirportGame 改造方向

- `Interact()` 瘦身：只算空间情境（靠近哪个站/机位/车，用 host 本地位置），把判定全部委托 `ShiftSim` 意图 API；事件→`Notify`。
- 删除散字段（`MealOrdered/MealProgress/MealReady/FuelReserve/fuelOperator/boarding/Cart.Load/Loaded/Arrival/Work`），改读 ShiftSim；`Cart` 只留位置/Visual/Owner。
- bot（`BotInput`）改调同一意图 API（不绕过竞争，M0 约定）；加油站式燃油流程的 bot 路径（拿枪—插车—开阀—等待—关阀—收枪—驾驶）在 M3.3 实现。
- 旅客渲染：表现层按 `(flightId, waypointIndex, progress01)` 用 `PavementPath` 还原坐标，阻车判定仍在 host `Move()`（读 ShiftSim 旅客位置换算）。

## 切片与验收（每片独立可验收，顺序交付）

### 当前实施状态（2026-10-06）

- **M3.0–M3.2：完成并验收通过。** 共享 Core、快照镜像、餐食、行李错送失败与回放归因均已落地；历史门禁保留于持续验收记录。
- **M3.3 燃油：完成并验收通过。** 数值修复后的 console / 静态编译、`M3FuelPlaytest.Run` 与独立 session 5 全 PASS；实际 bot、点按 / 长按、余油再上车、漫油、真实 LAN 暂停 / 续加和最终收管均通过。session 5 两端退出码 0、runtime errors 0，当前全量 session 5 同样 PASS，八张 1920×1080 见 `Evidence/m3-fuel-captures.txt`；独立子集保留于 `Evidence/netplay-fuel-only/`。
- **M3.3r 燃油流程简化：完成并验收通过（2026-10-06）。** 两套油管、全点按、关阀自动归位、机位自动注油 / 再点断开、2 秒满溢宽限已落地。console 五套 99 组 / 4370 断言 0 失败（simulation 11/209、single-cycle 10/127、network 26/521、identity 9/109、shift 43/3404），静态编译 PASS，见 `Evidence/m3r-console.txt`。`M3FuelPlaytest.Run` exit 0、86 项 PASS / 0 FAIL、runtime errors 0；独立 session 5 两端 PASS（`Evidence/netplay-fuel-only/`）。最终树全量复验：layout 14,430、MainLoop、M1 四图、M2 17 图、M3 失败回放、M4 合作、M6 结算 / MP4 均 exit 0、零运行时错误；完整六组 UDP 脚本 exit 0、12 进程 exit 0 / Final PASS / runtime errors 0、22 张任务图齐全；M4 四场景 UDP exit 0、八进程 exit 0、七图齐全。汇总见 `Evidence/m3r-unity-regression.txt`，复验前根目录产物归档于 `Evidence/pre-m3.3r-full/`，旧版 session 5 子集归档于 `Evidence/netplay-fuel-only-m3.3-pre-r/`。下方 M3.3 条目描述的是被替代的旧玩家流程。
- **M3.4 登机：完成并验收通过。** 关门仅停止新放行、已出发继续；取消与重开保留进度和固定旅客 ID，客户端开门前队伍 / 路线和权威阻车事实可见。独立 UDP session 6 两端退出码 0、runtime errors 0，完整 session 6 同样 PASS，当前六张 1920×1080 同态图及三份冻结快照见 `Evidence/m3-boarding-captures.txt`；独立子集另保留。
- **M3.5 收尾：完成；M3.0–M3.5 全部完成。** 最终树纯规则 / 编译、layout 14,430、燃油 bot、MainLoop 三个 ramp / 第四航班、M1 四图、M2 17 图与回放均 exit 0、PASS、零运行时错误。六组 UDP 完整脚本 exit 0，12 个 host / client 退出码均 0、报告均 Final PASS / runtime errors 0；四类任务 22 张 1920×1080 和三份登机冻结快照齐全。当前全量证据位于 `Evidence/` 根目录；独立子集和先前失败单独保留。
- **燃油数值修复后 console / 静态编译：PASS（2026-10-04，M3.3r 之前的历史数字）。** `Evidence/m3-final-console.txt`：五套必需回归 82 组 / 6056 断言，simulation 11/209、single-cycle 10/127、network 17/342、identity 9/109、shift 35/5269；auth 48 + manifest 7，共 6111 断言、0 失败，完整 C# 编译 PASS。编排端额外 localhost loopback 2 组 / 9 断言 PASS，未计入该文件汇总。
- 历史结果仍保留：2026-10-01 的 70 / 1164、2026-10-02 的 75 / 1586，以及首次 bot、燃油 LAN、登机 LAN 和余油再驾驶失败记录；它们不替代修复后当前证据。
- 所有 LAN 验收仍为本机隔离 Unity 进程、FakeAuth、真实 UDP 与明确 fixture，不代表 Android 双机、真实 OAuth 或 standalone 玩家包验收。

这组状态是本轮记录时可见的真实结果；证据路径、边界和后续验收按 [Docs/M3-TASKS-NET.md](M3-TASKS-NET.md) 维护。

### M3.0 状态提取与镜像通道（前置，无新玩法）

- 抽 ShiftSim（**行为保持旧规则**，餐食/行李/燃油/登机原样搬迁），AirportGame 改委托；本地班岗回归全绿。
- Snapshot 加 Shift/Flights 块 + client 镜像 + HUD 改读镜像；sandbox 仍可玩（Shift 块为空态）。
- 新 console 套件 `Tests/ShiftSimTests.cs` + `Tools/test-shift.sh`（mcs 通配 `Core/*.cs Core/Shift/*.cs`）：覆盖搬迁后的全部任务规则。
- NetTests 补协议 roundtrip（Shift 块序列化/反序列化、旅客参数表）。
- **通过条件**：现有门禁全绿 + shift/net 新套件绿 + M1/M2 capture 无回归 + netplay 双进程 sandbox 绿。

### M3.1 餐食 + 共享车辆（含公共规则落地）

- 公共规则首次落地：对象占用（一座一车）、装载归属（cargo→flightId）、交付读条（2.5s，`Deliver` 意图推进）、取消（餐食读条清零、货物留车上）、出货口阻塞、空车复用。
- LAN：client 下单/取餐/装车/运送/交付全链路；同帧双席抢一车唯一 owner（M2 已验证，保持）；重复交付防护（同一 (flightId,Meals) 只记一次）。
- 证据：ShiftSimTests（下单→等待→阻塞→装车→交付→复用；取消清零；重复交付拒绝）；netplay 剧本（client 全流程 + host 旁观一致）；capture `m3-meal-*.png`（双端同刻对照）。

### M3.2 行李

- 到达行李卸下运回（前置）→ 按所选目标航班装载并绑定原属航班号 → 对接实际接收机位交付并按该机位航班裁定；取消读条清零。目标接收航班与货物原属航班是两个独立身份。
- **送错 = 误接收航班行李任务永久失败**（交付完成即判定，不是提示后放行）；失败事件进快照/回放/埋点；归因文案明确（"CA120 收到了 B 航班的行李 · 行李任务失败"）。接收航班失败不额外扣分，原属航班不受影响，可从行李站重装；接收航班已完成行李任务时拒收并留货。
- 行李站资源语义与原属航班去向按 §待定项 2 默认值实施。
- 证据：console 覆盖错误投递、失败锁、原属航班重装和已完成接收方拒收；netplay client 错送双端同见失败与归因；wrong / deliver 双端 capture；本地结算回放结果提示可见且只触发一次。详情与边界记录于 [Docs/M3-TASKS-NET.md](M3-TASKS-NET.md)。

### M3.3 燃油（完成并验收通过；玩家流程已由 M3.3r 替代）

- 新流程替代旧版按住补油：在站拿枪 → 徒步到油车边插枪 → 回站开阀，油车自动注油 → 关阀 → 到车边收纳油枪 → 驾驶到机位 → 下车从车上取管并接到飞机 → 按住注油、松开后保留当前进度 → 短点收管。在可加注飞机旁，车载收纳枪短点取管 / 长按上车，手持枪短点接管 / 长按收纳；运输途中收纳枪普通点按驾驶。油车须在站边才能插入站内油枪；单人可走完整流程。
- 单枪是全局唯一物品；**收纳在油车上的油枪（Stowed）可随车运输**。驾驶时不能操作油枪或阀门；阀门开着、手持油枪或油管仍接在油车/飞机上时，油车不能驾驶。
- 站内资源无限，不扣减储备，也无需给站补油。油车储量与航班燃油进度按转移量同步变化；中断后均保留，可继续注油。
- **漫油**：阀门开着但油枪未插入油车，或油车满箱仍开阀时触发；生成路面油渍。油渍使油车减速，可由玩家徒步清理；漫油不消耗站内储备、不导致任务失败。
- 状态：最终数值修复后 console / 静态编译、`M3FuelPlaytest.Run`、独立 netplay session 5 与八张双端图均 PASS；完整六组 M3.5 总回归同样 PASS。
- 当前证据与失败历史见 [Docs/M3-TASKS-NET.md](M3-TASKS-NET.md)。M3.3 独立与全量验收结论均为 PASS；历史 FAIL 保留，不混作当前结果。

### M3.3r 燃油流程简化（2026-10-05 用户定稿，替代上节玩家流程；2026-10-06 完成并验收通过）

用户反馈原流程步骤过多、点按/长按语义随情境变化。定稿：**保留站内阀门，删减其余步骤**。上节的储备无限、中断保留进度、驾驶时不可操作、漫油不判失败等规则不变。

- **两套油管**：站内油枪只在油站与站边油车之间使用，**永不随车离站**；油车自带车载油管，专用于机位加注。删除"收纳枪随车运输"（`StowedOnTruck`）与全局唯一枪随车的语义。
- **站内（油车停在站边）**：点油站 → 拿枪；点油车 → 插枪；点阀门 → 开阀，油车按 `StationFillSeconds` 注油；再点阀门 → 关阀，**插在车上的油枪自动归位到油站**（无需走回车边收枪）。关阀时油枪若在手上（未插车），仍为手持，点油站归还（沿用 `ReturnNozzle`）。
- **机位（油车停在可加注机位）**：点油车 → 取车载油管；点飞机 → 接管，**自动注油**（按 `DockFuelSeconds` 转移，不再按住）；再点飞机 → 断开，油管自动收回油车。手持油管时点油车 = 收回（取消）。油车空 → 停止转移，不漫油。
- **全部为单次点按，删除长按**。"点油车"在机位的优先级：该机位航班仍需燃油且油车有油 → 取管；否则 → 上车驾驶。站内：手持油枪 → 插枪；否则 → 上车驾驶。
- **不可驾驶条件**：阀门开着、站内油枪插在车上、车载油管不在车上（手持或接在飞机上）。
- **漫油**：① 阀门开着而油枪未插车 → 立即漫油（不变）；② **满溢宽限**：油车满箱仍开阀，或飞机燃油已满仍接管，持续 `OverfillGraceSeconds = 2s` 后开始漫油，持续到关阀/断管为止。漫油不消耗站内储备、不消耗油车储量，不判任务失败；油渍、减速、徒步清理不变。
- bot 路径：拿枪—插车—开阀—等待—关阀—驾驶—取管—接管—等待—断管—驾驶。
- 快照/协议：油枪与车载油管拆成两个状态并加入满溢计时；client 仍为只读镜像。
- 证据：ShiftSimTests 覆盖新状态机（含关阀自动归位、满溢宽限前后、机位点按优先级、各不可驾驶条件）；`M3FuelPlaytest` 与 netplay session 5 剧本按新流程更新并 PASS；README / M0 §3 燃油行同步改写。
- **状态：完成并验收通过（2026-10-06）。** 上述证据均已 PASS，README / M0 §3 已同步；全量回归结果见“当前实施状态”与 [Docs/M3-TASKS-NET.md](M3-TASKS-NET.md)。

### M3.4 登机（完成并验收通过）

- 选航班 → 可见队伍与路线 → 放行 → 旅客穿越道路阻挡车辆（host 判定，client 镜像同样可见）→ 逐个登机累计（取消/中断保留进度）。
- 关登机口：仅停止新放行，已出发旅客继续（M0 §3）。
- 前置：餐食与燃油完成后可放行（沿用现规则）。
- 当前证据：console 覆盖前置、关门 / 取消 / 重开、固定旅客 ID 和 dt / 截止一致性；独立 session 6 验证 client 开 / 关 / 重开、host 阻车与同态镜像，六张图和三份冻结快照见 `Evidence/netplay-boarding-only/`。完整六组复验已 PASS，当前全量证据在 `Evidence/` 根目录。

### M3.5 收尾（完成）

- 更新 `Docs/M3-TASKS-NET.md` 持续验收记录与证据索引；README 实现范围段更新；M2-TODO 保持 M2 历史范围并链接 M3 进度。
- 全量门禁与双进程 netplay 六组（含四类任务）全部 PASS，capture 22 张任务图与三份冻结快照齐全，M1 / M2 / 本地回放最终树复验完成。

## 执行方式与门禁

**沿用 M2 方式（用户指示）**：本计划是唯一事实源；**每步开新 agent 实施**，本会话编排+验收，不亲自写代码。每个实施 agent 输入 = 本文件 + 涉及文件清单 + 回归门禁；输出 = 改动摘要 + 门禁结果。步间红则带错误回退给新 agent。

**每步必全绿**：`test-simulation.sh`、`test-single-cycle.sh`、`test-net.sh`、`test-identity.sh`、`test-shift.sh`（M3.0 起）、`check-unity-compile.py`、`DemoBuilder.ValidateScenes`、`MainLoopPlaytest.Run`、`M1ReviewCapture.Run`、`M2LobbyCapture.Run`；M3.1 起每片追加该模块 netplay 剧本与 capture。

**硬前置**：
1. **编辑器自动化需当前授权状态为可运行。** 2026-10-01 本轮编辑器状态已验证为授权绿；不据此推断新的有效期。M2 文档中“有效期至 2026-09-28”是旧记录，需在确有新证据时才更新到期日。后续每轮 editor/batchmode 自动化开始前确认当时授权仍可用。
2. M2 真机验收（两台 Android + 真实 OAuth）可与 M3.0–M3.2 并行，不阻塞；但 M3.4 完成后建议连 M3 一起在真机过一遍。

## 待定项（2026-10-01 随计划批准，默认值全部生效；实施到对应切片时按此执行）

1. **M3 LAN 对局形态**：任务练习模式（航班循环、无 300s 结算），M4 再接完整班岗。
2. **送错行李级联**（M3.2）：错送交付成立，货物被误接收航班吞下、其行李任务永久失败；原属航班可从行李站重新装载出发行李（行李站按航班无限供给），原属航班任务不受影响。
3. **站内燃油储备上限**（M3.3）：首关不设上限（资源库语义），漫油不消耗储备、只产生处罚事件。
4. **漫油触发与处罚**（M3.3）：阀门开着且油枪未插车/油箱满即溢出，路面生成油渍区，车辆经过打滑减速，需玩家徒步清理；不判任务失败。

## 明确不做（M3 边界）

- 300s 结算/星级/回放同步、host migration、断线续玩完整验收（M4）；语音（M5，已暂缓）；视频导出（M6）。
- 公网联机、房间码、深链入房；反作弊与安全加固。
- 美术升级（M1 样板已定的视觉回归不属本里程碑）。
