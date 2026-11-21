// VeloxDev customization: the glyph lives in XAML; the state colours are assigned here.
using System.ComponentModel;
using System.Drawing;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace TemplateNamespace;

public sealed partial class TemplateClass : VeloxDev.WinForms.Xaml.Controls.UserControl
{
    private ISlotComponent? slot;
    private INotifyPropertyChanged? notifier;

    public TemplateClass()
    {
        InitializeComponent();
        UpdateGlyph();
    }

    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public ISlotComponent? ViewModel
    {
        get => slot;
        set
        {
            if (ReferenceEquals(slot, value))
            {
                return;
            }

            if (notifier is not null)
            {
                notifier.PropertyChanged -= OnSlotPropertyChanged;
            }

            slot = value;
            Tag = value;
            notifier = value;
            if (notifier is not null)
            {
                notifier.PropertyChanged += OnSlotPropertyChanged;
            }

            UpdateGlyph();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && notifier is not null)
        {
            notifier.PropertyChanged -= OnSlotPropertyChanged;
        }

        base.Dispose(disposing);
    }

    private void OnSlotPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ISlotComponent.State) or null)
        {
            UpdateGlyph();
        }
    }

    private void UpdateGlyph()
    {
        RootPath.Fill = (slot?.State ?? SlotState.StandBy) switch
        {
            var state when state.HasFlag(SlotState.Sender) && state.HasFlag(SlotState.Receiver) => Color.Violet,
            var state when state.HasFlag(SlotState.Sender) => Color.Tomato,
            var state when state.HasFlag(SlotState.Receiver) => Color.Lime,
            _ => WorkflowSurfaceColors.Parse("TemplateSlotColor"),
        };
    }
}
