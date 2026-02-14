using System.ComponentModel;

namespace VeloxDev.Core.MVVM
{
    /// <summary>
    /// Marks and generates a notification property that supports <see cref="INotifyPropertyChanging"/> and <see cref="INotifyPropertyChanged"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This attribute is applicable in the following two scenarios:
    /// </para>
    /// <list type="bullet">
    ///   <item>
    ///     <description>
    ///       <see langword="field"/>
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///       <see langword="partial"/> <see langword="property"/>
    ///     </description>
    ///   </item>
    /// </list>
    /// </remarks>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
    public class VeloxPropertyAttribute : Attribute
    {

    }
}
