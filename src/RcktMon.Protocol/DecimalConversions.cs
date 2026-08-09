using ProtoDecimal = RcktMon.Protocol.V1.Decimal;

namespace RcktMon.Protocol;

/// <summary>
/// Conversions between the wire <see cref="ProtoDecimal"/> and <see cref="decimal"/>.
/// </summary>
/// <remarks>
/// <para>
/// The wire type is fixed-point — <c>value = units + nano / 1e9</c> — and mirrors the
/// T-Invest <c>Quotation</c>/<c>MoneyValue</c> shape, so quotes cross the whole system
/// without ever passing through a binary floating-point type.
/// </para>
/// <para>
/// Note this class is deliberately not called <c>DecimalExtensions</c> and never uses the
/// bare identifier <c>Decimal</c>: inside <c>RcktMon.Protocol</c>, <c>Decimal</c> resolves
/// to the generated protobuf message, not to <see cref="System.Decimal"/>. The
/// <c>decimal</c> keyword is unambiguous and is used throughout.
/// </para>
/// </remarks>
public static class DecimalConversions
{
    private const decimal NanoFactor = 1_000_000_000m;

    /// <summary>Number of fractional digits the wire format can represent.</summary>
    public const int WireScale = 9;

    /// <summary>Converts a wire value to <see cref="decimal"/>. A null message is 0.</summary>
    public static decimal ToDecimal(this ProtoDecimal? value)
        => value is null ? 0m : value.Units + value.Nano / NanoFactor;

    /// <summary>
    /// Converts an optional wire value, preserving the distinction between "absent"
    /// and "present and zero". Use this for <c>QuoteDelta</c> fields, where absent
    /// means <em>unchanged</em> and zero is a legitimate value.
    /// </summary>
    public static decimal? ToDecimalOrNull(this ProtoDecimal? value)
        => value is null ? null : value.Units + value.Nano / NanoFactor;

    /// <summary>Converts a <see cref="decimal"/> to the wire representation.</summary>
    /// <exception cref="OverflowException">
    /// The integral part does not fit in <see cref="long"/>. Real prices and volumes are
    /// nowhere near this, so hitting it means the value is corrupt and should fail loudly
    /// rather than wrap silently.
    /// </exception>
    public static ProtoDecimal ToProto(this decimal value)
    {
        // Round to the wire scale *before* splitting. Doing it in this order guarantees
        // `rounded - units` has at most 9 decimal places, so multiplying by 1e9 is exact
        // and `nano` can never land outside (-1e9, 1e9). Splitting first and rounding
        // after is the version of this function that has a carry bug in it.
        //
        // AwayFromZero rather than banker's rounding: at 1e-9 the choice is numerically
        // irrelevant, but it matches what people expect of money.
        decimal rounded = decimal.Round(value, WireScale, MidpointRounding.AwayFromZero);
        decimal units = decimal.Truncate(rounded);

        checked
        {
            return new ProtoDecimal
            {
                Units = (long)units,
                // Same sign as Units, per the T-Invest convention.
                Nano = (int)((rounded - units) * NanoFactor),
            };
        }
    }

    /// <summary>Converts an optional <see cref="decimal"/>, mapping null to null.</summary>
    public static ProtoDecimal? ToProtoOrNull(this decimal? value)
        => value is null ? null : ToProto(value.Value);

    /// <summary>True when the value is absent or exactly zero.</summary>
    public static bool IsZero(this ProtoDecimal? value)
        => value is null || (value.Units == 0 && value.Nano == 0);
}

/// <summary>
/// Encodes the 60-point sparkline carried on <c>Quote.sparkline</c>.
/// </summary>
/// <remarks>
/// Points are basis points relative to the day's open, as zigzag-encoded
/// <c>sint32</c> — about 90 bytes per instrument, against several hundred for a
/// repeated <c>Decimal</c>. One basis point is well below the resolution of a
/// sparkline a few dozen pixels tall, so nothing visible is lost.
/// </remarks>
public static class SparklineCodec
{
    /// <summary>Number of points a full sparkline carries (one per minute, last hour).</summary>
    public const int Points = 60;

    private const decimal BasisPoints = 10_000m;

    /// <summary>Encodes closing prices as basis points relative to <paramref name="dayOpen"/>.</summary>
    /// <returns>
    /// An empty array when <paramref name="dayOpen"/> is not positive — there is no
    /// meaningful baseline to measure against, and the client renders "no data" rather
    /// than a misleading flat line.
    /// </returns>
    public static int[] Encode(ReadOnlySpan<decimal> closes, decimal dayOpen)
    {
        if (dayOpen <= 0m || closes.IsEmpty)
        {
            return [];
        }

        var result = new int[closes.Length];
        for (int i = 0; i < closes.Length; i++)
        {
            decimal bp = (closes[i] - dayOpen) / dayOpen * BasisPoints;
            result[i] = (int)decimal.Round(bp, 0, MidpointRounding.AwayFromZero);
        }

        return result;
    }

    /// <summary>Reconstructs approximate prices from an encoded sparkline.</summary>
    public static decimal[] Decode(IReadOnlyList<int> basisPoints, decimal dayOpen)
    {
        if (dayOpen <= 0m || basisPoints.Count == 0)
        {
            return [];
        }

        var result = new decimal[basisPoints.Count];
        for (int i = 0; i < basisPoints.Count; i++)
        {
            result[i] = dayOpen * (1m + basisPoints[i] / BasisPoints);
        }

        return result;
    }
}
