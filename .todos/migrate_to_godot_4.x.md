# Migration Plan: Godot 4.x + Modern Renderer + Modern .NET

## Mục tiêu

Di chuyển Winithm từ Godot 3.x Mono + GLES2 sang Godot 4.x .NET với renderer hiện đại, ưu tiên Forward+ hoặc Mobile trên RenderingDevice. Không giữ tương thích GLES2/Compatibility/OpenGL nếu điều đó làm phức tạp code hoặc làm giảm chất lượng visual.

Mục tiêu chính:

- Bỏ toàn bộ workaround dành riêng cho Godot 3.x/GLES2.
- Chuyển render path sang Godot 4 RenderingDevice backend: Vulkan/D3D12/Metal tùy platform.
- Tận dụng shader, particle, glow, viewport/compositor và render feature mới thay vì duplicate node/layer thủ công.
- Nâng C# codebase khỏi `net472` và style API cũ.
- Tích hợp pipeline desktop capture shader theo `.todos/implement.md`.
- Giữ gameplay deterministic: timing, score, cursor, pooling, parser phải giữ hành vi gameplay hoặc được test rõ khi thay đổi.

## Nguồn Tham Chiếu

- Godot renderer docs: Godot 4 có Forward+, Mobile, Compatibility; Forward+/Mobile dùng RenderingDevice với Vulkan/D3D12/Metal, Compatibility dùng OpenGL.
  https://docs.godotengine.org/en/latest/tutorials/rendering/renderers.html
- Godot 3 -> 4 migration docs: cần dùng conversion tool trước, nhưng vẫn phải sửa thủ công API, node/resource rename, File/Directory API, signal, shader, project settings.
  https://docs.godotengine.org/en/4.0/tutorials/migrating/upgrading_to_godot_4.html
- Godot shader docs: shader resource dùng `.gdshader`, shader parameters đi qua `ShaderMaterial.set_shader_parameter()`.
  https://docs.godotengine.org/en/stable/classes/class_shader.html

## Trạng Thái Hiện Tại

- Project hiện tại là Godot 3.x Mono, csproj dùng `Godot.NET.Sdk/3.3.0` và `net472`.
- `project.godot` đang chạy main scene `res://Winithm.Client/Scenes/Desktop.tscn`.
- Autoload hiện tại:
  - `NoteSkinManager` trỏ tới `ResourcePackManager.cs`.
  - `DesktopManager` trỏ tới `DesktopManager.cs`.
- Renderer hiện tại đặt `quality/driver/driver_name="GLES2"`.
- Gameplay runtime đang do `Player.cs` orchestrate:
  - `AudioController`
  - `GroupController`
  - `ThemeChannelController`
  - `NoteController`
  - `HitFXController`
  - `WindowController`
  - `HitController`
  - `ScoreTracker`
- Chart pipeline hiện tại:
  - `WinithmIO.LoadLevel()`
  - `WNMParser.Parse()` cho metadata.
  - `WNCParser.Parse()` cho chart data.
  - `WindowManager.ComputeAllAnimations()`.
- Overlay shader data đã được parse nhưng runtime `OverlayController` đang trống.
- `.todos/implement.md` yêu cầu Desktop Capture Shader Pipeline để shader gameplay tác động lên desktop phía sau cửa sổ game trong suốt.

## Quyết Định Migration

- Không port theo hướng "chạy được trên Compatibility". Nếu code hiện tại chỉ tồn tại vì GLES2, xóa hoặc thay bằng implementation mới.
- Ưu tiên Forward+ cho desktop. Nếu muốn tiết kiệm cost ở game 2D, có thể benchmark Mobile renderer, nhưng không quay lại Compatibility/OpenGL.
- Không giữ `Particles2D -> CPUParticles2D fallback` cho GLES2.
- Không giữ duplicate glow layer nếu Godot 4 renderer/shader/postprocess cho phép glow thật sự.
- Không giữ API Godot 3 C# khi Godot 4 có API rõ hơn.
- Không nâng code style nửa vời: khi đã đổi sang Godot 4, cleanup luôn các pattern cũ như `RectSize`, `RectPosition`, string signal connect, `File`, `Directory`, `SetShaderParam`.
- `.NET 10` chỉ nên dùng nếu Godot version đang target support ổn định. Nếu Godot 4 stable tại thời điểm migration vẫn dùng baseline `net8.0`/`.NET 8 or later`, đặt framework theo template Godot chính thức trước, sau đó nâng lên `net10.0` ở nhánh riêng để xác minh editor, export và source generator. Không assume `net10.0` tương thích chỉ vì SDK máy có .NET 10.

## Phase 0: Chuẩn Bị An Toàn

- [ ] Tạo branch riêng: `migration/godot4-renderingdevice`.
- [ ] Commit sạch trạng thái Godot 3.x trước migration.
- [ ] Backup project hoặc giữ repo clean để có thể diff toàn bộ output của Godot conversion tool.
- [ ] Ghi lại baseline hiện tại:
  - [ ] `dotnet build Winithm.sln`
  - [ ] Godot 3 editor build.
  - [ ] Run demo level `frizka.allMyFellas/info`.
  - [ ] Capture video/screenshot note/window/hitfx/score UI hiện tại để so sánh visual.
- [ ] Sửa blocker compile hiện tại trước hoặc ghi nhận rõ:
  - [ ] `Winithm.Core/Scripts/Behaviors/Window.cs` import `System.Drawing`, gây ambiguous `Color` giữa `System.Drawing.Color` và `Godot.Color`.
  - [ ] Xóa `using System.Drawing;` hoặc alias rõ `Godot.Color`.

## Phase 1: Godot Project Conversion

- [ ] Mở bằng Godot 4 .NET editor, chạy conversion tool.
- [ ] Ưu tiên validate trước:
  - [ ] `godot4 --path <project> --validate-conversion-3 to4`
- [ ] Sau khi danh sách conversion hợp lý, chạy convert:
  - [ ] `godot4 --path <project> --convert-3 to4`
- [ ] Review toàn bộ diff `.tscn`, `.tres`, `.import`, `project.godot`.
- [ ] Không trust conversion tool tuyệt đối. Tool không xử lý đầy đủ C# API, shader behavior, project settings, custom runtime assumptions.

## Phase 2: Project Settings Và Build System

- [ ] Update `Winithm.csproj`.
- [ ] Thay `Godot.NET.Sdk/3.3.0` bằng Godot 4 SDK version do editor generate.
- [ ] Thay `net472`.
- [ ] Baseline an toàn: dùng target framework do Godot 4 .NET template tạo.
- [ ] Nếu nâng lên `.NET 10`:
  - [ ] Tạo branch/sub-task riêng.
  - [ ] Chỉ đổi sau khi project đã chạy ổn trên Godot 4 baseline.
  - [ ] Xác minh editor hot reload/build.
  - [ ] Xác minh desktop export.
  - [ ] Xác minh source generators, analyzer, nullable, trimming nếu bật.
- [ ] Giữ `Newtonsoft.Json` chỉ nếu còn cần. Hiện parser custom không dùng JSON trong các file cốt lõi đã đọc.
- [ ] Bật C# nullable theo từng phase, không bật toàn repo nếu chưa xử lý Godot node lifecycle:
  - [ ] `#nullable enable` theo module.
  - [ ] `private Node? _node;` cho node injected ở `_Ready`.
  - [ ] Guard runtime bằng explicit validation.

## Phase 3: Renderer Migration

- [ ] Xóa setting GLES2:
  - [ ] `quality/driver/driver_name="GLES2"`.
  - [ ] `2d/options/use_nvidia_rect_flicker_workaround`.
  - [ ] Các setting Godot 3 rendering cũ không còn ý nghĩa.
- [ ] Chọn renderer:
  - [ ] Desktop visual-first: Forward+.
  - [ ] Desktop performance-first 2D: benchmark Mobile.
  - [ ] Không dùng Compatibility làm target.
- [ ] Tắt fallback OpenGL nếu mục tiêu là bắt lỗi GPU/backend sớm thay vì silently degrade visual.
- [ ] Rebuild `default_env.tres` bằng Godot 4.
- [ ] Kiểm tra transparency/window:
  - [ ] Per-pixel transparency.
  - [ ] Borderless window.
  - [ ] Transparent viewport/background.
  - [ ] Desktop capture layer.
  - [ ] Postprocess overlay không phá alpha.

## Phase 4: C# API Migration Bắt Buộc

### Godot 3 Node/Resource Rename

- [ ] `Spatial` -> `Node3D`.
- [ ] `Particles2D` -> `GPUParticles2D`.
- [ ] `CPUParticles2D` vẫn tồn tại nhưng không dùng làm fallback GLES2 mặc định.
- [ ] `AnimatedSprite` -> `AnimatedSprite2D` nếu còn dùng.
- [ ] `Texture` type cần review theo Godot 4 resource type.
- [ ] `DynamicFont`/`DynamicFontData` cần migrate sang font resource Godot 4 tương ứng.

### Control API

- [ ] `RectSize` -> `Size`.
- [ ] `RectPosition` -> `Position`.
- [ ] `RectScale` -> `Scale`.
- [ ] `RectRotation` -> `RotationDegrees` hoặc API Godot 4 tương ứng.
- [ ] `RectMinSize` scene/property -> custom minimum size API mới.
- [ ] `rect_clip_content` scene/property -> clip contents API mới.
- [ ] Review toàn bộ UI script:
  - [ ] `PlayerWrapper.cs`
  - [ ] `PlayerArea.cs`
  - [ ] `Window.cs`
  - [ ] `WindowFrame.cs`
  - [ ] `ComponentController.cs`
  - [ ] `SongInfo.cs`
  - [ ] `ChartInfo.cs`
  - [ ] `PlayerCombo.cs`
  - [ ] `PlayerScore.cs`
  - [ ] `DigitRoller.cs`

### OS / Display API

- [ ] Migrate `OS.WindowPosition`.
- [ ] Migrate `OS.WindowSize`.
- [ ] Migrate `OS.WindowMaximized`.
- [ ] Migrate `OS.WindowBorderless`.
- [ ] Migrate `OS.WindowResizable`.
- [ ] Migrate `OS.WindowFullscreen`.
- [ ] Migrate `OS.GetScreenSize()`.
- [ ] Migrate `OS.GetWindowSafeArea()`.
- [ ] Dùng `DisplayServer`/`Window` APIs Godot 4.
- [ ] Review `DesktopManager.cs` vì đây là code phụ thuộc desktop/window platform nhiều nhất.

### File System API

- [ ] `File` -> `FileAccess`.
- [ ] `Directory` -> `DirAccess`.
- [ ] Update:
  - [ ] `WinithmIO.cs`
  - [ ] `WNMParser.cs`
  - [ ] `WNCParser.cs`
  - [ ] `WNMGenerator.cs`
  - [ ] `WNCGenerator.cs`
  - [ ] `ResourcePackManager.cs`
- [ ] Không chỉ sửa compile; cần giữ support `res://` và `user://`.

### Signal / Callable API

- [ ] Replace string-based `Connect("draw", this, nameof(...))`.
- [ ] Dùng Godot 4 C# event hoặc `Callable.From`.
- [ ] Files cần review:
  - [ ] `Window.cs`
  - [ ] `WindowFrame.cs`
  - [ ] `PlayerWrapper.cs`
  - [ ] `Player.cs`
  - [ ] `HitController.cs`

### Shader Material API

- [ ] `SetShaderParam` -> `SetShaderParameter`.
- [ ] `GetShaderParam` nếu có -> `GetShaderParameter`.
- [ ] Uniform name nên dùng `StringName` static/cache nếu gọi mỗi frame.
- [ ] Files cần review:
  - [ ] `Note.cs`
  - [ ] `SongInfo.cs`
  - [ ] `ChartInfo.cs`
  - [ ] `PlayerCombo.cs`
  - [ ] `PlayerScore.cs`
  - [ ] `ComponentController.cs`

### Await / Signals

- [ ] Review `await ToSignal(...)` trong `Player.LoadLevel`.
- [ ] Nếu Godot 4 C# có API signal await khác hoặc warning, đổi theo template mới.
- [ ] Tránh `async void` ngoài Godot signal/lifecycle. `LoadLevel` nên thành `Task` hoặc flow có cancellation nếu load level nhiều lần.

## Phase 5: Modern C# Refactor

Mục tiêu là tận dụng ngôn ngữ mới sau khi build Godot 4 đã ổn. Không trộn refactor style với migration compile trong cùng commit lớn.

- [ ] Chuyển switch statement dài sang switch expression khi tăng clarity:
  - [ ] `EasingFunctions.ParseEasing`
  - [ ] `StoryboardManager.ParseEventProperty`
  - [ ] `StoryboardManager.FormatEventProperty`
  - [ ] `WindowController.CalculateLifeCycleScale`
  - [ ] `HitFXSC` result color mapping.
- [ ] Dùng `Math.Clamp`/`Mathf.Clamp` nhất quán:
  - [ ] judgement offset.
  - [ ] progress percent.
  - [ ] pause rewind/recover.
  - [ ] shader progress params.
- [ ] Dùng pattern matching cho nullable/resource cases:
  - [ ] `if (node is not Control control) return;`
  - [ ] `if (material is ShaderMaterial mat)`.
- [ ] Dùng collection expressions chỉ khi target framework/compiler support chắc chắn.
- [ ] Dùng `readonly record struct` cho pure value nếu phù hợp:
  - [ ] `HitResult` có thể giữ struct hiện tại vì mutable trong gameplay dễ debug hơn; chỉ đổi nếu toàn bộ callsite ổn.
  - [ ] `ShaderParamDef`, `ShaderUniform` có thể cân nhắc.
- [ ] Dùng `Span`/allocation-free parsing chỉ sau khi correctness parser có test.
- [ ] Bật analyzers từng bước:
  - [ ] nullable warnings.
  - [ ] IDE simplification.
  - [ ] performance analyzers nếu không gây noise.

## Phase 6: Xóa GLES2 Workarounds

### HitFX Particles

- [ ] Xóa `UseCpuParticlesFallback` nếu chỉ tồn tại cho GLES2.
- [ ] Xóa `EnsureCpuParticleFallback()`.
- [ ] Xóa logic kiểm tra `driver_name == "GLES2"`.
- [ ] Dùng `GPUParticles2D` hoặc effect shader/custom draw trực tiếp.
- [ ] Rework `hitfx.tscn`:
  - [ ] CPUParticles2D hiện tại chỉ nên giữ nếu thật sự cần CPU deterministic visual.
  - [ ] Nếu muốn visual mạnh hơn, đổi sang GPUParticles2D + ParticleProcessMaterial Godot 4.

### Note Glow

- [ ] Không duplicate `GlowBase` layer chỉ để giả lập glow.
- [ ] Thiết kế lại `Note.tscn`:
  - [ ] `Head/Base`
  - [ ] `Head/Overlay`
  - [ ] `Body/Base`
  - [ ] optional dedicated glow pass nếu cần blur thật.
- [ ] Viết lại `NoteHighlight.gdshader` cho Godot 4.
- [ ] Nếu dùng real glow:
  - [ ] Render highlight/emissive note vào glow layer/viewport.
  - [ ] Blur/downsample trong shader hoặc dùng postprocess pipeline.
  - [ ] Composite lại lên main gameplay layer.
- [ ] ResourcePack config cần đổi từ `HighlightSpread/HighlightSize` dành cho fake duplicate layer sang params thật:
  - [ ] `GlowIntensity`
  - [ ] `GlowRadius`
  - [ ] `GlowThreshold`
  - [ ] `GlowColor`

### Window/Overlay Visual

- [ ] `Window.OnWindowBodyDraw()` không được mutate `WindowColor.a`; dùng local color.
- [ ] `UnresponsiveOverlay` hiện dùng `UNFOCUS_OVERLAY_TINT` trong draw, dù có `UNRESPONSIVE_OVERLAY_TINT`; sửa khi migrate.
- [ ] Tách window background, note layer, focus layer, hitfx layer, postprocess layer rõ hơn để shader pipeline dễ quản lý.

## Phase 7: Shader Và Overlay Pipeline

Overlay hiện có data model nhưng chưa có runtime controller. Migration Godot 4 là cơ hội triển khai thật thay vì giữ dead code.

- [ ] Hoàn thiện `OverlayController`.
- [ ] Kết nối `OverlayController` từ `Player.InitializeControllers()`.
- [ ] Initialize với:
  - [ ] `OverlayManager`
  - [ ] `Metronome`
  - [ ] `InnerShaderLayer`
  - [ ] `OuterShaderLayer`
  - [ ] `CaptureUserDesktop`
  - [ ] `ComponentController` nếu overlay `Affects UI`.
- [ ] Runtime overlay cần:
  - [ ] Spawn/reuse `ColorRect` hoặc `TextureRect` full-screen.
  - [ ] Load shader từ `res://Winithm.Core/Resources/Shaders/<ShaderFile>`.
  - [ ] Create `ShaderMaterial`.
  - [ ] Apply storyboard events per uniform.
  - [ ] Sort layer/sub-layer.
  - [ ] Disable invisible/inactive overlays.
- [ ] Shader params:
  - [ ] `OverlayData.InitParams`.
  - [ ] `OverlayData.ShaderParams`.
  - [ ] `StoryboardEvents<string>`.
- [ ] Kiểm tra shader file extension:
  - [ ] Godot 4 chỉ nên dùng `.gdshader`.
  - [ ] Chart đang reference `Bloom.glsl`, `Chromatic.glsl`; cần đổi format chart hoặc map extension.
- [ ] `ShaderUtils.ParseUserUniforms()` cần update theo syntax Godot 4 nếu dùng để auto-discover uniforms.

## Phase 8: Desktop Capture Shader Pipeline

Yêu cầu lấy từ `.todos/implement.md`: gameplay shader phải tác động lên hình ảnh desktop phía sau cửa sổ game trong suốt.

### Mục Tiêu

- [ ] Capture vùng desktop tương ứng với game window/player area.
- [ ] Upload ảnh capture vào texture nền dưới gameplay layers.
- [ ] Cho overlay shader xử lý texture đó.
- [ ] Khi không có shader cần desktop input, tắt capture để giữ transparency và tiết kiệm cost.

### Thiết Kế Runtime

- [ ] Tạo module tách biệt khỏi scene tree:
  - [ ] `DesktopCaptureService`
  - [ ] expose một output texture.
  - [ ] expose state: available, active, last frame timestamp, capture size.
- [ ] `Player.tscn` đã có node `CaptureUserDesktop`; dùng node này làm sink texture.
- [ ] `OverlayController` quyết định bật capture khi overlay yêu cầu desktop texture.
- [ ] Không capture mỗi frame mặc định.
- [ ] Capture rate configurable:
  - [ ] 30 FPS cho effect nặng.
  - [ ] 60 FPS cho effect cần phản hồi tốt.
  - [ ] uncapped chỉ khi profiling chứng minh ổn.
- [ ] Game render/update vẫn chạy FPS cao; capture texture reuse giữa các frame.

### Windows Backend

- [ ] Primary path: DXGI Desktop Duplication API.
- [ ] Fallback path: BitBlt.
- [ ] Gọi qua P/Invoke hoặc wrapper native riêng.
- [ ] DXGI path nên tránh CPU readback nếu có thể:
  - [ ] Capture GPU resource.
  - [ ] Copy/subresource.
  - [ ] Upload/update Godot texture tối thiểu.
- [ ] Nếu Godot C# không expose efficient external texture interop, benchmark:
  - [ ] `ImageTexture.Update(Image)`.
  - [ ] Native GDExtension texture bridge.
  - [ ] RenderingDevice texture upload.

### Edge Cases

- [ ] Multi-monitor.
- [ ] Window moved.
- [ ] Window resized.
- [ ] Borderless/fullscreen work area.
- [ ] Minimize/restore.
- [ ] DPI scaling.
- [ ] Transparent window region.
- [ ] Capture denied/unavailable.
- [ ] Non-Windows platform: feature disabled gracefully.

### Shader Contract

- [ ] Standard uniforms:
  - [ ] `sampler2D desktop_texture`
  - [ ] `vec2 desktop_texture_size`
  - [ ] `float capture_age`
  - [ ] `float beat`
  - [ ] `float time`
  - [ ] `float progress`
- [ ] Overlay chart event có thể animate:
  - [ ] blur radius.
  - [ ] chromatic strength.
  - [ ] hue shift.
  - [ ] glitch amount.
  - [ ] flash/threshold/intensity.
- [ ] Nếu overlay `Affects UI = 1`, composite shader sau ScoreUI.
- [ ] Nếu overlay `Affects UI = 0`, composite shader chỉ dưới UI.

## Phase 9: Gameplay Correctness Risks

### Timing

- [ ] Sửa `NoteController` đang gọi `_metronome.GetCurrentBPS(currentBeat)` trong khi method nhận seconds. Cần API rõ:
  - [ ] `GetBpsAtBeat(double beat)`.
  - [ ] `GetBpsAtSeconds(double seconds)`.
- [ ] `AudioController.CurrentBeat` vẫn là source of truth.
- [ ] Verify pause rewind/recover sau migration.
- [ ] Verify DSP clock Godot 4 AudioServer API tương thích.

### Input

- [ ] Review `InputEventKey` properties:
  - [ ] keycode/physical keycode naming thay đổi trong Godot 4.
  - [ ] action mapping từ `project.godot` có thể bị convert.
- [ ] `_UnhandledInput` vẫn phải phân biệt:
  - [ ] Pause.
  - [ ] Focus.
  - [ ] Close.
  - [ ] Tap/Hold key down/up.

### Scoring

- [ ] Add unit tests cho `ScoreEngine`.
- [ ] Add tests cho `HitResult.FromOffset`.
- [ ] Add tests cho hold combo = 2.
- [ ] Add tests cho autoplay score path.

### Parser

- [ ] Add golden-file tests:
  - [ ] parse `metadata.wnm`.
  - [ ] parse `info.wnc`.
  - [ ] generate back output.
  - [ ] parse generated output and compare core data.
- [ ] Nếu đổi chart shader extension từ `.glsl` sang `.gdshader`, cần migration rule cho old chart.

## Phase 10: Data/Manager Cleanup

- [ ] `BeatTime.NaN` hiện bằng `Zero`; đổi tên hoặc implement sentinel đúng.
- [ ] `ObjectFactory.SyncMaxIDSeed()` nên set seed next value, tránh reuse ID nếu decode trả đúng seed hiện tại.
- [ ] `GroupController.GetGroupNode()` validate ID trước khi đọc `_lastUpdateBeat[id]`.
- [ ] `ThemeChannelController.GetThemeColor()` validate ID trước khi đọc `_lastStates[id]`.
- [ ] `ResourcePackManager.Instance` access ở field initializer của `Note` có thể null tùy lifecycle; đổi sang lazy/default injection.
- [ ] `WindowManager`, `NoteManager`, `StoryboardManager`, `SpeedStepManager` đang dùng nhiều `IEnumerable.Count()/ElementAt()` trong batch add. Sau migration có thể tối ưu bằng materialize list một lần.
- [ ] `OverlayManager` comment hiện copy sai từ WindowData manager; sửa documentation.

## Phase 11: Resource Pack V2

- [ ] Định nghĩa schema resource pack mới.
- [ ] `config.ini` hiện tại có:
  - [ ] `particle`
  - [ ] `ninePatchHeadMarginH`
  - [ ] `ninePatchBodyMarginH`
  - [ ] `ninePatchBodyMarginV`
  - [ ] `highlightColor`
  - [ ] `highlightSpread`
  - [ ] `highlightSize`
  - [ ] `hitfxAutoResult`
  - [ ] `hitfxHoldTickMs`
  - [ ] `hitfxAdditiveBlending`
- [ ] Resource pack V2 nên có:
  - [ ] note textures/materials.
  - [ ] glow params.
  - [ ] hitfx scene.
  - [ ] sfx mapping.
  - [ ] optional shader/material overrides.
- [ ] ResourcePack should stop being a mutable struct if references and config grow. Prefer class/record-like resource object to avoid copy confusion.

## Phase 12: Scene Migration Checklist

- [ ] `Winithm.Client/Scenes/Desktop.tscn`
- [ ] `Winithm.Client/Scenes/Gameplay/PlayerWrapper.tscn`
- [ ] `Winithm.Client/Scenes/Gameplay/Player.tscn`
- [ ] `Winithm.Core/Scenes/ScoreUI.tscn`
- [ ] `Winithm.Core/Resources/Sprites/Window.tscn`
- [ ] `Winithm.Core/Resources/Sprites/Note.tscn`
- [ ] `Winithm.Core/Resources/Sprites/DigitRoller.tscn`
- [ ] `Winithm.Core/Resources/ResourcePacks/default/vfx/hitfx.tscn`
- [ ] Fix typo `ProgessBar` nếu muốn, nhưng nếu rename node phải update `ComponentController`.

## Phase 13: Shader Files Checklist

- [ ] `ProgressBar.gdshader`
- [ ] `SlantedStripes.gdshader`
- [ ] `NoteHighlight.gdshader`
- [ ] `WindowHinge.gdshader`
- [ ] Add/port:
  - [ ] `Bloom.gdshader`
  - [ ] `Chromatic.gdshader`
  - [ ] optional `Blur.gdshader`
  - [ ] optional `HueShift.gdshader`
  - [ ] optional `Glitch.gdshader`
- [ ] Remove `.glsl` references from chart format or support aliases during loading.

## Phase 14: Verification

- [ ] Build from CLI.
- [ ] Build from Godot editor.
- [ ] Run main scene.
- [ ] Load demo level.
- [ ] Verify autoplay.
- [ ] Verify manual play:
  - [ ] Tap.
  - [ ] Hold.
  - [ ] Drag.
  - [ ] Focus.
  - [ ] Close.
  - [ ] Pause rewind/recover.
- [ ] Verify visuals:
  - [ ] Window lifecycle.
  - [ ] Window transparency.
  - [ ] Note scroll speed.
  - [ ] Note glow.
  - [ ] HitFX.
  - [ ] Score UI.
  - [ ] Overlay shaders.
  - [ ] Desktop capture shader.
- [ ] Profile:
  - [ ] CPU frame time.
  - [ ] GPU frame time.
  - [ ] allocations per frame.
  - [ ] capture upload cost.
  - [ ] shader compile stutter.
- [ ] Export desktop build.

## Suggested Commit Order

1. Fix current Godot 3 compile blocker only.
2. Run Godot 4 conversion tool and commit raw conversion.
3. Fix project settings/csproj/build.
4. Fix core C# API compile errors.
5. Fix scenes/resources.
6. Remove GLES2 fallbacks.
7. Restore gameplay runtime.
8. Restore resource pack loading.
9. Rebuild note glow/hitfx for Godot 4 renderer.
10. Implement OverlayController.
11. Implement DesktopCaptureService.
12. Modernize C# syntax/style.
13. Add tests and profiling harness.

## Definition Of Done

- [ ] Project opens in Godot 4 .NET editor without conversion errors.
- [ ] `dotnet build` passes.
- [ ] Main scene runs.
- [ ] Demo chart loads and completes.
- [ ] No GLES2/Compatibility-specific code path remains in runtime.
- [ ] Note glow uses Godot 4 renderer/shader path, not duplicate fake glow layers unless deliberately retained as an aesthetic layer.
- [ ] Overlay shader system is functional.
- [ ] Desktop capture shader pipeline works on Windows and disables gracefully elsewhere.
- [ ] Gameplay timing and score are verified by tests or golden playback.
- [ ] Code style uses modern C# where it improves clarity, but avoids target-framework assumptions not supported by the selected Godot 4 version.
