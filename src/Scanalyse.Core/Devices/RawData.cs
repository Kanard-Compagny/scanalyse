using System.Text;

namespace Scanalyse.Core.Devices;

/// <summary>Façon d'interpréter les octets reçus d'un scanner en texte.</summary>
public enum TextDecoding
{
    /// <summary>UTF-8 si les octets en sont valides, sinon Latin-1 (cas le plus courant).</summary>
    Auto,

    /// <summary>UTF-8 strict.</summary>
    Utf8,

    /// <summary>ISO-8859-1 : un octet = un caractère, rien n'est perdu.</summary>
    Latin1,
}

/// <summary>Outils pour les octets bruts reçus d'un scanner (port série, HID POS).</summary>
public static class RawData
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>Convertit les octets en texte selon <paramref name="decoding"/>.</summary>
    public static string Decode(ReadOnlySpan<byte> bytes, TextDecoding decoding = TextDecoding.Auto)
    {
        switch (decoding)
        {
            case TextDecoding.Latin1:
                return Encoding.Latin1.GetString(bytes);
            case TextDecoding.Utf8:
                return Encoding.UTF8.GetString(bytes);
            default:
                try
                {
                    return StrictUtf8.GetString(bytes);
                }
                catch (DecoderFallbackException)
                {
                    return Encoding.Latin1.GetString(bytes);
                }
        }
    }

    /// <summary>Octets en hexadécimal séparés par des espaces (« 30 31 1D 0D »).</summary>
    public static string ToHex(ReadOnlySpan<byte> bytes, int max = 4096)
    {
        if (bytes.IsEmpty)
        {
            return string.Empty;
        }

        var n = Math.Min(bytes.Length, max);
        var sb = new StringBuilder(n * 3 + 4);
        for (var i = 0; i < n; i++)
        {
            if (i > 0)
            {
                sb.Append(' ');
            }

            sb.Append(bytes[i].ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
        }

        if (n < bytes.Length)
        {
            sb.Append(" …");
        }

        return sb.ToString();
    }
}
