using Avalonia_StyleGraph.ViewModels.Workflow;
using VeloxDev.Core.MVVM;

namespace Avalonia_StyleGraph.ViewModels;

public partial class MainViewModel
{
    [VeloxProperty] private StyleGraphViewModel styleGraphViewModel = new();
}
