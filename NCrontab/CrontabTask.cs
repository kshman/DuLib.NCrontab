namespace Du.NCrontab;

/// <summary>
/// 크론탭에서 실행할 작업(태스크)을 표현하는 클래스입니다.
/// 스케줄과 실행할 델리게이트(동기 Action 또는 비동기 Func)를 포함합니다.
/// </summary>
public class CrontabTask
{
	private readonly object _task;

	/// <summary>
	/// 태스크 아이디
	/// </summary>
	public ulong Id { get; }

	/// <summary>
	/// 태스크 스케줄
	/// </summary>
	/// <seealso cref="CrontabSchedule"/>
	public CrontabSchedule Schedule { get; set; }

	/// <summary>
	/// 내부용 생성자입니다. 고유 아이디와 스케줄, 실행할 델리게이트를 지정합니다.
	/// </summary>
	/// <param name="id">태스크 고유 아이디입니다.</param>
	/// <param name="schedule">태스크 실행 스케줄입니다.</param>
	/// <param name="task">실행할 델리게이트(Action&lt;CancellationToken&gt; 또는 Func&lt;CancellationToken, Task&gt;)입니다.</param>
	private CrontabTask(ulong id, CrontabSchedule schedule, object task)
	{
		Id = id;
		Schedule = schedule;
		_task = task;
	}

	/// <summary>
	/// 동기 작업(Action)을 실행하는 새 태스크 인스턴스를 만듭니다. 아이디는 자동 생성됩니다.
	/// </summary>
	/// <param name="schedule">해당 태스크의 실행 스케줄입니다.</param>
	/// <param name="action">실행할 동기 작업으로, 취소 토큰을 받아 처리합니다.</param>
	public CrontabTask(CrontabSchedule schedule, Action<CancellationToken> action)
		: this(Crontab.NewTaskId(), schedule, action) { }

	/// <summary>
	/// 크론 표현식 문자열을 사용하여 동기 작업 태스크를 생성합니다. 아이디는 자동 생성됩니다.
	/// </summary>
	/// <param name="expression">파싱할 크론 표현식 문자열입니다.</param>
	/// <param name="action">실행할 동기 작업입니다.</param>
	public CrontabTask(string expression, Action<CancellationToken> action)
		: this(Crontab.NewTaskId(), CrontabSchedule.Parse(expression), action) { }

	/// <summary>
	/// 비동기 작업(Func)을 실행하는 새 태스크 인스턴스를 만듭니다. 아이디는 자동 생성됩니다.
	/// </summary>
	/// <param name="schedule">해당 태스크의 실행 스케줄입니다.</param>
	/// <param name="func">실행할 비동기 함수로, 취소 토큰을 받아 <see cref="Task"/>를 반환합니다.</param>
	public CrontabTask(CrontabSchedule schedule, Func<CancellationToken, Task> func)
		: this(Crontab.NewTaskId(), schedule, func) { }

	/// <summary>
	/// 크론 표현식 문자열을 사용하여 비동기 작업 태스크를 생성합니다. 아이디는 자동 생성됩니다.
	/// </summary>
	/// <param name="expression">파싱할 크론 표현식 문자열입니다.</param>
	/// <param name="func">실행할 비동기 함수입니다.</param>
	public CrontabTask(string expression, Func<CancellationToken, Task> func)
		: this(Crontab.NewTaskId(), CrontabSchedule.Parse(expression), func) { }

	/// <summary>
	/// 동기 작업(Action)을 호출하여 실행합니다.
	/// </summary>
	/// <param name="ct">작업 실행 중 전달되는 취소 토큰입니다.</param>
	public void Invoke(CancellationToken ct)
	{
		var action = _task as Action<CancellationToken>;
		action!.Invoke(ct);
	}

	/// <summary>
	/// 비동기 함수(Func)를 호출하여 실행합니다.
	/// </summary>
	/// <param name="ct">작업 실행 중 전달되는 취소 토큰입니다.</param>
	/// <returns>실행 중인 비동기 작업을 나타내는 <see cref="Task"/>입니다.</returns>
	public Task AsyncInvoke(CancellationToken ct)
	{
		var func = _task as Func<CancellationToken, Task>;
		return func!.Invoke(ct);
	}

	/// <summary>
	/// 이 태스크가 비동기 함수(Func)를 사용하는지 여부를 반환합니다.
	/// </summary>
	public bool IsAsync => _task is Func<CancellationToken, Task>;

	/// <inheritdoc/>
	public override string ToString()
	{
		return $"{Id} ({Schedule})";
	}
}
