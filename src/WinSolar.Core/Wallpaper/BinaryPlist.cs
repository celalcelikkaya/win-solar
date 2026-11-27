using System.Text;

namespace WinSolar.Core.Wallpaper;

/// <summary>
/// A small reader for Apple binary property lists ("bplist00"), enough for the dynamic-wallpaper metadata:
/// dict → <see cref="Dictionary{TKey,TValue}"/>, array → <see cref="List{T}"/>, int → long, real/date → double,
/// string, bool, data → byte[]. Anything malformed throws <see cref="FormatException"/>.
/// </summary>
public static class BinaryPlist
{
    private const int MaxDepth = 32;

    public static object? Parse(byte[] bytes)
    {
        try
        {
            return new Reader(bytes).Top();
        }
        catch (Exception e) when (e is IndexOutOfRangeException or ArgumentException or OverflowException or DecoderFallbackException)
        {
            throw new FormatException("Damaged property list.", e);
        }
    }

    private sealed class Reader
    {
        private readonly byte[] b;
        private readonly int offsetSize;
        private readonly int refSize;
        private readonly long count;
        private readonly long top;
        private readonly long table;

        public Reader(byte[] bytes)
        {
            b = bytes;
            if (b.Length < 8 + 32 || Encoding.ASCII.GetString(b, 0, 8) != "bplist00") throw new FormatException("Not a binary property list.");
            var t = b.Length - 32;
            offsetSize = b[t + 6];
            refSize = b[t + 7];
            count = (long)Uint(t + 8, 8);
            top = (long)Uint(t + 16, 8);
            table = (long)Uint(t + 24, 8);
            if (offsetSize is < 1 or > 8 || refSize is < 1 or > 8 || count < 1 || top < 0 || top >= count
                || table < 8 || table + count * offsetSize > t)
                throw new FormatException("Damaged property list trailer.");
        }

        public object? Top() => Object(top, 0);

        private ulong Uint(long at, int size)
        {
            if (at < 0 || at + size > b.Length) throw new FormatException("Damaged property list.");
            ulong v = 0;
            for (var i = 0; i < size; i++) v = (v << 8) | b[at + i];
            return v;
        }

        private long Offset(long index)
        {
            if (index < 0 || index >= count) throw new FormatException("Damaged property list reference.");
            var o = (long)Uint(table + index * offsetSize, offsetSize);
            if (o < 8 || o >= table) throw new FormatException("Damaged property list offset.");
            return o;
        }

        /// <summary>Length nibble, or the int object that follows when it is 0xF. Returns the length and where the body starts.</summary>
        private (long Length, long Body) Length(long at)
        {
            var n = b[at] & 0x0F;
            if (n != 0x0F) return (n, at + 1);
            var marker = b[at + 1];
            if ((marker & 0xF0) != 0x10) throw new FormatException("Damaged property list length.");
            var size = 1 << (marker & 0x0F);
            var len = (long)Uint(at + 2, size);
            if (len < 0 || len > b.Length) throw new FormatException("Damaged property list length.");
            return (len, at + 2 + size);
        }

        private object? Object(long index, int depth)
        {
            if (depth > MaxDepth) throw new FormatException("Property list nests too deep.");
            var at = Offset(index);
            var marker = b[at];
            switch (marker >> 4)
            {
                case 0x0:
                    return marker switch
                    {
                        0x00 => null,
                        0x08 => false,
                        0x09 => true,
                        _ => throw new FormatException("Unknown property list value."),
                    };
                case 0x1:
                {
                    var size = 1 << (marker & 0x0F);
                    if (size > 8) throw new FormatException("Integer too large.");
                    var v = Uint(at + 1, size);
                    return size == 8 ? unchecked((long)v) : (long)v;
                }
                case 0x2:
                case 0x3:
                {
                    var size = marker == 0x33 ? 8 : 1 << (marker & 0x0F);
                    return size switch
                    {
                        4 => (double)BitConverter.Int32BitsToSingle(unchecked((int)Uint(at + 1, 4))),
                        8 => BitConverter.Int64BitsToDouble(unchecked((long)Uint(at + 1, 8))),
                        _ => throw new FormatException("Unknown real size."),
                    };
                }
                case 0x4:
                {
                    var (len, body) = Length(at);
                    if (body + len > b.Length) throw new FormatException("Damaged property list data.");
                    return b.AsSpan((int)body, (int)len).ToArray();
                }
                case 0x5:
                {
                    var (len, body) = Length(at);
                    if (body + len > b.Length) throw new FormatException("Damaged property list string.");
                    return Encoding.ASCII.GetString(b, (int)body, (int)len);
                }
                case 0x6:
                {
                    var (len, body) = Length(at);
                    if (body + len * 2 > b.Length) throw new FormatException("Damaged property list string.");
                    return Encoding.BigEndianUnicode.GetString(b, (int)body, (int)len * 2);
                }
                case 0xA:
                {
                    var (len, body) = Length(at);
                    var list = new List<object?>();
                    for (var i = 0; i < len; i++) list.Add(Object((long)Uint(body + i * refSize, refSize), depth + 1));
                    return list;
                }
                case 0xD:
                {
                    var (len, body) = Length(at);
                    var dict = new Dictionary<string, object?>();
                    for (var i = 0; i < len; i++)
                    {
                        if (Object((long)Uint(body + i * refSize, refSize), depth + 1) is not string key)
                            throw new FormatException("Property list key is not a string.");
                        dict[key] = Object((long)Uint(body + (len + i) * refSize, refSize), depth + 1);
                    }

                    return dict;
                }
                default:
                    throw new FormatException("Unknown property list value.");
            }
        }
    }
}
