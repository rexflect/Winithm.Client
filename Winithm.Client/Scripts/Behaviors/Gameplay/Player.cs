using Godot;
using Winithm.Core.Common;
using Winithm.Core.Controllers;
using Winithm.Core.Data;
using Winithm.Core.Logic;
using Winithm.Core.Managers;
using Winithm.Client.Controllers.Gameplay;

namespace Winithm.Client.Behaviors.Gameplay;

/// <summary>
/// Main gameplay orchestrator. Creates and wires all core controllers,
/// drives the game loop, and routes input to HitController.
/// </summary>
public partial class Player : Control
{
  // ── Exports ─────────────────────────────────────────────────────────────────

  [Export] public bool Autoplay = false;
  [Export] public float NoteSize = 1f;
  [Export] public float NoteSpeed = 1f;
  [Export] public bool NoteHighLightSimulation = false;

  // ── Scene nodes ──────────────────────────────────────────────────────────────

  private Node? _controllerRack;
  private Control? _objectsLayer;
  private Control? _hitFXLayer;
  private Label? _debug;

  // ── Core controllers ─────────────────────────────────────────────────────────

  private AudioController? _audioController;
  private ComponentController? _componentController;
  private NoteController? _noteController;
  private WindowController? _windowController;
  private HitResponseController? _hitResponseController;
  private GroupController? _groupController;
  private ThemeChannelController? _themeController;

  // ── Client controllers ───────────────────────────────────────────────────────

  private HitController? _hitController;
  private InputController? _inputController;
  private ScoreTracker? _scoreTracker;

  // ── Data ─────────────────────────────────────────────────────────────────────

  private ChartData? _chartData;

  // ── Misc ─────────────────────────────────────────────────────────────────────

  public static readonly string LEVEL_DIR = "res://Winithm.Assets/Levels";

  // ── Godot lifecycle ──────────────────────────────────────────────────────────

  public override void _Ready()
  {
    _objectsLayer = GetNodeOrNull<Control>("ObjectsLayer");
    _hitFXLayer = GetNodeOrNull<Control>("HitFXLayer");
    _controllerRack = GetNodeOrNull<Node>("ControllerRack");
    _componentController = GetNodeOrNull<ComponentController>("GameplayUI");
    _debug = GetNodeOrNull<Label>("Debug");

    SetAutoPlay(true);
    SetNoteSize(1.5f);
    SetNoteSpeed(10f);
    SetNoteHighLightSimulation(true);

    InitializeControllers();
    LoadDemoLevel();
  }

  public override void _Process(double delta)
  {
    if (_audioController is null || _audioController.Metronome is null)
    {
      GD.PushWarning("[Player] _audioController or _audioController.Metronome is not initialized!");
      return;
    }

    // Decrement pause cooldown.
    if (_pauseCooldown > 0)
      _pauseCooldown -= (float)delta;

    TickClock(delta);

    _inputController?.IsInputEnabled = !Autoplay && _pausePhase == PausePhase.Idle && _audioController.IsPlaying;

    // ── Per-frame gameplay updates ────────────────────────────────────────────

    // CurrentBeat is nullable — skip frame if metronome not yet ready.
    if (_audioController.CurrentBeat is not { } currentBeat)
    {
      GD.PushWarning("[Player] _audioController.Metronome is not ready!");
      return;
    }

    _debug?.Text =
      $"Beat: {currentBeat:F2}\n"
      + $"FPS: {Engine.GetFramesPerSecond()} | Frame: {delta * 1000:F2}ms | Vsync: {(DisplayServer.WindowGetVsyncMode() == DisplayServer.VSyncMode.Enabled ? "On" : "Off")}";


    var displaySize = DisplayServer.WindowGetSize();

    _windowController?.ScreenSize = displaySize;
    _windowController?.PlayerAreaSize = Size;
    _windowController?.Update(currentBeat);

    _noteController?.Update(currentBeat);
    _noteController?.SetNoteHighlightSimulation(NoteHighLightSimulation);

    double length = _audioController.LevelLength;

    UpdateScore(currentBeat);
    _componentController?.SongProgressPercent =
      length > 0 ? (float)(_audioController.CurrentTime / length) : 0f;
    _componentController?.ScreenSize = displaySize;
    _componentController?.Update(currentBeat);
  }

  public override void _UnhandledInput(InputEvent @event)
  {
    if (_hitController is null || _audioController is null) return;
    if (@event is not InputEventKey keyEvent) return;
    if (keyEvent.Echo) return;

    if (@event.IsAction("PauseKey"))
    {
      HandlePauseInput();
      return;
    }
  }

  // ── Key release routing ──────────────────────────────────────────────────────



  // ── Score update ─────────────────────────────────────────────────────────────

  private void UpdateScore(double currentBeat)
  {
    if (_scoreTracker is null || !IsInstanceValid(_noteController) || !IsInstanceValid(_windowController))
    {
      GD.PushWarning("[Player] _scoreTracker or _noteController or _windowController is not initialized!");
      return;
    }

    if (Autoplay)
    {
      int passed =
        _noteController.GetTotalComboPassedInActivingWindows(currentBeat)
        + _windowController.GetTotalComboPassedInDestroyedWindows(currentBeat);

      _scoreTracker.SetWeightGained(passed);
      _scoreTracker.SetComboEvaluated(passed);

      _componentController?.SetCombo(passed);
      _componentController?.SetScore(_scoreTracker.GetRealtimeScore());
      _componentController?.SetAccuracy(_scoreTracker.GetRealtimeAccuracy());
      _componentController?.SetStatus(ScoreEngine.CompletionStatus.AT);
    }
    else
    {
      _componentController?.SetCombo(_scoreTracker.GetCurrentCombo());
      _componentController?.SetScore(_scoreTracker.GetRealtimeScore());
      _componentController?.SetAccuracy(_scoreTracker.GetRealtimeAccuracy());
      _componentController?.SetStatus(_scoreTracker.GetStatus());
    }
  }

  // ── Controller initialisation ────────────────────────────────────────────────

  private void InitializeControllers()
  {
    _audioController = new AudioController() { Name = "AudioController" };
    _controllerRack?.AddChild(_audioController);

    _groupController = new GroupController() { Name = "GroupController" };
    _controllerRack?.AddChild(_groupController);

    _themeController = new ThemeChannelController() { Name = "ThemeChannelController" };
    _controllerRack?.AddChild(_themeController);

    _noteController = new NoteController() { Name = "NoteController" };
    _controllerRack?.AddChild(_noteController);

    _hitResponseController = new HitResponseController() { Name = "HitResponseController" };
    _controllerRack?.AddChild(_hitResponseController);

    _windowController = new WindowController() { Name = "WindowController" };
    _controllerRack?.AddChild(_windowController);

    _hitController = new HitController() { Name = "HitController" };
    _controllerRack?.AddChild(_hitController);

    _scoreTracker = new ScoreTracker();

    _inputController = new InputController() { Name = "InputController" };
    _controllerRack?.AddChild(_inputController);

    WireGameplayEvents();
  }

  private void WireGameplayEvents()
  {
    if (!IsInstanceValid(_hitController) || !IsInstanceValid(_inputController))
    {
      GD.PushError("[Player] _hitController or _inputController is not initialized!");
      return;
    }
    ;

    // ── Wire Input to HitController using Method Groups ───────────────────────
    // Routing validated hardware events directly to the evaluator.
    _inputController.OnFocusKeyPressed += _hitController.OnFocusKeyPressed;
    _inputController.OnCloseKeyPressed += _hitController.OnCloseKeyPressed;
    _inputController.OnNormalKeyPressed += _hitController.OnNormalKeyPressed;
    _inputController.OnKeyReleased += _hitController.OnKeyReleased;

    _inputController.IsInputEnabled = true;

    if (_scoreTracker is not null)
    {
      _hitController.OnHit += (_, result) => _scoreTracker.RegisterHit(result);
      _hitController.OnMiss += (_, result) => _scoreTracker.RegisterHit(result);
    }
    else
      GD.PushWarning("[Player] _scoreTracker is not initialized!");

    if (IsInstanceValid(_noteController))
    {
      _noteController.OnAutoHit += _hitController.HandleAutoHit;

      _noteController.OnDragReady += _hitController.HandleDragReady;

      _noteController.OnActiveHoldTick += _hitController.HandleActiveHoldTick;
      _noteController.OnActiveHoldEnded += _hitController.HandleActiveHoldEnded;

      _noteController.OnNoteMiss += _hitController.HandleNoteMiss;
    }
    else
      GD.PushWarning("[Player] _noteController is not initialized!");

    if (IsInstanceValid(_hitResponseController))
      _hitController.OnHitResponseRequested += _hitResponseController.RequestHitResponse;
    else
      GD.PushWarning("[Player] _hitResponseController is not initialized!");
  }

  // ── Level loading ─────────────────────────────────────────────────────────────

  private async void LoadDemoLevel() => LoadLevel("frizka.allMyFellas", "info");

  public async void LoadLevel(string songID, string chartID)
  {
    _chartData = WinithmIO.LoadLevel(LEVEL_DIR, songID, chartID);
    if (_chartData is null)
    {
      GD.PushError("[Player] Failed to load level data.");
      return;
    }

    var metronome = _chartData.SongMetaData.Audio.Metronome;
    _audioController?.Initialize(metronome);

    if (IsInstanceValid(_chartData.SongMetaData.Audio.SongStream))
      _audioController?.SetStream(_chartData.SongMetaData.Audio.SongStream);

    _groupController?.Initialize(_chartData.Groups);
    _themeController?.Initialize(_chartData.ThemeChannels);

    _noteController?.Initialize(metronome, _chartData.Windows, Autoplay);
    _noteController?.PlayerNoteSize = NoteSize;
    _noteController?.PlayerNoteSpeed = NoteSpeed;

    if (IsInstanceValid(_hitFXLayer) && IsInstanceValid(_noteController))
    {
      _hitResponseController?.Initialize(_hitFXLayer, _noteController);
      foreach (var pack in ResourcePackManager.Instance.GetAllResourcePacks() ?? [])
        _hitResponseController?.Prewarm(pack);
    }
    else
      GD.PushError("[Player] Failed to initialize HitResponseController.");


    if (IsInstanceValid(_objectsLayer)
        && IsInstanceValid(_groupController)
        && IsInstanceValid(_themeController)
        && IsInstanceValid(_noteController)
    )
    {
      _windowController?.Initialize(
        _objectsLayer, _chartData.Windows, metronome,
        _groupController, _themeController, _noteController
      );
    }
    else
      GD.PushError("[Player] Failed to initialize WindowController.");

    _componentController?.Initialize(
      _chartData.Components, metronome,
      _chartData.SongMetaData, _chartData.ChartMetadata
    );

    if (IsInstanceValid(_audioController) && IsInstanceValid(_noteController) && IsInstanceValid(_windowController))
      _hitController?.Initialize(_audioController, _noteController, _windowController);
    else
      GD.PushError("[Player] Failed to initialize HitController.");

    _scoreTracker?.SetTotalCombos(_chartData.Windows.TotalComboCount);

    _componentController?.SetAccuracy(1f);
    _componentController?.SetScore(0);
    _componentController?.SetCombo(0);
    _componentController?.SetStatus(Autoplay ? ScoreEngine.CompletionStatus.AT : ScoreEngine.CompletionStatus.AP);

    // Wait for Godot splash and renderer to settle before starting playback.
    await ToSignal(GetTree().CreateTimer(2.0), SceneTreeTimer.SignalName.Timeout);

    _audioController?.Resume();
  }

  // ── Setters ──────────────────────────────────────────────────────────────────

  public void SetAutoPlay(bool active) => Autoplay = active;
  public void SetNoteSize(float size) => NoteSize = size;
  public void SetNoteSpeed(float speed) => NoteSpeed = speed;
  public void SetNoteHighLightSimulation(bool active) => NoteHighLightSimulation = active;

}