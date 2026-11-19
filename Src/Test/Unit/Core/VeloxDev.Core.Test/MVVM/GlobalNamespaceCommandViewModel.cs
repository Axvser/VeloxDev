using VeloxDev.MVVM;

// 故意不写命名空间。全局命名空间的 ToDisplayString() 返回 "<global namespace>" —— 那个尖括号是非法的
// 文件名与标识符字符，拼进 hintName 会让生成器整个抛 ArgumentException，宿主只报一句
// 「生成器 Command 未能生成源」（CS8785），跟命名空间毫不相干，极难查。
// 这个类存在的唯一目的就是让那种情况重新出现时构建立刻失败。
public partial class GlobalNamespaceCommandViewModel
{
    internal bool Ran { get; private set; }

    [VeloxCommand]
    private Task RunAsync()
    {
        Ran = true;
        return Task.CompletedTask;
    }
}
