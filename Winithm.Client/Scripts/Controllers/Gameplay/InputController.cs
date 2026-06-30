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

  public event Action<InputEventMouseButton>? OnFocusKeyPressed;
  public event Action<string>? OnFocusInput;
  public event Action<string>? OnCloseInput;
  public event Action<InputEventKey>? OnNormalKeyPressed;
  public event Action<InputEventKey>? OnKeyReleased;

  private WindowController? _windowController;

  private readonly HashSet<Key> _heldKeys = [];
  private List<string> _hoveredWindowIds = [];
  private readonly Dictionary<string, Vector2> _windowSwipeDirs = [];

  private bool _isLeftMouseHeld = false;

  public static readonly float DIRECTION_CHANGE_ANGLE_THRESHOLD = 45f;
  public static readonly float MOTION_THRESHOLD = 25.0f;

  public void Initialize(WindowController windowController)
  {
    _windowController = windowController;
  }

  public override void _UnhandledInput(InputEvent @event)
  {
    if (!IsInputEnabled || @event is not InputEventKey { Echo: false } keyEvent)
      return;

    if (keyEvent.IsPressed())
    {
      // Skip if this key is already held (OS-level repeat disguised as new press)
      if (!_heldKeys.Add(keyEvent.Keycode))
        return;

      OnNormalKeyPressed?.Invoke(keyEvent);
    }
    else if (keyEvent.IsReleased())
    {
      // Skip release if not actually holding the key
      if (!_heldKeys.Remove(keyEvent.Keycode))
        return;

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
      if (mouseButtonEvent.ButtonIndex == MouseButton.Middle && mouseButtonEvent.Pressed)
      {
        OnFocusKeyPressed?.Invoke(mouseButtonEvent);
      }
      else if (mouseButtonEvent.ButtonIndex == MouseButton.Left)
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
          _windowSwipeDirs.Clear();
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

      var currentMotion = mouseMotionEvent.Relative;
      bool isSignificantMotion = currentMotion.LengthSquared() > MOTION_THRESHOLD;

      foreach (var windowId in targetWindowIds)
      {
        bool isNewHover = !_hoveredWindowIds.Contains(windowId);
        bool isDirectionChange = false;

        if (!isNewHover && isSignificantMotion)
        {
          var currentDir = currentMotion.Normalized();
          if (_windowSwipeDirs.TryGetValue(windowId, out var lastDir))
          {
            // If angle between last direction and current is > 45 degrees
            if (currentDir.Dot(lastDir) < MathF.Cos(Mathf.DegToRad(DIRECTION_CHANGE_ANGLE_THRESHOLD)))
            {
              isDirectionChange = true;
            }
          }
          _windowSwipeDirs[windowId] = currentDir;
        }

        if (isNewHover || isDirectionChange)
        {
          OnFocusInput?.Invoke(windowId);
          if (isNewHover && isSignificantMotion)
          {
            _windowSwipeDirs[windowId] = currentMotion.Normalized();
          }
        }
      }

      _hoveredWindowIds = targetWindowIds;
    }
  }
}