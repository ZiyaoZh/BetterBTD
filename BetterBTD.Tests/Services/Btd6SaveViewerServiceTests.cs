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
    public void Read_ValidSteamSave_ExtractsSummary()
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
            platformId: 18);

        var result = new Btd6SaveViewerService().Read(saveBytes, "Profile.Save");

        Assert.Equal("Profile.Save", result.FilePath);
        Assert.Equal("Profile.Save", result.FileName);
        Assert.Equal(18, result.PlatformId);
        Assert.Equal("Steam", result.PlatformName);
        Assert.Equal(1136, result.SavedBySkuId);
        Assert.Equal("Steam", result.SavedBySkuName);
        Assert.Equal("53.2", result.SavedByGameVersion);
        Assert.Equal("1,598,108", result.Xp);
        Assert.Equal("owner-123", result.OwnerId);
        Assert.Contains("\"savedBySkuId\": 1136", result.FormattedJson);
    }

    [Fact]
    public void Read_TooSmall_ThrowsInvalidData()
    {
        var service = new Btd6SaveViewerService();

        var ex = Assert.Throws<InvalidDataException>(() => service.Read(new byte[32]));

        Assert.Contains("too small", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Read_UnalignedEncryptedPayload_ThrowsInvalidData()
    {
        var data = new byte[93];
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0, 4), 1);

        var service = new Btd6SaveViewerService();

        var ex = Assert.Throws<InvalidDataException>(() => service.Read(data));

        Assert.Contains("multiple of 16", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static byte[] BuildSave(string json, int platformId)
    {
        const int headerLength = 44;
        const int passwordIndexLength = 8;
        const int saltLength = 24;
        const int dataOffset = headerLength + passwordIndexLength + saltLength;

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
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0, 4), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4, 4), 36);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(8, 4), (uint)platformId);
        data[headerLength] = 2;
        salt.CopyTo(data.AsSpan(headerLength + passwordIndexLength));
        encrypted.CopyTo(data.AsSpan(dataOffset));
        return data;
    }
}
