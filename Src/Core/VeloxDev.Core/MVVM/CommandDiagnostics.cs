using System;

namespace VeloxDev.MVVM;

// 命令管道的进程级诊断出口。刻意保持非泛型。
//
// VeloxCommand.HandlerException 是静态事件，而泛型类型上的静态成员是**每个闭合类型一份** ——
// 把它搬进泛型管道，订阅者就只会收到自己那个闭合形状的命令上报的异常，其余形状的静默丢失。
// 所以钩子的存储留在这里，泛型管道与兜底命令都经 Report 上报。
internal static class CommandDiagnostics
{
    internal static event Action<Exception>? HandlerException;

    internal static void Report(Exception exception)
    {
        try
        {
            HandlerException?.Invoke(exception);
        }
        catch
        {
            // 诊断钩子自己抛异常绝不能漏出去：Completed 是在 try 内发的，
            // 一旦逃逸就会被 catch (Exception) 抓住，把一次成功的执行误报成 Failed。
        }
    }
}
