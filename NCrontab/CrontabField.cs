using System.Collections;
using System.Globalization;
using Du.NCrontab.Implement;
using Du.NCrontab.Suppliment;
using Du.Properties;

namespace Du.NCrontab;

#pragma warning disable CS1573
/// <summary>
/// 단일 크론탭 필드 표현
/// </summary>
[Serializable]
public sealed class CrontabField
{
    private readonly BitArray _bits;
    private readonly FieldImpl _impl;

    private int MinValueSet { get; set; }
    private int MaxValueSet { get; set; }

    /// <summary>
    /// 주어진 종류에 따른 크론탭 필드를 분석해요
    /// </summary>
    public static CrontabField Parse(CrontabFieldKind kind, string expression) =>
        TryParse(kind, expression, v => v, e => throw e());

    /// <summary>
    /// 주어진 종류에 따른 크론탭 필드를 분석해요
    /// </summary>
    /// <param name="kind"></param>
    /// <param name="expression"></param>
    /// <returns></returns>
    public static CrontabField TryParse(CrontabFieldKind kind, string expression) =>
        TryParse(kind, expression, v => v, _ => null!);

    /// <summary>
    /// 주어진 종류에 따른 크론탭 필드를 분석해요
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="kind"></param>
    /// <param name="expression"></param>
    /// <param name="valueSelector"></param>
    /// <param name="errorSelector"></param>
    /// <returns></returns>
    public static T TryParse<T>(CrontabFieldKind kind, string expression,
                                Func<CrontabField, T> valueSelector,
                                Func<CrontabExceptionProvider, T> errorSelector)
    {
        var field = new CrontabField(FieldImpl.FromKind(kind));
        var error = field._impl.TryParse(expression, field.Accumulate, null, e => e);
        return error == null ? valueSelector(field) : errorSelector(error);
    }

    /// <summary>
    /// Parses a crontab field expression representing seconds.
    /// </summary>
    public static CrontabField Seconds(string expression) =>
        Parse(CrontabFieldKind.Second, expression);

    /// <summary>
    /// Parses a crontab field expression representing minutes.
    /// </summary>
    public static CrontabField Minutes(string expression) =>
        Parse(CrontabFieldKind.Minute, expression);

    /// <summary>
    /// Parses a crontab field expression representing hours.
    /// </summary>
    public static CrontabField Hours(string expression) =>
        Parse(CrontabFieldKind.Hour, expression);

    /// <summary>
    /// Parses a crontab field expression representing days in any given month.
    /// </summary>
    public static CrontabField Days(string expression) =>
        Parse(CrontabFieldKind.Day, expression);

    /// <summary>
    /// Parses a crontab field expression representing months.
    /// </summary>
    public static CrontabField Months(string expression) =>
        Parse(CrontabFieldKind.Month, expression);

    /// <summary>
    /// Parses a crontab field expression representing days of a week.
    /// </summary>
    public static CrontabField DaysOfWeek(string expression) =>
        Parse(CrontabFieldKind.DayOfWeek, expression);

    private CrontabField(FieldImpl impl)
    {
        _impl = impl ?? throw new ArgumentNullException(nameof(impl));
        _bits = new BitArray(impl.ValueCount);
        MinValueSet = int.MaxValue;
        MaxValueSet = -1;
    }

    /// <summary>
    /// Gets the first value of the field or -1.
    /// </summary>
    public int GetFirst() => MinValueSet < int.MaxValue ? MinValueSet : -1;

    /// <summary>
    /// Gets the next value of the field that occurs after the given
    /// start value or -1 if there is no next value available.
    /// </summary>
    public int Next(int start)
    {
        if (start < MinValueSet)
            return MinValueSet;

        var startIndex = ValueToIndex(start);
        var lastIndex = ValueToIndex(MaxValueSet);

        for (var i = startIndex; i <= lastIndex; i++)
        {
            if (_bits[i])
                return IndexToValue(i);
        }

        return -1;
    }

    private int IndexToValue(int index) => index + _impl.MinValue;
    private int ValueToIndex(int value) => value - _impl.MinValue;

    /// <summary>
    /// Determines if the given value occurs in the field.
    /// </summary>
    public bool Contains(int value) => _bits[ValueToIndex(value)];

    /// <summary>
    /// Accumulates the given range (start to end) and interval of values
    /// into the current set of the field.
    /// </summary>
    /// <remarks>
    /// To set the entire range of values representable by the field,
    /// set <param name="start" /> and <param name="end" /> to -1 and
    /// <param name="interval" /> to 1.
    /// </remarks>
    private T Accumulate<T>(int start, int end, int interval, T success, Func<CrontabExceptionProvider, T> errorSelector)
    {
        var minValue = _impl.MinValue;
        var maxValue = _impl.MaxValue;

        if (start == end)
        {
            if (start < 0)
            {
                //
                // We're setting the entire range of values.
                //
                if (interval <= 1)
                {
                    MinValueSet = minValue;
                    MaxValueSet = maxValue;
                    _bits.SetAll(true);
                    return success;
                }

                start = minValue;
                end = maxValue;
            }
            else
            {
                //
                // We're only setting a single value - check that it is in range.
                //
                if (start < minValue)
                    return OnValueBelowMinError(start, errorSelector);

                if (start > maxValue)
                    return OnValueAboveMaxError(start, errorSelector);
            }
        }
        else
        {
            //
            // For ranges, if the start is bigger than the end value then
            // swap them over.
            //
            if (start > end)
            {
                end ^= start;
                start ^= end;
                end ^= start;
            }

            if (start < 0)
                start = minValue;
            else if (start < minValue)
                return OnValueBelowMinError(start, errorSelector);

            if (end < 0)
                end = maxValue;
            else if (end > maxValue)
                return OnValueAboveMaxError(end, errorSelector);
        }

        if (interval < 1)
            interval = 1;

        int i;

        //
        // Populate the _bits table by setting all the bits corresponding to
        // the valid field values.
        //
        for (i = start - minValue; i <= (end - minValue); i += interval)
            _bits[i] = true;

        //
        // Make sure we remember the minimum value set so far Keep track of
        // the highest and lowest values that have been added to this field
        // so far.
        //
        if (MinValueSet > start)
            MinValueSet = start;

        i += (minValue - interval);

        if (MaxValueSet < i)
            MaxValueSet = i;

        return success;
    }

    private T OnValueAboveMaxError<T>(int value, Func<CrontabExceptionProvider, T> errorSelector) =>
        errorSelector(
            () => new CrontabException(string.Format(Resources.ExceptionKindValueOverflow,
                value, _impl.Kind.GetDescription(), _impl.MinValue, _impl.MaxValue)));

    private T OnValueBelowMinError<T>(int value, Func<CrontabExceptionProvider, T> errorSelector) =>
        errorSelector(
            () => new CrontabException(string.Format(Resources.ExceptionKindValueUnderflow,
                value, _impl.Kind.GetDescription(), _impl.MinValue, _impl.MaxValue)));

    /// <inheritdoc/>
    public override string ToString() => ToString(null);

    /// <inheritdoc cref="ToString()"/>
    public string ToString(string? format)
    {
        var writer = new StringWriter(CultureInfo.InvariantCulture);

        switch (format)
        {
            case null:
            case "G":
                Format(writer, true);
                break;
            case "N":
                Format(writer);
                break;
            default:
                throw new FormatException();
        }

        return writer.ToString();
    }

    /// <summary>
    /// TextWriter에 포맷해요
    /// </summary>
    /// <param name="writer"></param>
    public void Format(TextWriter writer) => Format(writer, false);

    /// <summary>
    /// TextWriter에 포맷해요<br/>
    /// 단 이름은 빼고
    /// </summary>
    /// <param name="writer"></param>
    /// <param name="noNames"></param>
    public void Format(TextWriter writer, bool noNames) =>
        _impl.Format(this, writer, noNames);
}
#pragma warning restore CS1573
