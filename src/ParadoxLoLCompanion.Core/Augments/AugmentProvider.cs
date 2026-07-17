namespace ParadoxLoLCompanion.Core.Augments;

/// <summary>
/// Cache-first como StatsProvider: miss → fetch a Blitz → parse → cachear.
/// <c>null</c> ante cualquier fallo; la app funciona igual sin tier list.
/// </summary>
public sealed class AugmentProvider
{
    /// <summary>Un parse con menos que esto es un redesign de Blitz, no datos:
    /// mejor sin tier list que con uno truncado cacheado 48 h.</summary>
    internal const int MinCredibleAugments = 100;

    private readonly IBlitzAugmentSource _source;
    private readonly AugmentCache _cache;
    private readonly Action<string>? _log;

    public AugmentProvider(IBlitzAugmentSource? source = null, AugmentCache? cache = null,
        Action<string>? log = null)
    {
        _source = source ?? new BlitzAugmentClient(log: log);
        _cache = cache ?? new AugmentCache();
        _log = log;
    }

    public async Task<AugmentTierList?> GetAsync(string patch, CancellationToken ct = default)
    {
        if (_cache.TryRead(patch, out var cached))
            return cached;

        var html = await _source.GetAugmentsHtmlAsync(ct).ConfigureAwait(false);
        if (html is null)
            return null;

        var list = BlitzAugmentParser.Parse(html);
        if (list.Augments.Count < MinCredibleAugments)
        {
            _log?.Invoke(
                $"Blitz augments: parse produced {list.Augments.Count} augments — page layout changed? Ignoring.");
            return null;
        }
        _cache.Write(patch, list);
        return list;
    }
}
