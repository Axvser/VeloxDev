using Avalonia.Controls;
using Avalonia.Input;
using Avalonia_StyleGraph.ViewModels.Workflow.Helper.StyleItems;
using Newtonsoft.Json.Linq;
using System;
using System.Threading;
using System.Threading.Tasks;
using VeloxDev.Core.Interfaces.WorkflowSystem;
using VeloxDev.Core.TransitionSystem;
using VeloxDev.Core.WorkflowSystem;

namespace Avalonia_StyleGraph.ViewModels.Workflow.Helper
{
    public class ProcessorHelper : WorkflowHelper.ViewModel.Node
    {
        private HoverProcessorViewModel? _viewModel;

        private BorderStyle hovered_style = new();
        private BorderStyle nohovered_style = new();

        private WeakReference<Border>? host = null;

        public Border? Host
        {
            get
            {
                if (host is null) return null;
                if (host.TryGetTarget(out var value)) return value;
                return null;
            }
            set
            {
                if (host is null)
                {
                    if (value is null) return;
                    value.PointerEntered += PointerEntered;
                    value.PointerExited += PointerExited;
                    host = new WeakReference<Border>(value);
                }
                else
                {
                    host.TryGetTarget(out var oldValue);
                    if (ReferenceEquals(oldValue, value)) return;
                    if (oldValue is not null)
                    {
                        oldValue.PointerEntered -= PointerEntered;
                        oldValue.PointerExited -= PointerExited;
                    }
                    if (value is null) return;
                    value.PointerEntered += PointerEntered;
                    value.PointerExited += PointerExited;
                    host = new WeakReference<Border>(value);
                }
            }
        }

        public override void Initialize(IWorkflowNodeViewModel node)
        {
            base.Initialize(node);
            _viewModel = node as HoverProcessorViewModel;
        }

        public override async Task WorkAsync(object? parameter, CancellationToken ct)
        {
            try
            {
                if (parameter is not BorderStyle style || _viewModel is null)
                    return;

                if (style.PointerHoverd) hovered_style = style;
                else nohovered_style = style;

                var target = Host;
                if (target is not null)
                {
                    if (target.IsPointerOver)
                    {
                        hovered_style.Transition.Execute(target);
                    }
                    else
                    {
                        nohovered_style.Transition.Execute(target);
                    }
                }

                return;
            }
            catch (OperationCanceledException) { return; }
            catch { throw; }
        }

        private void PointerEntered(object? sender, PointerEventArgs e)
        {
            if (host is not null && host.TryGetTarget(out var value))
                hovered_style.Transition.Execute(value);
        }

        private void PointerExited(object? sender, PointerEventArgs e)
        {
            if (host is not null && host.TryGetTarget(out var value))
                nohovered_style.Transition.Execute(value);
        }
    }
}
