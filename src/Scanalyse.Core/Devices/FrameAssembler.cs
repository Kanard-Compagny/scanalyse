namespace Scanalyse.Core.Devices;

/// <summary>
/// Regroupe les octets d'un port série en lectures complètes.
/// </summary>
/// <remarks>
/// Un scanner envoie chaque lecture d'un seul jet ; la lecture est terminée quand la ligne reste silencieuse
/// pendant <see cref="IdleGap"/>. On ne coupe pas sur CR ou LF : le suffixe (CR, CR+LF, Tab…) fait partie
/// de ce qu'on veut diagnostiquer et doit rester dans la lecture.
/// </remarks>
public sealed class FrameAssembler
{
    private readonly List<byte> _buffer = [];
    private DateTimeOffset _last;

    /// <param name="idleGap">Silence qui marque la fin d'une lecture.</param>
    /// <param name="maxLength">Taille maximale d'une lecture : au-delà, elle est livrée telle quelle.</param>
    public FrameAssembler(TimeSpan idleGap, int maxLength = 64 * 1024)
    {
        if (idleGap <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(idleGap));
        }

        IdleGap = idleGap;
        MaxLength = maxLength;
    }

    /// <summary>Silence qui marque la fin d'une lecture.</summary>
    public TimeSpan IdleGap { get; }

    /// <summary>Taille maximale d'une lecture.</summary>
    public int MaxLength { get; }

    /// <summary>Des octets attendent la fin de la lecture.</summary>
    public bool HasPending => _buffer.Count > 0;

    /// <summary>Ajoute des octets reçus à l'instant <paramref name="now"/> ; renvoie une lecture si la taille maximale est atteinte.</summary>
    public byte[]? Append(ReadOnlySpan<byte> bytes, DateTimeOffset now)
    {
        // Un silence plus long que l'écart attendu entre deux arrivées : la lecture précédente était finie.
        byte[]? done = null;
        if (_buffer.Count > 0 && now - _last >= IdleGap)
        {
            done = Take();
        }

        foreach (var b in bytes)
        {
            _buffer.Add(b);
        }

        _last = now;
        if (_buffer.Count >= MaxLength)
        {
            var full = Take();
            return done is null ? full : [.. done, .. full];
        }

        return done;
    }

    /// <summary>Renvoie la lecture en attente si la ligne est silencieuse depuis <see cref="IdleGap"/>.</summary>
    public byte[]? Poll(DateTimeOffset now) =>
        _buffer.Count > 0 && now - _last >= IdleGap ? Take() : null;

    /// <summary>Renvoie tout ce qui est en attente, sans attendre le silence.</summary>
    public byte[]? Flush() => _buffer.Count > 0 ? Take() : null;

    private byte[] Take()
    {
        var bytes = _buffer.ToArray();
        _buffer.Clear();
        return bytes;
    }
}
