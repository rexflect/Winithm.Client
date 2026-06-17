using System.Collections.Generic;
using Winithm.Core.Data;
using Winithm.Core.Logic;

namespace Winithm.Client.Controllers.Gameplay
{
  /// <summary>
  /// Client-side score tracker. Translates <see cref="HitResult"/> events from
  /// <see cref="HitController"/> into normalized values for <see cref="ScoreEngine"/>,
  /// and stores the full hit history for the result screen.
  /// </summary>
  public class ScoreTracker
  {
    private readonly ScoreEngine _engine = new();

    // Full ordered history of every judgement — used by the result screen.
    private readonly List<HitResult> _hitHistory = [];

    // ── Setup ────────────────────────────────────────────────────────────────────

    public void SetTotalCombos(int count) => _engine.SetTotalCombos(count);

    // ── Recording hits ───────────────────────────────────────────────────────────

    /// <summary>
    /// Records a hit or miss from HitController, stores it in history,
    /// and forwards the normalized judgement to ScoreEngine.
    /// </summary>
    public void RegisterHit(HitResult result)
    {
      _hitHistory.Add(result);

      int comboEarned = result.Note.Type == NoteType.Hold ? 2 : 1;
      _engine.RecordJudgement(result.Weight, comboEarned);
    }

    // ── Autoplay passthrough ─────────────────────────────────────────────────────

    /// <summary>Directly sets accumulated weight (Autoplay path).</summary>
    public void SetWeightGained(float weight) => _engine.SetWeightGained(weight);

    /// <summary>Directly sets evaluated combo count (Autoplay path).</summary>
    public void SetComboEvaluated(int combo) => _engine.SetComboEvaluated(combo);

    // ── Score queries (forwarded from engine) ────────────────────────────────────

    public int GetCurrentCombo() => _engine.CurrentCombo;
    public int GetMaxCombo() => _engine.MaxCombo;
    public int GetRealtimeScore() => _engine.RealtimeScore;
    public int GetScorePredict() => _engine.ScorePredict;
    public float GetRealtimeAccuracy() => _engine.RealtimeAccuracy;
    public float GetAccuracyPredict() => _engine.AccuracyPredict;
    public ScoreEngine.CompletionStatus GetStatus() => _engine.GetStatus();

    // ── Result screen data ───────────────────────────────────────────────────────

    /// <summary>Full ordered list of every hit/miss result this session.</summary>
    public IReadOnlyList<HitResult> HitHistory => _hitHistory;

    // ── Reset ────────────────────────────────────────────────────────────────────

    public void Reset()
    {
      _engine.Reset();
      _hitHistory.Clear();
    }
  }
}