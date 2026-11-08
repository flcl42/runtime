// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// Full-adder lowering folds s = a + b; k = s < a; t = s + c; c = k + (t < s) into one
// carry addition that reuses the first addition's operand reads. The operands must
// still hold the same values at the second addition: a redefinition of a or b in
// between (direct, through a load, or through a promoted parent) must keep the
// explicit carry representation; the subtractor form has the same hazard. In-place accumulation (acc += b; acc += c) only
// redefines the first sum itself and must still fold.
using System;
using System.Runtime.CompilerServices;
using Xunit;

public static unsafe class FullAdderOperandRedefinition
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong Add(ulong a, ulong b, ref ulong c)
    {
        ulong s = a + b, k = s < a ? 1UL : 0UL, t = s + c;
        c = k + (t < s ? 1UL : 0UL);
        return t;
    }

    private struct Pair
    {
        public ulong Lo;
        public ulong Hi;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ulong RedefineFirstOperand(ulong a0, ulong b0, ulong a1, ulong b1, ulong w, ulong* r)
    {
        ulong c = 0;
        r[0] = Add(a0, b0, ref c);
        ulong s = a1 + b1;
        ulong k = s < a1 ? 1UL : 0UL;
        a1 ^= w;
        ulong t = s + c;
        c = k + (t < s ? 1UL : 0UL);
        r[1] = t;
        r[2] = a1;
        return c;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ulong RedefineSecondOperandByLoad(ulong a0, ulong b0, ulong a1, ulong b1, ulong a2, ulong b2, ulong* r)
    {
        ulong c = 0;
        r[0] = Add(a0, b0, ref c);
        ulong s = a1 + b1;
        ulong k = s < a1 ? 1UL : 0UL;
        b1 = r[5];
        ulong t = s + c;
        c = k + (t < s ? 1UL : 0UL);
        r[1] = t;
        r[2] = Add(a2, b2, ref c);
        r[3] = b1;
        return c;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static uint RedefineAddend32(uint a0, uint b0, uint a1, uint b1, uint w, uint* r)
    {
        uint s0 = a0 + b0;
        uint c = s0 < a0 ? 1U : 0U;
        uint s1 = a1 + b1;
        uint k = s1 < a1 ? 1U : 0U;
        b1 += w;
        uint t1 = s1 + c;
        c = k + (t1 < s1 ? 1U : 0U);
        r[0] = s0;
        r[1] = t1;
        r[2] = b1;
        return c;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ulong RedefineThroughParent(ulong a0, ulong b0, ulong a1, Pair q, ulong* r)
    {
        ulong c = 0;
        r[0] = Add(a0, b0, ref c);
        ulong s = a1 + q.Lo;
        ulong k = s < a1 ? 1UL : 0UL;
        q = new Pair { Lo = r[4], Hi = r[5] };
        ulong t = s + c;
        c = k + (t < s ? 1UL : 0UL);
        r[1] = t;
        r[2] = q.Lo + q.Hi;
        return c;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ulong AccumulateInPlace(ulong a0, ulong b0, ulong acc, ulong b1, ulong* r)
    {
        ulong c = 0;
        r[0] = Add(a0, b0, ref c);
        acc += b1;
        ulong k = acc < b1 ? 1UL : 0UL;
        acc += c;
        c = k + (acc < c ? 1UL : 0UL);
        r[1] = acc;
        return c;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ulong RedefineSubtrahend(ulong a0, ulong b0, ulong a1, ulong b1, ulong w, ulong* r)
    {
        ulong s0 = a0 - b0;
        ulong c = a0 < b0 ? 1UL : 0UL;
        ulong s = a1 - b1;
        ulong k = a1 < b1 ? 1UL : 0UL;
        b1 ^= w;
        ulong t = s - c;
        c = k + (s < c ? 1UL : 0UL);
        r[0] = s0;
        r[1] = t;
        r[2] = b1;
        return c;
    }

    private static readonly ulong[] Values =
    {
        0, 1, 2, 0x7FFFFFFFFFFFFFFFUL, 0x8000000000000000UL, ulong.MaxValue - 1, ulong.MaxValue, 0x123456789ABCDEF0UL
    };

    [Fact]
    public static int TestEntryPoint()
    {
        int errors = 0;
        ulong* r = stackalloc ulong[8];
        uint* r32 = stackalloc uint[8];
        foreach (ulong a0 in Values)
        foreach (ulong b0 in Values)
        foreach (ulong a1 in Values)
        foreach (ulong b1 in Values)
        {
            ulong w = a0 ^ b1 ^ 0x5555;
            UInt128 low = (UInt128)a0 + b0;
            ulong c0 = (ulong)(low >> 64);
            UInt128 high = (UInt128)a1 + b1 + c0;
            ulong t1 = (ulong)high;
            ulong c1 = (ulong)(high >> 64);

            ulong got = RedefineFirstOperand(a0, b0, a1, b1, w, r);
            if (got != c1 || r[0] != (ulong)low || r[1] != t1 || r[2] != (a1 ^ w))
            {
                if (errors++ < 4) Console.WriteLine($"RedefineFirstOperand({a0:x},{b0:x},{a1:x},{b1:x},{w:x}): {got},{r[1]:x} expected {c1},{t1:x}");
            }

            r[5] = w;
            got = RedefineSecondOperandByLoad(a0, b0, a1, b1, b0, a0, r);
            UInt128 third = (UInt128)b0 + a0 + c1;
            if (got != (ulong)(third >> 64) || r[1] != t1 || r[2] != (ulong)third || r[3] != w)
            {
                if (errors++ < 4) Console.WriteLine($"RedefineSecondOperandByLoad({a0:x},{b0:x},{a1:x},{b1:x}): {got},{r[1]:x} expected {(ulong)(third >> 64)},{t1:x}");
            }

            r[4] = w;
            r[5] = a1;
            got = RedefineThroughParent(a0, b0, a1, new Pair { Lo = b1, Hi = 7 }, r);
            if (got != c1 || r[1] != t1 || r[2] != w + a1)
            {
                if (errors++ < 4) Console.WriteLine($"RedefineThroughParent({a0:x},{b0:x},{a1:x},{b1:x},{w:x}): {got},{r[1]:x} expected {c1},{t1:x}");
            }

            got = RedefineSubtrahend(a0, b0, a1, b1, w, r);
            {
                ulong d0 = a0 - b0, borrow0 = a0 < b0 ? 1UL : 0UL;
                ulong d1 = a1 - b1, borrow1 = a1 < b1 ? 1UL : 0UL;
                ulong t2 = d1 - borrow0;
                ulong borrowOut = borrow1 + (d1 < borrow0 ? 1UL : 0UL);
                if (got != borrowOut || r[0] != d0 || r[1] != t2 || r[2] != (b1 ^ w))
                {
                    if (errors++ < 4) Console.WriteLine($"RedefineSubtrahend({a0:x},{b0:x},{a1:x},{b1:x},{w:x}): {got},{r[1]:x} expected {borrowOut},{t2:x}");
                }
            }

            got = AccumulateInPlace(a0, b0, a1, b1, r);
            if (got != c1 || r[1] != t1)
            {
                if (errors++ < 4) Console.WriteLine($"AccumulateInPlace({a0:x},{b0:x},{a1:x},{b1:x}): {got},{r[1]:x} expected {c1},{t1:x}");
            }

            uint A0 = (uint)a0, B0 = (uint)b0, A1 = (uint)a1, B1 = (uint)b1, W = (uint)w;
            ulong low32 = (ulong)A0 + B0;
            uint c32 = (uint)(low32 >> 32);
            ulong high32 = (ulong)A1 + B1 + c32;
            uint got32 = RedefineAddend32(A0, B0, A1, B1, W, r32);
            if (got32 != (uint)(high32 >> 32) || r32[0] != (uint)low32 || r32[1] != (uint)high32 || r32[2] != B1 + W)
            {
                if (errors++ < 4) Console.WriteLine($"RedefineAddend32({A0:x},{B0:x},{A1:x},{B1:x},{W:x}): {got32},{r32[1]:x} expected {(uint)(high32 >> 32)},{(uint)high32:x}");
            }
        }
        Console.WriteLine(errors == 0 ? "Full-adder operand redefinition: all cases passed." : $"Full-adder operand redefinition: {errors} errors.");
        return errors == 0 ? 100 : 1;
    }
}
