# CODELY.md

## 项目概述

本目录是 **Overcooked-like 混乱合作手游** 的游戏开发目录（游戏代码、资源、配置放这里）。

产品定义（已锁定）：**混乱合作（chaotic co-op）+ 手游 F2P + 社交留存层 + 海外市场**。参考对标：Overcooked 2、Moving Out；反面教材：Party Animals（内容跑步机+联机崩盘）、鹅鸭杀（内容消耗即崩）。社交是留存手段，不是产品本体——游戏先行。

本目录是上层研究项目 `/Users/qinziqian/Documents/overcooked-like/` 的实现层：产品方向、受众调研、冷启动策略全部沉淀在父目录研究文档中，本目录只放游戏本体。

## 当前状态（2026-10-05，M6 已实现，后续 M7 有独立交付记录）

M3 四类任务、M4 有限 LAN 班岗与断线续玩、共同回放和合作记忆均已实现。用户确认跳过 M5 语音；M6 新增结算四按钮、无声 MP4 导出与 Android/Mac 系统分享、Firebase Firestore 账号进度客户端和部署规则。只有本机参与结算且身份未变的真实账号可同步；游客/FakeAuth 不上传，退出玩家不补记 bot 成果，重复记录不重复入账。

本机 Unity 结算/导出验收与四场景真实 UDP 回归已通过。Firestore 部署、真实账号联调、两台 Android 设备编码/分享和真人合作体验仍待验收，不以模拟传输测试替代。M6 范围与证据见 `Docs/M6-RESULTS-SHARING.md`；工程内后续 M7 已修复身份恢复、迁移重试及邀请说明，并保留 1.0.7 交付记录，当前整体状态以 `Docs/M7-DELIVERY-PLAN.md`、`Docs/M7-RUNBOOK.md`、`Docs/M7-PRD-COVERAGE.md` 和 `README.md` 为准。 1.0.8 画面精修见 `Docs/VISUAL-POLISH-1.0.8.md`；2026-10-06 角色/货件/设施模型精修见 `Docs/VISUAL-POLISH-1.0.9-MODELS.md`（未出包）。M2–M4 文件保留历史阶段范围。上游历史研究方向与已确认的本轮 PRD/里程碑不一致时，遵循用户最新确认的本轮范围（如真人 LAN 登录前置、M5 跳过）。

## 上游研究文档（产品决策的唯一事实来源）

修改任何产品行为前，先读父目录对应文档：

| 文件 | 内容 |
|---|---|
| `../overcooked-like-audience-and-coldstart-report.md` | 主报告（2026-08-05）：OC2 受众调研、五分群、双层增长飞轮、三层资产架构、留存设计 |
| `../coldstart-operations-discussion.md` | 运营纪要（2026-08-06）：产品边界、10 条设计原则、UGC 转化链路、LatAm 渠道策略、Party Animals 复盘、Red-Team 审查 |
| `../brazil-couple-gamer-ecosystem.md` | 巴西情侣玩家内容生态验证（2026-09-11）：@mathvsl "Jogos igual Overcooked pra jogar de casal" 68 万播放，证明移动端情侣合作游戏是供给空白+内容蓝海 |
| `../brazil-ua-cpi-comparison.md` | 巴西买量成本对比（2026-08-21）：Kwai CPI $0.08–0.12（官方+实测）、TikTok 推算 $0.2–0.6、Meta ≈$0.85 |
| `../scripts/steam_review_analysis.py` | Steam 评测分析脚本（OC2 appid 728880），Python 3 标准库 only |
| `../oc2_couple_analysis.json` | 情侣关键词分析结果：情侣向评测占 6.98%、其中好评率 93.4%（高于大盘） |
| `../oc2_lang_counts.json` | OC2 评测语言分布：schinese 47.8% / english 23.0% / brazilian 6.8% |
| `../oc2_reviews_raw.jsonl` | 17MB 原始评测数据（84,269 条），只做数据源，勿整体载入上下文 |

## 产品边界（已锁定）

- 移动端（跨端、低配安卓覆盖）、多人合作式派对游戏
- 混乱合作机制：非对抗、非欺诈、非纯运气（合作压力，仅在有信任的关系里产生快感）
- **第一阶段：纯熟人组队，组与组之间无流动性**——不做陌生人匹配
- 第二阶段在线匹配存在结构性风险（合作高压机制对陌生人是毒药），是否做未决定
- 种子用户：年轻情侣（异地恋为尖角）；市场：LatAm 或 SEA 单语种打透（巴西调研最深，倾向 LatAm）
- 商业化：F2P，具体模型待定

## 技术实现硬约束（研究结论推导，不可妥协）

任何架构/功能/关卡设计必须满足：

1. **联机质量是运营红线**：OC2 简中差评第一主题是服务器；滩头用户（异地恋跨地区联机）延迟容忍度最低——卡顿不是"游戏问题"是"今晚约会毁了"。上线前须 MX/BR 实际网络压测；创作者集中发布前联机容量按 10 倍预估。
2. **拉人摩擦 = 0**：游客身份 + 邀请链接/房间码即开局，无需注册先玩。注册/登录只能作为资产沉淀的升级项，不能是组队前置条件。
3. **资产三层架构**：
   - 个人资产（段位、厨师收藏、成就）必须可携带——分手后还能回来的底仓；
   - 关系资产（默契值、共同战绩）必须可归档、可重建，禁止"需双方持续浇灌"的供奉式设计（QQ 情侣空间是反面教材）；
   - 搭子档案系统必须通用化（情侣/朋友/家庭共用一套），不做情侣专属符号，避免继承浪漫关系的脆弱性。
4. **自动高光回放是产品功能，优先级高于关卡数量**：全程后台录制（只留本地缓冲）、模板化自动剪辑、高光选取逻辑 = 语音情绪最高（音量/语速突变的 15 秒，不是操作最秀）、默认剥离人声轨（隐私红线）、涉及对方出镜/出声时一键"发给 ta 确认"。
5. **关卡设计制造"关系瞬间"而非"物理混乱"**：分工不对称（一方扛压一方辅助）、必须沟通但来不及（信息只给一个人看）、共犯时刻（可坑队友但后果共担）。验收标准：素材消音后是否还成立、换人是否还成立。
6. **指标按"组"不按"人"**：埋点从第一天就要能按组（pair/squad）聚合——组队完成率（北极星候选）、组周存活率、单人留守率、配对流失率与个人流失率分开统计。
7. **召回分层约束**：分手后 0–8 周绝不推送（游戏成为条件化线索，打开≈自虐）；6–8 周后只用去人格化理由（新关卡/新赛季）。数据模型要支持"同一用户换搭子重建关系资产"。

## Building and Running

技术选型：Unity 6.5（6000.5.10f1），C#，Built-in 3D 渲染。产品入口为 `Assets/Scenes/CabinLobby.unity`；直接运行 `Assets/Scenes/PalmBay.unity` 会以 solo 启动完整班岗。

运行与构建：见 `README.md`。菜单 `Palm Bay / Build macOS demo` 输出本机包，`Palm Bay / Build Android demo` 输出 APK（需 Android Build Support）。

测试脚本：`./Tools/test-simulation.sh`、`./Tools/test-single-cycle.sh`、`./Tools/test-net.sh`、`./Tools/test-identity.sh`、`./Tools/test-shift.sh`、`./Tools/test-auth-protocol.sh`、`./Tools/test-loopback-oauth.sh`、`./Tools/test-android-manifest.sh`、`./Tools/test-netplay.sh`；编译检查：`python3 Tools/check-unity-compile.py`。场景校验用 `-executeMethod DemoBuilder.ValidateScenes`（带 `-quit`）；玩法回归 `-executeMethod MainLoopPlaytest.Run`、截图 `-executeMethod M1ReviewCapture.Run` / `M2LobbyCapture.Run`（均不带 `-quit`）；本地回放提示验收入口为 `M3ReplayFailureCapture.Run`。`test-netplay.sh` 验证 M2 sandbox 和 M3 任务剧本；本机真实 UDP 双进程不是两台物理设备测试。`M2LobbyCapture.Run` 与 `test-netplay.sh` 应串行运行，避免固定 UDP 端口冲突。MainLoop 是独立旧调试场景，不能替代 M3 新任务规则验收。

2026-10-01 本轮团结 2022.3.61t14（arm64）编辑器自动化授权状态已确认可运行；当前授权有效期没有新证据，M2 文档中“有效期至 2026-09-28”仅作历史记录。Google 登录 refresh token 存于本地 `PlayerPrefs`，仅供原型，不是安全凭据存储；分析事件目前只写本地 JSONL，GA4 尚未接入。M3 本地回放帧记录含任务失败与事件，跨帧越过事件时只在结算回放提示区显示一次归因；失败列表不按帧重建，LAN 回放同步已由 M4 实现，M6 进一步提供无声视频导出。`M3ReplayFailureCapture.Run` 已验证归因可见、单次触发、按 Step(dt) 到期消失和 Retry 清理，报告与截图见 `Evidence/m3-baggage-replay.txt`、`Evidence/m3-baggage-replay.png`。

技术选型需考量的产品约束（非决定）：实时多人同步 netcode（需对抗 LatAm 高延迟/弱网）、移动 F2P、低端安卓覆盖、录像/回放管线、语音采集（高光情绪检测）、pt-BR/es-419 本地化、WhatsApp 分享链路（竖版邀请卡+链接预览图）。

## Development Conventions

- 研究文档与 CODELY.md 使用中文（混排英文术语），与父目录文档保持一致；代码标识符用英文。
- 研究脚本约定：Python 3 标准库 only（`steam_review_analysis.py` 用 urllib/json/re，无第三方依赖）。
- `*.jsonl` / `*.json` 数据文件是分析产物不是源码，要改分析逻辑而非手改数据。
- `../oc2_reviews_raw.jsonl`（17MB）读取时流式处理，勿整体读入。

## 待决策项（影响代码架构，决策后回写）

| 决策 | 状态 |
|---|---|
| 游戏引擎/技术栈 | Unity 6.5（6000.5.10f1）/ C# / Built-in |
| 滩头区域（巴西 vs SEA） | 调研倾向 LatAm/巴西，未最终写死 |
| 关系载体（房间制 vs 匹配+公会制） | 倾向搭子档案系统 |
| 变现模型（打赏/身份体系 vs 订阅/通行证） | 待定 |
| 阶段二陌生人匹配（带保护性设计 or 永不做） | 未决定 |
