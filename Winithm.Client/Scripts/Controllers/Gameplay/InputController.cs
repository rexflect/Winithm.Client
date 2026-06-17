using System;
using Godot;

namespace Winithm.Client.Controllers.Gameplay;

/// <summary>
/// Managed input routing for gameplay keys.
/// </summary>
public partial class InputController : Node
{
  public bool IsInputEnabled { get; set; } = true;

  public event Action<InputEventKey>? OnFocusKeyPressed;
  public event Action<InputEventKey>? OnCloseKeyPressed;
  public event Action<InputEventKey>? OnNormalKeyPressed;
  public event Action<InputEventKey>? OnKeyReleased;

  public override void _UnhandledInput(InputEvent @event)
  {
    if (!IsInputEnabled || @event is not InputEventKey { Echo: false } keyEvent)
      return;

    if (keyEvent.IsAction("FocusNoteKey"))
    {
      if (keyEvent.Pressed) OnFocusKeyPressed?.Invoke(keyEvent);
    }
    else if (keyEvent.IsAction("CloseNoteKey"))
    {
      if (keyEvent.Pressed) OnCloseKeyPressed?.Invoke(keyEvent);
    }
    else
    {
      if (keyEvent.Pressed)
        OnNormalKeyPressed?.Invoke(keyEvent);
      else
        OnKeyReleased?.Invoke(keyEvent);
    }
  }
}