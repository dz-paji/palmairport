# 场景重建与验证

`PalmBay.unity` 是产品入口，`MainLoop.unity` 是单航班调试入口。两张场景都由编辑器菜单维护；`AirportWorld.Build()` 是两者共用的布局来源，MainLoop 保存一份可检查的烘焙副本。

编辑器菜单：

- `Palm Bay / Rebuild PalmBay scene (product)`：重建只包含 `AirportGame` 的产品场景。
- `Palm Bay / Rebuild MainLoop scene (debug)`：按当前 `Level1Map` 和 `AirportWorld` 生成、持久化网格/材质并保存调试场景。
- `Palm Bay / Rebuild both scenes`：依次重建两张场景，并保持 PalmBay 在 Build Settings 首位。
- `Palm Bay / Rebuild and capture MainLoop scene (debug)`：重建 MainLoop，保存后重载并写入 `Evidence/main-loop-scene.png`。
- `Palm Bay / Validate saved scenes and capture MainLoop`：重建、保存、重载两张场景，检查缺失脚本、网格/材质引用、布局版本和运行入口，然后截图；几何路径检查同时调用 `LevelLayoutValidation.Validate()`。
- `Palm Bay / Build macOS demo`：使用 `[PalmBay, MainLoop]` 的 Build Settings 顺序构建，PalmBay 作为首场景。

无头验证可在 `game` 目录执行：

```sh
"/path/to/Unity" -batchmode -quit -projectPath "$PWD" \
  -executeMethod DemoBuilder.ValidateScenes -logFile /tmp/palmbay-scenes.log
```

`SceneLayoutSync` 会在 MainLoop 启动时读取 `SceneLayoutMarker`。若静态环境缺失或版本落后，它会禁用旧的 `Airport World`，按当前地图重建环境，并按 `CrewSpawn`、`CartPark`、`PlanePark` 和 `ConfigureCamera` 重新定位角色、车辆、飞机与摄像机。旧的 `Assets/Generated` 资源不会被删除；重建会继续使用唯一资产路径保存新引用。
