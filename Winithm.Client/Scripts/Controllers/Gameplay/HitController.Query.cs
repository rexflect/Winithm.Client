using Godot;
using System;
using System.Collections.Generic;
using Winithm.Core.Data;
using Winithm.Core.Managers;

using Constants = Winithm.Core.Constants;

namespace Winithm.Client.Controllers.Gameplay;

public partial class HitController
{
  // =============================================
  // Hit Evaluation API
  // =============================================

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

          // Sorted by time: all subsequent notes are even further ahead (offsetMs becomes more negative)
          if (offsetMs < -closestAbsMs) break;

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

  /// <summary>
  /// Finds one closest Focus note for each side in the given window that falls within the Good timing window.
  /// </summary>
  private readonly List<(NoteSide Side, NoteData Note, double OffsetMs)> _focusNotesCache = [];
  public List<(NoteSide Side, NoteData Note, double OffsetMs)> FindFocusNotesInWindow(string windowId, double currentBeat)
  {
    if (_noteController is null || _audioController?.Metronome is null)
    {
      GD.PushWarning("[HitController] _noteController or _audioController.Metronome is not initialized!");
      return [];
    }

    _focusNotesCache.Clear();
    if (!_noteController.WindowStates.TryGetValue(windowId, out var state)) return _focusNotesCache;

    double goodWindowMs = Constants.HitResult.TimmingWindowMs[HitResultType.Good];

    foreach (var sideEntry in state.WindowData.Notes)
    {
      var side = sideEntry.Key;
      var noteList = sideEntry.Value;
      int cursor = state.EvalCursors[side];

      NoteData? closestNote = null;
      double closestAbsMs = goodWindowMs;
      double bestOffsetMs = 0;

      for (int i = cursor; i < noteList.Count; i++)
      {
        var note = noteList[i];
        if (note.IsEvaluated
            || note.Type is not NoteType.Focus
            || !note.IsHittable
            || !note.IsLifecycleBounded
        ) continue;

        double offsetMs = _audioController?.Metronome?.ToDeltaMilliSeconds(
          note.StartBeat.AbsoluteValue, currentBeat
        ) ?? 0;

        if (offsetMs < -goodWindowMs) break;

        double absMs = Math.Abs(offsetMs);
        if (absMs <= closestAbsMs)
        {
          closestAbsMs = absMs;
          closestNote = note;
          bestOffsetMs = offsetMs;
        }
      }

      if (closestNote is not null)
      {
        _focusNotesCache.Add((side, closestNote, bestOffsetMs));
      }
    }

    return _focusNotesCache;
  }

  /// <summary>
  /// Finds the closest Close note in the given window within the Miss timing window.
  /// </summary>
  public (NoteData Note, double OffsetMs)? FindCloseNoteInWindow(string windowId, double currentBeat)
  {
    if (_noteController is null || _audioController?.Metronome is null)
    {
      GD.PushWarning("[HitController] _noteController or _audioController.Metronome is not initialized!");
      return null;
    }

    if (!_noteController.WindowStates.TryGetValue(windowId, out var state)) return null;

    if (state.WindowVisual.UnFocus) return null;

    double missWindowMs = Constants.HitResult.TimmingWindowMs[HitResultType.Miss];

    NoteData? closestNote = null;
    double closestAbsMs = missWindowMs;
    double bestOffsetMs = 0;

    foreach (var sideEntry in state.WindowData.Notes)
    {
      var side = sideEntry.Key;
      var noteList = sideEntry.Value;
      int cursor = state.EvalCursors[side];

      for (int i = cursor; i < noteList.Count; i++)
      {
        var note = noteList[i];
        if (note.IsEvaluated
            || note.Type != NoteType.Close
            || !note.IsHittable
            || !note.IsLifecycleBounded
        ) continue;

        double offsetMs = _audioController.Metronome.ToDeltaMilliSeconds(
          note.StartBeat.AbsoluteValue, currentBeat
        );

        if (offsetMs < -missWindowMs) break;

        double absMs = Math.Abs(offsetMs);
        if (absMs <= closestAbsMs)
        {
          closestAbsMs = absMs;
          closestNote = note;
          bestOffsetMs = offsetMs;
        }
      }
    }

    if (closestNote is not null) return (closestNote, bestOffsetMs);
    return null;
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