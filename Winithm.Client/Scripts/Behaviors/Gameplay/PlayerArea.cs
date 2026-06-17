using Godot;

namespace Winithm.Client.Behaviors.Gameplay;

public partial class PlayerArea : Control
{
  private PlayerWrapper? _wrapper;

  public override void _Ready()
  {
    _wrapper = GetParentOrNull<PlayerWrapper>();
  }

  public override void _Draw()
  {
    if (IsInstanceValid(_wrapper) && _wrapper.AspectMode == GameplayAspectMode.Ratio16_9)
    {
      DrawRect(new Rect2(Vector2.Zero, Size), new Color(0, 1, 1, 1f), false, 2.0f, true);
    }
  }
}
