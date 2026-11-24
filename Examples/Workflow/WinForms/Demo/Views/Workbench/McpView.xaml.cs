using System.ComponentModel;
using System.Drawing;
using Demo.ViewModels.Workflow.Helper;
using Easel;
using Easel.Controls;
using Easel.Visuals.Controls;
using Button = Easel.Visuals.Controls.Button;

namespace Demo.Views.Workbench;

/// <summary>
/// The sidebar's MCP page: one line per server, the error detail under a failed one, and a reload action.
/// The Agent helper owns the status; this view only renders it.
/// </summary>
public sealed partial class McpView : WorkbenchViewBase
{
    private AgentHelper? _helper;

    /// <summary>Creates the page from XAML.</summary>
    public McpView()
    {
        InitializeComponent();
    }

    /// <summary>Binds the page to an Agent helper, or unbinds it when the session has none.</summary>
    public void SetHelper(AgentHelper? helper)
    {
        if (ReferenceEquals(_helper, helper))
        {
            return;
        }

        if (_helper is not null)
        {
            _helper.Mcp.Status.PropertyChanged -= OnStatusChanged;
        }

        _helper = helper;
        if (_helper is not null)
        {
            _helper.Mcp.WithSynchronizationContext(SynchronizationContext.Current);
            _helper.Mcp.Status.PropertyChanged += OnStatusChanged;
            Refresh();
            _ = _helper.LoadMcpServersAsync();
        }
        else
        {
            StatusPanel.Children.Clear();
        }
    }

    private void OnStatusChanged(object? sender, PropertyChangedEventArgs e)
        => WorkbenchDispatcher.Post(Refresh);

    private void Refresh()
    {
        if (_helper is not { } helper)
        {
            return;
        }

        var status = helper.Mcp.Status;
        StatusPanel.Children.Clear();
        StatusPanel.Add(new TextBlock
        {
            Text = $"MCP: 存活 {status.ConnectedCount} · 错误 {status.ErrorCount} · 安装/连接 {status.WorkingCount}",
            FontSize = 8.5,
            FontWeight = Easel.Visuals.FontWeight.Bold,
            Foreground = WorkbenchPalette.Success,
        });

        foreach (var server in status.Servers)
        {
            StatusPanel.Add(new TextBlock
            {
                Text = $"{server.Name} — {server.StateText}" + (server.ToolCount > 0 ? $" ({server.ToolCount} tools)" : string.Empty),
                FontSize = 8.5,
                Foreground = server.IsError
                    ? WorkbenchPalette.Danger
                    : server.IsConnected ? WorkbenchPalette.Success : WorkbenchPalette.TextTertiary,
            });

            if (server.IsError && !string.IsNullOrEmpty(server.Error))
            {
                StatusPanel.Add(new TextBlock
                {
                    Text = "    " + server.Error,
                    FontSize = 8,
                    Foreground = WorkbenchPalette.Danger,
                    Wrap = true,
                });
            }
        }

        var reload = new Button
        {
            Text = "重载",
            Width = 56,
            Height = 22,
            Margin = new Thickness(0, 8, 0, 0),
            Background = new SolidColorBrush(WorkbenchPalette.Input),
            Foreground = WorkbenchPalette.TextSecondary,
            BorderBrush = new SolidColorBrush(WorkbenchPalette.Border),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(4, 0, 4, 0),
            FontSize = 8,
            MinHeight = 20,
            CornerRadius = 3,
        };
        reload.Click += async (_, _) =>
        {
            if (_helper is { } current)
            {
                await current.LoadMcpServersAsync();
            }
        };
        StatusPanel.Add(reload);
    }
}
