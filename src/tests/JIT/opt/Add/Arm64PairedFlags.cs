// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Runtime.CompilerServices;
using Xunit;

public static unsafe class Arm64PairedFlags
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void StoreCarry(ulong a, ulong b, ulong x, ulong* r)
    {
        r[0] = x;
        ulong s = a + b;
        r[1] = s < a ? 1UL : 0UL;
        r[2] = s;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void StoreAdc(ulong a, ulong b, ulong h, ulong x, ulong* r)
    {
        r[0] = x;
        ulong s = a + b;
        r[1] = h + (s < a ? 1UL : 0UL);
        r[2] = s;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void StoreCarry32(uint a, uint b, uint x, uint* r)
    {
        r[0] = x;
        uint s = a + b;
        r[1] = s < a ? 1U : 0U;
        r[2] = s;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void StoreBorrow(ulong a, ulong b, ulong x, ulong* r)
    {
        r[0] = x;
        ulong d = a - b;
        r[1] = a < b ? 1UL : 0UL;
        r[2] = d;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ulong LoadCarry(ulong a, ulong b, ulong* p, ulong* r)
    {
        ulong x = p[0], s = a + b;
        ulong y = p[1] + (s < a ? 1UL : 0UL);
        r[0] = s;
        return x ^ y;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ulong KeepCarry(ulong a, ulong b, ulong h, ulong x, ulong y, ulong* r)
    {
        ulong s = a + b;
        r[0] = s;
        h ^= x;
        ulong high = h + (s < a ? 1UL : 0UL);
        ulong u = x + y;
        r[1] = u < x ? 1UL : 0UL;
        r[2] = u;
        return high;
    }

    [Fact]
    public static int TestEntryPoint()
    {
        ulong[] values = { 0, 1, 2, 7, 0x80000000, 0xffffffff, 1UL << 32, 1UL << 63, ulong.MaxValue };
        ulong* r = stackalloc ulong[3];
        ulong* p = stackalloc ulong[2];
        for (int n = 0; n < 100; n++)
        foreach (ulong a in values)
        foreach (ulong b in values)
        {
            ulong x = unchecked(a + (ulong)n), h = unchecked(b - (ulong)n);
            UInt128 sum = (UInt128)a + b;
            ulong c = (ulong)(sum >> 64);
            StoreCarry(a, b, x, r);
            if (r[0] != x || r[1] != c || r[2] != (ulong)sum) throw new Exception("Paired carry store");
            StoreAdc(a, b, h, x, r);
            if (r[0] != x || r[1] != unchecked(h + c) || r[2] != (ulong)sum) throw new Exception("Paired ADC store");
            StoreBorrow(a, b, x, r);
            if (r[0] != x || r[1] != (a < b ? 1UL : 0UL) || r[2] != unchecked(a - b)) throw new Exception("Paired borrow store");
            uint* r32 = (uint*)r;
            uint aa = (uint)a, bb = (uint)b;
            ulong sum32 = (ulong)aa + bb;
            StoreCarry32(aa, bb, (uint)x, r32);
            if (r32[0] != (uint)x || r32[1] != (uint)(sum32 >> 32) || r32[2] != (uint)sum32) throw new Exception("Paired uint carry store");
            p[0] = x; p[1] = h;
            ulong loaded = LoadCarry(a, b, p, r);
            if (loaded != (x ^ unchecked(h + c)) || r[0] != (ulong)sum) throw new Exception("Paired carry load");
            ulong y = values[(n + 3) % values.Length];
            UInt128 otherSum = (UInt128)x + y;
            ulong kept = KeepCarry(a, b, h, x, y, r);
            if (kept != unchecked((h ^ x) + c) || r[0] != (ulong)sum || r[1] != (ulong)(otherSum >> 64) || r[2] != (ulong)otherSum)
                throw new Exception("Paired store used the previous arithmetic flags");
        }
        return 100;
    }
}
