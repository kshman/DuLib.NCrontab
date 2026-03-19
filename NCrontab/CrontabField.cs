using System.Collections;
using System.Globalization;
using Du.NCrontab.Implement;
using Du.NCrontab.Supplement;
using Du.Properties;

namespace Du.NCrontab;

/// <summary>
/// 단일 크론탭 필드 표현입니다.
/// 크론 표현식의 한 필드(초, 분, 시, 일, 월, 요일)를 파싱하고 해당 값의 집합을 관리합니다.
/// </summary>
[Serializable]
public sealed class CrontabField
{
	private readonly BitArray _bits;
	private readonly FieldImpl _impl;

	private int MinValueSet { get; set; }
	private int MaxValueSet { get; set; }

	/// <summary>
	/// 주어진 종류에 따른 크론탭 필드를 분석합니다.
	/// </summary>
	/// <param name="kind">분석할 필드의 종류입니다(초/분/시/일/월/요일).</param>
	/// <param name="expression">필드에 대한 크론 표현식 문자열입니다.</param>
	/// <returns>분석된 <see cref="CrontabField"/> 인스턴스를 반환합니다.</returns>
	public static CrontabField Parse(CrontabFieldKind kind, string expression) =>
		TryParse(kind, expression, v => v, e => throw e());

	/// <summary>
	/// 주어진 종류에 따른 크론탭 필드를 분석합니다(오류 발생시 null 반환).
	/// </summary>
	/// <param name="kind">분석할 필드의 종류입니다.</param>
	/// <param name="expression">필드 표현식입니다.</param>
	/// <returns>분석에 성공하면 <see cref="CrontabField"/>, 실패하면 <c>null</c>을 반환합니다.</returns>
	public static CrontabField TryParse(CrontabFieldKind kind, string expression) =>
		TryParse(kind, expression, v => v, _ => null!);

	/// <summary>
	/// 주어진 종류에 따른 크론탭 필드를 분석합니다. 제네릭 방식으로 성공/오류 처리 결과를 선택할 수 있습니다.
	/// </summary>
	/// <typeparam name="T">성공 또는 오류 시 반환할 타입입니다.</typeparam>
	/// <param name="kind">분석할 필드 종류입니다.</param>
	/// <param name="expression">필드 표현식입니다.</param>
	/// <param name="valueSelector">파싱에 성공했을 때 <see cref="CrontabField"/>에서 변환할 함수를 제공합니다.</param>
	/// <param name="errorSelector">파싱에 실패했을 때 오류 제공자에서 변환할 함수를 제공합니다.</param>
	/// <returns>성공 또는 실패에 따라 <typeparamref name="T"/> 타입의 값을 반환합니다.</returns>
	public static T TryParse<T>(CrontabFieldKind kind, string expression,
								Func<CrontabField, T> valueSelector,
								Func<CrontabExceptionProvider, T> errorSelector)
	{
		var field = new CrontabField(FieldImpl.FromKind(kind));
		var error = field._impl.TryParse(expression, field.Accumulate, null, e => e);
		return error == null ? valueSelector(field) : errorSelector(error);
	}

	/// <summary>
	/// 초(second) 필드를 나타내는 크론탭 표현식을 파싱합니다.
	/// </summary>
	public static CrontabField Seconds(string expression) =>
		Parse(CrontabFieldKind.Second, expression);

	/// <summary>
	/// 분(minute) 필드를 나타내는 크론탭 표현식을 파싱합니다.
	/// </summary>
	public static CrontabField Minutes(string expression) =>
		Parse(CrontabFieldKind.Minute, expression);

	/// <summary>
	/// 시(hour) 필드를 나타내는 크론탭 표현식을 파싱합니다.
	/// </summary>
	public static CrontabField Hours(string expression) =>
		Parse(CrontabFieldKind.Hour, expression);

	/// <summary>
	/// 월의 일(day) 필드를 나타내는 크론탭 표현식을 파싱합니다.
	/// </summary>
	public static CrontabField Days(string expression) =>
		Parse(CrontabFieldKind.Day, expression);

	/// <summary>
	/// 월(month) 필드를 나타내는 크론탭 표현식을 파싱합니다.
	/// </summary>
	public static CrontabField Months(string expression) =>
		Parse(CrontabFieldKind.Month, expression);

	/// <summary>
	/// 요일(day of week) 필드를 나타내는 크론탭 표현식을 파싱합니다.
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
	/// 필드에 설정된 값들 중에서 가장 작은(첫 번째) 값을 반환합니다. 값이 없으면 -1을 반환합니다.
	/// </summary>
	public int GetFirst() => MinValueSet < int.MaxValue ? MinValueSet : -1;

	/// <summary>
	/// 지정한 시작 값 이후에 발생하는 다음 필드 값을 반환합니다. 사용 불가하면 -1을 반환합니다.
	/// </summary>
	/// <param name="start">다음 값을 검색할 기준 시작 값입니다.</param>
	/// <returns>시작 값 이후의 다음 값, 없으면 -1.</returns>
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
	/// 지정한 값이 필드의 집합에 포함되어 있는지 확인합니다.
	/// </summary>
	/// <param name="value">확인할 값입니다.</param>
	/// <returns>값이 포함되어 있으면 <c>true</c>, 그렇지 않으면 <c>false</c>입니다.</returns>
	public bool Contains(int value) => _bits[ValueToIndex(value)];

	/// <summary>
	/// 지정된 범위(start ~ end)와 간격(interval)에 따라 필드의 값 집합을 누적합니다.
	/// </summary>
	/// <remarks>
	/// 필드가 표현할 수 있는 전체 범위를 설정하려면 <paramref name="start"/>와 <paramref name="end"/>를 -1로 설정하고
	/// <paramref name="interval"/>을 1로 설정하세요.
	/// </remarks>
	/// <typeparam name="T">성공 또는 오류 시 반환할 타입입니다.</typeparam>
	/// <param name="start">범위의 시작 값입니다. 음수이면 구현된 최솟값으로 바뀝니다.</param>
	/// <param name="end">범위의 끝 값입니다. 음수이면 구현된 최댓값으로 바뀝니다.</param>
	/// <param name="interval">범위 내에서 선택할 간격입니다. 1보다 작으면 1로 간주됩니다.</param>
	/// <param name="success">성공 시 반환할 값입니다.</param>
	/// <param name="errorSelector">오류 발생 시 <see cref="CrontabExceptionProvider"/>를 받아 반환값을 생성하는 함수입니다.</param>
	/// <returns>성공 또는 오류에 따라 <typeparamref name="T"/> 타입의 값을 반환합니다.</returns>
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
	/// 주어진 <see cref="TextWriter"/>에 필드 내용을 형식화하여 씁니다.
	/// </summary>
	/// <param name="writer">출력 대상 <see cref="TextWriter"/>입니다.</param>
	public void Format(TextWriter writer) => Format(writer, false);

	/// <summary>
	/// 주어진 <see cref="TextWriter"/>에 필드 내용을 형식화하여 씁니다. 이름(문자열 매핑)은 제외할 수 있습니다.
	/// </summary>
	/// <param name="writer">출력 대상 <see cref="TextWriter"/>입니다.</param>
	/// <param name="noNames">이름(예: 월 또는 요일의 문자열)을 제외하려면 <c>true</c>로 설정합니다.</param>
	public void Format(TextWriter writer, bool noNames) =>
		_impl.Format(this, writer, noNames);
}
