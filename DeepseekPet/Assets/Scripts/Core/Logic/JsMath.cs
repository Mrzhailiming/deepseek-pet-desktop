using System;

namespace DeepseekPet.Core
{
    /// <summary>
    /// 和 JS 逐位一致的几个数学小工具。规则层全程 double（JS 只有 double），取整走这里而不是 Math.Round：
    /// C# 的 Math.Round 默认是银行家舍入，JS 的 Math.round 是 floor(x + 0.5)。
    /// </summary>
    public static class JsMath
    {
        const double Ln2 = 0.69314718055994530942;

        /// <summary>JS 的 Math.round。</summary>
        public static double Round(double x)
        {
            return Math.Floor(x + 0.5);
        }

        /// <summary>
        /// JS 的 Math.log2。netstandard2.1 没有 Math.Log2；ln(x)/ln2 在 2 的整数次幂上会差一个 ulp
        /// （log2(8) 算出 2.9999999999999996），而食物曲线正好在这些点上取整，所以整数次幂单独认出来。
        /// </summary>
        public static double Log2(double x)
        {
            if (x > 0 && !double.IsInfinity(x))
            {
                var exponent = Math.Round(Math.Log(x) / Ln2);
                if (exponent >= -1074 && exponent <= 1023 && Math.Pow(2, exponent) == x) return exponent;
            }
            return Math.Log(x) / Ln2;
        }

        /// <summary>JS 的 Math.imul：32 位整数乘法，只留低 32 位。</summary>
        public static uint Imul(uint a, uint b)
        {
            return unchecked(a * b);
        }

        /// <summary>murmur3 的收尾函数（constants.js 里 pickLineIndex / pickDailyQuests 用的那一段）。</summary>
        public static uint Fmix32(uint x)
        {
            x ^= x >> 16;
            x = Imul(x, 0x85ebca6b);
            x ^= x >> 13;
            x = Imul(x, 0xc2b2ae35);
            x ^= x >> 16;
            return x;
        }

        /// <summary>JS 的 ToUint32：非负整数值的 double 取模 2^32（x >>> 0）。</summary>
        public static uint ToUint32(double x)
        {
            if (double.IsNaN(x) || double.IsInfinity(x)) return 0;
            var t = Math.Truncate(x);
            var m = t % 4294967296.0;
            if (m < 0) m += 4294967296.0;
            return (uint)m;
        }
    }
}
