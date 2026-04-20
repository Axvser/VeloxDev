// TextEx.cs
using System;
using System.Text;

namespace PartialCraft.CSharp;

public static class TextEx
{
    /// <summary>
    /// 带缩进地添加一行文本
    /// </summary>
    /// <param name="builder"></param>
    /// <param name="depth">缩进深度</param>
    /// <param name="text">文本</param>
    public static void AppendIndentedLine(this StringBuilder builder, int depth, string text) => builder.AppendLine(new string(' ', depth * 4) + text);

    /// <summary>
    /// 获取一个带缩进功能的字符串构建器
    /// </summary>
    /// <param name="baseDepth">基础缩进深度（每级缩进4个空格）</param>
    /// <returns>返回IndentStringBuilder实例</returns>
    public static IndentStringBuilder GetBuilder(int baseDepth = 0)
    {
        return new IndentStringBuilder(baseDepth);
    }

    /// <summary>
    /// 为字符串添加指定深度的缩进
    /// </summary>
    /// <param name="text">要缩进的文本</param>
    /// <param name="depth">缩进深度</param>
    /// <returns>添加缩进后的文本</returns>
    public static string WithIndent(this string text, int depth)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        string indent = new(' ', depth * 4);
        // 处理多行文本
        var lines = text.Split('\n');
        var result = new StringBuilder();

        for (int i = 0; i < lines.Length; i++)
        {
            if (i > 0) result.Append('\n');
            // 不为空行添加缩进
            if (!string.IsNullOrWhiteSpace(lines[i]))
            {
                result.Append(indent);
                result.Append(lines[i].TrimEnd());
            }
        }

        return result.ToString();
    }
}

/// <summary>
/// 带缩进功能的字符串构建器
/// </summary>
public class IndentStringBuilder(int baseDepth = 0) : IDisposable
{
    private readonly StringBuilder _builder = new();

    /// <summary>
    /// 添加一行文本（自动缩进）
    /// </summary>
    public void AppendLine(string? text = null)
    {
        if (text != null)
        {
            string indent = new(' ', baseDepth * 4);
            _builder.Append(indent);
            _builder.AppendLine(text);
        }
        else
        {
            _builder.AppendLine();
        }
    }

    /// <summary>
    /// 添加一行带指定缩进的文本
    /// </summary>
    public void AppendLine(int relativeDepth, string text)
    {
        string indent = new(' ', (baseDepth + relativeDepth) * 4);
        _builder.Append(indent);
        _builder.AppendLine(text);
    }

    /// <summary>
    /// 增加缩进级别
    /// </summary>
    public void PushIndent()
    {
        baseDepth++;
    }

    /// <summary>
    /// 减少缩进级别
    /// </summary>
    public void PopIndent()
    {
        if (baseDepth > 0)
            baseDepth--;
    }

    /// <summary>
    /// 获取构建的字符串
    /// </summary>
    public override string ToString()
    {
        return _builder.ToString();
    }

    public void Dispose()
    {
        // 清理资源（如果有的话）
    }
}