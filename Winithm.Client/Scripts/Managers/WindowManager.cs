using Godot;

namespace Winithm.Client.Managers;

public partial class WindowManager : Node
{
  [Export]
  public Control? DesktopEnvironment { get; private set; }
  public override void _Ready()
  {
    DesktopEnvironment = GetNodeOrNull<Control>("DesktopEnvironment");
  }
}
