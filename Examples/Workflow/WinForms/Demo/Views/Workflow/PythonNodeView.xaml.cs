// VeloxDev customization: the Python card's typography, accent and script editor policy.
using System.ComponentModel;
using Demo.ViewModels;
using Easel;
using Easel.Visuals;

namespace Demo.Views.Workflow;

/// <summary>The Python script node card: description, script editor and its dynamic port strips.</summary>
public sealed partial class PythonNodeView : Easel.Visuals.Controls.UserControl
{
    private bool _syncingScript;
    private PythonScriptNodeViewModel? _bound;

    /// <summary>Creates the card and runs the generated initializer.</summary>
    public PythonNodeView()
    {
        InitializeComponent();
        ApplyTheme();
        scriptBox.PropertyChanged += OnScriptPropertyChanged;
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

        _bound = DataContext as PythonScriptNodeViewModel;
        if (_bound is not null)
        {
            _bound.PropertyChanged += OnModelChanged;
        }

        LoadScript();
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PythonScriptNodeViewModel.Script))
        {
            LoadScript();
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
        accentBar.Background = new SolidColorBrush(CardTheme.AccentScript);
        CardStyle.Title(titleText);
        CardStyle.Value(orderText);
        CardStyle.Capsule(statusText);
        CardStyle.Label(descriptionText);
        CardStyle.Value(scriptNameText);
        CardStyle.Value(scriptKindText);
        CardStyle.Mono(scriptBox);
        CardStyle.PortNames(PART_InputSlotsList);
        CardStyle.PortNames(PART_OutputSlotsList);
    }

    private void LoadScript()
    {
        if (DataContext is not PythonScriptNodeViewModel python)
        {
            return;
        }

        _syncingScript = true;
        try
        {
            if (scriptBox.Text != python.Script)
            {
                scriptBox.Text = python.Script ?? string.Empty;
            }
        }
        finally
        {
            _syncingScript = false;
        }
    }

    private void OnScriptPropertyChanged(object? sender, DependencyPropertyChangedEventArgs e)
    {
        if (!ReferenceEquals(e.Property, Easel.Visuals.Controls.TextBox.TextProperty)
            || _syncingScript || DataContext is not PythonScriptNodeViewModel python)
        {
            return;
        }

        if (python.Script != scriptBox.Text)
        {
            python.Script = scriptBox.Text;
        }
    }
}
