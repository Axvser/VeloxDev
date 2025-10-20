using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VeloxDev.Core.AOT;
using VeloxDev.Core.AspectOriented;
using VeloxDev.Core.MVVM;

namespace TemplateSimulator.ViewModels
{
    internal partial class Class1
    {
        [AOTReflection]
        internal partial class Class2
        {
            [VeloxProperty][AspectOriented] private string name = string.Empty;
            [VeloxProperty] private object? param = null;

            [VeloxCommand] // 排队执行
            private static Task Work(object? parameter, CancellationToken ct)
            {
                return Task.CompletedTask;
            }

            [VeloxCommand(semaphore: 15)] // 最多15个Task并发，超出的部分自动排队执行
            private static async Task WorkAsync(object? parameter, CancellationToken ct)
            {
                try
                {
                    await Task.Delay(1000, ct); // 外部取消 WorkAsyncCommand 这里的 ct 就会取消
                }
                catch (TaskCanceledException ex)
                {
                    Debug.WriteLine(ex);
                }
            }
        }
    }
}
