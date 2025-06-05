﻿using VeloxDev.WPF.StructuralDesign.Theme;

namespace VeloxDev.WPF.Theme
{
    /// <summary>
    /// ✨ View >>> Under the dark theme, builds a new value for the specified property
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
    public sealed class Dark(params object?[] param) : Attribute, IThemeAttribute
    {
        public object?[] Parameters { get; set; } = param;
    }
}
