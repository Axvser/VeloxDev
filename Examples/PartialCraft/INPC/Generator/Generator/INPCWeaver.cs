using Microsoft.CodeAnalysis;
using PartialCraft.CSharp;
using PartialCraft.CSharp.CodeWeavers;
using System.Collections.Generic;

namespace Generator;

public class INPCWeaver : ClassEntry
{
    private readonly List<IFieldSymbol> _fieldsToGenerate = [];

    public override bool CanWeave()
    {
        _fieldsToGenerate.Clear();
        if(Symbol is null) return false;

        // 查找需要生成属性的字段（私有且以下划线开头）
        foreach (var member in Symbol.GetMembers().OfType<IFieldSymbol>())
        {
            if (member.DeclaredAccessibility == Accessibility.Private &&
                member.Name.StartsWith("_") &&
                member.Name.Length > 1)
            {
                _fieldsToGenerate.Add(member);
            }
        }

        return _fieldsToGenerate.Count > 0;
    }

    public override string GetFileName()
    {
        return $"{Symbol!.Name}_INPC.g.cs";
    }

    protected override string[] GenerateBaseInterfaces()
        => [
            "System.ComponentModel.INotifyPropertyChanged",
           ];

    protected override string[] GenerateBaseTypes() 
        => [];

    protected override string GenerateBody(int depth)
    {
        using var builder = TextEx.GetBuilder(depth);

        // 生成PropertyChanged事件
        builder.AppendLine("public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;");
        builder.AppendLine();

        // 生成触发事件的方法
        builder.AppendLine("protected virtual void OnPropertyChanged(string propertyName)");
        builder.AppendLine("{");
        builder.PushIndent();
        builder.AppendLine("PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));");
        builder.PopIndent();
        builder.AppendLine("}");
        builder.AppendLine();

        // 为每个字段生成属性
        foreach (var field in _fieldsToGenerate)
        {
            string fieldName = field.Name;
            string propertyName = fieldName.Substring(1);
            propertyName = char.ToUpper(propertyName[0]) + propertyName.Substring(1);

            builder.AppendLine($"public {field.Type.ToDisplayString()} {propertyName}");
            builder.AppendLine("{");
            builder.PushIndent();

            // Getter
            builder.AppendLine($"get => {fieldName};");

            // Setter
            builder.AppendLine("set");
            builder.AppendLine("{");
            builder.PushIndent();

            builder.AppendLine($"if (!System.Collections.Generic.EqualityComparer<{field.Type.ToDisplayString()}>.Default.Equals({fieldName}, value))");
            builder.AppendLine("{");
            builder.PushIndent();

            builder.AppendLine($"{fieldName} = value;");
            builder.AppendLine($"OnPropertyChanged(nameof({propertyName}));");

            builder.PopIndent();
            builder.AppendLine("}");

            builder.PopIndent();
            builder.AppendLine("}");

            builder.PopIndent();
            builder.AppendLine("}");
        }

        return builder.ToString();
    }
}