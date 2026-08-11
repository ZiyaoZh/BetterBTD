using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using BetterBTD.Services.Tools;
using Newtonsoft.Json;

namespace BetterBTD.Tests.Services;

public sealed class Btd6SaveViewerServiceTests
{
    [Fact]
    public void Read_ValidVersion1Save_ExtractsSummary()
    {
        var saveBytes = BuildSave(
            """
            {
              "savedBySkuId": 1136,
              "savedByGameVersion": "53.2",
              "rank": 48.0,
              "xp": 1598108,
              "monkeyMoney": 3708,
              "trophies": 42,
              "ownerID": "owner-123",
              "timeStamp": "2026-03-27T19:49:05.896705-07:00"
            }
            """,
            saveCount: 7);

        var result = new Btd6SaveViewerService().Read(saveBytes, "Profile.Save");

        Assert.Equal("Profile.Save", result.FilePath);
        Assert.Equal("Profile.Save", result.FileName);
        Assert.Equal(1U, result.FileFormatVersion);
        Assert.Equal(7U, result.SaveCount);
        Assert.Equal(1136, result.SavedBySkuId);
        Assert.Equal("Steam", result.SavedBySkuName);
        Assert.Equal("53.2", result.SavedByGameVersion);
        Assert.Equal("1,598,108", result.Xp);
        Assert.Equal("owner-123", result.OwnerId);
        Assert.Contains("\"savedBySkuId\": 1136", result.FormattedJson);
    }

    [Fact]
    public void Read_Version2ExtendedHeader_DecryptsUsingDynamicOffset()
    {
        var saveBytes = BuildSave(
            """
            {
              "savedBySkuId": 35,
              "savedByGameVersion": "56.0",
              "rank": 155,
              "ownerID": "owner-v2"
            }
            """,
            saveCount: 23_521,
            fileFormatVersion: 2,
            headerExtension: Enumerable.Range(1, 35).Select(i => (byte)i).ToArray());

        var result = new Btd6SaveViewerService().Read(saveBytes, "Profile.Save");

        Assert.Equal(2U, result.FileFormatVersion);
        Assert.Equal(23_521U, result.SaveCount);
        Assert.Equal(35, result.SavedBySkuId);
        Assert.Equal("Unknown (35)", result.SavedBySkuName);
        Assert.Equal("56.0", result.SavedByGameVersion);
        Assert.Equal("owner-v2", result.OwnerId);
    }

    [Fact]
    public void Read_TooSmall_ThrowsInvalidData()
    {
        var service = new Btd6SaveViewerService();

        var ex = Assert.Throws<InvalidDataException>(() => service.Read(new byte[7]));

        Assert.Contains("too small", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Read_UnalignedEncryptedPayload_ThrowsInvalidData()
    {
        var valid = BuildSave("{\"savedBySkuId\":35}", saveCount: 1);
        var data = new byte[valid.Length + 1];
        valid.CopyTo(data, 0);

        var service = new Btd6SaveViewerService();

        var ex = Assert.Throws<InvalidDataException>(() => service.Read(data));

        Assert.Contains("multiple of 16", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Read_HeaderPayloadTooShort_ThrowsInvalidData()
    {
        var data = new byte[92];
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0, 4), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4, 4), 35);

        var ex = Assert.Throws<InvalidDataException>(() => new Btd6SaveViewerService().Read(data));

        Assert.Contains("required 36", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Read_TruncatedEncryptionMetadata_ThrowsInvalidData()
    {
        var data = new byte[8 + 36 + 31];
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0, 4), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4, 4), 36);

        var ex = Assert.Throws<InvalidDataException>(() => new Btd6SaveViewerService().Read(data));

        Assert.Contains("encryption metadata", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Read_OversizedHeaderLength_ThrowsInvalidData()
    {
        var data = new byte[92];
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0, 4), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4, 4), uint.MaxValue);

        var ex = Assert.Throws<InvalidDataException>(() => new Btd6SaveViewerService().Read(data));

        Assert.Contains("truncated", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static byte[] BuildSave(
        string json,
        uint saveCount,
        uint fileFormatVersion = 1,
        byte[]? headerExtension = null)
    {
        const int headerPrefixLength = 8;
        const int baseHeaderPayloadLength = 36;
        const int passwordIndexLength = 8;
        const int saltLength = 24;
        headerExtension ??= [];

        var headerPayloadLength = baseHeaderPayloadLength + headerExtension.Length;
        var headerEnd = headerPrefixLength + headerPayloadLength;
        var dataOffset = headerEnd + passwordIndexLength + saltLength;

        var parsed = JsonConvert.DeserializeObject<object>(json) ?? throw new InvalidDataException("Invalid test JSON.");
        var compactJson = JsonConvert.SerializeObject(parsed, Formatting.None);
        var jsonBytes = Encoding.UTF8.GetBytes("\uFEFF" + compactJson);

        byte[] compressed;
        using (var compressedStream = new MemoryStream())
        {
            using (var zlibStream = new ZLibStream(compressedStream, CompressionLevel.Fastest, leaveOpen: true))
            {
                zlibStream.Write(jsonBytes);
            }

            compressed = compressedStream.ToArray();
        }

        var salt = Enumerable.Range(1, saltLength).Select(i => (byte)i).ToArray();
        var keyIv = Rfc2898DeriveBytes.Pbkdf2(
            "11"u8.ToArray(),
            salt,
            10,
            HashAlgorithmName.SHA1,
            32);

        byte[] encrypted;
        using (var aes = Aes.Create())
        {
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.IV = keyIv[..16];
            aes.Key = keyIv[16..32];

            using var encryptor = aes.CreateEncryptor();
            encrypted = encryptor.TransformFinalBlock(compressed, 0, compressed.Length);
        }

        var data = new byte[dataOffset + encrypted.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0, 4), fileFormatVersion);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4, 4), (uint)headerPayloadLength);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(headerPrefixLength, 4), saveCount);
        headerExtension.CopyTo(data.AsSpan(headerPrefixLength + baseHeaderPayloadLength));
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(headerEnd, passwordIndexLength), 2);
        salt.CopyTo(data.AsSpan(headerEnd + passwordIndexLength));
        encrypted.CopyTo(data.AsSpan(dataOffset));
        return data;
    }
}
