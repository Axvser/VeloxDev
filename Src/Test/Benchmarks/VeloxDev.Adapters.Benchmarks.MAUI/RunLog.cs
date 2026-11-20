using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using VeloxDev.Adapters.Benchmarks.Headed;
using VeloxDev.Benchmarks;

namespace VeloxDev.Adapters.Benchmarks.MAUI;

// 有头基准的进度出口：WinUI 进程没有控制台，文件才是能事后读的那一份。
internal sealed class RunLog : IDisposable
{
    private readonly StreamWriter _writer;

    private RunLog(string path, StreamWriter writer)
    {
        Path = path;
        _writer = writer;
    }

    internal string Path { get; }

    internal static RunLog Create(HeadedOptions? options, string stem)
    {
        var directory = options?.OutputPath
            ?? BenchmarkArtifacts.For("view-pipeline");
        Directory.CreateDirectory(directory);

        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var path = System.IO.Path.Combine(directory, $"{stem}-{stamp}.log");
        return new RunLog(path, new StreamWriter(path, append: false) { AutoFlush = true });
    }

    internal void Write(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
        Console.WriteLine(line);
        Debug.WriteLine(line);
        _writer.WriteLine(line);
    }

    public void Dispose() => _writer.Dispose();

}
