using Godot;
using System.Collections.Generic;
using System.Linq;
using Winithm.Core.Common;
using Winithm.Native;

namespace Winithm.Client.Managers;

/// <summary>
/// OS-like Window Manager for Godot.Window instances.
/// Handles lifecycle, focus, and grouping Windows on the taskbar.
/// </summary>
public partial class WindowDesktopManager : CanvasLayer
{
  public Control? DesktopEnvironment { get; private set; }

  // Track open windows by ID
  public static WindowDesktopManager? Instance { get; private set; }

  public IPlatformProvider PlatformProvider { get; private set; } = PlatformProviderFactory.Create();
  public Color? AccentColor => PlatformProvider.GetAccentColor();

  public static float ScreenScaleFactor 
    => OSDisplayUtils.GetReferenceResolutionScale(DisplayServer.WindowGetSize());

  private readonly Dictionary<string, Window> _windows = [];

  public override void _EnterTree()
  {
    Instance = this;
  }

  public override void _Ready()
  {
    DesktopEnvironment = GetNodeOrNull<Control>("DesktopEnvironment");
  }

  /// <summary>
  /// Registers and opens a window. Ensures it's transient to group into a single taskbar icon.
  /// </summary>
  public void OpenWindow(string id, Window window)
  {
    if (_windows.ContainsKey(id))
    {
      FocusWindow(id);
      return;
    }

    window.Transient = true;
    window.TransientToFocused = true;
    window.Exclusive = true;

    // Preserve the design size before scaling for ContentScale
    var designSize = window.Size;

    window.Size = new Vector2I(
      (int)(window.Size.X * ScreenScaleFactor),
      (int)(window.Size.Y * ScreenScaleFactor)
    );

    // Auto stretch: content inside the window scales to match the actual size
    window.ContentScaleSize = designSize;
    window.ContentScaleMode = Window.ContentScaleModeEnum.CanvasItems;
    window.ContentScaleAspect = Window.ContentScaleAspectEnum.Expand;

    // Listen for close requests to clean up
    window.CloseRequested += () => CloseWindow(id);

    _windows[id] = window;
    DesktopEnvironment?.AddChild(window);

    // window.AlwaysOnTop = true;

    window.Show();
    window.GrabFocus();
  }

  /// <summary>
  /// Closes and frees a window by ID.
  /// </summary>
  public void CloseWindow(string id)
  {
    if (!_windows.Remove(id, out var window)) return;

    window.Hide();
    if (IsInstanceValid(window))
    {
      window.QueueFree();
    }
  }

  /// <summary>
  /// Closes all managed windows.
  /// </summary>
  public void CloseAllWindows()
  {
    foreach (var id in _windows.Keys.ToList())
    {
      CloseWindow(id);
    }
  }

  /// <summary>
  /// Gets a window by ID, cast to a specific Window type.
  /// </summary>
  public T? GetWindow<T>(string id) where T : Window
  {
    return _windows.GetValueOrDefault(id) as T;
  }

  /// <summary>
  /// Checks if a window is currently open.
  /// </summary>
  public bool HasWindow(string id) => _windows.ContainsKey(id);
  public bool HasWindow(Window window) => _windows.ContainsValue(window);

  /// <summary>
  /// Brings a window to the front and focuses it.
  /// </summary>
  public void FocusWindow(string id)
  {
    if (_windows.TryGetValue(id, out var window))
      window.GrabFocus();
  }

  /// <summary>
  /// Returns a list of all currently managed windows.
  /// </summary>
  public IReadOnlyList<Window> GetAllWindows() => [.. _windows.Values];

  /// <summary>
  /// Toggles whether the main game window passes mouse clicks through to the OS desktop.
  /// </summary>
  public void SetMainGameClickThrough(bool passthrough)
  {
    var mainWindow = GetWindow();
    if (mainWindow == null) return;

    // 1. Ask Godot to do it (handles MacOS/Linux where Native API might be a stub, or simple cases)
    mainWindow.MousePassthrough = passthrough;

    // 2. Enforce via Native API (fixes Godot 4.x Windows bugs with MousePassthrough)
    var hwnd = (nint)DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle, mainWindow.GetWindowId());
    PlatformProvider.SetClickThrough(hwnd, passthrough);
  }
}
