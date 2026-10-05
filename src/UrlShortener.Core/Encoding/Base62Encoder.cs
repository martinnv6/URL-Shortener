namespace UrlShortener.Core.Encoding;

/// <summary>
/// Zero-allocation Base62 encoding engine.
/// Converts unsigned 64-bit integers to compact, URL-safe Base62 strings using
/// stack-allocated memory via <c>stackalloc</c> and <see cref="Span{T}"/> slicing.
///
/// Character set: 0-9, a-z, A-Z (62 characters total).
/// Maximum output length: 11 characters (for <see cref="ulong.MaxValue"/>).
///
/// Performance: The modulo-division loop performs ZERO heap allocations.
/// The only allocation is the final <see cref="string"/> constructor call,
/// which is unavoidable since a <see cref="string"/> must be returned.
/// </summary>
public static class Base62Encoder
{
    private const int Base = 62;

    /// <summary>
    /// Maximum number of Base62 characters required to represent <see cref="ulong.MaxValue"/>.
    /// Calculated as: ceil(log62(2^64 - 1)) = 11.
    /// </summary>
    private const int MaxBase62Length = 11;

    /// <summary>
    /// Encodes an unsigned 64-bit integer into a Base62 string.
    /// </summary>
    /// <param name="value">The value to encode.</param>
    /// <returns>A Base62-encoded string representation of the value.</returns>
    public static string Encode(ulong value)
    {
        // Special case: 0 maps directly to the first character in the alphabet.
        if (value == 0)
            return "0";

        // ReadOnlySpan<char> over a string literal is optimized by the JIT
        // to point directly at the static data segment — no heap copy.
        ReadOnlySpan<char> alphabet = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";

        // Stack-allocate the working buffer.
        // This is the critical zero-allocation optimization: the buffer lives
        // entirely on the current thread's stack frame, not the managed heap,
        // so it imposes zero GC pressure.
        Span<char> buffer = stackalloc char[MaxBase62Length];

        // Fill the buffer right-to-left (least-significant digit first).
        int position = MaxBase62Length;

        while (value > 0)
        {
            position--;
            buffer[position] = alphabet[(int)(value % (ulong)Base)];
            value /= (ulong)Base;
        }

        // Slice to the populated region and construct the final string.
        // This is the ONLY heap allocation in the entire method.
        return new string(buffer[position..]);
    }
}
