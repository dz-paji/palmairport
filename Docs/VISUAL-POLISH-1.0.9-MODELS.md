# PALM BAY 1.0.9 模型与大厅精修（角色 / 货件 / 设施 / 机舱）

2026-10-06。用户在 1.0.8 画面精修后反馈“局内模型细节不足”，本轮只精修模型几何，不改班岗规则、关卡、联机与登录条件。

## 范围与实现

1. **地勤角色重做**（`AirportWorld.Characters.cs`，取代 `AirportWorld.cs` 中旧的球体/胶囊体拼装）：
   - 躯干、裤腿、手臂、脸和帽顶改为缓存旋转体（lathe）网格，法线平滑、接缝合并；腰带与扣、鞋底与鞋带、肩球与手。
   - 反光背心改为贴合躯干的薄壳 + 两道反光带 + 拉链，不再悬浮在躯干外；工牌挂绳、腰带对讲机。
   - 头部：蛋形脸、后 200° 头发壳（留鬓角不遮脸）、刘海、眼睛高光、眉毛、腮红、鼻、嘴。
   - 耳机：耳罩、六段头带、麦克风杆；工作帽：圆顶、帽带、D 形帽檐、顶扣、前徽。
   - 脚底身份环保留；脚底仍在 y=0，整体高约 1.29，玩法层位置/朝向/缩放逻辑不变。
2. **旅客独立工厂** `AirportWorld.CreatePassenger(name, color, position, seed)`：同一套身体改短袖便装、领口与纽扣、按 seed 轮换四种发型/发色（短发、丸子头、遮阳帽、长发）、墨镜、拉杆箱或背包（四种配色）。`AirportGame`（排队、登机、回放）与 `MainLoopGame` 的旅客调用点已切换；地勤仍用 `CreateCrew`。
3. **车载货件** `AirportWorld.CreateCargo(kind, cart)`：取代玩法层的单色立方体/圆柱。餐车=箱顶两层保温餐箱 + 托盘杯子；行李车=第一节拖斗内三只行李；油车=泵箱顶横卧油桶（箍、端盖、标签、垫木）。默认隐藏，玩法层仍只做显隐；不再创建实例材质。
4. **设施细节** `BuildFacilityDetail`：墙面横向板缝、四角立柱、每侧坡面两道瓦线、西侧带台阶的门与壁灯、屋顶通风口（餐食中心为烟囱）。不改设施占地与东侧交互正面。
5. 花丛改为三瓣叠加。
6. **机舱大厅精修**（`CabinWorld.cs`，用户反馈"大厅不好看"）：
   - 光照：方向光开启软阴影（外壳、天花板、行李架、舷窗设为不投影，避免封闭机舱被整体遮黑），机舱专用暖色三色环境光，线性轻雾（30–95）增景深，卧铺与厨房三盏暖点光。`AirportWorld.ApplyEnvironment` 明确关闭雾，进入班岗不泄漏。
   - 新增 `AirportStyle.Finish.Glow` 自发光材质：舷窗玻璃/海面/云/太阳、天花板两条暖光带、行李架下洗墙灯、走道地灯、壁灯、卧铺台灯罩、厨房灯与招牌。
   - 天花板改为 10 块圆弧板 + 板缝 + 中央灯带，地毯加横纹，舷窗加百叶轨。
   - 卧铺上的玩家/搭档改用局内地勤模型半躺（脸朝上，可见耳机与背心），换色时重建模型；枕头加厚、毛毯抬高。座椅加背袋、杂志、滚边；厨房前加饮料车，舱壁两角加盆栽棕榈。
   - 机位更低更近（FOV 40），减少天花板占比；玩家名牌移到卧铺上方。
   - `LobbyBuilder` 重建场景前清空 `Assets/Generated/CabinLobby`，孤儿资产从 1,976 个降到 274 个。

飞机与三类车辆主体沿用 1.0.8 结果。所有新几何无碰撞体，材质经 `AirportStyle.SharedMaterial` 共享。

## 验证

- [完整源码编译](../Evidence/visual-polish/compile.txt)：`python3 Tools/check-unity-compile.py` PASS；两条既有编码器警告保留。
- [模型近景与几何检查](../Evidence/visual-polish/after/model-review.txt)：`ModelPolishCapture.Run` PASS，新增旅客四款合影与货件显示。
- [局内运行与双比例截图](../Evidence/visual-polish/after/m1-review.txt)：`M1ReviewCapture.RunVisualPolish` PASS，运行时错误 0；[局内画面](../Evidence/visual-polish/after/m1-ingame-16x9.png)、[近景三连](../Evidence/visual-polish/after/m1-sample-trio.png) 可见旅客行李、地勤背心耳机、拖斗货件与设施屋顶细节。
- [大厅完整回归](../Evidence/visual-polish/after/m2-lobby-review.txt)：`M2LobbyCapture.RunVisualPolish` 重建、保存并校验场景（756 个网格/材质均持久化），回归 PASS，运行时错误 0，安全区检查双比例 PASS；[新大厅画面](../Evidence/visual-polish/after/m2-lobby-main.png)、[搭档入座](../Evidence/visual-polish/after/m2-lobby-host-fixture.png)。
- 大厅改动后重跑局内捕获 PASS，确认雾与阴影设置不影响班岗场景。

| 模型 | 可见 Renderer | 三角形数 | 几何包围盒（Unity单位） |
| --- | ---: | ---: | --- |
| 餐车（含货件） | 65 | 6,496 | 1.58 × 1.89 × 1.77 |
| 行李车（含货件） | 79 | 7,708 | 1.50 × 1.19 × 3.28 |
| 油车（含货件） | 54 | 6,380 | 1.58 × 1.54 × 2.31 |
| 飞机 | 42 | 4,648 | 6.16 × 2.02 × 3.80 |
| 地勤 | 64 | 15,092 | 0.71 × 1.29 × 0.62 |
| 旅客 ×4 | 198 | 64,260 | 3.70 × 1.21 × 0.73 |

角色三角形数高于旧版（10,768），主要来自眼睛/腮红/手等仍使用 Unity 球体；同屏最多两名地勤 + 八名旅客，统计是几何复杂度，不是 draw call 或帧率，无 Android 真机性能实测。

## 未做

- 未构建新的 Mac / Android 包，未改版本号；交付包仍为 1.0.8。
- 未重做角色骨骼或行走动画；角色仍为整体位移 + 朝向。
- 三盏点光为逐像素光，低端 Android 帧率无实测；若真机掉帧可先关点光（自发光条带仍保留氛围）。

## 重现

模型近景：菜单 `Palm Bay / Capture visual polish - runtime models`，命令行 `-executeMethod ModelPolishCapture.Run -quit`。局内：`-executeMethod M1ReviewCapture.RunVisualPolish`（不加 `-quit`）。证据输出到 `Evidence/visual-polish/after/`。
