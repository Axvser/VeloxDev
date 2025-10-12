using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Input;
using System.Collections.Generic;
using VeloxDev.Core.Mono;

namespace Demo.Views;

/* 实现一个60FPS的输入检测系统，每一帧检测是否触发了快捷方式 */

[MonoBehaviour(60)]
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        _manager = new WindowNotificationManager(this) { MaxItems = 3 };
        Loaded += (s, e) =>
        {
            KeyDown += User_KeyDown;
            KeyUp += User_KeyUp;
            CanMonoBehaviour = true;
        };
    }

    private readonly WindowNotificationManager _manager;
    private readonly HashSet<Key> _pressedKeys = [];
    private readonly HashSet<KeyModifiers> _pressedModifiers = [];

    partial void Update()
    {
        if (_pressedKeys.Contains(Key.H) && _pressedModifiers.Contains(KeyModifiers.Control))
        {
            _manager.Show(new Notification("Message", $"[ Ctrl + H ] has been invoked"));
            _pressedKeys.Remove(Key.H);
            _pressedModifiers.Remove(KeyModifiers.Control);
        }
    }

    private void User_KeyDown(object? sender, KeyEventArgs e)
    {
        _pressedKeys.Add(e.Key);
        _pressedModifiers.Add(e.KeyModifiers);
    }

    private void User_KeyUp(object? sender, KeyEventArgs e)
    {
        _pressedKeys.Remove(e.Key);
        _pressedModifiers.Remove(e.KeyModifiers);
    }
}