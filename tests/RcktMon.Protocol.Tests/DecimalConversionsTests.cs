using RcktMon.Protocol;
using Xunit;
using ProtoDecimal = RcktMon.Protocol.V1.Decimal;

namespace RcktMon.Protocol.Tests;

public class DecimalConversionsTests
{
    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("-1")]
    [InlineData("0.01")]
    [InlineData("-0.01")]
    [InlineData("123.456789")]
    [InlineData("-123.456789")]
    // A ruble price with the smallest increment MOEX actually quotes.
    [InlineData("0.0001")]
    // Full wire precision.
    [InlineData("0.000000001")]
    [InlineData("-0.000000001")]
    // Turnover-sized values: day volume cost runs into the billions.
    [InlineData("999999999.999999999")]
    [InlineData("-999999999.999999999")]
    public void RoundTrips_exactly(string literal)
    {
        decimal original = decimal.Parse(literal, System.Globalization.CultureInfo.InvariantCulture);

        decimal actual = original.ToProto().ToDecimal();

        Assert.Equal(original, actual);
    }

    [Fact]
    public void Units_and_nano_share_a_sign()
    {
        // The T-Invest convention, which the wire format mirrors: a negative value has
        // both components negative. Mixed signs would decode to the wrong number.
        ProtoDecimal negative = (-1.5m).ToProto();

        Assert.Equal(-1, negative.Units);
        Assert.Equal(-500_000_000, negative.Nano);
    }

    [Fact]
    public void Carries_into_units_instead_of_overflowing_nano()
    {
        // This is the case that breaks the naive implementation. Splitting first and
        // rounding second gives nano == 1_000_000_000, which is outside the legal range
        // and decodes as 1.0 too large. Rounding to the wire scale first makes it
        // impossible.
        decimal justUnderTwo = 1.9999999999m;

        ProtoDecimal encoded = justUnderTwo.ToProto();

        Assert.Equal(2, encoded.Units);
        Assert.Equal(0, encoded.Nano);
        Assert.InRange(encoded.Nano, -999_999_999, 999_999_999);
        Assert.Equal(2m, encoded.ToDecimal());
    }

    [Fact]
    public void Carries_for_negative_values_too()
    {
        ProtoDecimal encoded = (-1.9999999999m).ToProto();

        Assert.Equal(-2, encoded.Units);
        Assert.Equal(0, encoded.Nano);
        Assert.Equal(-2m, encoded.ToDecimal());
    }

    [Fact]
    public void Nano_stays_in_range_across_a_sweep_of_near_unit_values()
    {
        // Property-ish sweep over the region where carry mistakes hide.
        for (int i = 1; i <= 20; i++)
        {
            decimal value = 1m - (decimal)i / 1_000_000_000_0m;

            ProtoDecimal encoded = value.ToProto();

            Assert.InRange(encoded.Nano, -999_999_999, 999_999_999);
            Assert.False(encoded.Units > 0 && encoded.Nano < 0, $"mixed sign at i={i}");
            Assert.False(encoded.Units < 0 && encoded.Nano > 0, $"mixed sign at i={i}");
        }
    }

    [Fact]
    public void Null_decodes_to_zero_but_optional_decode_preserves_absence()
    {
        // QuoteDelta relies on this distinction: an absent field means UNCHANGED, and a
        // price of exactly zero is a legitimate value, so the two cannot share an
        // encoding.
        ProtoDecimal? absent = null;

        Assert.Equal(0m, absent.ToDecimal());
        Assert.Null(absent.ToDecimalOrNull());
        Assert.Equal(0m, new ProtoDecimal().ToDecimalOrNull());
        Assert.True(absent.IsZero());
        Assert.True(new ProtoDecimal().IsZero());
        Assert.False(1m.ToProto().IsZero());
    }

    [Fact]
    public void Rounds_beyond_wire_scale_rather_than_truncating()
    {
        // 10 decimal places: the last digit cannot survive, and should round rather than
        // silently vanish.
        Assert.Equal(0.000000002m, 0.0000000015m.ToProto().ToDecimal());
        Assert.Equal(0.000000001m, 0.0000000014m.ToProto().ToDecimal());
    }

    [Fact]
    public void Overflowing_the_integral_part_throws_rather_than_wrapping()
    {
        decimal tooBig = (decimal)long.MaxValue + 1000m;

        Assert.Throws<OverflowException>(() => tooBig.ToProto());
    }
}

public class SparklineCodecTests
{
    [Fact]
    public void Encodes_basis_points_relative_to_the_day_open()
    {
        decimal[] closes = [100m, 101m, 99m, 107.42m];

        int[] encoded = SparklineCodec.Encode(closes, dayOpen: 100m);

        Assert.Equal([0, 100, -100, 742], encoded);
    }

    [Fact]
    public void Returns_empty_when_there_is_no_usable_baseline()
    {
        // A zero or negative open means the instrument has not traded today. Rendering a
        // flat line would claim "no movement", which is a different and wrong statement.
        Assert.Empty(SparklineCodec.Encode([100m, 101m], dayOpen: 0m));
        Assert.Empty(SparklineCodec.Encode([100m, 101m], dayOpen: -1m));
        Assert.Empty(SparklineCodec.Encode([], dayOpen: 100m));
        Assert.Empty(SparklineCodec.Decode([], dayOpen: 100m));
        Assert.Empty(SparklineCodec.Decode([1, 2], dayOpen: 0m));
    }

    [Fact]
    public void Decode_reconstructs_prices_within_one_basis_point()
    {
        decimal dayOpen = 250.75m;
        decimal[] closes = [250.75m, 255.20m, 248.10m, 262.99m];

        decimal[] roundTripped = SparklineCodec.Decode(
            SparklineCodec.Encode(closes, dayOpen),
            dayOpen);

        Assert.Equal(closes.Length, roundTripped.Length);
        for (int i = 0; i < closes.Length; i++)
        {
            // One basis point of the open is the encoding's resolution by construction.
            Assert.InRange(
                Math.Abs(roundTripped[i] - closes[i]),
                0m,
                dayOpen / 10_000m);
        }
    }
}
