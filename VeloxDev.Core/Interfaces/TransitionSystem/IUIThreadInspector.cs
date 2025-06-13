using System;
using System.Collections.Generic;
using System.Text;

namespace VeloxDev.Core.Interfaces.TransitionSystem
{
    public interface IUIThreadInspector
    {
        public bool IsUIThread();
    }
}
