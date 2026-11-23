// VeloxDev customization: the Controller card's typography, accent and code-behind editor policy.
using System.ComponentModel;
using Demo.ViewModels;
using VeloxDev.WinForms.Xaml;
using VeloxDev.WinForms.Xaml.Visuals;

namespace Demo.Views.Workflow;

/// <summary>The Controller node card: compile / run / stop / close plus the seed payload editor.</summary>
public sealed partial class ControllerView : VeloxDev.WinForms.Xaml.Visuals.Controls.UserControl
{
    private bool _syncingSeed;
    private ControllerViewModel? _bound;

    /// <summary>Creates the card and runs the generated initializer.</summary>
    public ControllerView()
    {
        InitializeComponent();
        ApplyTheme();
        seedBox.PropertyChanged += OnSeedPropertyChanged;
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (!ReferenceEquals(e.Property, DataContextProperty))
        {
            return;
        }

        if (_bound is not null)
        {
            _bound.PropertyChanged -= OnModelChanged;
        }

        _bound = DataContext as ControllerViewModel;
        if (_bound is not null)
        {
            _bound.PropertyChanged += OnModelChanged;
        }

        LoadSeed();
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ControllerViewModel.SeedPayload))
        {
            LoadSeed();
        }
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing && _bound is not null)
        {
            _bound.PropertyChanged -= OnModelChanged;
            _bound = null;
        }

        base.Dispose(disposing);
    }

    private void ApplyTheme()
    {
        accentBar.Background = new SolidColorBrush(CardTheme.AccentController);
        CardStyle.Title(titleText);
        CardStyle.Label(seedLabel);
        CardStyle.Field(seedBox);
        CardStyle.GhostButton(compileButton);
        CardStyle.GhostButton(runButton);
        CardStyle.GhostButton(stopButton);
        CardStyle.GhostButton(closeButton);
        compileButton.Foreground = CardTheme.ActionCompile;
        runButton.Foreground = CardTheme.ActionRun;
        stopButton.Foreground = CardTheme.ActionStop;
        closeButton.Foreground = CardTheme.ActionClose;
    }

    // The visual TextBox exposes Text as a dependency property; the native IME island is placed through the
    // camera transform and only appears while the field is focused.
    private void LoadSeed()
    {
        if (DataContext is not ControllerViewModel controller)
        {
            return;
        }

        _syncingSeed = true;
        try
        {
            if (seedBox.Text != controller.SeedPayload)
            {
                seedBox.Text = controller.SeedPayload ?? string.Empty;
            }
        }
        finally
        {
            _syncingSeed = false;
        }
    }

    private void OnSeedPropertyChanged(object? sender, DependencyPropertyChangedEventArgs e)
    {
        if (!ReferenceEquals(e.Property, VeloxDev.WinForms.Xaml.Visuals.Controls.TextBox.TextProperty)
            || _syncingSeed || DataContext is not ControllerViewModel controller)
        {
            return;
        }

        if (controller.SeedPayload != seedBox.Text)
        {
            controller.SeedPayload = seedBox.Text;
        }
    }
}
