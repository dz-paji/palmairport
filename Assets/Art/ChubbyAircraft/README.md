# 圆润机队 v2 — B737-800 / A380-800 / de Havilland Comet 4

本版替换原来的 ShortTwin / MediumTwin / LongQuad 同轮廓变体。现在是三套独立机体与四套涂装布局，共 12 个可直接使用的 Unity Prefab。比例经过玩具风格压缩，不是工程级 1:1 复刻。

## 三种独立结构

| 机型 | 长 × 高 × 翼展（Unity 单位，实测包围盒） | 发动机 | 外形识别点 |
| --- | --- | --- | --- |
| B737-800 | 5.867 × 2.644 × 5.282 | 2 台翼下吊挂 | 单层机身，略压扁的进气口，高上翘翼梢，6 轮起落架 |
| A380-800 | 8.667 × 3.644 × 8.881 | 4 台翼下吊挂 | 全长双层机身、两排窗，宽翼展，小型翼尖围栏，22 轮起落架 |
| Comet 4 | 5.217 × 2.294 × 5.187 | 4 台翼根内埋 | 每侧两台内埋式涡喷，无吊架，翼上封闭 pinion 油箱，无翼梢小翼，10 轮起落架 |

A380 的长度约为 737 的 1.48 倍、翼展约 1.68 倍。Comet 更短、更低。预览 `identity-lineup.png`、`identity-front.png` 与 `catalogue-board.png` 使用同样的镜头和每像素比例，未把小机型单独放大。

## 涂装布局

机体上没有航空公司文字、鹤徽、羚羊、brushwing 或其他 Logo；机尾是原创几何分区。

| 目录名 | 布局参考与改造 |
| --- | --- |
| NavyCoral | BA 风格：白色上机身、连续深蓝机腹和深蓝发动机；尾翼原创红白蓝斜向色块 |
| PorcelainRed | JAL 风格：白机身、白发动机、白尾翼、浅灰下腹；尾翼局部红色抽象色块替代红鹤徽 |
| JadeIvory | CX 风格：白机身、长灰色侧带、玉绿尾翼；没有 brushwing 标志 |
| BurgundySilver | QR 风格：浅灰上机身、白色下腹、浅灰发动机与尾翼；尾翼局部酒红斜切块替代羚羊徽 |

这些是风格移植组合，并非真实运营机队清单。JAL 运营过 737-800，BA 和 Qatar 运营 A380；Comet 4 的历史运营方包括 BOAC。没有把现代 CX／QR／JAL 配色的 Comet 描述为真实飞机。

## Unity 使用

1. 导入 `Builds/ChubbyAircraft.unitypackage`。
2. 从 `Assets/Art/ChubbyAircraft/<配色>_<机型>/` 拖入对应 Prefab。
3. `ChubbyAircraft.prefab` 是 NavyCoral_B737_800 默认入口。

Prefab 仅含 Transform、MeshFilter、MeshRenderer，无脚本依赖、外部贴图或碰撞体。机头朝本地 +X，上方为 +Y，轮胎接地 y=0，导出根缩放为 (1,1,1)。材质采用 Built-in Standard；已在 Unity 6000.6.4f1 编译、渲染并重新加载验证。URP/HDRP 项目需转换材质；未验证旧版团结引擎直接导入 Unity 6 资产。

每款导出的 Prefab 按材质合并为 17–18 个 Renderer。基础三角形约 737 2.9 万、A380 4.8 万、Comet 3.3 万，涂装带会小幅增加面数；准确值见 `Evidence/chubby-aircraft/delivery-report.txt`。未做移动设备性能测试。静态模型没有骨骼、收放起落架或旋转风扇动画。

各款 OBJ + MTL 位于 `Builds/ChubbyAircraft/`，可用于 DCC 编辑；MTL 是纯色近似，准确材质以 Unity Prefab 为准。网格有基础程序化 UV，不是手工展开的贴图图集。

## 当前游戏接入

`AirportWorld.CreatePlane(flightId, position)` 用完整航班号的稳定哈希轮换，主机、客户端和回放一致。开局三个航班覆盖三种机型。游戏内所有机型统一缩放 0.56，保留大小差异并适配原机位间距，没有逐机型归一化。

`CreatePlaneVariant(name, palette, configuration, position)` 可直接指定原始尺寸模型：配色 0–3；机型 0=737-800、1=A380-800、2=Comet4。旧的自定义颜色 `CreatePlane(name, color, position)` 入口保留，生成 737。

导出菜单：`Palm Bay / Export chubby aircraft model and previews`。导出工具源码在原项目 `Assets/Editor/ChubbyAircraftDelivery.cs`。工具验证 2/4/4 发动机、左右对称、Comet 翼根层级、大小关系、网格有限值、持久化几何哈希及 Prefab 重新加载。

## 参考资料

- Boeing 737 Next Generation：[官方机型特点](https://www.boeing.com/Commercial/737ng/737-next-generation-design-highlights)
- BA A380-800：[官方机型及照片](https://www.britishairways.com/content/en/information/about-ba/fleet-facts/airbus-a380-800)
- BAE Systems Comet 3/4：[历史资料与比例](https://www.baesystems.com/heritage/page/de-havilland-dh-106-comet-3-and-4)
- JAL 737-800：[机型照片](https://www.jal.co.jp/kr/ko/aircraft/737.html)
- Cathay：[涂装分区说明](https://www.cathaypacific.com/cx/en_CA/inspiration/cathay-stories/how-cathay-pacific-refreshed-its-aircraft-livery.html)
- Qatar：[涂装分区说明](https://www.qatarairways.com/en-se/press-releases/2006/Mar/aboutus_news_08march06.html)
