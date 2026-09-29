using System.Text;

namespace RelayHook.AspNetCore.Delivery;

internal static class Utf8Truncator
{
    public static string? Truncate(string? value, int maximumBytes)
    {
        if (value is null || maximumBytes == 0)
        {
            return maximumBytes == 0 ? null : value;
        }

        if (Encoding.UTF8.GetByteCount(value) <= maximumBytes)
        {
            return value;
        }

        var length = Math.Min(value.Length, maximumBytes);
        while (length > 0 && Encoding.UTF8.GetByteCount(value.AsSpan(0, length)) > maximumBytes)
        {
            length--;
        }

        return value[..length];
    }
}
