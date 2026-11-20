#if NETSTANDARD2_0 || NETFRAMEWORK
// netstandard2.0 / netframework4.6.1 没有 System.HashCode。Core 只把它当内部哈希累加器用，
// 这里补一个等价的小实现，避免为一个类型引入 Microsoft.Bcl.HashCode（它在 net461 上会警告）。
namespace System
{
    internal struct HashCode
    {
        private int _value;

        public void Add<T>(T value)
        {
            unchecked
            {
                _value = (_value * 31) + (value is null ? 0 : value.GetHashCode());
            }
        }

        public int ToHashCode() => _value;

        public static int Combine<T1>(T1 value1)
        {
            var hash = new HashCode();
            hash.Add(value1);
            return hash.ToHashCode();
        }

        public static int Combine<T1, T2>(T1 value1, T2 value2)
        {
            var hash = new HashCode();
            hash.Add(value1);
            hash.Add(value2);
            return hash.ToHashCode();
        }

        public static int Combine<T1, T2, T3>(T1 value1, T2 value2, T3 value3)
        {
            var hash = new HashCode();
            hash.Add(value1);
            hash.Add(value2);
            hash.Add(value3);
            return hash.ToHashCode();
        }

        public static int Combine<T1, T2, T3, T4>(T1 value1, T2 value2, T3 value3, T4 value4)
        {
            var hash = new HashCode();
            hash.Add(value1);
            hash.Add(value2);
            hash.Add(value3);
            hash.Add(value4);
            return hash.ToHashCode();
        }

        public static int Combine<T1, T2, T3, T4, T5>(T1 value1, T2 value2, T3 value3, T4 value4, T5 value5)
        {
            var hash = new HashCode();
            hash.Add(value1);
            hash.Add(value2);
            hash.Add(value3);
            hash.Add(value4);
            hash.Add(value5);
            return hash.ToHashCode();
        }

        public static int Combine<T1, T2, T3, T4, T5, T6>(T1 value1, T2 value2, T3 value3, T4 value4, T5 value5, T6 value6)
        {
            var hash = new HashCode();
            hash.Add(value1);
            hash.Add(value2);
            hash.Add(value3);
            hash.Add(value4);
            hash.Add(value5);
            hash.Add(value6);
            return hash.ToHashCode();
        }

        public static int Combine<T1, T2, T3, T4, T5, T6, T7>(T1 value1, T2 value2, T3 value3, T4 value4, T5 value5, T6 value6, T7 value7)
        {
            var hash = new HashCode();
            hash.Add(value1);
            hash.Add(value2);
            hash.Add(value3);
            hash.Add(value4);
            hash.Add(value5);
            hash.Add(value6);
            hash.Add(value7);
            return hash.ToHashCode();
        }

        public static int Combine<T1, T2, T3, T4, T5, T6, T7, T8>(T1 value1, T2 value2, T3 value3, T4 value4, T5 value5, T6 value6, T7 value7, T8 value8)
        {
            var hash = new HashCode();
            hash.Add(value1);
            hash.Add(value2);
            hash.Add(value3);
            hash.Add(value4);
            hash.Add(value5);
            hash.Add(value6);
            hash.Add(value7);
            hash.Add(value8);
            return hash.ToHashCode();
        }
    }
}
#endif
