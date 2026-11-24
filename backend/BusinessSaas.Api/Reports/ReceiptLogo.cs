using System.Buffers.Binary;

namespace BusinessSaas.Api.Reports;

public static class ReceiptLogo
{
    // Validate a bounded PNG envelope before storage; the eventual renderer
    // must still validate decoding. No SVG, external image URLs or file paths.
    public static bool IsValid(byte[] bytes)
    {
        ReadOnlySpan<byte> png = bytes;
        if (png.Length is < 45 or > 32768 ||
            !png[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) ||
            BinaryPrimitives.ReadUInt32BigEndian(png[8..12]) != 13 ||
            !png[12..16].SequenceEqual("IHDR"u8) ||
            !png[^12..].SequenceEqual(new byte[] { 0, 0, 0, 0, 73, 69, 78, 68, 174, 66, 96, 130 }))
            return false;

        var width = BinaryPrimitives.ReadUInt32BigEndian(png[16..20]);
        var height = BinaryPrimitives.ReadUInt32BigEndian(png[20..24]);
        if (width is < 1 or > 384 || height is < 1 or > 192) return false;

        // Check chunk boundaries and require image data before the final IEND.
        var offset = 8;
        var hasImageData = false;
        while (offset <= png.Length - 12)
        {
            var size = BinaryPrimitives.ReadUInt32BigEndian(png.Slice(offset, 4));
            if (size > png.Length - offset - 12) return false;
            var type = png.Slice(offset + 4, 4);
            if (type.SequenceEqual("IDAT"u8) && size > 0) hasImageData = true;
            offset += 12 + (int)size;
            if (type.SequenceEqual("IEND"u8)) return size == 0 && offset == png.Length && hasImageData;
        }
        return false;
    }
}
