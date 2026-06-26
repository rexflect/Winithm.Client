using Godot;
using System;
using Winithm.Client.Managers;

namespace Winithm.Client.Behaviors.Gameplay;

/// <summary>
/// Pause / rewind / recover state machine for <see cref="Player"/>.
/// </summary>
public partial class Player
{
  // ── Pause / rewind constants ─────────────────────────────────────────────────

  /// <summary>How far back (in chart seconds) a pause rewinds the clock.</summary>
  public readonly float PAUSE_REWIND_SECS = 3f;

  /// <summary>Wall-clock duration (seconds) of the rewind animation.</summary>
  public readonly float REWIND_DURATION_SECS = 0.5f;

  // ── State machine ────────────────────────────────────────────────────────────

  // Phases:
  //   Idle      – normal playback
  //   Rewinding – clock is being pushed backward in real time
  //   Recovering – clock is advancing back toward the saved position (after unpause)

  private enum PausePhase { Idle, Rewinding, Recovering }

  private PausePhase _pausePhase = PausePhase.Idle;

  // Chart-time position at the moment Pause was pressed.
  private double _timeAtPause = 0d;

  // Target chart-time for the rewind end (may be 0 if pause was very early).
  private double _rewindTarget = 0d;

  // Actual rewind distance: timeAtPause - rewindTarget (≤ PAUSE_REWIND_SECS).
  private double _rewindDistance = 0d;

  // Wall-clock seconds remaining in the current rewind animation.
  private float _rewindTimeLeft = 0f;

  // Rate at which the clock moves during the rewind animation (chart-secs / wall-sec).
  private double _rewindRate = 0d;

  // Cooldown to prevent instant re-pause right after an unpause.
  private float _pauseCooldown = 0f;

  // ── Clock tick dispatch ──────────────────────────────────────────────────────

  private void TickClock(double delta)
  {
    switch (_pausePhase)
    {
      case PausePhase.Rewinding:
        TickRewind(delta);
        break;

      case PausePhase.Recovering:
        TickRecover(delta);
        break;

      default:
        _audioController?.Tick(delta);
        break;
    }
  }

  // ── Per-phase tick logic ─────────────────────────────────────────────────────

  /// <summary>
  /// Pushes the paused clock backward at <see cref="_rewindRate"/> until the
  /// animation timer expires or the rewind target is reached.
  /// </summary>
  private void TickRewind(double delta)
  {
    if (!IsInstanceValid(_audioController))
    {
      GD.PushWarning("[Player] _audioController is not initialized!");
      return;
    }

    _rewindTimeLeft -= (float)delta;

    double step = _rewindRate * delta; // chart-seconds to move back this frame

    // Check whether the next step would overshoot the target.
    double distanceLeft = _audioController.CurrentTime - _rewindTarget;
    if (step >= distanceLeft || _rewindTimeLeft <= 0f)
    {
      // Snap to target and freeze — wait for the player to unpause.
      _audioController.AdjustTime(-distanceLeft);
      _rewindTimeLeft = 0f;
    }
    else
    {
      _audioController.AdjustTime(-step);
    }
  }

  /// <summary>
  /// Lets the audio clock run forward normally until it reaches
  /// <see cref="_timeAtPause"/>, at which point recovery ends.
  /// </summary>
  private void TickRecover(double delta)
  {
    if (!IsInstanceValid(_audioController))
    {
      GD.PushWarning("[Player] _audioController is not initialized!");
      return;
    }

    _audioController.Tick(delta);

    if (_audioController.CurrentTime >= _timeAtPause)
    {
      _pausePhase = PausePhase.Idle;
    }
  }

  // ── Pause input handler ──────────────────────────────────────────────────────

  private void HandlePauseInput()
  {
    switch (_pausePhase)
    {
      case PausePhase.Idle:
        BeginPause();
        return;

      case PausePhase.Rewinding:
        // Now handled by PauseWindow buttons, but allow manual unpause if we want.
        // If they press pause key again, we can treat it as resume if it's ready.
        if (_rewindTimeLeft <= 0f && _pauseCooldown <= 0f)
        {
          var wm = WindowManager.Instance;
          if (wm?.HasWindow(UI.PauseWindow.WINDOW_ID) ?? false)
          {
            var pauseWindow = wm?.GetWindow<UI.PauseWindow>(UI.PauseWindow.WINDOW_ID);
            pauseWindow?.OnResume?.Invoke();
          }
          else
            BeginRecover();
        }
        return;

      case PausePhase.Recovering:
        // Block re-pause during recovery to avoid abuse.
        return;
    }
  }

  // ── Pause / recover transitions ──────────────────────────────────────────────

  /// <summary>
  /// Captures the current position, computes the rewind target and animation
  /// rate, then begins the rewind phase.
  /// </summary>
  private void BeginPause()
  {
    if (!IsInstanceValid(_audioController))
    {
      GD.PushWarning("[Player] _audioController is not initialized!");
      return;
    }

    _timeAtPause = _audioController.CurrentTime;
    _audioController.Pause();

    // How far back we want to go (clamped so we never go below 0).
    _rewindTarget = Math.Max(0d, _timeAtPause - PAUSE_REWIND_SECS);
    _rewindDistance = _timeAtPause - _rewindTarget; // actual distance ≤ PAUSE_REWIND_SECS

    // Scale animation duration proportionally when we can't go back the full amount.
    // Full distance → REWIND_DURATION_SECS; shorter → proportionally less time.
    float animDuration = (float)(_rewindDistance / PAUSE_REWIND_SECS) * REWIND_DURATION_SECS;
    _rewindTimeLeft = animDuration;

    // Speed of the rewind animation in chart-seconds per wall-second.
    _rewindRate = _rewindDistance > 0d ? _rewindDistance / animDuration : 0d;

    _pausePhase = PausePhase.Rewinding;
    _componentController?.DrainPauseBar();

    // Allow main game window to be completely click-through to the OS desktop
    GetWindow().MousePassthrough = true; 

    var wm = WindowManager.Instance;
    var pauseWindowScene = GD.Load<PackedScene>("res://Winithm.Client/Scenes/UI/PauseWindow.tscn");
    
    if (pauseWindowScene is not null)
    {
      var pauseWindow = pauseWindowScene.Instantiate<UI.PauseWindow>();
      
      pauseWindow.OnResume = () =>
      {
        wm?.CloseWindow("PauseWindow");
        GetWindow().MousePassthrough = false;
        GetWindow().GrabFocus();
        BeginRecover();
      };
      
      pauseWindow.OnRetry = () =>
      {
        wm?.CloseWindow("PauseWindow");
        GetWindow().MousePassthrough = false;
        GetWindow().GrabFocus();
        _pausePhase = PausePhase.Idle;
        RestartLevel();
      };
      
      pauseWindow.OnQuit = () =>
      {
        GetTree().Quit();
      };
      
      wm?.OpenWindow("PauseWindow", pauseWindow);
    }
    else
    {
      GD.PushError("[Player] Could not load PauseWindow.tscn!");
    }
  }

  /// <summary>
  /// Starts recovery: resumes audio from the rewind position so the clock
  /// advances naturally back to <see cref="_timeAtPause"/>.
  /// </summary>
  private void BeginRecover()
  {
    if (!IsInstanceValid(_audioController))
    {
      GD.PushWarning("[Player] _audioController is not initialized!");
      return;
    }

    _pausePhase = PausePhase.Recovering;
    _audioController.Resume();
    _componentController?.FillPauseBar();

    // Apply cooldown equal to recovery duration (= rewind distance at 1× speed).
    _pauseCooldown = (float)_rewindDistance;
  }
}