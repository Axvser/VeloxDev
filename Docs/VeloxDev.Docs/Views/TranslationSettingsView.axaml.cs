using Avalonia.Controls;
using Avalonia.Interactivity;
using System;

namespace VeloxDev.Docs.Views;

public partial class TranslationSettingsView : UserControl
{
    public event Action? Applied;
    public event Action? Cancelled;

    public string? ApiKey
    {
        get => ApiKeyBox.Text;
        set => ApiKeyBox.Text = value;
    }

    public string? Endpoint
    {
        get => EndpointBox.Text;
        set => EndpointBox.Text = value;
    }

    public string? Model
    {
        get => ModelBox.Text;
        set => ModelBox.Text = value;
    }

    public TranslationSettingsView()
    {
        InitializeComponent();

        ApiKey = Environment.GetEnvironmentVariable("DASHSCOPE_API_KEY")
              ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY")
              ?? string.Empty;
        Endpoint = Environment.GetEnvironmentVariable("OPENAI_ENDPOINT")
                ?? string.Empty;
        Model = Environment.GetEnvironmentVariable("OPENAI_MODEL")
             ?? string.Empty;

        ApplyButton.Click += OnApply;
        CancelButton.Click += OnCancel;
    }

    private void OnApply(object? sender, RoutedEventArgs e)
    {
        Environment.SetEnvironmentVariable("DASHSCOPE_API_KEY", ApiKey ?? string.Empty);
        Environment.SetEnvironmentVariable("OPENAI_ENDPOINT", Endpoint ?? string.Empty);
        Environment.SetEnvironmentVariable("OPENAI_MODEL", Model ?? string.Empty);
        Applied?.Invoke();
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        Cancelled?.Invoke();
    }
}
