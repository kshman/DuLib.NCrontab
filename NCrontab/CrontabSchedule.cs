using System.Globalization;
using Du.Properties;

namespace Du.NCrontab;

#pragma warning disable CS1573
/// <summary>
/// Crontab 스케줄
/// </summary>
[Serializable]
public sealed class CrontabSchedule
{
    private static Calendar Calendar => CultureInfo.InvariantCulture.Calendar;
    private static readonly CrontabField SecondZero = CrontabField.Seconds("0");

    private static readonly char[] SeparatorSpace = { ' ' };

    private readonly CrontabField? _seconds;
    private readonly CrontabField _minutes;
    private readonly CrontabField _hours;
    private readonly CrontabField _days;
    private readonly CrontabField _months;
    private readonly CrontabField _daysOfWeek;

    /// <summary>
    /// 문자열로 스케줄을 만들어요.
    /// </summary>
    /// <param name="expression"></param>
    /// <param name="includeSecond"></param>
    /// <returns></returns>
    public static CrontabSchedule Parse(string? expression, bool includeSecond = false) =>
        TryParse(expression, includeSecond, v => v, e => throw e());

    /// <summary>
    /// 문자열로 스케줄을 만들어요.
    /// </summary>
    /// <param name="expression"></param>
    /// <param name="includeSecond"></param>
    /// <returns></returns>
    public static CrontabSchedule TryParse(string? expression, bool includeSecond = false) =>
        TryParse(expression ?? string.Empty, includeSecond, v => v, _ => null!);

    /// <summary>
    /// 문자열로 스케줄을 만들어요.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="expression"></param>
    /// <param name="valueSelector"></param>
    /// <param name="errorSelector"></param>
    /// <returns></returns>
    public static T TryParse<T>(string? expression,
                                Func<CrontabSchedule, T> valueSelector,
                                Func<CrontabExceptionProvider, T> errorSelector) =>
        TryParse(expression ?? string.Empty, false, valueSelector, errorSelector);

    /// <summary>
    /// 문자열로 스케줄을 만들어요.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="expression"></param>
    /// <param name="includeSecond"></param>
    /// <param name="valueSelector"></param>
    /// <param name="errorSelector"></param>
    /// <returns></returns>
    /// <exception cref="ArgumentNullException"></exception>
    public static T TryParse<T>(string? expression, bool includeSecond, Func<CrontabSchedule, T> valueSelector, Func<CrontabExceptionProvider, T> errorSelector)
    {
        if (expression == null)
            throw new ArgumentNullException(nameof(expression));

        var tokens = expression.Split(SeparatorSpace, StringSplitOptions.RemoveEmptyEntries);

        var expectedTokenCount = includeSecond ? 6 : 5;
        if (tokens.Length < expectedTokenCount || tokens.Length > expectedTokenCount)
        {
            return errorSelector(() => new CrontabException(string.Format(Resources.ExceptionInvalidScheduleExpression,
                expression, includeSecond ? Resources.SixComponentDesc : Resources.FiveComponentDesc)));
        }

        var fields = new CrontabField[6];

        var offset = includeSecond ? 0 : 1;
        for (var i = 0; i < tokens.Length; i++)
        {
            var kind = (CrontabFieldKind)i + offset;
            var field = CrontabField.TryParse(kind, tokens[i], v =>
                new { ErrorProvider = (CrontabExceptionProvider?)null, Value = (CrontabField?)v }, e =>
                new { ErrorProvider = (CrontabExceptionProvider?)e, Value = (CrontabField?)null });

            if (field.ErrorProvider != null)
                return errorSelector(field.ErrorProvider);
            fields[i + offset] = field.Value!; // non-null by mutual exclusivity!
        }

        return valueSelector(new CrontabSchedule(fields[0], fields[1], fields[2], fields[3], fields[4], fields[5]));
    }

    //
    private CrontabSchedule(
        CrontabField? seconds,
        CrontabField minutes, CrontabField hours,
        CrontabField days, CrontabField months,
        CrontabField daysOfWeek)
    {
        _seconds = seconds;
        _minutes = minutes;
        _hours = hours;
        _days = days;
        _months = months;
        _daysOfWeek = daysOfWeek;
    }

    /// <summary>
    /// 기준 시간과 지정 끝 시간에 따라 스케줄의 모든 항목을 열거해줘요.
    /// 이 메소드는 열거할 때만 계산하도록 지연 수행을 사용해요.
    /// </summary>
    /// <remarks>
    /// 이 메소드는 <paramref name="baseTime"/> 값이 예약에 포함된 경우
    /// 해당 값을 반환하지 않아요. 예를들면, <paramref name="baseTime" />이
    /// 자정이고 <c>* * * * *</c>(분 마다의 의미)로 스케줄이 만들어진 경우,
    /// 스케줄의 다음 발생 시간은 자정이 아닌 자정 +1분이 되요.
    /// 또한 <paramref name="baseTime"/> <em>뒤</em>의 <em>다음</em> 항목을
    /// 반환해요. 한편, <param name="endTime" />은 배타적 값이예요.
    /// </remarks>
    public IEnumerable<DateTime> GetNextOccurrences(DateTime baseTime, DateTime endTime)
    {
        for (var occurrence = TryGetNextOccurrence(baseTime, endTime);
             occurrence < endTime;
             occurrence = TryGetNextOccurrence(occurrence.Value, endTime))
        {
            yield return occurrence.Value;
        }
    }

    /// <summary>
    /// 기준 시간부터 시작하는 스케줄의 다음 항목을 가져와요.
    /// </summary>
    public DateTime GetNextOccurrence(DateTime baseTime) =>
        GetNextOccurrence(baseTime, DateTime.MaxValue);

    /// <summary>
    /// 기준 시간과 지정 끝 시간에 따라 시작하는 스케줄의 다음 항목을 가져와요.
    /// </summary>
    /// <remarks>
    /// 이 메소드는 <paramref name="baseTime"/> 값이 예약에 포함된 경우
    /// 해당 값을 반환하지 않아요. 예를들면, <paramref name="baseTime" />이
    /// 자정이고 <c>* * * * *</c>(분 마다의 의미)로 스케줄이 만들어진 경우,
    /// 스케줄의 다음 발생 시간은 자정이 아닌 자정 +1분이 되요.
    /// 또한 <paramref name="baseTime"/> <em>뒤</em>의 <em>다음</em> 항목을
    /// 반환해요. 한편, <param name="endTime" />은 배타적 값이예요.
    /// </remarks>
#pragma warning disable CS1573 // 매개 변수와 짝이 맞는 매개 변수 태그가 XML 주석에 없습니다. 다른 매개 변수는 짝이 맞는 태그가 있습니다.
    public DateTime GetNextOccurrence(DateTime baseTime, DateTime endTime) =>
        TryGetNextOccurrence(baseTime, endTime) ?? endTime;
#pragma warning restore CS1573 // 매개 변수와 짝이 맞는 매개 변수 태그가 XML 주석에 없습니다. 다른 매개 변수는 짝이 맞는 태그가 있습니다.

    private DateTime? TryGetNextOccurrence(DateTime baseTime, DateTime endTime)
    {
        const int nil = -1;

        var baseYear = baseTime.Year;
        var baseMonth = baseTime.Month;
        var baseDay = baseTime.Day;
        var baseHour = baseTime.Hour;
        var baseMinute = baseTime.Minute;
        var baseSecond = baseTime.Second;

        var endYear = endTime.Year;
        var endMonth = endTime.Month;
        var endDay = endTime.Day;

        var year = baseYear;
        var month = baseMonth;
        var day = baseDay;
        var hour = baseHour;
        var minute = baseMinute;
        var second = baseSecond + 1;

        //
        // Second
        //
        var seconds = _seconds ?? SecondZero;
        second = seconds.Next(second);

        if (second == nil)
        {
            second = seconds.GetFirst();
            minute++;
        }

        //
        // Minute
        //
        minute = _minutes.Next(minute);

        if (minute == nil)
        {
            second = seconds.GetFirst();
            minute = _minutes.GetFirst();
            hour++;
        }
        else if (minute > baseMinute)
        {
            second = seconds.GetFirst();
        }

        //
        // Hour
        //
        hour = _hours.Next(hour);

        if (hour == nil)
        {
            minute = _minutes.GetFirst();
            hour = _hours.GetFirst();
            day++;
        }
        else if (hour > baseHour)
        {
            second = seconds.GetFirst();
            minute = _minutes.GetFirst();
        }

        //
        // Day
        //
        day = _days.Next(day);

    RetryDayMonth:
        if (day == nil)
        {
            second = seconds.GetFirst();
            minute = _minutes.GetFirst();
            hour = _hours.GetFirst();
            day = _days.GetFirst();
            month++;
        }
        else if (day > baseDay)
        {
            second = seconds.GetFirst();
            minute = _minutes.GetFirst();
            hour = _hours.GetFirst();
        }

        //
        // Month
        //
        month = _months.Next(month);

        if (month == nil)
        {
            second = seconds.GetFirst();
            minute = _minutes.GetFirst();
            hour = _hours.GetFirst();
            day = _days.GetFirst();
            month = _months.GetFirst();
            year++;
        }
        else if (month > baseMonth)
        {
            second = seconds.GetFirst();
            minute = _minutes.GetFirst();
            hour = _hours.GetFirst();
            day = _days.GetFirst();
        }

        //
        // Stop processing when year is too large for the datetime or calendar
        // object. Otherwise we would get an exception.
        //
        if (year > Calendar.MaxSupportedDateTime.Year)
            return null;

        //
        // The day field in a cron expression spans the entire range of days
        // in a month, which is from 1 to 31. However, the number of days in
        // a month tend to be variable depending on the month (and the year
        // in case of February). So a check is needed here to see if the
        // date is a border case. If the day happens to be beyond 28
        // (meaning that we're dealing with the suspicious range of 29-31)
        // and the date part has changed then we need to determine whether
        // the day still makes sense for the given year and month. If the
        // day is beyond the last possible value, then the day/month part
        // for the schedule is re-evaluated. So an expression like "0 0
        // 15,31 * *" will yield the following sequence starting on midnight
        // of Jan 1, 2000:
        //
        //  Jan 15, Jan 31, Feb 15, Mar 15, Apr 15, Apr 31, ...
        //
        var dateChanged = day != baseDay || month != baseMonth || year != baseYear;

        if (day > 28 && dateChanged && day > Calendar.GetDaysInMonth(year, month))
        {
            if (year >= endYear && month >= endMonth && day >= endDay)
                return endTime;

            day = nil;
            goto RetryDayMonth;
        }

        var nextTime = new DateTime(year, month, day, hour, minute, second, 0, baseTime.Kind);

        if (nextTime >= endTime)
            return endTime;

        //
        // Day of week
        //
        if (_daysOfWeek.Contains((int)nextTime.DayOfWeek))
            return nextTime;

        return TryGetNextOccurrence(new DateTime(year, month, day, 23, 59, 59, 0, baseTime.Kind), endTime);
    }

    /// <summary>
    /// 계산을 모두 완료한 Crontab 표현 문자열을 가져와요.
    /// </summary>
    public override string ToString()
    {
        var writer = new StringWriter(CultureInfo.InvariantCulture);

        if (_seconds != null)
        {
            _seconds.Format(writer, true);
            writer.Write(' ');
        }
        _minutes.Format(writer, true); writer.Write(' ');
        _hours.Format(writer, true); writer.Write(' ');
        _days.Format(writer, true); writer.Write(' ');
        _months.Format(writer, true); writer.Write(' ');
        _daysOfWeek.Format(writer, true);

        return writer.ToString();
    }
}
#pragma warning restore CS1573
