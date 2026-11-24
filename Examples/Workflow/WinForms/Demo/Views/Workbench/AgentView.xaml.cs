using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows.Forms;
using Demo.ViewModels.Workflow.Helper;
using Demo.Workflow;
using Easel.Visuals;
using VeloxDev.AI.Safety;
using VeloxDev.AI.SubAgents;

namespace Demo.Views.Workbench;

/// <summary>
/// The inspector's AI page: permission mode, the transcript and the prompt box, above the sub-agent roster.
/// The host owns the session; this view pushes the helper's mode and the tree's ask command.
/// </summary>
public sealed partial class AgentView : WorkbenchViewBase
{
    private WorkflowDemoSession? _demo;
    private SubAgentScope? _scope;
    private ObservableCollection<string>? _log;
    private bool _suppressPermission;

    /// <summary>Creates the page from XAML.</summary>
    public AgentView()
    {
        InitializeComponent();
    }

    /// <summary>Binds the page to a session, or unbinds it.</summary>
    public void SetSession(WorkflowDemoSession? demo)
    {
        _demo = demo;
        _suppressPermission = true;
        try
        {
            PermissionPicker.Items.Clear();
            PermissionPicker.SelectedItem = null;
            if (demo?.Tree.GetHelper() is AgentHelper helper)
            {
                foreach (var mode in Enum.GetValues<AgentPermissionMode>())
                {
                    PermissionPicker.Items.Add(mode);
                }

                PermissionPicker.SelectedItem = helper.PermissionMode;
            }
        }
        finally
        {
            _suppressPermission = false;
        }

        if (_log is not null)
        {
            _log.CollectionChanged -= OnLogChanged;
        }

        _log = demo?.Tree.AgentLog;
        if (_log is not null)
        {
            _log.CollectionChanged += OnLogChanged;
        }

        RebuildLog();
        SyncSubAgents();
    }

    private void OnLogChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => WorkbenchDispatcher.Post(RebuildLog);

    private void RebuildLog()
    {
        AgentLog.ItemsSource = _log is null ? Array.Empty<string>() : _log.ToList();
    }

    /// <summary>Rebinds the sub-agent card when the Agent scope appears or is replaced.</summary>
    public void SyncSubAgents()
    {
        var scope = (_demo?.Tree.GetHelper() as AgentHelper)?.SubAgents;
        if (ReferenceEquals(scope, _scope))
        {
            return;
        }

        _scope = scope;
        SubAgents.SetScope(scope);
    }

    private void OnPermissionChanged(object? sender, EventArgs e)
    {
        if (_suppressPermission || PermissionPicker.SelectedItem is not AgentPermissionMode mode)
        {
            return;
        }

        if (_demo?.Tree.GetHelper() is not AgentHelper helper)
        {
            return;
        }

        helper.PermissionMode = mode;
        helper.Scope?.SetPermissionMode(mode);
    }

    private void OnSend(object? sender, EventArgs e)
    {
        var text = AgentInput.Text?.Trim();
        if (string.IsNullOrEmpty(text) || _demo is null)
        {
            return;
        }

        _demo.Tree.AskCommand.Execute(text);
        AgentInput.Text = string.Empty;
    }

    private void OnInputKeyDown(object? sender, VisualKeyEventArgs e)
    {
        if (e.KeyCode != Keys.Enter)
        {
            return;
        }

        OnSend(sender, EventArgs.Empty);
        e.Handled = true;
    }
}
