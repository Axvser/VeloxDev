using Demo.ViewModels;
using Jalium.UI;
using VeloxDev.WorkflowSystem;

namespace Demo.Views.Workflow;

// 端口在卡片上的摆位 —— 设计坐标（缩放前）下的那一处定义。
// 本平台把端口画在表面上（NodeEditorSurface），不画在卡里，所以这里的常量有两个读者：表面按它算端口的画布坐标
// （连线端点与命中测试都从这里来），表面同时按它写字形与端口名。一个来源，两边就不可能对不齐。
// 摆位规则：Controller / Timer 各只有一个不带名字的口，中心落在卡边中点（外溢一半骑在卡边）；Python / Enum 用
// SlotEnumerator，口按行排（一行一个，行首/行尾带名字），行从标题行下面起（Python 还要让开卡顶那条描述带）。
// 于是「有没有名字」在这里就等价于「按行排还是居中」。
internal static class NodePorts
{
    // 标题行高度（设计单位）。端口行从它下面起。
    public const double TitleBarH = CardPalette.HeaderHeight;

    // 端口行的行距。24 的口 + 上下各 4 的呼吸。
    public const double RowH = 32;

    // 行排到底时给卡底留的一口气。
    public const double BottomGap = 6;

    // 输入口的横坐标就是卡左边线：口有一半骑在卡外。
    public const double InputPortX = 0;

    // 输出口离卡右边线的距离，同上为 0。
    public const double OutputInset = 0;

    // Controller / Timer 那颗口的方框边长（设计单位）。
    public const double PortBoxLarge = 32;

    // Python / Enum 行内那口小一号的方框边长。
    public const double PortBoxSmall = 24;

    // 端口名离卡边多远。两边同值，左右两条口因此读起来是对称的。
    public const double SlotNameInset = 14;

    // Python 卡顶部那条描述带的固定高度。端口行要让它开，卡片自己也按同一个常量给这条带留高。
    public const double DescriptorHeight = 38;

    // Enum 卡主体里那段固定表头（两个下拉 + 三行小写标签）的高度，端口行排在这段之下。
    // 表头高度不能由内容量出来 —— 端口行的位置要由表面事先算好（它手上没有卡片的布局树可问），所以表头在卡片里
    // 用固定像素行排，高度就是下面这个数，卡片与端口两边共用。
    // 各段（含主体上边距 10）：10 / 标签 13 / 间隔 6 / 下拉 26 / 组间 14 / 标签 13 / 间隔 6 / 下拉 26 / 组间 14 / 标签 13 / 间隔 6。
    public const double EnumBodyTop =
        BodyPaddingTop + LabelRowHeight + RowSpacing + FieldRowHeight + GroupSpacing
        + LabelRowHeight + RowSpacing + FieldRowHeight + GroupSpacing
        + LabelRowHeight + RowSpacing;

    // 主体左右两条端口的名字那两条通道的宽度（Python 卡是三列布局的边列）。
    public const double PortColumnWidth = 64;

    // 小写标签行的高度、以及表头里各段的间距 —— 卡片与这里的表头常量共用的那几个数。
    public const double LabelRowHeight = 13;
    public const double FieldRowHeight = 26;
    public const double RowSpacing = 6;
    public const double GroupSpacing = 14;
    public const double BodyPaddingTop = 10;

    // 输入口，带显示名（单口节点为空串）。
    public static IReadOnlyList<(IWorkflowSlotViewModel Slot, string Name)> Inputs(IWorkflowNodeViewModel node)
    {
        if (node is PythonScriptNodeViewModel python)
        {
            return python.InputSlots.Items.Select(i => ((IWorkflowSlotViewModel)i.Slot, i.Name)).ToList();
        }

        var single = SingleSlot(node, "InputSlot");
        return single is { } s ? [(s, string.Empty)] : [];
    }

    // 输出口，带显示名（单口节点为空串）。
    public static IReadOnlyList<(IWorkflowSlotViewModel Slot, string Name)> Outputs(IWorkflowNodeViewModel node)
    {
        switch (node)
        {
            case PythonScriptNodeViewModel python:
                return python.OutputSlots.Items.Select(i => ((IWorkflowSlotViewModel)i.Slot, i.Name)).ToList();
            case EnumSelectorNodeViewModel e:
                return e.OutputSlots.Items.Select(i => ((IWorkflowSlotViewModel)i.Slot, i.Name)).ToList();
        }

        var single = SingleSlot(node, "OutputSlot");
        return single is { } s ? [(s, string.Empty)] : [];
    }

    // 节点显示名：Common/Lib 的视图模型发布的 Title（Timer / PythonScript / EnumSelector 都有），不发布的就是空串 ——
    // 控制器视图模型是其中一个，所以它的卡片自己给标题，而不是读这里。
    public static string TitleOf(IWorkflowNodeViewModel node)
        => node.GetType().GetProperty("Title")?.GetValue(node)?.ToString() ?? string.Empty;

    // 这个节点的口是按行排的（带名字的 SlotEnumerator），还是卡边中点一个口。
    public static bool UsesPortRows(IWorkflowNodeViewModel node)
        => node is PythonScriptNodeViewModel or EnumSelectorNodeViewModel;

    // 这颗口的方框边长（设计单位）：行内的小口 24，卡边中点的大口 32。
    public static double PortBoxSize(IWorkflowNodeViewModel node)
        => UsesPortRows(node) ? PortBoxSmall : PortBoxLarge;

    // 第一行端口的上沿（设计单位）：每张卡的端口行都从自己那段表头下面起。
    public static double RowsTop(IWorkflowNodeViewModel node)
        => TitleBarH + (node switch
        {
            PythonScriptNodeViewModel => DescriptorHeight,
            EnumSelectorNodeViewModel => EnumBodyTop,
            _ => 0,
        });

    // 行距：装得下就用固定 RowH，装不下就压到可用高度里均分，尽量不把行推到卡外（尽力而为的装箱，端口比行名重要）。
    public static double RowPitchFor(int count, IWorkflowNodeViewModel node, double designHeight)
    {
        if (count <= 0)
        {
            return RowH;
        }

        double usable = System.Math.Max(0, designHeight - RowsTop(node) - BottomGap);
        return count * RowH <= usable ? RowH : System.Math.Max(4, usable / count);
    }

    // 输入口中心（设计坐标）。
    public static Point InputCenterLocalDesign(IWorkflowNodeViewModel node, int i, double designHeight)
    {
        if (node is EnumSelectorNodeViewModel)
        {
            // Enum 的输入口居中在主体上（不含标题行），与设计稿一致
            return new Point(InputPortX, TitleBarH + ((designHeight - TitleBarH) / 2));
        }

        if (!UsesPortRows(node))
        {
            return new Point(InputPortX, designHeight / 2);
        }

        double pitch = RowPitchFor(Inputs(node).Count, node, designHeight);
        return new Point(InputPortX, RowsTop(node) + (pitch * i) + (pitch / 2));
    }

    // 输出口中心（设计坐标）。
    public static Point OutputCenterLocalDesign(IWorkflowNodeViewModel node, int i, double designWidth, double designHeight)
    {
        if (!UsesPortRows(node))
        {
            return new Point(designWidth - OutputInset, designHeight / 2);
        }

        double pitch = RowPitchFor(Outputs(node).Count, node, designHeight);
        return new Point(designWidth - OutputInset, RowsTop(node) + (pitch * i) + (pitch / 2));
    }

    // 找出一个插槽是它节点的输入还是输出，以及在那一串里的序号。
    public static (bool IsInput, int Index)? IndexOf(IWorkflowNodeViewModel node, IWorkflowSlotViewModel slot)
    {
        var inputs = Inputs(node);
        for (int i = 0; i < inputs.Count; i++)
        {
            if (ReferenceEquals(inputs[i].Slot, slot))
            {
                return (true, i);
            }
        }

        var outputs = Outputs(node);
        for (int i = 0; i < outputs.Count; i++)
        {
            if (ReferenceEquals(outputs[i].Slot, slot))
            {
                return (false, i);
            }
        }

        return null;
    }

    private static IWorkflowSlotViewModel? SingleSlot(IWorkflowNodeViewModel node, string propertyName)
        => node.GetType().GetProperty(propertyName)?.GetValue(node) as IWorkflowSlotViewModel;
}
