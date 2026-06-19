using Godot;

namespace Winithm.Client.Behaviors.Gameplay;

public enum GameplayAspectMode { Ratio16_9, Expand }

public partial class PlayerWrapper : Control
{
  [Export] public GameplayAspectMode AspectMode { get; set; } = GameplayAspectMode.Ratio16_9;

  private Control? _gameArea;
  private const float BaseWidth = 1280f;
  private const float BaseHeight = 720f;
  private const float AspectRatio = BaseWidth / BaseHeight;

  public override void _Ready()
  {
    _gameArea = GetNodeOrNull<Control>("PlayerArea");
    Resized += ApplyAspectMode;
    ApplyAspectMode();
  }

  public void SetAspectMode(GameplayAspectMode mode)
  {
    AspectMode = mode;
    ApplyAspectMode();
  }

  public void ToggleAspectMode()
  {
    SetAspectMode(AspectMode == GameplayAspectMode.Expand
        ? GameplayAspectMode.Ratio16_9
        : GameplayAspectMode.Expand);
  }

  private void ApplyAspectMode()
  {
    if (!IsInsideTree()) {
      GD.PushError("[PlayerWrapper] Not inside tree");
      return;
    }

    var containerSize = Size;

    switch (AspectMode)
    {
      case GameplayAspectMode.Ratio16_9:
        float targetW = containerSize.Y * AspectRatio;
        float targetH = containerSize.Y;

        if (targetW > containerSize.X)
        {
          targetW = containerSize.X;
          targetH = containerSize.X / AspectRatio;
        }

        _gameArea?.SetDeferred("size", new Vector2(targetW, targetH));
        _gameArea?.SetDeferred("position", (containerSize - new Vector2(targetW, targetH)) * 0.5f);
        break;

      case GameplayAspectMode.Expand:
        _gameArea?.SetDeferred("position", Vector2.Zero);
        _gameArea?.SetDeferred("size", containerSize);
        break;
    }

    _gameArea?.QueueRedraw();
  }
}
