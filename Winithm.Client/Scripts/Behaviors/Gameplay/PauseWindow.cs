using Godot;
using System;
using Winithm.Client.Managers;

namespace Winithm.Client.Behaviors.Gameplay;

/// <summary>
/// Pause window UI. Manages Resume, Retry, and Quit actions.
/// </summary>
public partial class PauseWindow : Window
{
  public const string WINDOW_ID = "PauseWindow";

  public WindowDesktopManager? windowDesktopManager => WindowDesktopManager.Instance;

  public Action? OnResume { get; set; }
  public Action? OnRetry { get; set; }
  public Action? OnQuit { get; set; }

  private Button? _resumeButton;
  private Button? _retryButton;
  private Button? _quitButton;

  private ColorRect? _pad1;
  private ColorRect? _pad2;

  public override void _Ready()
  {
    _resumeButton = GetNodeOrNull<Button>("Panel/ResumeBtn");
    _retryButton = GetNodeOrNull<Button>("Panel/RestartBtn");
    _quitButton = GetNodeOrNull<Button>("Panel/QuitBtn");

    _pad1 = GetNodeOrNull<ColorRect>("Panel/Pad1");
    _pad2 = GetNodeOrNull<ColorRect>("Panel/Pad2");

    if (windowDesktopManager?.AccentColor is not null and var accentColor)
    {
      _pad1?.Color = accentColor.Value with { A = 1f };
      _pad2?.Color = accentColor.Value with { A = 1f };
    }

    _resumeButton?.Pressed += () => OnResume?.Invoke();
    _retryButton?.Pressed += () => OnRetry?.Invoke();
    _quitButton?.Pressed += () => OnQuit?.Invoke();

    CloseRequested += () => OnResume?.Invoke();
  }
}