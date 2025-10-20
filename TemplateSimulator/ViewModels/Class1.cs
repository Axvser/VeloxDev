using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VeloxDev.Core.MVVM;

namespace TemplateSimulator.ViewModels
{
    internal partial class Class1
    {
        internal partial class Class2
        {
            [VeloxProperty] private string name = string.Empty;
            [VeloxProperty] private object? param = null;
        }
    }
}
