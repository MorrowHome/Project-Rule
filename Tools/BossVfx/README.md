# 离线烘焙 Boss 特效

已有游戏资源可以直接使用，不需要执行本工具。重新烘焙会覆盖七张图集；建议在单独工程/工作树中操作。

1. 下载官方 Effekseer for Unity 1.80.7，临时导入运行库及 Editor 导入器；使用 Unity 渲染后端，不需要 HDRP/移动平台示例。此依赖是 MIT 工具，不是游戏运行依赖。
2. 将 `Doc/特效资源/tktk02` 的七个 `.efkproj` 和 Parts 目录复制进 Unity 项目的 `Assets/__BossVfxBake`。把 `BakeBossVfx.cs` 复制进该目录的 Editor 子目录。等待 Unity 完成编译和 `.efkproj` 导入。
3. 从 Unity 项目根目录运行下面 PowerShell。Editor update 会处理请求，将结果写入 `Library/BossVfx/result`。烘焙使用独立离屏相机，临时借用 Built-in 管线，finally 恢复项目 URP 设置，不保存项目设置或场景。

```powershell
New-Item -ItemType Directory -Path Library/BossVfx -Force | Out-Null
Set-Content -LiteralPath Library/BossVfx/request.tmp -Value atlas -NoNewline
Move-Item -LiteralPath Library/BossVfx/request.tmp -Destination Library/BossVfx/request -Force
```

4. Python 需安装 Pillow 和 NumPy。在 Unity 项目根目录运行 `python ../Tools/BossVfx/prepare_atlases.py`。它读取刚生成的 4096×4096 原始图集，保持所有帧共同中心锚点，输出 2048×2048、8×8 图集，格子留透明边并在末尾五帧渐隐。**不要对已经处理过的图集重复运行**。脚本打印 FirstFrame/FrameCount；如源效果修改导致时长变化，同时更新 `EnemyVfxSetup` 中对应数值。
5. 删除临时 `Assets/__BossVfxBake` 及临时 Effekseer 运行库，待 Unity 刷新后执行 `RuleGame/Enemies/接入 Boss 作者特效（保留 AI 与参数）`。

每帧原生效果推进两帧，即 30 FPS。最多记录 64 帧，超出部分不会保留；需要更长效果时，应同时调整烘焙工具和 Clip 网格/时长。BeamTexture、ProjectileTexture 来自同一作者的 Line6、Particle02，不是手绘替代品。
