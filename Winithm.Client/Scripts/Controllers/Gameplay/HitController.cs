using Godot;
using System;
using System.Collections.Generic;
using Winithm.Core.Controllers;
using Winithm.Core.Data;
using Winithm.Core.Managers;

using Constants = Winithm.Core.Constants;

namespace Winithm.Client.Controllers.Gameplay;

/// <summary>
/// Central input evaluator for gameplay.
/// Handles keyboard input and routes to NoteController evaluation methods.
///
/// Input rules:
/// - Tap/Hold: Any key (except Tab and \). N simultaneous notes = N presses needed.
/// - Hold sustain: At least one key must be held. Early release beyond Good window = miss.
/// - Drag: Any key held OR within Good window (125ms) after last release = auto hit.
/// - Focus (Tab): One press resolves ALL Focus notes across ALL windows.
///     Also ends focusable state on any currently-focusable windows.
///     Miss = AddStartFocusable on the missed note's window.
/// - Close (Backslash): Like Focus but miss = SetUnresponsive.
/// </summary>
public partial class HitController : Node
{
  public event Action<string, HitResult>? OnHit;
  public event Action<string, HitResult>? OnMiss;
  public event Action<string, NoteData, HitResult, bool>? OnHitResponseRequested;

  private AudioController? _audioController;
  private NoteController? _noteController;
  private WindowController? _windowController;

  private int _keysHeldCount = 0;
  private readonly Dictionary<string, double> _lastMouseOutBeat = [];

  private readonly Dictionary<NoteData, long> _lastHoldTickIndex = [];

  private readonly List<(string WindowId, NoteData Note)> _activeHoldsCache = [];

  public bool IsInputEnabled { get; set; } = true;

  public void Initialize(
    AudioController audioController,
    NoteController noteController,
    WindowController windowController
  )
  {
    _audioController = audioController;
    _noteController = noteController;
    _windowController = windowController;
  }


  // =============================================
  // Input Entry Points
  // =============================================

  /// <summary>Called when a Tap/Hold key is pressed.</summary>
  public void OnNormalKeyPressed(InputEventKey @event)
  {
    _keysHeldCount++;
    if (!IsInstanceValid(_audioController))
    {
      GD.PushWarning("[HitController] _audioController is not initialized!");
      return;
    }

    double currentBeat = _audioController?.CurrentBeat ?? 0;
    ProcessSingleHit(currentBeat);
  }

  /// <summary>Called when Focus key is pressed to clear focusable state.</summary>
  public void HandleFocusClear(InputEventKey @event)
  {
    if (!IsInstanceValid(_audioController))
    {
      GD.PushWarning("[HitController] _audioController is not initialized!");
      return;
    }

    double currentBeat = _audioController?.CurrentBeat ?? 0;
    EndAllActiveFocusable(currentBeat);
  }

  public void HandleFocusInput(string windowId)
  {
    if (!IsInstanceValid(_audioController)) return;
    double currentBeat = _audioController?.CurrentBeat ?? 0;
    ProcessFocusHit(windowId, currentBeat);
  }

  public void HandleCloseInput(string windowId)
  {
    if (!IsInstanceValid(_audioController)) return;
    double currentBeat = _audioController?.CurrentBeat ?? 0;
    ProcessCloseHit(windowId, currentBeat);
  }

  /// <summary>Called when any gameplay key is released.</summary>
  public void OnKeyReleased(InputEventKey @event)
  {
    _keysHeldCount = Math.Max(0, _keysHeldCount - 1);


    // If all keys released, check for early hold releases
    if (_keysHeldCount == 0)
      CheckHoldEarlyRelease();
  }

  // =============================================
  // Per-Frame Processing
  // =============================================

  /// <summary>
  /// Called each frame. Handles drag auto-hit when a key is held
  /// or within Good timing window (125ms) after release.
  /// </summary>
  public bool IsDragActive(string windowId, double currentBeat)
  {
    if (!IsInstanceValid(_windowController))
    {
      GD.PushWarning("[HitController] _windowController is not initialized!");
      return false;
    }

    var mousePos = GetViewport().GetMousePosition();

    if (_windowController.IsMouseOverWindowId(windowId, mousePos))
    {
      _lastMouseOutBeat[windowId] = currentBeat;
      return true;
    }

    if (!IsInstanceValid(_audioController)
      || _audioController?.Metronome is null)
    {
      GD.PushWarning("[HitController] _audioController or _audioController.Metronome is not initialized!");
      return false;
    }

    if (_lastMouseOutBeat[windowId] > double.MinValue)
    {
      double elapsedMs = _audioController.Metronome.ToDeltaMilliSeconds(
        _lastMouseOutBeat[windowId], currentBeat
      );
      return elapsedMs <= Constants.HitResult.TimmingWindowMs[HitResultType.Bad];
    }

    return false;
  }

  // =============================================
  // NoteController Event Handlers
  // =============================================

  /// <summary>Fired by NoteController when a note passes timing window without being hit.</summary>
  public void HandleNoteMiss(string windowId, NoteData note)
  {
    if (!note.IsHittable) return;
    if (!IsInstanceValid(_windowController))
    {
      GD.PushWarning("[HitController] _windowController is not initialized!");
      return;
    }

    var result = HitResult.Miss(note);
    note.IsEvaluated = true;
    OnMiss?.Invoke(windowId, result);

    // Focus miss → make the window focusable
    if (note.Type == NoteType.Focus)
      _windowController.AddStartFocusable(windowId, note.StartBeat.AbsoluteValue);

    // Close miss → make the window unresponsive
    if (note.Type == NoteType.Close)
      _windowController.SetUnresponsive(windowId);
  }

  /// <summary>Fired by NoteController when a Drag note enters judgement zone.</summary>
  public void HandleDragReady(string windowId, NoteData note, double elapsedMs)
  {
    if (!IsInstanceValid(_noteController)
      || _audioController?.CurrentBeat is null)
    {
      GD.PushWarning("[HitController] _noteController or _audioController.Metronome is not initialized!");
      return;
    }

    double currentBeat = _audioController.CurrentBeat.Value;
    
    if (!IsDragActive(windowId, currentBeat)) return;

    var result = HitResult.FromBinary(note, elapsedMs);
    if (result.IsHit)
    {
      note.IsEvaluated = true;
      _noteController?.ConsumeNote(windowId, note);
      OnHit?.Invoke(windowId, result);

      OnHitResponseRequested?.Invoke(windowId, note, result, true);
    }
  }

  /// <summary>Fired by NoteController each frame for active hold notes.</summary>
  public void HandleActiveHoldTick(string windowId, NoteData note)
  {
    if (note is null) return;
    // Hold sustain is checked on key release (CheckHoldEarlyRelease)
    // Nothing to do per-tick here; the hold continues as long as keys are held.
    if (!IsInstanceValid(_audioController))
    {
      GD.PushWarning("[HitController] _audioController is not initialized!");
      return;
    }


    if (_audioController.Metronome is null)
    {
      GD.PushWarning("[HitController] _audioController.Metronome is not initialized!");
      return;
    }

    int intervalMs = note.ResourcePack.Config.HitFXHoldTickMs;

    double activeMs = _audioController.Metronome.ToDeltaMilliSeconds(
      (double)note.StartBeat.AbsoluteValue,
      _audioController.CurrentBeat ?? 0.0
    );
    if (activeMs < intervalMs) return;

    long tickIndex = (long)Math.Floor(activeMs / intervalMs);
    if (!_lastHoldTickIndex.TryGetValue(note, out long lastTickIndex))
    {
      _lastHoldTickIndex[note] = 0;
      return;
    }
    if (tickIndex <= lastTickIndex) return;

    _lastHoldTickIndex[note] = tickIndex;

    OnHitResponseRequested?.Invoke(windowId, note, note.HoldStartResult, false);
  }

  /// <summary>Fired by NoteController when a hold note reaches its tail.</summary>
  public void HandleActiveHoldEnded(string windowId, NoteData note)
  {
    // Hold completed successfully
    if (note is null) return;

    note.IsEvaluated = true;
    _lastHoldTickIndex.Remove(note);
    OnHit?.Invoke(windowId, note.HoldStartResult);
  }

  /// <summary>Fired by NoteController for auto-hit (autoplay/ghost notes).</summary>
  public void HandleAutoHit(string windowId, NoteData note)
  {
    if (note is null) return;

    if (note.Type == NoteType.Hold)
    {
      _lastHoldTickIndex[note] = 0;
      note.HoldStartResult = HitResult.AutoHit(note);
    }

    OnHitResponseRequested?.Invoke(windowId, note, HitResult.AutoHit(note), true);

    if (note.Type != NoteType.Hold) _noteController?.ConsumeNote(windowId, note);
  }

  // =============================================
  // Hit Processing Logic
  // =============================================

  /// <summary>
  /// Single hit: finds the closest Tap/Hold note across focused windows.
  /// One key press = one note consumed.
  /// </summary>
  private void ProcessSingleHit(double currentBeat)
  {
    var best = FindClosestNote(NoteType.Tap, currentBeat);
    if (!best.HasValue) return;

    string windowId = best.Value.WindowId;
    var note = best.Value.Note;

    double offsetMs = _audioController?.Metronome?.ToDeltaMilliSeconds(
      note.StartBeat.AbsoluteValue, currentBeat
    ) ?? 0;

    var result = HitResult.FromOffset(note, offsetMs);

    if (result.IsHit)
    {
      if (note.Type == NoteType.Hold)
      {
        // track offset, begin hold tracking
        note.HoldStartResult = result;
        SetHoldActive(windowId, note);
        _lastHoldTickIndex[note] = 0;
      }
      else
      {
        // Tap: consume immediately
        note.IsEvaluated = true;
        _noteController?.ConsumeNote(windowId, note);
        OnHit?.Invoke(windowId, result);
      }

      OnHitResponseRequested?.Invoke(windowId, note, result, true);
    }
  }

  private void ProcessFocusHit(string windowId, double currentBeat)
  {
    var focusNotes = FindFocusNotesInWindow(windowId, currentBeat);
    
    foreach (var (side, note, offsetMs) in focusNotes)
    {
      var result = HitResult.FromBinary(note, offsetMs);
      if (result.IsHit)
      {
        note.IsEvaluated = true;
        _noteController?.ConsumeNote(windowId, note);
        OnHit?.Invoke(windowId, result);
        OnHitResponseRequested?.Invoke(windowId, note, result, true);
      }
    }
  }

  private void ProcessCloseHit(string windowId, double currentBeat)
  {
    var closeNote = FindCloseNoteInWindow(windowId, currentBeat);
    if (closeNote.HasValue)
    {
      var (note, offsetMs) = closeNote.Value;
      var result = HitResult.FromOffset(note, offsetMs);
      if (result.IsHit)
      {
        note.IsEvaluated = true;
        _noteController?.ConsumeNote(windowId, note);
        OnHit?.Invoke(windowId, result);
        OnHitResponseRequested?.Invoke(windowId, note, result, true);
      }
    }
  }

  /// <summary>
  /// When all keys are released, check all active holds.
  /// If a hold note is about to end within Good window (125ms), let it complete naturally.
  /// Otherwise, force a miss.
  /// </summary>
  private void CheckHoldEarlyRelease()
  {
    if (!IsInstanceValid(_noteController) || !IsInstanceValid(_audioController))
    {
      GD.PushWarning("[HitController] _noteController or _audioController is not initialized!");
      return;
    }

    double currentBeat = _audioController.CurrentBeat ?? 0;

    var activeHolds = GetActiveHolds();

    foreach (var (windowId, note) in activeHolds)
    {
      if (note is null) continue;

      double holdEndBeat = note.StartBeat.AbsoluteValue + note.Length;
      double remainingMs = _audioController?.Metronome?.ToDeltaMilliSeconds(
        currentBeat, holdEndBeat
      ) ?? 0;

      // If the hold is about to end within Good window, let it complete naturally
      if (remainingMs <= Constants.HitResult.TimmingWindowMs[HitResultType.Bad]) continue;

      // Early release → miss
      note.IsEvaluated = true;
      note.IsHoldActive = false;
      _lastHoldTickIndex.Remove(note);
      if (note.IsLifecycleBounded)
      {
        var result = HitResult.Miss(note);
        OnMiss?.Invoke(windowId, result);
      }
    }
  }

  /// <summary>
  /// Ends focusable state on all windows that are currently in a focusable period.
  /// Called when Left Shift is pressed regardless of whether Focus notes were hit.
  /// </summary>
  private void EndAllActiveFocusable(double currentBeat)
  {
    if (!IsInstanceValid(_windowController))
    {
      GD.PushWarning("[HitController] _windowController is not initialized!");
      return;
    }

    // Iterate all active windows and end focusable state on those currently in a period.
    foreach (var entry in _windowController.GetActiveWindowIds())
      if (_windowController.IsFocusableAt(entry, currentBeat))
        _windowController.AddEndFocusable(entry, currentBeat);
  }
}