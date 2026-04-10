using System;

namespace KOM_DUMP_MARCH.KomLib
{
    internal static class Adler32
    {
        private const uint BASE = 0xFFF1;

        public static uint Compute(byte[] data, int offset, int length)
        {
            uint s1 = 1, s2 = 0;
            for (int i = offset; i < offset + length; i++)
            {
                s1 = (s1 + data[i]) % BASE;
                s2 = (s2 + s1) % BASE;
            }
            return (s2 << 16) | s1;
        }

        public static uint Compute(byte[] data) => Compute(data, 0, data.Length);
    }
}
