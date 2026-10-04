// VeloxDev customization: Set BindingContext to your IWorkflowTreeViewModel before the control is loaded.
using VeloxDev.WorkflowSystem;

namespace Demo.Controls;

public partial class TreeView : ContentView
{
    public TreeView()
    {
        InitializeComponent();

        // Keep the canvas-info HUD current on every scroll / viewport change (it reads helper.Viewport,
        // which the surface behavior refreshes; the model events cover scale / visible counts).
        PART_ScrollViewer.Scrolled += (_, _) => InfoOverlay.Update();
        PART_ScrollViewer.SizeChanged += (_, _) => InfoOverlay.Update();

        // The tree is assigned to this control by the page; propagate it explicitly so the HUD's
        // BindingContextChanged fires even if inheritance doesn't reach the nested overlay.
        BindingContextChanged += (_, _) => InfoOverlay.BindingContext = BindingContext;
        SyncLinkInput();
    }

    // VeloxDev customization: 悬停高亮是这本 demo 的。overlay 默认什么都不画，订阅树的输入事件、
    // 把「现在轮到哪条线」交给它，它才照 #FFFFFFFF 画那一条 —— 换色/换画法都在这里改。
    private IWorkflowInputEvents? _linkInput;

    private void SyncLinkInput()
    {
        if (_linkInput is not null)
        {
            _linkInput.Input.PointerEntered -= OnLinkPointerEntered;
            _linkInput.Input.PointerExited -= OnLinkPointerExited;
            _linkInput.Input.KeyDown -= OnLinkKeyDown;
            _linkInput = null;
        }

        if (BindingContext is IWorkflowTreeViewModel tree && tree.GetHelper() is IWorkflowInputEvents events)
        {
            _linkInput = events;
            events.Input.PointerEntered += OnLinkPointerEntered;
            events.Input.PointerExited += OnLinkPointerExited;

            // VeloxDev customization: 删除也是宿主的 —— 路由把这次按键交过来（target 就是那条线）。
            events.Input.KeyDown += OnLinkKeyDown;
        }
    }

    private void OnLinkPointerEntered(object? sender, WorkflowPointerEnteredEventArgs e)
        => PART_LinkLayer.SelectedLink = e.Target as IWorkflowLinkViewModel;

    private void OnLinkPointerExited(object? sender, WorkflowPointerExitedEventArgs e)
        => PART_LinkLayer.SelectedLink = null;

    private static void OnLinkKeyDown(object? sender, WorkflowKeyDownEventArgs e)
    {
        if (e.Key != WorkflowKey.Delete || e.Handle.PreventDefault) return;
        if (e.Target is not IWorkflowLinkViewModel link || !link.DeleteCommand.CanExecute(null)) return;

        link.DeleteCommand.Execute(null);
    }
}
