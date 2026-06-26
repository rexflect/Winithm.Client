using Godot;
using System;

namespace Winithm.Client.UI;

/// <summary>
/// Pause window UI. Manages Resume, Retry, and Quit actions.
/// </summary>
public partial class PauseWindow : Window
{
  public const string WINDOW_ID = "PauseWindow";

  public Action? OnResume { get; set; }
  public Action? OnRetry { get; set; }
  public Action? OnQuit { get; set; }

  private Button? _resumeButton;
  private Button? _retryButton;
  private Button? _quitButton;

  public override void _Ready()
  {
    _resumeButton = GetNodeOrNull<Button>("Panel/VBoxContainer/ResumeButton");
    _retryButton = GetNodeOrNull<Button>("Panel/VBoxContainer/RetryButton");
    _quitButton = GetNodeOrNull<Button>("Panel/VBoxContainer/QuitButton");

    _resumeButton?.Pressed += () => OnResume?.Invoke();
    _retryButton?.Pressed += () => OnRetry?.Invoke();
    _quitButton?.Pressed += () => OnQuit?.Invoke();

    CloseRequested += () => OnResume?.Invoke();
  }
}
