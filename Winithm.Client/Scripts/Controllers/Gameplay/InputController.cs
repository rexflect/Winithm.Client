using System;
using System.Collections.Generic;
using Godot;
using Winithm.Core.Controllers;

namespace Winithm.Client.Controllers.Gameplay;

/// <summary>
/// Managed input routing for gameplay keys and mouse interactions.
/// </summary>
public partial class InputController : Node
{
  public bool IsInputEnabled { get; set; } = true;

  public event Action<InputEventKey>? OnFocusKeyPressed;
  public event Action<string>? OnFocusInput;
  public event Action<string>? OnCloseInput;
  public event Action<InputEventKey>? OnNormalKeyPressed;
  public event Action<InputEventKey>? OnKeyReleased;

  private WindowController? _windowController;
  private List<string> _hoveredWindowIds = [];
  private bool _isLeftMouseHeld = false;

  public void Initialize(WindowController windowController)
  {
    _windowController = windowController;
  }

  public override void _UnhandledInput(InputEvent @event)
  {
    if (!IsInputEnabled || @event is not InputEventKey { Echo: false } keyEvent)
      return;

    if (InputMap.HasAction("FocusNoteKey") && keyEvent.IsAction("FocusNoteKey"))
    {
      if (keyEvent.Pressed) OnFocusKeyPressed?.Invoke(keyEvent);
    }
    else
    {
      if (keyEvent.Pressed)
        OnNormalKeyPressed?.Invoke(keyEvent);
      else
        OnKeyReleased?.Invoke(keyEvent);
    }
  }

  public override void _Input(InputEvent @event)
  {
    if (!IsInstanceValid(_windowController))
    {
      GD.PushWarning("[InputController] _windowController is not initialized.");
      return;
    }


    if (!IsInputEnabled) return;

    if (@event is InputEventMouseButton mouseButtonEvent)
    {
      if (mouseButtonEvent.ButtonIndex == MouseButton.Left)
      {
        if (mouseButtonEvent.Pressed)
        {
          _isLeftMouseHeld = true;
          _hoveredWindowIds = _windowController.GetListWindowIdsAtMousePosition(mouseButtonEvent.GlobalPosition);
          foreach (var windowId in _hoveredWindowIds)
            OnFocusInput?.Invoke(windowId);
        }
        else
        {
          _isLeftMouseHeld = false;
          _hoveredWindowIds.Clear();
        }
      }
      else if (mouseButtonEvent.ButtonIndex == MouseButton.Right && mouseButtonEvent.Pressed)
      {
        var windowIds = _windowController.GetListWindowIdsAtMousePosition(mouseButtonEvent.GlobalPosition);
        foreach (var windowId in windowIds)
          OnCloseInput?.Invoke(windowId);
      }
    }
    else if (@event is InputEventMouseMotion mouseMotionEvent && _isLeftMouseHeld)
    {
      var targetWindowIds = _windowController.GetListWindowIdsAtMousePosition(mouseMotionEvent.GlobalPosition);
      
      foreach (var windowId in targetWindowIds)
      {
        if (!_hoveredWindowIds.Contains(windowId))
        {
          OnFocusInput?.Invoke(windowId);
        }
      }
      
      _hoveredWindowIds = targetWindowIds;
    }
  }
}