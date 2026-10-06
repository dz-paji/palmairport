# M1 局内高保真样板

> 状态：2026-09-22 证据齐、全回归绿；**待用户实机确认美术方向**。Android APK 已于 2026-09-23 构建成功，仍待真机安装和视觉确认。
> 上游：[P0-HIGH-FIDELITY-PLAN.md](P0-HIGH-FIDELITY-PLAN.md) §4 M1 行；基线：[M0-BASELINE.md](M0-BASELINE.md)。

## 1. 目标与范围

M1 交付局内高保真样板：场景、角色、车辆、镜头、基础 HUD，以及 F5（作业反馈）/F6（搭档提示）的视觉部分。视觉基准为 PRD 局内 v7  mockup：高角度斜视透视、左设施/中道路/右停机坪构图、玩具感圆润模型、软阴影、海浪沙滩层次、HUD 五区。

不改动的地基：Level1Map 全部坐标与路网数学（M0 锁定）；AirportSimulation 纯逻辑；Built-in 管线 + Standard shader；工厂契约（CreateCart/CreateCrew/CreatePlane 只返根 Transform）。

## 2. 关键决策

| 决策点 | 结论 | 理由 |
|---|---|---|
| HUD 技术 | **运行时程序化 uGUI Canvas**（用户拍板），不引入 TMP，OS 中文字体动态回退 | 无第三方资产、batchmode 可截图、手机比例可验证 |
| Canvas 模式 | Screen Space - Camera（worldCamera=游戏相机） | UI 随相机一并渲进 RenderTexture，截图证据含 HUD |
| 镜头 | 透视 FOV 34°，看向作业区中心 (-4,0,0)，偏移 (0,25,-20.5)，按 aspect 自适应 dolly（横向可视 ≥33m）；结算仍切正交俯视 | v7 斜视构图；19.5:9 与 16:9 截图均已验证 |
| 调色板 | 唯一事实源 `AirportStyle`：餐橙 F0904A / 行蓝 5FA8E0 / **燃油绿 45B85C**（替换旧黄）/ 登机青蓝 45C4CE；P1 F89A55 / P2 56B6D9 | 对齐 v7 语义色；材质按 (色, 光泽档) 缓存 |
| 车辆形态 | 按 ServiceKind 分剪影：餐车=厢式货箱+图标牌；行李车=牵引头+双拖斗；油车=罐体+胶管 | v7 剪影辨识度，工厂契约不变 |
| 触屏输入 | 左下 VirtualJoystick + 右下 ActionHoldButton，经 `AirportGame.SetInput(int, CrewInput)` **逐玩家注入**（Move 覆盖阈值 0.01，按钮位 OR），触屏 P1 与键盘 P2 同机可混用 | 移动端是产品本体；同机双人同屏 |
| 玩法动画驱动 | 飞机滑行/推出/起飞由协程改为 **Step 驱动的分段动画**（`PlaneMotion`/`MotionLeg`），`AirportGame.ExternalControl` + `public void Step(float dt)` 暴露固定步长推进 | batchmode 播放器循环停摆（见 §4），编辑器工具必须能确定性步进 |

## 3. P0 M1 验收对照

| 通过条件 | 状态 | 证据 |
|---|---|---|
| 一机位、一站点、一车可操作 | ✅ 机位 A + 行李站 + 行李车完整流程（取到达→送回→装出发→按住交付） | `Evidence/m1-ingame-16x9.png`、`m1-boarding-16x9.png` |
| 两名角色可移动 | ✅ P1 登机口放行、P2 机位交付同框 | 同上 |
| 手机比例可读 | ✅ 2340×1080（19.5:9）五区 HUD 完整无裁切 | `Evidence/m1-ingame-19_5x9.png` |
| 以实机画面确认美术方向 | ⏳ 编辑器截图已出，**待 APK 真机安装后确认** | `Evidence/m1-*.png`、`m1-review.txt` |

回归（2026-09-22 全绿）：test-simulation 10/199、test-single-cycle 10/127、check-unity-compile PASS、ValidateScenes exit 0（LayoutVersion 202609221、meshes=534）、MainLoopPlaytest PASS（3 航班全流程）、M1ReviewCapture PASS（5 图）。

## 4. batchmode 播放器循环停摆（重要发现）

**现象**：batchmode Play Mode 中，当最后一个活跃协程（TaxiIn）完成时，播放器循环整体停摆——Update 不再执行（Sim.Elapsed 冻结），注入输入不被消费，`EditorApplication.QueuePlayerLoopUpdate()` 无法唤醒。大厅/暂停等状态标志全部正常。

**结论**：batchmode 下任何依赖"协程 + 实时 Update"的玩法推进都不可靠。编辑器自动化工具一律改用外部步进：

- `AirportGame.ExternalControl = true` 后 Update 不驱动，由工具以固定 dt 调 `Step(dt)`；
- 所有玩法动画（飞机滑行/起飞、旅客行走、作业进度）由 Step 推进，不留协程依赖；
- HUD 刷新走 `AirportHudCanvas.RefreshNow()`（LateUpdate 仅转发），截图前手动调用；
- 截图用 RenderTexture + `cam.Render()` + ReadPixels（batchmode 下 ScreenCapture 无产物）。

`M1ReviewCapture.Run`（菜单 Palm Bay/Capture M1 review screenshots，batchmode 不带 -quit）即按此实现，构图完全确定：7.5s 停稳 → 放行 8 旅客 → 行李三段流程 → 交付进度条穿过截图。

## 5. 已知问题与开放项

- **美术方向待用户实机确认**：相机角度/俯仰、燃油换绿色、玩具感程度，需真机画面评审后冻结。
- **Android 打包阻塞已解除（2026-09-23）**：初始 Mono2x+ARM64 构建触发 `Target architecture not specified`，之后依次修正为 IL2CPP+ARM64、添加内置 `com.unity.modules.androidjni:1.0.0` 并将 activity 主题改为安装模板提供的 `@style/TuanjieThemeSelector`。最终 `BuildAndroid` 成功生成约 12 MB `Builds/PalmBay.apk`（marker `PALM_BAY_ANDROID_READY path=Builds/PalmBay.apk scenes=3`）。荣耀 Magic 8（BKQ-AN00）与 Galaxy S23 的安装、运行及真机视觉确认仍待完成。
- **两台皆旗舰**：低端 Android 覆盖是延后风险（M0 §5 已记）。
- 截图为离屏 RT 渲染，FPS 读数（9 FPS）不代表真机性能，仅编辑器内参考。
- 语音麦克风按钮为占位 toast；PartnerEdgeIndicator 组件就位但全图相机下少触发（相机拉近方案留备）。

## 6. 证据索引

| 文件 | 内容 |
|---|---|
| `Evidence/m1-lobby-16x9.png` | 大厅：班岗卡、双模式按钮、两架停机装饰机 |
| `Evidence/m1-ingame-16x9.png` | 局内 16:9：五区 HUD、旅客放行、P2 交付进度条 |
| `Evidence/m1-ingame-19_5x9.png` | 局内 19.5:9 手机比例：安全区与触控热区 |
| `Evidence/m1-boarding-16x9.png` | 旅客走向机位、交付接近完成 |
| `Evidence/m1-sample-trio.png` | 打磨焦点三件套特写：机位 A + 餐食站方向 + 行李车 |
| `Evidence/m1-review.txt` | 采集结果（PASS/FAIL + 分辨率 + 运行时错误计数） |
