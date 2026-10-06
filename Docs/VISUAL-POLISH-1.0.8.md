# PALM BAY 1.0.8 画面精修

2026-10-04。用户在 M7 交付后选择“画面精美一些”，本轮据此新增视觉精修工作，不代表原路线已存在 M8/M9 功能定义。

## 实施计划与范围

1. 局内：修复屋顶异常暗面，统一柔和光照，精修海岸、棕榈、建筑外立面和停机坪；按用户追问继续精修飞机与三类车辆主体建模。
2. 大厅：降低过曝，精修座椅、舷窗、行李架、地毯与木色饰边；重建并保存机舱场景。
3. UI：减轻中文模拟粗体，增加主卡片、选中态和航班板层次，保持已有操作入口与安全区。
4. 同场景截图对照、16:9 / 19.5:9 回归，然后构建新的 Mac / Android 包。

本轮不改变班岗规则、关卡、联机与登录条件；M5 语音继续延期。无 Android 真机，不部署 Firestore、不修改 OAuth 配置。

## 已完成的实现

- 海岸使用圆角分层低面数网格；修复原浪花低于浅水面而被遮挡的问题，浪花与海面细纹合并为网格。
- 屋顶和机翼等硬表面使用独立面法线；无 skybox 场景显式使用三色环境光，避免屋顶黑色斜面。
- 设施增加窗框、窗台、屋檐和屋脊；停机坪增加低对比混凝土接缝；棕榈叶改为弯曲双面叶片。
- 三类地勤车辆改为倒角车身与驾驶舱，增加侧窗车门、格栅、圆肩轮胎和轮毂、翼子板；餐车增加卷帘和后门，行李车增加牵引杆/地板/行李箱细节，油车增加圆端罐体/仪表/卷管盘。
- 飞机改为连续曲面机身、贴合曲面的圆角舷窗与风挡、薄翼截面/翼梢、空心进气口/内壁/风扇和起落架细节；航班标签适度缩小。人物仅补倒角鞋、工作牌和对讲机，未重做角色骨骼或动画。
- 机舱增加共享倒角网格、头枕、安全带、圆角舷窗、行李架分缝及木色饰边；模型没有增加碰撞体。
- UI 保留布局和回调，调整中文字重、主卡片轻阴影、色板选中环、禁用按钮文字，以及航班板行与时间线状态层次。

## 验证

- [完整源码编译](../Evidence/visual-polish/compile.txt)：PASS；两条既有编码器警告保留。
- [局内运行与双比例截图](../Evidence/visual-polish/after/m1-review.txt)：PASS，运行时错误 0。
- [大厅完整回归](../Evidence/visual-polish/after/m2-lobby-review.txt)：PASS，运行时错误 0；覆盖衣柜、登录与年龄测试替身、房间显示、手动 IP、安全区、邀请预览、单人与同机双人及大厅往返。
- 大厅场景已生成、保存并重新加载，457 个 MeshFilter 的网格和材质均为持久化资产，无 Missing Script；避免只改生成器而安装包仍显示旧机舱。
- [新局内画面](../Evidence/visual-polish/after/m1-ingame-16x9.png)、[新大厅画面](../Evidence/visual-polish/after/m2-lobby-main.png)；原图保存在 `Evidence/visual-polish/before/`，原 M7 截图不覆盖。
- 截图使用编辑器固定状态与合成输入，并按产品 MSAA 设置渲染。截图中的 1 FPS 是批处理驱动的采样显示，不能用于判断实际性能；截图中的 Fake 测试标签不代表正式包使用 Fake 登录。

精修增加了机舱装饰几何；已共享材质和重复网格，但没有 Android 帧率、发热或内存实测，不宣称真机性能通过。M7 尚缺的独立整局、真实账号、系统分享与双设备验收继续保留。

## 局内模型验收

[模型近景与几何检查](../Evidence/visual-polish/after/model-review.txt)直接调用产品工厂，无新增碰撞体，网格与材质完整，包围盒有效。独立近景隐藏航班浮动标签并使用统一地面以检查几何；实际班岗中的同一模型已重新回归。

| 模型 | 可见 Renderer | 三角形数 | 几何包围盒（Unity单位） |
| --- | ---: | ---: | --- |
| 餐车 | 56 | 5,340 | 1.58 × 1.60 × 1.77 |
| 行李车 | 74 | 7,488 | 1.50 × 1.19 × 3.28 |
| 油车 | 47 | 5,756 | 1.58 × 1.54 × 2.23 |
| 飞机 | 42 | 4,648 | 6.16 × 2.02 × 3.80 |
| 人物 | 24 | 10,768 | 0.85 × 1.21 × 0.62 |

统计是几何复杂度，不是 draw call 或帧率。飞机、轮胎、油罐、卷管和倒角构件共享缓存网格。旧版基础人物仍使用较多 Unity 球体/胶囊体；后续可依据真机 profile 再降面，不能把本机截图当作移动端性能结论。

[飞机近景](../Evidence/visual-polish/after/model-aircraft.png)、[餐车近景](../Evidence/visual-polish/after/model-meals.png)、[行李车近景](../Evidence/visual-polish/after/model-baggage.png)、[油车近景](../Evidence/visual-polish/after/model-fuel.png)。可打开源码工程的 `Evidence/visual-polish/compare.html` 拖动查看新旧场景与模型画廊。

## 安装与操作

Mac：解压 `Palm-Bay-macOS.zip`，从 Finder 打开 `Palm Bay.app`。Android：安装 `PalmBay-Android.apk`，要求 ARM64、Android 7.0 / API 24 以上，版本 1.0.8 / code 8。无需卸载旧版本；如系统拒绝覆盖，请先确认签名，避免丢失存档。

大厅选择 bot 搭档后开始，游客可离线游玩。WASD / 触屏摇杆移动，E / 情境按钮交互，持续动作按住；Q 切目标航班，Esc 设置，F1 帮助。同机双人的第二位用方向键和右 Shift。

双方登录并验证年龄后可同 Wi-Fi 组队；广播发现失败可输入房主局域网 IP。没有公开下载链接。正式包缺少真实 OAuth 配置时会明确提示，不使用 Fake 账号替代登录。游客 + bot 可完整进行五分钟班岗、结算与重试。

Mac 回放导出需要本机 ffmpeg；Android 编码和分享接收应用尚待真机验证。打开分享选择器不等于内容已发送。云端不可用时，符合资格的结算在本机排队，原账号恢复后重试。

## 重现画面验收

在团结编辑器中使用菜单 `Palm Bay / Capture visual polish - airport`、`Palm Bay / Capture visual polish - lobby`。大厅命令会重建生成场景。命令行入口分别为 `M1ReviewCapture.RunVisualPolish` 和 `M2LobbyCapture.RunVisualPolish`，不加 `-quit`，验收完成自动退出；证据输出到 `Evidence/visual-polish/after/`。独立模型菜单为 `Palm Bay / Capture visual polish - runtime models`，命令行入口 `ModelPolishCapture.Run`（使用 `-quit`）。

首轮 env 包装命令因许可证 IPC 失败；改用已有批准的团结直接路径后正常启动。一轮编译碰到并行编辑的中间态，完成合并后局内与大厅最终回归均通过；日志保留在 `Evidence/visual-polish/`，最终局内回归为 `models-ingame-final.log`，大厅为 `lobby-final-capture.log`，模型为 `model-final-capture.log`；中间导入新 partial 文件的编译信息不替代最终回归结果。

## 构建与交付

Mac 与 Android 最终构建均通过，版本 1.0.8 / code 8，旧 M7 1.0.7 归档保留。

- [Mac 构建](../Evidence/visual-polish/visual-macos-build.log)、[Android 构建](../Evidence/visual-polish/visual-android-build.log)。增量构建首次重建依赖图会报告旧图的 partial 缺失，后续重新生成图并编译成功；最终产品构建标记均通过。
- [Mac 包内容检查](../Evidence/visual-polish/macos-payload-review.txt)确认实际程序集含新飞机/车辆/环境工厂，且无 smoke 自动运行资源。
- 交付包内 `manifest.json` 和 `SHA256SUMS` 记录文件大小和摘要。构建成功不代表真实账号、双物理设备或真机性能验收。

