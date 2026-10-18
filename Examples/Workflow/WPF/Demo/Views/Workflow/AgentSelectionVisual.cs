using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using Demo.Workflow;
using VeloxDev.WorkflowSystem;

namespace Demo.Views.Workflow;

/// <summary>
/// Paints the Agent working set on a node card.
/// </summary>
/// <remarks>
/// A node view calls <see cref="Attach"/> once. The helper follows the view's DataContext — pooled views are
/// re-bound — and <see cref="AgentSelection.Changed"/>, writing an attached flag that <c>CardTheme.xaml</c>
/// turns into a violet border and glow. Nothing is added to the node view model.
/// </remarks>
public static class AgentSelectionVisual
{
    /// <summary>Whether this view's node is in the Agent's working set.</summary>
    public static readonly DependencyProperty IsSelectedProperty =
        DependencyProperty.RegisterAttached(
            "IsSelected", typeof(bool), typeof(AgentSelectionVisual), new PropertyMetadata(false));

    /// <summary>Gets the attached selection flag.</summary>
    public static bool GetIsSelected(DependencyObject element) => (bool)element.GetValue(IsSelectedProperty);

    /// <summary>Sets the attached selection flag.</summary>
    public static void SetIsSelected(DependencyObject element, bool value) => element.SetValue(IsSelectedProperty, value);

    private static readonly ConditionalWeakTable<UserControl, Attachment> Attachments = new();

    /// <summary>Wires one node view to its node's Agent selection. Call from the view's constructor.</summary>
    public static void Attach(UserControl view)
    {
        var attachment = Attachments.GetValue(view, static v => new Attachment(v));
        view.DataContextChanged += attachment.OnDataContextChanged;
        view.Loaded += attachment.OnLoaded;
        view.Unloaded += attachment.OnUnloaded;
        attachment.Bind();
    }

    private sealed class Attachment(UserControl view)
    {
        private IWorkflowNodeViewModel? _node;

        internal void Bind()
        {
            if (view.DataContext is not IWorkflowNodeViewModel node)
            {
                Detach();
                SetIsSelected(view, false);
                return;
            }

            if (!ReferenceEquals(_node, node))
            {
                DetachNode();
                _node = node;
                AgentSelection.Changed += OnSelectionChanged;
            }

            SetIsSelected(view, AgentSelection.IsSelected(node));
        }

        private void Detach()
        {
            DetachNode();
            SetIsSelected(view, false);
        }

        internal void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e) => Bind();

        internal void OnLoaded(object sender, RoutedEventArgs e) => Bind();

        internal void OnUnloaded(object sender, RoutedEventArgs e) => Detach();

        private void DetachNode()
        {
            if (_node is not null)
                AgentSelection.Changed -= OnSelectionChanged;

            _node = null;
        }

        private void OnSelectionChanged(IWorkflowNodeViewModel node)
        {
            if (ReferenceEquals(node, _node))
                SetIsSelected(view, AgentSelection.IsSelected(node));
        }
    }
}
