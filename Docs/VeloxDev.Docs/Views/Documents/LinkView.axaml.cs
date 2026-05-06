using Avalonia.Input;
using System.ComponentModel;
using VeloxDev.Docs.ViewModels;

namespace VeloxDev.Docs;

public partial class LinkView : WikiElementViewBase
{
    private LinkProvider? _provider;
    private bool _isPressed;

    public LinkView()
    {
        InitializeComponent();
        InitializeEditChrome(ChromeBorder, DisplayPanel, EditPanel);
        DataContextChanged += (_, _) => AttachProvider();
        AttachProvider();

        DisplayPanel.PointerPressed += OnDisplayPointerPressed;
        DisplayPanel.PointerReleased += OnDisplayPointerReleased;
        DisplayPanel.PointerCaptureLost += (_, _) => _isPressed = false;
        DisplayPanel.PointerExited += (_, _) => _isPressed = false;
    }

    private void OnDisplayPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(DisplayPanel).Properties.IsLeftButtonPressed)
        {
            _isPressed = true;
            e.Handled = true;
        }
    }

    private void OnDisplayPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isPressed)
            return;

        _isPressed = false;
        if (!DisplayPanel.IsPointerOver || _provider is null)
            return;

        e.Handled = true;
        if (_provider.OpenCommand.CanExecute(this))
            _provider.OpenCommand.Execute(this);
    }

    private void AttachProvider()
    {
        if (_provider is not null)
            _provider.PropertyChanged -= OnProviderChanged;

        _provider = DataContext as LinkProvider;
        if (_provider is not null)
            _provider.PropertyChanged += OnProviderChanged;

        Refresh();
    }

    private void OnProviderChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LinkProvider.Text))
            Refresh();
    }

    private void Refresh() => SetTextViaRun(LinkDisplayText, _provider?.Text);
}
