using System.Diagnostics;
using System.Globalization;
using System.Runtime.Serialization;
using Du.NCrontab.Suppliment;
using Du.Properties;

namespace Du.NCrontab.Implement;

internal delegate T FieldAccumulator<T>(int start, int end, int interval, T success, Func<CrontabExceptionProvider, T> onError);

[Serializable]
internal class FieldImpl : IObjectReference
{
    public static readonly FieldImpl Second = new(CrontabFieldKind.Second, 0, 59, null);
    public static readonly FieldImpl Minute = new(CrontabFieldKind.Minute, 0, 59, null);
    public static readonly FieldImpl Hour = new(CrontabFieldKind.Hour, 0, 23, null);
    public static readonly FieldImpl Day = new(CrontabFieldKind.Day, 1, 31, null);
    public static readonly FieldImpl Month = new(CrontabFieldKind.Month, 1, 12, new[]
    {
        Resources.January,
        Resources.February,
        Resources.March,
        Resources.April,
        Resources.May,
        Resources.June,
        Resources.July,
        Resources.August,
        Resources.September,
        Resources.October,
        Resources.November,
        Resources.December
    });
    public static readonly FieldImpl DayOfWeek = new(CrontabFieldKind.DayOfWeek, 0, 6, new[]
    {
        Resources.Sunday,
        Resources.Monday,
        Resources.Tuesday,
        Resources.Wednesday,
        Resources.Thursday,
        Resources.Friday,
        Resources.Saturday
    });

    private static readonly FieldImpl[] FieldByKind = { Second, Minute, Hour, Day, Month, DayOfWeek };
    private static readonly CompareInfo Comparer = CultureInfo.InvariantCulture.CompareInfo;

    private static readonly char[] SeparatorComma = { ',' };

    private readonly string[]? _names; // TODO reconsider empty array == unnamed

    public CrontabFieldKind Kind { get; }
    public int MinValue { get; }
    public int MaxValue { get; }

    public int ValueCount => MaxValue - MinValue + 1;

    public static FieldImpl FromKind(CrontabFieldKind kind)
    {
        if (Enum.IsDefined(typeof(CrontabFieldKind), kind))
            return FieldByKind[(int)kind];

        var kinds = string.Join(", ",
            /*Enum.GetNames<CrontabFieldKind>()*/
            Enum.GetValues<CrontabFieldKind>().Select(i => i.GetDescription()));
        throw new ArgumentException($@"{Resources.ExceptionInvalidFieldKind}: {kinds}.", nameof(kind));
    }

    private FieldImpl(CrontabFieldKind kind, int minValue, int maxValue, string[]? names)
    {
        Debug.Assert(Enum.IsDefined(typeof(CrontabFieldKind), kind));
        Debug.Assert(minValue >= 0);
        Debug.Assert(maxValue >= minValue);
        Debug.Assert(names == null || names.Length == (maxValue - minValue + 1));

        Kind = kind;
        MinValue = minValue;
        MaxValue = maxValue;
        _names = names;
    }

    public void Format(CrontabField? field, TextWriter? writer) =>
        Format(field, writer, false);

    public void Format(CrontabField? field, TextWriter? writer, bool noNames)
    {
        if (field == null)
            throw new ArgumentNullException(nameof(field));
        if (writer == null)
            throw new ArgumentNullException(nameof(writer));

        var next = field.GetFirst();
        var count = 0;

        while (next != -1)
        {
            var first = next;
            int last;

            do
            {
                last = next;
                next = field.Next(last + 1);
            }
            while (next - last == 1);

            if (count != 0 || first != MinValue || last != MaxValue)
            {
                if (count > 0)
                    writer.Write(',');

                if (first == last)
                    FormatValue(first, writer, noNames);
                else
                {
                    FormatValue(first, writer, noNames);
                    writer.Write('-');
                    FormatValue(last, writer, noNames);
                }

                count++;
            }
            else
            {
                writer.Write('*');
                return;
            }
        }
    }

    private void FormatValue(int value, TextWriter writer, bool noNames)
    {
        if (noNames || _names == null)
        {
            if (value is >= 0 and < 100)
                FastFormatNumericValue(value, writer);
            else
                writer.Write(value.ToString(CultureInfo.InvariantCulture));
        }
        else
        {
            var index = value - MinValue;
            writer.Write(_names[index]);
        }
    }

    private static void FastFormatNumericValue(int value, TextWriter writer)
    {
        Debug.Assert(value is >= 0 and < 100);

        if (value < 10)
            writer.Write((char)('0' + value));
        else
        {
            writer.Write((char)('0' + (value / 10)));
            writer.Write((char)('0' + (value % 10)));
        }
    }

    public void Parse(string str, FieldAccumulator<CrontabExceptionProvider?> acc) =>
        TryParse(str, acc, null, ep => throw ep());

    public T TryParse<T>(string str, FieldAccumulator<T>? acc, T success,
                         Func<CrontabExceptionProvider, T> errorSelector)
    {
        if (acc == null)
            throw new ArgumentNullException(nameof(acc));

        if (string.IsNullOrEmpty(str))
            return success;

        try
        {
            return InternalParse(str, acc, success, errorSelector);
        }
        catch (FormatException e)
        {
            return OnParseException(e, str, errorSelector);
        }
        catch (CrontabException e)
        {
            return OnParseException(e, str, errorSelector);
        }
    }

    private T OnParseException<T>(Exception innerException, string str, Func<CrontabExceptionProvider, T> errorSelector) =>
        errorSelector(() =>
            new CrontabException(string.Format(Resources.ExceptionInvalidFieldExpression, str, Kind.GetDescription()), innerException));

    private T InternalParse<T>(string str, FieldAccumulator<T> acc, T success, Func<CrontabExceptionProvider, T> errorSelector)
    {
        if (str.Length == 0)
            return errorSelector(() => new CrontabException(Resources.ExceptionEmptyField));

        //
        // Completed, look for a list of values (e.g. 1,2,3).
        //
        var commaIndex = str.IndexOf(',');

        if (commaIndex > 0)
        {
            var result = success;
            using var token = ((IEnumerable<string>)str.Split(SeparatorComma)).GetEnumerator();
            while (token.MoveNext() && result == null)
                result = InternalParse(token.Current, acc, success, errorSelector);

            return result;
        }

        int? every = null;

        //
        // Look for stepping first (e.g. */2 = every 2nd).
        //
        var slashIndex = str.IndexOf('/');

        if (slashIndex > 0)
        {
            every = int.Parse(str[(slashIndex + 1)..], CultureInfo.InvariantCulture);
            str = str[..slashIndex];
        }

        //
        // Completed, look for wildcard (*).
        //
        if (str is ['*'])
            return acc(-1, -1, every ?? 1, success, errorSelector);

        //
        // Completed, look for a range of values (e.g. 2-10).
        //
        var dashIndex = str.IndexOf('-');

        if (dashIndex > 0)
        {
            var first = ParseValue(str[..dashIndex]);
            var last = ParseValue(str[(dashIndex + 1)..]);

            return acc(first, last, every ?? 1, success, errorSelector);
        }

        //
        // Finally, handle the case where there is only one number.
        //
        var value = ParseValue(str);

        if (every == null)
            return acc(value, value, 1, success, errorSelector);

        Debug.Assert(every != 0);
        return acc(value, MaxValue, every.Value, success, errorSelector);
    }

    private int ParseValue(string str)
    {
        if (str.Length == 0)
            throw new CrontabException(Resources.ExceptionEmptyField);

        var firstChar = str[0];

        if (firstChar is >= '0' and <= '9')
            return int.Parse(str, CultureInfo.InvariantCulture);

        if (_names == null)
        {
            throw new CrontabException(string.Format(Resources.ExceptionInvalidFieldExpressionParse,
                str, MinValue, MaxValue, Kind.GetDescription()));
        }

        for (var i = 0; i < _names.Length; i++)
        {
            if (Comparer.IsPrefix(_names[i], str, CompareOptions.IgnoreCase))
                return i + MinValue;
        }

        throw new CrontabException(string.Format(Resources.ExceptionNotKnowFollow, str, string.Join(", ", _names)));
    }

    public object GetRealObject(StreamingContext context) => FromKind(Kind);
}
