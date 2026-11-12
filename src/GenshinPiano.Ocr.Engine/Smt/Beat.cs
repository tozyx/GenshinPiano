using System.Numerics;

namespace GenshinPiano.Ocr.Engine.Smt;

internal readonly record struct Beat : IComparable<Beat>
{
    public BigInteger Numerator { get; }
    private readonly BigInteger _denominatorMinusOne;
    public BigInteger Denominator => _denominatorMinusOne + BigInteger.One;
    private BigInteger Divisor => Denominator;
    public Beat(BigInteger numerator, BigInteger denominator)
    {
        if (denominator.IsZero) throw new DivideByZeroException();
        if (denominator.Sign < 0) { numerator = -numerator; denominator = -denominator; }
        var gcd = BigInteger.GreatestCommonDivisor(BigInteger.Abs(numerator), denominator);
        Numerator = numerator / gcd;
        _denominatorMinusOne = denominator / gcd - BigInteger.One;
    }
    public static Beat Zero => new(0, 1);
    public static implicit operator Beat(int value) => new(value, 1);
    public static Beat operator +(Beat a, Beat b) => new(a.Numerator * b.Divisor + b.Numerator * a.Divisor, a.Divisor * b.Divisor);
    public static Beat operator -(Beat a, Beat b) => new(a.Numerator * b.Divisor - b.Numerator * a.Divisor, a.Divisor * b.Divisor);
    public static Beat operator *(Beat a, int value) => new(a.Numerator * value, a.Divisor);
    public static Beat operator /(Beat a, int value) => new(a.Numerator, a.Divisor * value);
    public static bool operator <(Beat a, Beat b) => a.CompareTo(b) < 0;
    public static bool operator >(Beat a, Beat b) => a.CompareTo(b) > 0;
    public static bool operator <=(Beat a, Beat b) => a.CompareTo(b) <= 0;
    public static bool operator >=(Beat a, Beat b) => a.CompareTo(b) >= 0;
    public int CompareTo(Beat other) => (Numerator * other.Divisor).CompareTo(other.Numerator * Divisor);
    public double Value => (double)Numerator / (double)Divisor;
    public Beat Abs() => new(BigInteger.Abs(Numerator), Divisor);
    public long Ticks(int ppq = 480)
    {
        var value = BigInteger.DivRem(Numerator * ppq, Divisor, out var remainder);
        if (BigInteger.Abs(remainder) * 2 >= Divisor) value += Numerator.Sign;
        return checked((long)value);
    }
    public override string ToString() => Divisor.IsOne ? Numerator.ToString() : $"{Numerator}/{Divisor}";
}
