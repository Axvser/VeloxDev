using System.Collections.ObjectModel;
using VeloxDev.Core.MVVM;

namespace Demo.ViewModels.CLI;

public partial class CLViewModel
{
    [VeloxProperty] private ObservableCollection<CLItemViewModel> _items = [];
}
