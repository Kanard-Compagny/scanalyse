using System.Globalization;
using System.Text;

namespace Scanalyse.Core.Printing;

/// <summary>
/// Requêtes SNMP v1 « get » minimales (encodage BER), pour identifier n'importe quelle imprimante réseau : description,
/// modèle, numéro de série et langages (MIB imprimante, RFC 3805). Lecture seule, communauté « public » par défaut.
/// </summary>
public static class Snmp
{
    /// <summary>Description du système (marque, modèle, firmware selon le fabricant).</summary>
    public const string SysDescr = "1.3.6.1.2.1.1.1.0";

    /// <summary>Nom du système.</summary>
    public const string SysName = "1.3.6.1.2.1.1.5.0";

    /// <summary>Description du périphérique (souvent « marque modèle »).</summary>
    public const string DeviceDescr = "1.3.6.1.2.1.25.3.2.1.3.1";

    /// <summary>Numéro de série (MIB imprimante).</summary>
    public const string SerialNumber = "1.3.6.1.2.1.43.5.1.1.17.1";

    /// <summary>Description de l'interpréteur n (langage) de la MIB imprimante : préfixe, suivi de « .n ».</summary>
    public const string InterpreterDescrPrefix = "1.3.6.1.2.1.43.15.1.1.5.1";

    /// <summary>Requête « get » pour un OID.</summary>
    public static byte[] BuildGet(string oid, int requestId, string community = "public")
    {
        ArgumentNullException.ThrowIfNull(oid);
        ArgumentNullException.ThrowIfNull(community);
        var varbind = Tlv(0x30, [.. Tlv(0x06, EncodeOid(oid)), .. Tlv(0x05, [])]);
        var pdu = Tlv(0xA0, [.. Integer(requestId), .. Integer(0), .. Integer(0), .. Tlv(0x30, varbind)]);
        return Tlv(0x30, [.. Integer(0), .. Tlv(0x04, Encoding.ASCII.GetBytes(community)), .. pdu]);
    }

    /// <summary>
    /// Lit une réponse : identifiant de requête, code d'erreur SNMP (0 = aucune) et valeurs reçues (texte ; OID en
    /// notation pointée ; nombre en décimal). Null si le paquet n'est pas une réponse SNMP lisible.
    /// </summary>
    public static (int RequestId, int Error, IReadOnlyList<(string Oid, string? Value)> Values)? ParseResponse(byte[] packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        try
        {
            var pos = 0;
            Expect(packet, ref pos, 0x30);
            Read(packet, ref pos, 0x02); // version
            Read(packet, ref pos, 0x04); // communauté
            Expect(packet, ref pos, 0xA2);
            var requestId = (int)ToInteger(Read(packet, ref pos, 0x02));
            var error = (int)ToInteger(Read(packet, ref pos, 0x02));
            Read(packet, ref pos, 0x02); // index de l'erreur
            var listEnd = Expect(packet, ref pos, 0x30);
            var values = new List<(string, string?)>();
            while (pos < listEnd)
            {
                Expect(packet, ref pos, 0x30);
                var oid = DecodeOid(Read(packet, ref pos, 0x06));
                var tag = packet[pos];
                var raw = Read(packet, ref pos, tag);
                string? value = tag switch
                {
                    0x04 => Text(raw),
                    0x06 => DecodeOid(raw),
                    0x02 or 0x41 or 0x42 or 0x43 => ToInteger(raw).ToString(CultureInfo.InvariantCulture),
                    _ => null, // NULL, noSuchObject…
                };
                values.Add((oid, value));
            }

            return (requestId, error, values);
        }
        catch (Exception ex) when (ex is IndexOutOfRangeException or ArgumentOutOfRangeException or FormatException or OverflowException)
        {
            return null;
        }
    }

    private static string Text(byte[] raw)
    {
        var s = Encoding.UTF8.GetString(raw);
        return new string([.. s.Where(c => !char.IsControl(c) || c is '\r' or '\n')]).Trim();
    }

    private static byte[] Integer(long value)
    {
        var bytes = new List<byte>();
        do
        {
            bytes.Insert(0, (byte)(value & 0xFF));
            value >>= 8;
        }
        while (value != 0 && value != -1);
        if ((bytes[0] & 0x80) != 0 && value == 0)
        {
            bytes.Insert(0, 0);
        }

        return Tlv(0x02, [.. bytes]);
    }

    private static long ToInteger(byte[] raw)
    {
        long v = raw.Length > 0 && (raw[0] & 0x80) != 0 ? -1 : 0;
        foreach (var b in raw)
        {
            v = (v << 8) | b;
        }

        return v;
    }

    private static byte[] Tlv(byte tag, byte[] content)
    {
        var length = content.Length;
        byte[] len = length < 0x80 ? [(byte)length]
            : length < 0x100 ? [0x81, (byte)length]
            : [0x82, (byte)(length >> 8), (byte)length];
        return [tag, .. len, .. content];
    }

    private static byte[] EncodeOid(string oid)
    {
        var parts = oid.Split('.').Select(p => long.Parse(p, CultureInfo.InvariantCulture)).ToArray();
        var bytes = new List<byte> { (byte)((parts[0] * 40) + parts[1]) };
        foreach (var p in parts.Skip(2))
        {
            var chunk = new List<byte> { (byte)(p & 0x7F) };
            var rest = p >> 7;
            while (rest > 0)
            {
                chunk.Insert(0, (byte)(0x80 | (rest & 0x7F)));
                rest >>= 7;
            }

            bytes.AddRange(chunk);
        }

        return [.. bytes];
    }

    private static string DecodeOid(byte[] raw)
    {
        if (raw.Length == 0)
        {
            return string.Empty;
        }

        var parts = new List<long> { raw[0] / 40, raw[0] % 40 };
        long current = 0;
        foreach (var b in raw.Skip(1))
        {
            current = (current << 7) | (long)(b & 0x7F);
            if ((b & 0x80) == 0)
            {
                parts.Add(current);
                current = 0;
            }
        }

        return string.Join('.', parts);
    }

    /// <summary>Lit l'en-tête d'un élément construit et renvoie la position de sa fin.</summary>
    private static int Expect(byte[] p, ref int pos, byte tag)
    {
        if (p[pos] != tag)
        {
            throw new FormatException();
        }

        pos++;
        var length = Length(p, ref pos);
        return pos + length;
    }

    /// <summary>Lit un élément simple et renvoie son contenu.</summary>
    private static byte[] Read(byte[] p, ref int pos, byte tag)
    {
        if (p[pos] != tag)
        {
            throw new FormatException();
        }

        pos++;
        var length = Length(p, ref pos);
        var content = p[pos..(pos + length)];
        pos += length;
        return content;
    }

    private static int Length(byte[] p, ref int pos)
    {
        var first = p[pos++];
        if (first < 0x80)
        {
            return first;
        }

        var count = first & 0x7F;
        var length = 0;
        for (var i = 0; i < count; i++)
        {
            length = (length << 8) | p[pos++];
        }

        return length;
    }
}
