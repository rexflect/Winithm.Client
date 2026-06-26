using Godot;
using Winithm.Native;

namespace Winithm.Client.Managers;

/// <summary>
/// Manages the application window to fill the safe area (full screen minus OS chrome).
/// <para>
/// Uses <see cref="IPlatformProvider"/> from <c>Winithm.Native</c> for platform-specific
/// work area detection, with <see cref="DisplayServer.ScreenGetUsableRect"/> as fallback.
/// </para>
/// </summary>
public partial class DesktopManager : Node
{
  public static DesktopManager? Instance { get; private set; }

  private static readonly IPlatformProvider Platform = PlatformProviderFactory.Create();

  public override void _EnterTree()
  {
    Instance = this;
  }

  public override void _Ready()
  {
    ApplyFullScreen();
  }

  /// <summary>
  /// Positions and sizes the game window to fill the safe area on the current monitor,
  /// excluding the OS taskbar/dock/panel.
  /// </summary>
  public void ApplyFullScreen()
  {
    var window = GetWindow();
    var currentPos = window.Position;

    var safeArea = Platform.GetWorkArea(currentPos.X, currentPos.Y);

    if (safeArea.IsEmpty)
    {
      // Fallback: Godot's built-in usable rect for the current screen
      var screenIndex = DisplayServer.WindowGetCurrentScreen();
      var usable = DisplayServer.ScreenGetUsableRect(screenIndex);
      safeArea = new SafeAreaRect(
        usable.Position.X, usable.Position.Y,
        usable.Size.X, usable.Size.Y
      );
    }

    // Set windowed mode first so we can control position/size manually,
    // then apply borderless + unresizable to make it look like true fullscreen.
    window.Mode = Window.ModeEnum.Windowed;
    window.Borderless = true;
    window.Unresizable = true;
    window.Position = new Vector2I(safeArea.X, safeArea.Y);
    window.Size = new Vector2I(safeArea.Width, safeArea.Height);
  }

  public override void _ExitTree()
  {
    if (Instance == this)
      Instance = null;
  }
}