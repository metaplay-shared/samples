namespace WebClient.Meta;

/// <summary>
/// One sprite in the reward animation. It plays an emit stage from the reward icon to its emit point, then a travel
/// stage to its currency's HUD chip (meta-shell.css defines both). All values are precomputed because the shell re-renders
/// while sprites fly, and values computed at render time would change each sprite's path on every render.
/// </summary>
/// <param name="Index">Position in its currency's burst. Also the seed for the random values.</param>
/// <param name="DelayMs">Milliseconds from the start of the whole plan to the start of the emit stage.</param>
/// <param name="EmitMs">The emit stage's duration in milliseconds.</param>
/// <param name="TravelMs">The travel stage's duration in milliseconds.</param>
/// <param name="EmitX">Horizontal offset of the emit point from the icon, in burst radii.</param>
/// <param name="EmitY">Vertical offset of the emit point from the icon, in burst radii.</param>
/// <param name="Spin">Rotation in degrees over both stages. Negative values rotate the other way.</param>
/// <param name="Scale">Size multiplier, within the range from <see cref="WalletBurstPlan.ScaleRangeOf"/>.</param>
public readonly record struct BurstSprite(
    int    Index,
    int    DelayMs,
    int    EmitMs,
    int    TravelMs,
    double EmitX,
    double EmitY,
    double Spin,
    double Scale)
{
    /// <summary>The time in milliseconds from the start of the plan until this sprite reaches the HUD chip.</summary>
    public int LandsAtMs => DelayMs + EmitMs + TravelMs;
}

/// <summary>One currency's part of the animation: the amount and the sprites that carry it.</summary>
public sealed record CurrencyBurst(CurrencyKind Currency, long Amount, IReadOnlyList<BurstSprite> Sprites)
{
    /// <summary>
    /// The time in milliseconds until the last sprite lands, when the HUD shows the full balance again.
    /// </summary>
    public int LandsAtMs => Sprites.Count == 0 ? 0 : Sprites.Max(sprite => sprite.LandsAtMs);

    /// <summary>The number of sprites that have landed by <paramref name="elapsedMs"/>.</summary>
    public int LandedBy(int elapsedMs) => Sprites.Count(sprite => sprite.LandsAtMs <= elapsedMs);
}

/// <summary>
/// The animation plan for a reward flying to the HUD: one <see cref="CurrencyBurst"/> per currency. Non-currency
/// items get no animation. The plan is computed here so that tests can check it without a browser, and
/// <c>WalletBurstLayer</c> only renders it. All stage durations are fractions of the sequence length, which is
/// <see cref="DefaultBurstMs"/> unless a test overrides it with the <c>burstMs</c> query parameter.
/// </summary>
public sealed record WalletBurstPlan(IReadOnlyList<CurrencyBurst> Bursts)
{
    public static readonly WalletBurstPlan Empty = new WalletBurstPlan(Array.Empty<CurrencyBurst>());

    /// <summary>
    /// The default length in milliseconds of one currency's animation, covering the delays, the emit stage and the
    /// travel stage.
    /// </summary>
    public const int DefaultBurstMs = 800;

    /// <summary>The emit stage's fraction of the sequence length.</summary>
    private const double EmitFraction = 0.22;

    /// <summary>The travel stage's fraction of the sequence length.</summary>
    private const double TravelFraction = 0.40;

    /// <summary>
    /// The fraction of the sequence length over which sprite start delays are spread. The first sprite starts
    /// with no delay, and the last one waits this whole fraction, so it lands exactly at the end of the sequence.
    /// </summary>
    private const double DelayFraction = 0.38;

    /// <summary>
    /// The delay between the starts of consecutive currencies in one reward, as a fraction of the sequence length.
    /// It keeps bursts to different HUD chips visibly separate. With the default values, a reward with every
    /// currency must still finish within <see cref="WalletBurstBalances.MaxAnimationMs"/>.
    /// </summary>
    private const double CurrencyOffsetFraction = 0.15;

    /// <summary>
    /// The minimum distance of an emit point from the icon, as a fraction of the burst radius, so that every sprite
    /// visibly moves away from the icon.
    /// </summary>
    private const double MinEmitRadius = 0.55;

    public bool IsEmpty => Bursts.Count == 0;

    /// <summary>The time in milliseconds until the last sprite of any currency lands.</summary>
    public int DurationMs => Bursts.Count == 0 ? 0 : Bursts.Max(burst => burst.LandsAtMs);

    /// <summary>The total number of sprites across all currencies.</summary>
    public int SpriteCount => Bursts.Sum(burst => burst.Sprites.Count);

    public CurrencyBurst? Of(CurrencyKind kind) => Bursts.FirstOrDefault(burst => burst.Currency == kind);

    /// <summary>
    /// The plan for a reward. Only items where <see cref="RewardViewItem.TravelsToWallet"/> is true are animated.
    /// Returns <see cref="Empty"/> if nothing is animated or <paramref name="burstMs"/> is not positive.
    /// <para>
    /// Items of the same currency are combined into one burst of their total, so that one HUD chip never receives
    /// two overlapping bursts.
    /// </para>
    /// </summary>
    public static WalletBurstPlan For(RewardView bundle, int burstMs = DefaultBurstMs)
    {
        if (bundle.IsEmpty || burstMs <= 0)
            return Empty;

        List<CurrencyBurst> bursts = new List<CurrencyBurst>();

        // Use CurrencyKind order instead of bundle order, so that the same reward always animates its currencies
        // in the same order.
        foreach (CurrencyKind kind in Enum.GetValues<CurrencyKind>())
        {
            long amount = bundle.Items
                .Where(item => item.TravelsToWallet && item.Currency == kind)
                .Sum(item => item.Amount);

            if (amount <= 0)
                continue;

            int startMs = (int)Math.Round(CurrencyOffsetFraction * burstMs) * bursts.Count;
            bursts.Add(new CurrencyBurst(kind, amount, SpritesFor(kind, amount, startMs, burstMs)));
        }

        return bursts.Count == 0 ? Empty : new WalletBurstPlan(bursts);
    }

    private static IReadOnlyList<BurstSprite> SpritesFor(CurrencyKind kind, long amount, int startMs, int burstMs)
    {
        int spriteCount = SpriteCountFor(kind, amount);
        int emitMs      = (int)Math.Round(EmitFraction * burstMs);
        int travelMs    = (int)Math.Round(TravelFraction * burstMs);
        int delaySpanMs = (int)Math.Round(DelayFraction * burstMs);

        // The maximum random offset added to each sprite's delay. It is at most one gap between evenly spaced
        // sprites, so sprites stay in roughly the same order.
        int maxJitterMs = delaySpanMs / spriteCount;

        (double smallest, double largest) = ScaleRangeOf(kind);

        BurstSprite[] sprites = new BurstSprite[spriteCount];
        int seed = (int)kind;

        for (int i = 0; i < spriteCount; i++)
        {
            // Spread the delays evenly from zero to delaySpan and add a random offset to each sprite except the
            // first. Clamp at delaySpan so that no sprite lands after the end of the sequence.
            double spreadFraction = spriteCount == 1 ? 0.0 : (double)i / (spriteCount - 1);
            int    jitterMs       = i == 0 ? 0 : (int)SeededValueBetween(seed, i, 2, 0, maxJitterMs);
            int    delayMs        = startMs + Math.Min(delaySpanMs, (int)Math.Round(delaySpanMs * spreadFraction) + jitterMs);

            // A random angle and a random radius between MinEmitRadius and 1, so the emit points fill a disc
            // instead of a ring. Computed here because the CSS does no trigonometry.
            double angle  = SeededValueBetween(seed, i, 1, 0, 360) * Math.PI / 180.0;
            double radius = SeededValueBetween(seed, i, 3, MinEmitRadius, 1.0);

            sprites[i] = new BurstSprite(
                Index:    i,
                DelayMs:  delayMs,
                EmitMs:   emitMs,
                TravelMs: travelMs,
                EmitX:    Math.Cos(angle) * radius,
                EmitY:    Math.Sin(angle) * radius,
                Spin:     SeededValueBetween(seed, i, 4, -540, 540),
                Scale:    SeededValueBetween(seed, i, 5, smallest, largest));
        }

        return sprites;
    }

    /// <summary>
    /// The size range of one currency's sprites. Gems have a narrower range than other currencies because
    /// gem sprites with widely different sizes look like broken glass.
    /// </summary>
    public static (double Smallest, double Largest) ScaleRangeOf(CurrencyKind kind) => kind switch
    {
        CurrencyKind.Gems => (0.86, 1.06),
        _                 => (0.7, 1.15),
    };

    /// <summary>
    /// The number of sprites for an amount of a currency. Larger amounts get more sprites, up to the currency's
    /// maximum.
    /// <para>
    /// The minimum sprite count is set high enough that even the smallest reward looks like a burst instead of a
    /// few scattered sprites.
    /// </para>
    /// </summary>
    public static int SpriteCountFor(CurrencyKind kind, long amount)
    {
        if (amount <= 0)
            return 0;

        CurrencyScale scale = ScaleOf(kind);

        // Scale logarithmically between Floor and Cap, so that differences between small rewards are visible
        // and differences between large rewards are compressed. Amounts at or below Floor get MinSprites.
        double amountFraction = amount <= scale.Floor
            ? 0.0
            : Math.Clamp(
                Math.Log((double)amount / scale.Floor) / Math.Log((double)scale.Cap / scale.Floor),
                0.0,
                1.0);

        return scale.MinSprites + (int)Math.Round((scale.MaxSprites - scale.MinSprites) * amountFraction);
    }

    /// <summary>
    /// A currency's typical reward range (<c>Floor</c> to <c>Cap</c>) and the sprite counts for each end.
    /// <para>
    /// Each currency has its own scale because typical amounts differ between currencies. Spin tokens are
    /// granted a few at a time, so they get fewer sprites.
    /// </para>
    /// </summary>
    private readonly record struct CurrencyScale(long Floor, long Cap, int MinSprites, int MaxSprites);

    private static CurrencyScale ScaleOf(CurrencyKind kind) => kind switch
    {
        CurrencyKind.Coins      => new CurrencyScale(Floor: 50, Cap: 1_200, MinSprites: 18, MaxSprites: 36),
        CurrencyKind.Gems       => new CurrencyScale(Floor: 5,  Cap: 120,   MinSprites: 18, MaxSprites: 36),
        CurrencyKind.SpinTokens => new CurrencyScale(Floor: 1,  Cap: 5,     MinSprites: 14, MaxSprites: 22),
        // A currency without its own scale uses the smallest minimum sprite count of the listed currencies and a
        // low maximum, because its typical amounts are unknown.
        _                       => new CurrencyScale(Floor: 1,  Cap: 10,    MinSprites: 14, MaxSprites: 18),
    };

    /// <summary>
    /// A pseudo-random value in [<paramref name="from"/>, <paramref name="to"/>) hashed from the seed, sprite index and
    /// salt. A shared <c>Random</c> would give a sprite a different value depending on how many values were drawn
    /// before it.
    /// </summary>
    private static double SeededValueBetween(int seed, int index, int salt, double from, double to)
    {
        unchecked
        {
            uint hash = (uint)(seed * 374_761_393 + index * 668_265_263 + salt * 2_246_822_519);
            hash = (hash ^ (hash >> 13)) * 1_274_126_177u;
            hash ^= hash >> 16;
            return from + (to - from) * ((hash & 0xFFFFFF) / (double)0x1000000);
        }
    }
}
