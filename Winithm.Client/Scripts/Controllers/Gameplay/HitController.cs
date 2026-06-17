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
  private double _lastKeyReleaseBeat = double.MinValue;

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

  /// <summary>Called when Left Shift (Focus) is pressed.</summary>
  public void OnFocusKeyPressed(InputEventKey @event)
  {
    if (!IsInstanceValid(_audioController))
    {
      GD.PushWarning("[HitController] _audioController is not initialized!");
      return;
    }

    double currentBeat = _audioController?.CurrentBeat ?? 0;

    // End focusable state on all currently-focusable windows
    EndAllActiveFocusable(currentBeat);

    // Try to hit Focus notes
    ProcessBroadcastHit(NoteType.Focus, currentBeat);
  }

  /// <summary>Called when Right Shift (Close) is pressed.</summary>
  public void OnCloseKeyPressed(InputEventKey @event)
  {
    if (!IsInstanceValid(_audioController))
    {
      GD.PushWarning("[HitController] _audioController is not initialized!");
      return;
    }

    double currentBeat = _audioController?.CurrentBeat ?? 0;
    ProcessBroadcastHit(NoteType.Close, currentBeat);
  }

  /// <summary>Called when any gameplay key is released.</summary>
  public void OnKeyReleased(InputEventKey @event)
  {
    _keysHeldCount = Math.Max(0, _keysHeldCount - 1);

    if (IsInstanceValid(_audioController))
      _lastKeyReleaseBeat = _audioController.CurrentBeat ?? double.MinValue;
    else
      GD.PushWarning("[HitController] _audioController is not initialized!");


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
  public bool IsDragActive(double currentBeat)
  {
    if (!IsInstanceValid(_audioController)
      || _audioController?.Metronome is null)
    {
      GD.PushWarning("[HitController] _audioController or _audioController.Metronome is not initialized!");
      return false;
    }

    if (_keysHeldCount > 0) return true;

    if (_lastKeyReleaseBeat > double.MinValue)
    {
      double elapsedMs = _audioController.Metronome.ToDeltaMilliSeconds(
        _lastKeyReleaseBeat, currentBeat
      );
      return elapsedMs <= Constants.HitResult.TimmingWindowMs[HitResultType.Good];
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
    if (!IsInstanceValid(_noteController))
    {
      GD.PushWarning("[HitController] _noteController is not initialized!");
      return;
    }

    bool dragActive = _noteController.Autoplay || IsDragActive(_audioController?.CurrentBeat ?? 0);
    if (!dragActive) return;

    var result = HitResult.DragHit(note, elapsedMs);
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

  /// <summary>
  /// Broadcast hit: evaluates ALL notes of the given type across ALL windows.
  /// Used for Focus and Close notes.
  /// </summary>
  private void ProcessBroadcastHit(NoteType type, double currentBeat)
  {
    var results = TryEvaluateAll(type, currentBeat);

    foreach (var (WindowId, Note, OffsetMs) in results)
    {
      var result = HitResult.FromOffset(Note, OffsetMs);

      if (result.IsHit)
      {
        Note.IsEvaluated = true;
        _noteController?.ConsumeNote(WindowId, Note);
        OnHit?.Invoke(WindowId, result);

        OnHitResponseRequested?.Invoke(WindowId, Note, result, true);
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
    double goodWindowMs = Constants.HitResult.TimmingWindowMs[HitResultType.Good];

    var activeHolds = GetActiveHolds();

    foreach (var (windowId, note) in activeHolds)
    {
      if (note is null) continue;

      double holdEndBeat = note.StartBeat.AbsoluteValue + note.Length;
      double remainingMs = _audioController?.Metronome?.ToDeltaMilliSeconds(
        currentBeat, holdEndBeat
      ) ?? 0;

      // If the hold is about to end within Good window, let it complete naturally
      if (remainingMs <= goodWindowMs) continue;

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

  // =============================================
  // Hit Evaluation API
  // =============================================

  /// <summary>
  /// Finds all hittable Focus/Close notes. If closest is Perfect, absorbs all
  /// Perfect notes (max 1 per window). If sloppy, absorbs only the closest.
  /// </summary>
  public List<(string WindowId, NoteData Note, float OffsetMs)> TryEvaluateAll(NoteType type, double currentBeat)
  {
    double missWindowMs = Constants.HitResult.TimmingWindowMs[HitResultType.Miss];
    double perfectWindowMs = Constants.HitResult.TimmingWindowMs[HitResultType.Perfect];

    var candidates = new List<(string WindowId, NoteData Note, float OffsetMs)>();
    float closestAbsMs = float.MaxValue;

    (string WindowId, NoteData Note, float OffsetMs)? closestCandidate = null;

    // Pass 1: Gather all hittable notes and track closest
    if (_noteController == null) return [];

    foreach (var entry in _noteController?.WindowStates ?? [])
    {
      string windowId = entry.Key;
      var state = entry.Value;
      if (state?.WindowVisual is null || state.WindowData?.Notes is null) continue;

      if (type != NoteType.Focus && state.WindowVisual.UnFocus) continue;

      foreach (var sideEntry in state.WindowData.Notes)
      {
        int cursor = state.EvalCursors[sideEntry.Key];
        var noteList = sideEntry.Value;

        for (int i = cursor; i < noteList.Count; i++)
        {
          NoteData note = noteList[i];
          if (note.IsEvaluated || note.Type != type || !note.IsHittable) continue;

          double offsetMs = _audioController?.Metronome?.ToDeltaMilliSeconds(
            note.StartBeat.AbsoluteValue, currentBeat
          ) ?? 0;

          // Sorted by time: all subsequent notes are even further ahead
          if (offsetMs > missWindowMs) break;

          float absMs = Mathf.Abs((float)offsetMs);
          if (absMs <= missWindowMs)
          {
            var candidate = (windowId, note, (float)offsetMs);
            candidates.Add(candidate);

            if (absMs < closestAbsMs)
            {
              closestAbsMs = absMs;
              closestCandidate = candidate;
            }
          }
        }
      }
    }

    // Pass 2: Smart grouping based on closest hit quality
    var results = new List<(string, NoteData, float)>();

    if (closestCandidate.HasValue)
    {
      if (closestAbsMs <= perfectWindowMs)
      {
        // Perfect hit: group all Perfect notes (max 1 per window)
        var bestPerWindow = new Dictionary<string, (string WindowId, NoteData Note, float OffsetMs)>();
        foreach (var candidate in candidates)
        {
          float candidateAbsMs = Math.Abs(candidate.OffsetMs);
          if (candidateAbsMs <= perfectWindowMs)
          {
            if (!bestPerWindow.ContainsKey(candidate.WindowId) ||
                candidateAbsMs < Math.Abs(bestPerWindow[candidate.WindowId].OffsetMs))
            {
              bestPerWindow[candidate.WindowId] = candidate;
            }
          }
        }
        results.AddRange(bestPerWindow.Values);
      }
      else
      {
        // Sloppy hit: only consume closest to prevent chain-downgrading
        results.Add(closestCandidate.Value);
      }
    }

    return results;
  }

  /// <summary>
  /// Single-target hit: finds the closest Tap/Hold note across focused windows.
  /// </summary>
  public (string WindowId, NoteData Note)? FindClosestNote(NoteType type, double currentBeat)
  {
    string? bestWindowId = null;
    NoteData? closestNote = null;
    double closestAbsMs = Constants.HitResult.TimmingWindowMs[HitResultType.Miss];

    if (!IsInstanceValid(_noteController))
    {
      GD.PushWarning("[HitController] _noteController is not initialized!");
      return null;
    }

    foreach (var entry in _noteController.WindowStates ?? [])
    {
      string windowId = entry.Key;
      var state = entry.Value;

      if (state is null) continue;

      if (state.WindowVisual.UnFocus) continue;

      foreach (var sideEntry in state.WindowData.Notes)
      {
        int cursor = state.EvalCursors[sideEntry.Key];
        var noteList = sideEntry.Value;

        for (int i = cursor; i < noteList.Count; i++)
        {
          var note = noteList[i];
          if (note.IsEvaluated || !note.IsHittable) continue;
          if (note.IsHoldActive) continue;

          bool typeMatches = note.Type == type || (type == NoteType.Tap && note.Type == NoteType.Hold);
          if (!typeMatches) continue;

          double offsetMs = _audioController?.Metronome?.ToDeltaMilliSeconds(
            note.StartBeat.AbsoluteValue, currentBeat
          ) ?? 0;

          // Sorted by time: all subsequent notes are even further ahead
          if (offsetMs > closestAbsMs) break;

          float absMs = Mathf.Abs((float)offsetMs);
          if (absMs < closestAbsMs)
          {
            closestAbsMs = absMs;
            closestNote = note;
            bestWindowId = windowId;
          }
        }
      }
    }

    return (bestWindowId is not null && closestNote is not null) ? (bestWindowId, closestNote) : null;
  }

  /// <summary>Marks a note as an active hold and tracks it for completion.</summary>
  public void SetHoldActive(string windowId, NoteData note)
  {
    if (IsInstanceValid(_noteController) && _noteController.WindowStates.TryGetValue(windowId, out var state))
    {
      note.IsHoldActive = true;
      state.ActiveHolds.Add(note);
    }
    else
    {
      GD.PushWarning("[HitController] _noteController is not initialized!");
    }
  }

  /// <summary>Returns all currently active hold notes across all windows.</summary>
  public List<(string WindowId, NoteData Note)> GetActiveHolds()
  {
    _activeHoldsCache.Clear();

    foreach (var entry in _noteController?.WindowStates ?? [])
    {
      if (entry.Value is null) continue;
      foreach (var holdNote in entry.Value.ActiveHolds)
      {
        if (!holdNote.IsEvaluated)
          _activeHoldsCache.Add((entry.Key, holdNote));
      }
    }
    return _activeHoldsCache;
  }
}
