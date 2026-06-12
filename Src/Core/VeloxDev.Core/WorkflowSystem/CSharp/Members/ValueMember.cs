using VeloxDev.MVVM;

namespace VeloxDev.WorkflowSystem.CSharp;

public sealed partial class ValueMember
{
    [VeloxProperty] private string path = string.Empty;
    [VeloxProperty] private string _value = string.Empty;
    [VeloxProperty] private string valueType = string.Empty;
    [VeloxProperty] private string valueName = string.Empty;
    [VeloxProperty] private bool isEnabled;

    internal CSharpObject? Parent { get; set; }

    partial void OnValueChanged(string oldValue, string newValue)
    {
        if (!string.Equals(oldValue, newValue, StringComparison.Ordinal))
        {
            IsEnabled = true;
        }
    }
}
