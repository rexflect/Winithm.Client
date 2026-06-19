using Godot;
using System;
using System.Collections.Generic;
using Winithm.Core.Data;

using Constants = Winithm.Core.Constants;

namespace Winithm.Client.Controllers.Gameplay;

public partial class HitController
{
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