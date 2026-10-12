using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace TruckModsListUpdater.Core.Sii;

/// <summary>How the file was stored on disk.</summary>
public enum SiiContainer { Plain, ScsC, ThreeNK }

/// <summary>What the (decrypted) payload is.</summary>
public enum SiiContent { Text, Binary }

public sealed class SiiFormatException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>A decoded SII file. <see cref="Text"/> is set for text content; binary content is read-only.</summary>
public sealed record SiiDocument(SiiContainer Container, SiiContent Content, byte[] Payload, string? Text)
{
    /// <summary>Only text payloads can be edited and written back losslessly.</summary>
    public bool IsWritable => Content == SiiContent.Text;

    public IReadOnlyList<SiiUnit> ReadUnits() =>
        Content == SiiContent.Text ? SiiText.Parse(Text!) : new BsiiReader(Payload).ReadAll();
}

/// <summary>
/// Strict decoder for SCS .sii files. It never returns placeholder text: anything it
/// cannot decode throws <see cref="SiiFormatException"/>.
/// </summary>
public static class SiiCodec
{
    // Publicly documented SII key (SII_Decrypt and every save editor use it).
    static readonly byte[] ScsCKey =
    [
        0x2a, 0x5f, 0xcb, 0x17, 0x91, 0xd2, 0x2f, 0xb6, 0x02, 0x45, 0xb3, 0xd8, 0x36, 0x9e, 0xd0, 0xb2,
        0xc2, 0x73, 0x71, 0x56, 0x3f, 0xbf, 0x1f, 0x3c, 0x9e, 0xdf, 0x6b, 0x11, 0x82, 0x5a, 0x5d, 0x0a,
    ];

    const int ScsCHeaderSize = 56; // magic(4) hmac(32) iv(16) size(4)
    const int ThreeNKHeaderSize = 6; // magic(4) unknown(1) seed(1)

    public static SiiDocument DecodeFile(string path) => Decode(File.ReadAllBytes(path));

    public static SiiDocument Decode(byte[] data)
    {
        if (StartsWith(data, "ScsC"u8)) return DecodePayload(SiiContainer.ScsC, DecryptScsC(data));
        if (StartsWith(data, "3nK\x01"u8)) return DecodePayload(SiiContainer.ThreeNK, Decode3nK(data));
        return DecodePayload(SiiContainer.Plain, data);
    }

    static SiiDocument DecodePayload(SiiContainer container, byte[] payload)
    {
        if (StartsWith(payload, "BSII"u8)) return new SiiDocument(container, SiiContent.Binary, payload, null);

        var text = Encoding.UTF8.GetString(StripBoms(payload));
        if (!text.TrimStart().StartsWith("SiiNunit", StringComparison.Ordinal))
            throw new SiiFormatException("Not a SII file (no ScsC, 3nK, BSII or SiiNunit signature).");
        return new SiiDocument(container, SiiContent.Text, payload, text);
    }

    static byte[] DecryptScsC(byte[] data)
    {
        if (data.Length < ScsCHeaderSize + 16) throw new SiiFormatException("Encrypted SII file is truncated.");
        var iv = data.AsSpan(36, 16);
        int size = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(52, 4));
        try
        {
            using var aes = Aes.Create();
            aes.Key = ScsCKey;
            var compressed = aes.DecryptCbc(data.AsSpan(ScsCHeaderSize), iv, PaddingMode.PKCS7);

            using var z = new ZLibStream(new MemoryStream(compressed), CompressionMode.Decompress);
            using var output = new MemoryStream(Math.Max(size, 0));
            z.CopyTo(output);
            if (output.Length != size)
                throw new SiiFormatException($"Decrypted SII size {output.Length} does not match header size {size}.");
            return output.ToArray();
        }
        catch (Exception e) when (e is CryptographicException or InvalidDataException)
        {
            throw new SiiFormatException("Could not decrypt SII file: " + e.Message, e);
        }
    }

    static byte[] Decode3nK(byte[] data)
    {
        if (data.Length < ThreeNKHeaderSize) throw new SiiFormatException("3nK SII file is truncated.");
        byte seed = data[5];
        var output = new byte[data.Length - ThreeNKHeaderSize];
        for (int i = 0; i < output.Length; i++)
            output[i] = (byte)(data[ThreeNKHeaderSize + i] ^ ThreeNKKey((byte)(seed + i)));
        return output;
    }

    /// <summary>3nK key table entry, from SII_Decrypt: Key[i] = (((i shl 2) xor not i) shl 3) xor i.</summary>
    internal static byte ThreeNKKey(byte i) => (byte)((((i << 2) ^ ~i) << 3) ^ i);

    /// <summary>3nK is a symmetric XOR; encoding exists for tests and completeness.</summary>
    internal static byte[] Encode3nK(byte[] payload, byte seed)
    {
        var output = new byte[payload.Length + ThreeNKHeaderSize];
        "3nK\x01"u8.CopyTo(output);
        output[5] = seed;
        for (int i = 0; i < payload.Length; i++)
            output[ThreeNKHeaderSize + i] = (byte)(payload[i] ^ ThreeNKKey((byte)(seed + i)));
        return output;
    }

    /// <summary>Encrypts a payload the way the game does (HMAC left zero). Used by tests.</summary>
    internal static byte[] EncodeScsC(byte[] payload)
    {
        using var compressed = new MemoryStream();
        using (var z = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true)) z.Write(payload);
        var iv = RandomNumberGenerator.GetBytes(16);
        using var aes = Aes.Create();
        aes.Key = ScsCKey;
        var cipher = aes.EncryptCbc(compressed.ToArray(), iv, PaddingMode.PKCS7);

        var output = new byte[ScsCHeaderSize + cipher.Length];
        "ScsC"u8.CopyTo(output);
        iv.CopyTo(output.AsSpan(36));
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(52), payload.Length);
        cipher.CopyTo(output.AsSpan(ScsCHeaderSize));
        return output;
    }

    /// <summary>Removes any number of leading UTF-8 BOMs (hand-edited profiles can have several).</summary>
    internal static ReadOnlySpan<byte> StripBoms(ReadOnlySpan<byte> data)
    {
        while (data.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF])) data = data[3..];
        return data;
    }

    static bool StartsWith(ReadOnlySpan<byte> data, ReadOnlySpan<byte> magic) => data.StartsWith(magic);
}
