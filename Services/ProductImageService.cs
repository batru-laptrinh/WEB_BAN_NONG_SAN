using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace WEB_BAN_NONG_SAN.Services;

/// <summary>
/// Stores a deliberately small PNG subset. Every upload is structurally checked,
/// inflated with a strict output bound and rewritten without metadata or trailing
/// data. Original names/content types never determine storage or response headers.
/// </summary>
public sealed class ProductImageService(IWebHostEnvironment environment)
{
    public const int MaximumBytes = 2 * 1024 * 1024;
    private const int MaximumDimension = 2048;
    private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];
    private readonly string directory = Path.Combine(environment.ContentRootPath, "App_Data", "product-images");

    public async Task<string> SaveAsync(IFormFile upload, CancellationToken cancellationToken = default)
    {
        if (upload.Length <= 0 || upload.Length > MaximumBytes)
            throw new InvalidDataException("Ảnh phải là PNG có dung lượng tối đa 2 MB.");

        await using var input = upload.OpenReadStream();
        using var source = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = await input.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (source.Length + count > MaximumBytes)
                throw new InvalidDataException("Ảnh vượt dung lượng cho phép.");
            source.Write(buffer, 0, count);
        }

        var canonical = CanonicalPng(source.ToArray());
        Directory.CreateDirectory(directory);
        var fileName = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16)) + ".png";
        var path = Path.Combine(directory, fileName);
        try
        {
            await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await output.WriteAsync(canonical, cancellationToken);
        }
        catch
        {
            // A failed write may have left an incomplete file. Only this newly
            // generated path can be removed; uploaded names are never used.
            if (File.Exists(path)) File.Delete(path);
            throw;
        }
        return "/ProductImages/" + fileName;
    }

    public string? GetExistingPath(string fileName)
    {
        if (fileName.Length != 36 || !fileName.EndsWith(".png", StringComparison.Ordinal) ||
            !ProductImageHexValidation.ContainsOnlyHex(fileName.AsSpan(0, 32))) return null;
        var path = Path.Combine(directory, fileName);
        return File.Exists(path) ? path : null;
    }

    public void DeleteGenerated(string? imagePath)
    {
        const string prefix = "/ProductImages/";
        if (imagePath?.StartsWith(prefix, StringComparison.Ordinal) != true) return;
        var path = GetExistingPath(imagePath[prefix.Length..]);
        if (path != null) File.Delete(path);
    }

    private static byte[] CanonicalPng(byte[] source)
    {
        const string invalid = "Chỉ hỗ trợ ảnh PNG RGB/RGBA 8-bit, không xen kẽ, kích thước tối đa 2048 × 2048.";
        if (source.Length < 57 || !source.AsSpan(0, 8).SequenceEqual(Signature))
            throw new InvalidDataException(invalid);

        byte[]? header = null;
        using var compressed = new MemoryStream();
        var offset = 8;
        var sawData = false;
        var dataEnded = false;
        var sawEnd = false;
        while (offset < source.Length)
        {
            if (offset + 12 > source.Length) throw new InvalidDataException(invalid);
            var length = BinaryPrimitives.ReadUInt32BigEndian(source.AsSpan(offset, 4));
            if (length > MaximumBytes || (long)offset + 12 + length > source.Length)
                throw new InvalidDataException(invalid);
            var size = (int)length;
            var typeBytes = source.AsSpan(offset + 4, 4);
            if (typeBytes.ToArray().Any(x => x is not (>= (byte)'A' and <= (byte)'Z') and not (>= (byte)'a' and <= (byte)'z')))
                throw new InvalidDataException(invalid);
            var type = Encoding.ASCII.GetString(typeBytes);
            var data = source.AsSpan(offset + 8, size);
            if (Crc32(source.AsSpan(offset + 4, size + 4)) !=
                BinaryPrimitives.ReadUInt32BigEndian(source.AsSpan(offset + 8 + size, 4)))
                throw new InvalidDataException(invalid);

            if (header == null && type != "IHDR") throw new InvalidDataException(invalid);
            if (type == "IHDR")
            {
                if (header != null || size != 13) throw new InvalidDataException(invalid);
                header = data.ToArray();
                var width = BinaryPrimitives.ReadUInt32BigEndian(data[..4]);
                var height = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(4, 4));
                if (width is 0 or > MaximumDimension || height is 0 or > MaximumDimension ||
                    data[8] != 8 || data[9] is not (2 or 6) || data[10] != 0 || data[11] != 0 || data[12] != 0)
                    throw new InvalidDataException(invalid);
            }
            else if (type == "IDAT")
            {
                if (dataEnded || sawEnd) throw new InvalidDataException(invalid);
                sawData = true;
                compressed.Write(data);
            }
            else if (type == "IEND")
            {
                if (!sawData || size != 0) throw new InvalidDataException(invalid);
                sawEnd = true;
                offset += size + 12;
                break;
            }
            else
            {
                // Only standard ancillary metadata may be present; it is removed.
                // Reject palettes, animation and unknown critical chunks.
                if (type is not ("tEXt" or "zTXt" or "iTXt" or "pHYs" or "gAMA" or "cHRM" or "sRGB" or "iCCP" or "tIME" or "bKGD" or "sBIT"))
                    throw new InvalidDataException(invalid);
                if (sawData) dataEnded = true;
            }
            offset += size + 12;
        }
        if (!sawEnd || offset != source.Length || header == null) throw new InvalidDataException(invalid);

        var rowLength = checked((int)BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(0, 4)) *
            (header[9] == 2 ? 3 : 4) + 1);
        var heightValue = (int)BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(4, 4));
        var expected = checked(rowLength * heightValue);
        var pixels = new byte[expected];
        compressed.Position = 0;
        try
        {
            using var inflate = new ZLibStream(compressed, CompressionMode.Decompress, leaveOpen: true);
            var read = 0;
            while (read < expected)
            {
                var current = inflate.Read(pixels, read, expected - read);
                if (current == 0) throw new InvalidDataException(invalid);
                read += current;
            }
            if (inflate.ReadByte() != -1) throw new InvalidDataException(invalid);
        }
        catch (InvalidDataException) { throw new InvalidDataException(invalid); }
        for (var row = 0; row < heightValue; row++)
            if (pixels[row * rowLength] > 4) throw new InvalidDataException(invalid);

        using var canonicalData = new MemoryStream();
        using (var deflate = new ZLibStream(canonicalData, CompressionLevel.Optimal, leaveOpen: true))
            deflate.Write(pixels);
        using var output = new MemoryStream();
        output.Write(Signature);
        WriteChunk(output, "IHDR", header);
        WriteChunk(output, "IDAT", canonicalData.ToArray());
        WriteChunk(output, "IEND", []);
        return output.ToArray();
    }

    private static void WriteChunk(Stream stream, string type, byte[] data)
    {
        Span<byte> word = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(word, data.Length);
        stream.Write(word);
        var payload = Encoding.ASCII.GetBytes(type).Concat(data).ToArray();
        stream.Write(payload);
        BinaryPrimitives.WriteUInt32BigEndian(word, Crc32(payload));
        stream.Write(word);
    }

    private static uint Crc32(ReadOnlySpan<byte> value)
    {
        var crc = uint.MaxValue;
        foreach (var item in value)
        {
            crc ^= item;
            for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0u);
        }
        return ~crc;
    }
}

internal static class ProductImageHexValidation
{
    public static bool ContainsOnlyHex(this ReadOnlySpan<char> text)
    {
        foreach (var character in text)
            if (character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')) return false;
        return true;
    }
}
