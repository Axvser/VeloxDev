﻿#if NET

namespace VeloxDev.Core.Generators
{
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
    public class AspectOrientedAttribute : Attribute
    {

    }
}

#endif