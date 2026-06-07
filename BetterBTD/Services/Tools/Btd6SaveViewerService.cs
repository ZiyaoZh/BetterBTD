using System.Buffers.Binary;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using BetterBTD.Models.Tools;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BetterBTD.Services.Tools;

public sealed class Btd6SaveViewerService
{
    private const int HeaderLength = 44;
    private const int PasswordIndexLength = 8;
    private const int SaltLength = 24;
    private const int DataOffset = HeaderLength + PasswordIndexLength + SaltLength;
    private const int PlatformSteam = 18;
    private const int PlatformAppleArcade = 95;
    private const int SkuSteam = 1136;
    private const int SkuAppleArcade = 1108;
    private const int Pbkdf2Iterations = 10;
    private static readonly byte[] Password = "11"u8.ToArray();

    private static readonly Lazy<Btd6SaveViewerService> InstanceHolder = new(() => new Btd6SaveViewerService());

    public static Btd6SaveViewerService Instance => InstanceHolder.Value;

    public Btd6SaveDocument Load(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("Save file path is required.", nameof(filePath));
        }

        var data = File.ReadAllBytes(filePath);
        return Read(data, filePath);
    }

    public Btd6SaveDocument Read(byte[] data, string filePath = "")
    {
        ArgumentNullException.ThrowIfNull(data);

        if (data.Length < DataOffset + 16)
        {
            throw new InvalidDataException("File is too small to be a valid BTD6 save.");
        }

        var version = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(0, sizeof(uint)));
        if (version != 1)
        {
            throw new InvalidDataException($"Unexpected file version: {version}.");
        }

        var platformId = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(8, sizeof(uint)));
        var salt = data.AsSpan(HeaderLength + PasswordIndexLength, SaltLength).ToArray();
        var encrypted = data.AsSpan(DataOffset).ToArray();

        if (encrypted.Length % 16 != 0)
        {
            throw new InvalidDataException($"Encrypted payload size {encrypted.Length} is not a multiple of 16.");
        }

        var jsonBytes = DecryptJsonBytes(encrypted, salt);
        var jsonText = DecodeJsonText(jsonBytes);
        var root = JToken.Parse(jsonText);
        var obj = root as JObject;

        return new Btd6SaveDocument
        {
            FilePath = filePath,
            FileName = string.IsNullOrWhiteSpace(filePath) ? string.Empty : Path.GetFileName(filePath),
            FileSizeBytes = data.Length,
            PlatformId = platformId,
            PlatformName = GetPlatformName(platformId),
            SavedBySkuId = GetInt(obj, "savedBySkuId"),
            SavedBySkuName = GetSkuName(GetInt(obj, "savedBySkuId")),
            SavedByGameVersion = FormatToken(obj?["savedByGameVersion"]),
            Rank = FormatToken(obj?["rank"]),
            Xp = FormatToken(obj?["xp"]),
            MonkeyMoney = FormatToken(obj?["monkeyMoney"]),
            Trophies = FormatToken(obj?["trophies"]),
            OwnerId = FormatToken(obj?["ownerID"]),
            TimeStamp = FormatToken(obj?["timeStamp"]),
            JsonSizeBytes = jsonBytes.Length,
            FormattedJson = root.ToString(Formatting.Indented),
            Root = root
        };
    }

    public static string GetPlatformName(int platformId)
    {
        return platformId switch
        {
            PlatformSteam => "Steam",
            PlatformAppleArcade => "Apple Arcade",
            _ => $"Unknown ({platformId})"
        };
    }

    public static string GetSkuName(int? skuId)
    {
        return skuId switch
        {
            SkuSteam => "Steam",
            SkuAppleArcade => "Apple Arcade",
            null => "N/A",
            _ => $"Unknown ({skuId})"
        };
    }

    private static byte[] DecryptJsonBytes(byte[] encrypted, byte[] salt)
    {
        var keyIv = Rfc2898DeriveBytes.Pbkdf2(
            Password,
            salt,
            Pbkdf2Iterations,
            HashAlgorithmName.SHA1,
            32);

        using var aes = Aes.Create();
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.IV = keyIv[..16];
        aes.Key = keyIv[16..32];

        byte[] decrypted;
        try
        {
            using var decryptor = aes.CreateDecryptor();
            decrypted = decryptor.TransformFinalBlock(encrypted, 0, encrypted.Length);
        }
        catch (CryptographicException ex)
        {
            throw new InvalidDataException("Decryption failed. The file may not be a standard BTD6 save.", ex);
        }

        try
        {
            using var compressedStream = new MemoryStream(decrypted);
            using var zlibStream = new ZLibStream(compressedStream, CompressionMode.Decompress);
            using var jsonStream = new MemoryStream();
            zlibStream.CopyTo(jsonStream);
            return jsonStream.ToArray();
        }
        catch (InvalidDataException ex)
        {
            throw new InvalidDataException("Decompression failed after decryption. The file may be corrupt.", ex);
        }
    }

    private static string DecodeJsonText(byte[] jsonBytes)
    {
        var text = Encoding.UTF8.GetString(jsonBytes);
        return text.Length > 0 && text[0] == '\uFEFF' ? text[1..] : text;
    }

    private static int? GetInt(JObject? obj, string propertyName)
    {
        var token = obj?[propertyName];
        if (token is null || token.Type == JTokenType.Null)
        {
            return null;
        }

        return token.Type is JTokenType.Integer or JTokenType.Float
            ? token.Value<int>()
            : null;
    }

    private static string FormatToken(JToken? token)
    {
        if (token is null || token.Type == JTokenType.Null || token.Type == JTokenType.Undefined)
        {
            return "N/A";
        }

        return token.Type switch
        {
            JTokenType.Integer => token.Value<long>().ToString("N0", CultureInfo.CurrentCulture),
            JTokenType.Float => token.Value<double>().ToString("N2", CultureInfo.CurrentCulture),
            JTokenType.Boolean => token.Value<bool>() ? "true" : "false",
            JTokenType.String => token.Value<string>() ?? string.Empty,
            _ => token.ToString(Formatting.None)
        };
    }
}
