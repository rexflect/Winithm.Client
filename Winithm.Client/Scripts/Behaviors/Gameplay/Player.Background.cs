using Godot;

namespace Winithm.Client.Behaviors.Gameplay;

public partial class Player
{
  // ── Background Setup ─────────────────────────────────────────────────────────

  public void SetupBackground()
  {
    if (_backgroundTexRect is null) return;

    // Apply brightness using ShaderMaterial
    if (_backgroundTexRect.Material is ShaderMaterial sm)
    {
      sm.SetShaderParameter("brightness", BackgroundBrightness);
    }

    switch (BackgroundMode)
    {
      case BackgroundMode.None:
        GetWindow().ExcludeFromCapture = false;
        _backgroundTexRect.Texture = null;
        break;

      case BackgroundMode.Illustration:
        GetWindow().ExcludeFromCapture = false;
        if (_chartData != null)
          _backgroundTexRect.Texture = _chartData.SongMetaData.Illustration.IllustrationTexture;
        break;

      case BackgroundMode.Desktop:
        GetWindow().ExcludeFromCapture = true;

        _desktopCaster?.Call("start");

        var desktopTexture = (Texture2D?)(GodotObject?)_desktopCaster?.Call("get_texture");
        if (desktopTexture != null)
        {
          _desktopTextureRef = desktopTexture; // Prevent GC collection
          _desktopAtlasTexture ??= new AtlasTexture();
          _desktopAtlasTexture.Atlas = desktopTexture;
          _backgroundTexRect.Texture = _desktopAtlasTexture;
          UpdateDesktopCaptureRegion();
        }
        break;
    }
  }

  public void DisableBackground()
  {
    _desktopCaster?.Call("stop");
    _backgroundTexRect?.Texture = null;
  }

  private void UpdateDesktopCaptureRegion()
  {
    if (_backgroundTexRect?.Texture is AtlasTexture atlas && atlas.Atlas != null)
    {
      Vector2I screenSize = DisplayServer.ScreenGetSize();
      Vector2 textureSize = atlas.Atlas.GetSize();

      // Calculate DPI scale (physical vs logical resolution)
      Vector2 dpiScale = new Vector2(
          textureSize.X / Mathf.Max(1, screenSize.X),
          textureSize.Y / Mathf.Max(1, screenSize.Y)
      );

      Rect2 globalRect = _backgroundTexRect.GetGlobalRect();
      Vector2 screenPos = _backgroundTexRect.GetScreenPosition();

      // Scale logical coordinates to physical screen coordinates
      Rect2 region = new Rect2(
          screenPos * dpiScale,
          globalRect.Size * dpiScale
      );

      // Clamp region to avoid sampling outside texture bounds
      region.Position = new Vector2(
          Mathf.Clamp(region.Position.X, 0, textureSize.X),
          Mathf.Clamp(region.Position.Y, 0, textureSize.Y)
      );

      region.Size = new Vector2(
          Mathf.Min(region.Size.X, textureSize.X - region.Position.X),
          Mathf.Min(region.Size.Y, textureSize.Y - region.Position.Y)
      );

      atlas.Region = region;
    }
  }
}