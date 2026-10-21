using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace Scanalyse.Core.Content;

/// <summary>Une commande d'un profil StageNow (« zh(...) », « download(...) »…).</summary>
/// <param name="Name">Nom de la commande, ou du module (CSP) pour « zh ».</param>
/// <param name="Arguments">Arguments bruts.</param>
/// <param name="Settings">Réglages JSON pour une commande « zh », sinon <c>null</c>.</param>
public sealed record StageNowCall(string Name, IReadOnlyList<string> Arguments, JsonElement? Settings);

/// <summary>Profil de configuration Zebra StageNow lu dans un code-barres (PDF417 le plus souvent).</summary>
/// <param name="Part">Numéro de ce code dans la série.</param>
/// <param name="Total">Nombre total de codes de la série.</param>
/// <param name="Script">Script décompressé, vide si le contenu n'a pas pu être décompressé.</param>
/// <param name="Calls">Commandes reconnues dans le script.</param>
public sealed record StageNowProfile(int Part, int Total, string Script, IReadOnlyList<StageNowCall> Calls)
{
    /// <summary>
    /// Reconnaît un profil StageNow (en-tête « SN? ») et le décompresse.
    /// Renvoie <c>null</c> si les octets ne sont pas un profil StageNow.
    /// </summary>
    public static StageNowProfile? TryDecode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 8 || bytes[0] != (byte)'S' || bytes[1] != (byte)'N' || bytes[2] != (byte)'?')
        {
            return null;
        }

        int part = bytes[4], total = bytes[5];
        var sb = new StringBuilder(bytes.Length);
        foreach (var b in bytes[6..])
        {
            var c = (char)b;
            if (char.IsAsciiLetterOrDigit(c) || c is '+' or '/' or '=')
            {
                sb.Append(c);
            }
        }

        byte[] payload;
        try
        {
            var b64 = sb.ToString();
            var pad = b64.Length % 4;
            payload = Convert.FromBase64String(pad == 0 ? b64 : b64 + new string('=', 4 - pad));
        }
        catch (FormatException)
        {
            return new StageNowProfile(part, total, string.Empty, []);
        }

        var start = -1;
        for (var i = 0; i < Math.Min(32, payload.Length - 2); i++)
        {
            if (payload[i] == 0x1F && payload[i + 1] == 0x8B && payload[i + 2] == 0x08)
            {
                start = i;
                break;
            }
        }

        if (start < 0)
        {
            return new StageNowProfile(part, total, string.Empty, []);
        }

        string script;
        try
        {
            using var input = new MemoryStream(payload, start, payload.Length - start);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var reader = new StreamReader(gzip, Encoding.UTF8);
            script = reader.ReadToEnd();
        }
        catch (InvalidDataException)
        {
            return new StageNowProfile(part, total, string.Empty, []);
        }

        return new StageNowProfile(part, total, script, ParseCalls(script));
    }

    /// <summary>Découpe un script en appels « nom(arg1, arg2); » en respectant les chaînes entre guillemets.</summary>
    internal static List<StageNowCall> ParseCalls(string src)
    {
        var calls = new List<StageNowCall>();
        var i = 0;
        while (i < src.Length)
        {
            if (!(char.IsAsciiLetter(src[i]) || src[i] == '_'))
            {
                i++;
                continue;
            }

            var j = i;
            while (j < src.Length && (char.IsAsciiLetterOrDigit(src[j]) || src[j] is '_' or '.'))
            {
                j++;
            }

            var k = j;
            while (k < src.Length && char.IsWhiteSpace(src[k]))
            {
                k++;
            }

            if (k >= src.Length || src[k] != '(')
            {
                i = j;
                continue;
            }

            var name = src[i..j];
            i = k + 1;
            var args = new List<string>();
            var cur = new StringBuilder();
            var depth = 0;
            while (i < src.Length)
            {
                var ch = src[i];
                if (ch is '"' or '\'')
                {
                    var q = ch;
                    i++;
                    while (i < src.Length && src[i] != q)
                    {
                        if (src[i] == '\\' && i + 1 < src.Length)
                        {
                            cur.Append(src[i + 1]);
                            i += 2;
                        }
                        else
                        {
                            cur.Append(src[i++]);
                        }
                    }

                    i++;
                    continue;
                }

                if (ch == '(')
                {
                    depth++;
                }
                else if (ch == ')')
                {
                    if (depth == 0)
                    {
                        args.Add(cur.ToString().Trim());
                        i++;
                        break;
                    }

                    depth--;
                }
                else if (ch == ',' && depth == 0)
                {
                    args.Add(cur.ToString().Trim());
                    cur.Clear();
                    i++;
                    continue;
                }

                cur.Append(ch);
                i++;
            }

            if (name == "zh" && args.Count >= 2)
            {
                JsonElement? json = null;
                try
                {
                    using var doc = JsonDocument.Parse(args[1]);
                    json = doc.RootElement.Clone();
                }
                catch (JsonException)
                {
                }

                calls.Add(new StageNowCall(args[0], args, json));
            }
            else
            {
                calls.Add(new StageNowCall(name, args, null));
            }
        }

        return calls;
    }
}
