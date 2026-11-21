using System.Runtime.CompilerServices;

namespace VeloxDev.WinForms.Xaml.Controls;

/// <summary>A declarative context menu backed by a native <see cref="System.Windows.Forms.ContextMenuStrip"/>.</summary>
public class ContextMenu : System.Windows.Forms.ContextMenuStrip
{
    /// <summary>Creates an empty context menu.</summary>
    public ContextMenu()
    {
    }
}

/// <summary>A menu item for <see cref="ContextMenu"/> and <see cref="Menu"/>.</summary>
public class MenuItem : System.Windows.Forms.ToolStripMenuItem
{
    /// <summary>Creates an empty menu item.</summary>
    public MenuItem()
    {
    }

    /// <summary>The item text, using the WPF property name.</summary>
    public new string? Text
    {
        get => base.Text;
        set => base.Text = value;
    }

    /// <summary>The item text, using the WPF property name.</summary>
    public string? Header
    {
        get => base.Text;
        set => base.Text = value;
    }

    /// <summary>The nested items, using the WPF collection name.</summary>
    public System.Windows.Forms.ToolStripItemCollection Items => DropDownItems;
}

/// <summary>A menu bar backed by a native <see cref="System.Windows.Forms.MenuStrip"/>.</summary>
public class Menu : System.Windows.Forms.MenuStrip
{
    /// <summary>Creates an empty menu bar.</summary>
    public Menu()
    {
    }
}

/// <summary>A declarative tooltip value; the host shows its <see cref="Content"/> on hover.</summary>
public class ToolTip : System.ComponentModel.Component
{
    /// <summary>Creates an empty tooltip.</summary>
    public ToolTip()
    {
    }

    /// <summary>The text or object shown by the tooltip.</summary>
    public object? Content { get; set; }
}

/// <summary>Attaches declarative tooltips to any WinForms control.</summary>
public static class ToolTipService
{
    /// <summary>The tooltip content attached to a control.</summary>
    public static readonly DependencyProperty ToolTipProperty = DependencyProperty.RegisterAttached(
        "ToolTip",
        typeof(object),
        typeof(ToolTipService),
        new PropertyMetadata(null, OnToolTipChanged));

    private static readonly ConditionalWeakTable<System.Windows.Forms.Control, ToolTipHolder> Holders = new();

    /// <summary>Reads the tooltip content of <paramref name="control"/>.</summary>
    public static object? GetToolTip(System.Windows.Forms.Control control)
        => ToolTipProperty.GetValue(control);

    /// <summary>Writes the tooltip content of <paramref name="control"/>.</summary>
    public static void SetToolTip(System.Windows.Forms.Control control, object? value)
        => ToolTipProperty.SetValue(control, value);

    private static void OnToolTipChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (!Holders.TryGetValue(target, out var holder))
        {
            holder = new ToolTipHolder();
            Holders.Add(target, holder);
            target.Disposed += (_, _) => holder.Tip.Dispose();
        }

        holder.Tip.SetToolTip(target, ResolveText(e.NewValue));
    }

    private static string ResolveText(object? value)
        => value switch
        {
            null => string.Empty,
            ToolTip tooltip => tooltip.Content?.ToString() ?? string.Empty,
            _ => value.ToString() ?? string.Empty,
        };

    private sealed class ToolTipHolder
    {
        public ToolTipHolder()
        {
            Tip = new System.Windows.Forms.ToolTip
            {
                ShowAlways = true,
                InitialDelay = 350,
                ReshowDelay = 100,
            };
        }

        public System.Windows.Forms.ToolTip Tip { get; }
    }
}
