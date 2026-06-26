using Godot;
using System.Collections.Generic;
using System.Linq;

namespace Winithm.Client.Managers;

/// <summary>
/// OS-like Window Manager for Godot.Window instances.
/// Handles lifecycle, focus, and grouping Windows on the taskbar.
/// </summary>
public partial class WindowManager : Node
{
  public Control? DesktopEnvironment { get; private set; }

  // Track open windows by ID
  public static WindowManager? Instance { get; private set; }

  private readonly Dictionary<string, Window> _windows = [];

  public override void _Ready()
  {
    Instance = this;
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
    window.ForceNative = true;

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
    window.QueueFree();
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
}
