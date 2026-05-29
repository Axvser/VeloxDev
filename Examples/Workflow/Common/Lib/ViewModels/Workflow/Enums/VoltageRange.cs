using System;
using System.Collections.Generic;
using System.Text;
using VeloxDev.AI;

namespace Demo.ViewModels;

[AgentContext(AgentLanguages.English, "Voltage range enumeration")]
internal enum VoltageRange
{
    Low = -1,
    Zero = 0,
    High = 1
}
