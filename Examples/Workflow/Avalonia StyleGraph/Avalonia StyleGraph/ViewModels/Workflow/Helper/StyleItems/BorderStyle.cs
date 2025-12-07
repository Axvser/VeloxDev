using Avalonia.Controls;
using VeloxDev.Avalonia.PlatformAdapters;

namespace Avalonia_StyleGraph.ViewModels.Workflow.Helper.StyleItems
{
    public class BorderStyle
    {
        public bool PointerHoverd { get; set; } = false;

        public Transition<Border>.StateSnapshot Transition { get; set; } =
            Transition<Border>.Create()
            .Effect(TransitionEffects.Hover);
    }
}
