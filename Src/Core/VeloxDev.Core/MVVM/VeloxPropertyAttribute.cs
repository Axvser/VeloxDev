namespace VeloxDev.Core.MVVM
{
    /// <summary>
    /// <para> Field ➤ Notify Property </para>
    /// <para> Partial Property ➤ Notify Property </para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
    public class VeloxPropertyAttribute : Attribute
    {

    }
}
