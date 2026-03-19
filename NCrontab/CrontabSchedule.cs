using System.Globalization;
using Du.Properties;

namespace Du.NCrontab;

/// <summary>
/// Crontab 스케줄
/// </summary>
[Serializable]
public sealed class CrontabSchedule
{
	private static Calendar Calendar => CultureInfo.InvariantCulture.Calendar;
	private static readonly CrontabField SecondZero = CrontabField.Seconds("0");

	private static readonly char[] SeparatorSpace = [' '];

	private readonly CrontabField? _seconds;
	private readonly CrontabField _minutes;
	private readonly CrontabField _hours;
	private readonly CrontabField _days;
	private readonly CrontabField _months;
	private readonly CrontabField _daysOfWeek;

	/// <summary>
	/// 문자열 표현식을 사용해 <see cref="CrontabSchedule"/> 인스턴스를 만듭니다.
	/// </summary>
	/// <param name="expression">크론 스케줄 표현식(공백으로 구분된 필드 목록)입니다.</param>
	/// <param name="includeSecond">초 필드를 포함하는 표현식인지 여부입니다. <c>true</c>이면 6개 구성요소, 아니면 5개입니다.</param>
	/// <returns>파싱된 <see cref="CrontabSchedule"/> 인스턴스를 반환합니다. 표현식이 유효하지 않으면 예외가 발생합니다.</returns>
	public static CrontabSchedule Parse(string? expression, bool includeSecond = false) =>
		TryParse(expression, includeSecond, v => v, e => throw e());

	/// <summary>
	/// 문자열 표현식을 사용해 <see cref="CrontabSchedule"/>를 시도 파싱합니다. 실패하면 <c>null</c>을 반환합니다.
	/// </summary>
	/// <param name="expression">크론 스케줄 표현식입니다.</param>
	/// <param name="includeSecond">초 필드 포함 여부입니다.</param>
	/// <returns>파싱에 성공하면 <see cref="CrontabSchedule"/>, 실패하면 <c>null</c>을 반환합니다.</returns>
	public static CrontabSchedule TryParse(string? expression, bool includeSecond = false) =>
		TryParse(expression ?? string.Empty, includeSecond, v => v, _ => null!);

	/// <summary>
	/// 문자열 표현식으로 스케줄을 시도 파싱하고 결과를 제네릭 방식으로 반환합니다.
	/// </summary>
	/// <typeparam name="T">성공 또는 실패 시 반환할 타입입니다.</typeparam>
	/// <param name="expression">크론 표현식 문자열입니다.</param>
	/// <param name="valueSelector">파싱 성공 시 <see cref="CrontabSchedule"/>를 변환할 함수입니다.</param>
	/// <param name="errorSelector">파싱 실패 시 오류 제공자를 변환할 함수입니다.</param>
	/// <returns>성공 또는 실패에 따라 <typeparamref name="T"/> 형식의 값을 반환합니다.</returns>
	public static T TryParse<T>(string? expression,
								Func<CrontabSchedule, T> valueSelector,
								Func<CrontabExceptionProvider, T> errorSelector) =>
		TryParse(expression ?? string.Empty, false, valueSelector, errorSelector);

	/// <summary>
	/// 문자열 표현식으로 스케줄을 파싱하며, 성공/오류에 대해 지정된 선택자를 호출합니다.
	/// </summary>
	/// <typeparam name="T">성공 또는 오류 시 반환할 타입입니다.</typeparam>
	/// <param name="expression">파싱할 표현식 문자열입니다. <c>null</c>이면 예외가 발생합니다.</param>
	/// <param name="includeSecond">초 필드 포함 여부입니다.</param>
	/// <param name="valueSelector">파싱 성공 시 결과를 변환할 함수입니다.</param>
	/// <param name="errorSelector">파싱 실패 시 오류를 변환할 함수입니다.</param>
	/// <returns>성공 또는 실패에 따라 <typeparamref name="T"/> 타입의 값을 반환합니다.</returns>
	/// <exception cref="ArgumentNullException">expression이 <c>null</c>일 때 발생합니다.</exception>
	public static T TryParse<T>(string? expression, bool includeSecond, Func<CrontabSchedule, T> valueSelector, Func<CrontabExceptionProvider, T> errorSelector)
	{
		ArgumentNullException.ThrowIfNull(expression);

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

	/// <summary>
	/// 내부용 생성자입니다. 필드별로 이미 파싱된 <see cref="CrontabField"/>들을 전달합니다.
	/// </summary>
	/// <param name="seconds">초 필드(초 포함 표현식이 아닌 경우 <c>null</c>일 수 있음).</param>
	/// <param name="minutes">분 필드입니다.</param>
	/// <param name="hours">시 필드입니다.</param>
	/// <param name="days">일 필드입니다.</param>
	/// <param name="months">월 필드입니다.</param>
	/// <param name="daysOfWeek">요일 필드입니다.</param>
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
	/// 기준 시간부터 지정된 끝 시간까지 스케줄의 모든 발생 항목을 지연 열거(lazy)로 반환합니다.
	/// </summary>
	/// <remarks>
	/// <paramref name="baseTime"/>가 스케줄에 포함되는 경우에도 이 메서드는 해당 시점을 반환하지 않습니다.
	/// 예를 들어 <paramref name="baseTime"/>가 자정이고 스케줄이 "* * * * *"(매 분)인 경우,
	/// 다음 발생은 자정이 아니라 자정 + 1분이 됩니다. 이 메서드는 <paramref name="baseTime"/> 이후의
	/// "다음" 항목들을 반환합니다. 또한 <paramref name="endTime"/>는 배타적(exclusive)입니다.
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
	/// 기준 시간부터 시작하는 스케줄의 다음 발생 시간을 반환합니다.
	/// </summary>
	/// <param name="baseTime">기준 시작 시간입니다.</param>
	/// <returns>다음 발생 시간을 반환합니다. 존재하지 않으면 <see cref="DateTime.MaxValue"/>를 반환합니다.</returns>
	public DateTime GetNextOccurrence(DateTime baseTime) =>
		GetNextOccurrence(baseTime, DateTime.MaxValue);

	/// <summary>
	/// 기준 시간과 종료 시간을 지정하여 스케줄의 다음 발생 시간을 반환합니다.
	/// </summary>
	/// <param name="baseTime">기준 시작 시간입니다.</param>
	/// <param name="endTime">탐색할 종료 시간(배타적)입니다.</param>
	/// <returns>다음 발생 시간이 존재하면 해당 시간을 반환하고, 없으면 <paramref name="endTime"/>를 반환합니다.</returns>
	/// <remarks>
	/// <paramref name="baseTime"/>가 스케줄에 포함되는 경우에도 해당 시점은 반환되지 않습니다.
	/// 이 메서드는 <paramref name="baseTime"/> 이후의 "다음" 항목을 반환하며, <paramref name="endTime"/>는 배타적입니다.
	/// </remarks>
	public DateTime GetNextOccurrence(DateTime baseTime, DateTime endTime) =>
		TryGetNextOccurrence(baseTime, endTime) ?? endTime;

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
