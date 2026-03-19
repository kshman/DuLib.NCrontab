using Du.NCrontab.Supplement;
using Du.Properties;
using Microsoft.Extensions.Logging;

namespace Du.NCrontab;

/// <summary>
/// 크론을 실행하고 등록된 스케줄에 따라 작업을 호출하는 런타임입니다.
/// </summary>
/// <remarks>
/// 생성자에 로거를 전달하여 내부 이벤트와 예외를 기록할 수 있습니다.
/// </remarks>
/// <param name="logger">작업 실행과 관련된 로그를 기록할 `ILogger` 인스턴스입니다. `null`이면 로깅을 수행하지 않습니다.</param>
public class Crontab(ILogger? logger = null) : IDisposable
{
	private static ulong _sTaskId;

	private readonly ILogger? _logger = logger;
	private readonly Lock _lock = new();

	private readonly List<CrontabTask> _tasks = [];
	private CancellationTokenSource? _cts;
	private CancellationToken? _nct;

	private bool _disposed;

	#region 속성
	/// <summary>
	/// 실행 중인가
	/// </summary>
	public bool IsRunning { get; private set; }
	/// <summary>
	/// 루프에서 예외를 잡아내는가
	/// 이 값이 참이면 예외를 발생해요!
	/// </summary>
	public bool IsThrowException { get; set; }
	/// <summary>
	/// 비동기 태스크를 기다리는가
	/// </summary>
	public bool IsWaitAsyncTask { get; set; } = true;
	/// <summary>
	/// 기다릴 때 틱에 대한 정밀도
	/// </summary>
	public TimeSpan WaitDensity { get; set; } = TimeSpan.FromMilliseconds(100);
	/// <summary>
	/// 루프 카운트
	/// </summary>
	public int LoopCount { get; private set; }

	/// <summary>
	/// 이슈가 시작할 때
	/// </summary>
	public event EventHandler<CrontabEnterEventArg>? Enter;
	/// <summary>
	/// 이슈가 끝날 때
	/// </summary>
	public event EventHandler<CrontabLeaveEventArg>? Leave;

	#endregion

	#region 클래스

	/// <inheritdoc/>
	public void Dispose()
	{
		Dispose(true);
		GC.SuppressFinalize(this);
	}

	/// <summary>
	/// Dispose 패턴 작업
	/// </summary>
	/// <param name="disposing">관리되는 리소스를 해제할 때는 <c>true</c>, 그렇지 않으면 <c>false</c>입니다.</param>
	protected virtual void Dispose(bool disposing)
	{
		if (_disposed)
			return;

		if (disposing)
		{
			lock (_lock)
			{
				_tasks.Clear();

				if (IsRunning)
				{
					_cts?.Cancel();
					IsRunning = false;
				}
			}
		}

		_cts?.Dispose();

		_disposed = true;
	}
	#endregion

	#region 시작 / 중단
	/// <summary>
	/// 동기적으로 크론 루프를 시작합니다(내부적으로 StartAsync를 동기 대기).
	/// </summary>
	/// <param name="ct">호출자가 취소를 요청할 때 사용하는 <see cref="CancellationToken"/>입니다. 기본값은 취소 없음입니다.</param>
	public void Start(CancellationToken ct = default)
	{
		try
		{
			StartAsync(ct).Wait(CancellationToken.None);
		}
		catch (AggregateException ex)
		{
			if (ex.InnerException != null)
				throw ex.InnerException;
			throw;
		}
	}

	/// <summary>
	/// 비동기적으로 크론 루프를 시작합니다.
	/// 등록된 태스크를 기준으로 다음 실행 시점까지 대기하고, 해당 시점에 태스크를 실행합니다.
	/// 루프는 취소 토큰이 신호를 받거나 <see cref="Stop"/> 또는 <see cref="Dispose()"/> 호출로 중단됩니다.
	/// </summary>
	/// <param name="ct">외부에서 루프를 취소할 때 사용하는 <see cref="CancellationToken"/>입니다.</param>
	/// <returns>루프 시작 작업을 나타내는 <see cref="Task"/>입니다. 루프가 종료되면 완료됩니다.</returns>
	/// <exception cref="InvalidOperationException">이미 크론이 실행 중일 때 호출하면 발생합니다.</exception>
	public async Task StartAsync(CancellationToken ct = default)
	{
		lock (_lock)
		{
			if (IsRunning)
				throw new InvalidOperationException(Resources.ExceptionAlreadyRun);

			IsRunning = true;

			_nct = ct;
			_cts = new CancellationTokenSource();
			_nct?.Register(() => _cts.Cancel());
		}

		_logger?.CrontabStart();

		try
		{
			LoopCount = 0;

			while (!ct.IsCancellationRequested && IsRunning)
			{
				var now = DateTime.Now;
				var issue = DateTime.MaxValue;
				var tasks = new List<CrontabTask>();

				lock (_lock)
				{
					foreach (var t in _tasks)
					{
						var next = t.Schedule.GetNextOccurrence(now, DateTime.MaxValue);

						if (next == DateTime.MaxValue)
							continue;

						if (next == issue)
						{
							tasks.Add(t);
						}
						else if (next < issue)
						{
							issue = next;

							tasks.Clear();
							tasks.Add(t);
						}
					}
				}

				var wait = InternalGetWaitTimeSpan(tasks.Count, issue, now);
				var cancel = await Task.Delay(wait, _cts.Token).ContinueWith(_ => ct.IsCancellationRequested, CancellationToken.None);

				if (!IsRunning || cancel)
				{
					_logger?.CancelOnLoop();
					break;
				}

				if (tasks.Count == 0)
					continue;

				var begin = DateTime.Now;
				var ids = tasks.Select(x => x.Id).ToArray();
				Enter?.Invoke(this, new CrontabEnterEventArg(begin, ids));

				foreach (var i in tasks.TakeWhile(_ => !_cts.IsCancellationRequested))
				{
					_logger?.TaskSignaled(i.Id);

					try
					{
						if (!i.IsAsync)
						{
							i.Invoke(ct);
						}
						else
						{
							if (IsWaitAsyncTask)
								await i.AsyncInvoke(ct);
							else
								_ = Task.Run(async () => await i.AsyncInvoke(ct), ct);
						}
					}
					catch (Exception ex) when (!IsThrowException)
					{
						// 태스크 안에서 오류 처리 안하면 여기서 다 걍 무시해버림
						_logger?.TaskException(i.Id, ex.InnerException?.Message ?? ex.Message);
					}
				}

				Leave?.Invoke(this, new CrontabLeaveEventArg(begin, DateTime.Now, ids));

				LoopCount++;
				_logger?.TaskLoopComplete(LoopCount, ids.Length);
			}
		}
		finally
		{
			IsRunning = false;
		}
	}

	//
	private TimeSpan InternalGetWaitTimeSpan(int count, DateTime next, DateTime now)
	{
		if (count == 0)
			return Timeout.InfiniteTimeSpan;

		var delta = next - now;
		var seconds = Math.Min(Math.Ceiling(delta.TotalSeconds), (delta + WaitDensity).TotalSeconds);
		var tick = (long)(seconds * TimeSpan.TicksPerSecond);

		return TimeSpan.FromTicks(tick);
	}

	/// <summary>
	/// 중단
	/// </summary>
	public void Stop()
	{
		_logger?.CrontabStop();

		lock (_lock)
		{
			if (!IsRunning)
				return;

			_cts?.Cancel();
			IsRunning = false;
		}
	}

	/// <summary>
	/// 태스크에 따로 스케줄을 넣었다면, 이걸 써야 시간이 맞는다
	/// </summary>
	public void RefreshTask()
	{
		_logger?.RefreshTask();

		lock (_lock)
			InternalReset();
	}

	//
	private void InternalReset()
	{
		_cts?.Cancel();
		_cts = new CancellationTokenSource();
		_nct?.Register(() => _cts.Cancel());
	}
	#endregion

	#region 태스크 추가
	/// <summary>
	/// 이미 만들어진 `CrontabTask` 인스턴스를 추가합니다.
	/// </summary>
	/// <param name="cronTask">등록할 <see cref="CrontabTask"/> 인스턴스입니다.</param>
	public void AddTask(CrontabTask cronTask) => InternalAddTask(cronTask);

	/// <summary>
	/// 동기 작업을 실행하는 태스크를 표현식 또는 스케줄로 추가합니다.
	/// </summary>
	/// <param name="crontab">작업의 실행 스케줄을 나타내는 <see cref="CrontabSchedule"/>입니다.</param>
	/// <param name="action">실행될 동기 작업으로, <see cref="CancellationToken"/>을 받아 취소를 처리할 수 있습니다.</param>
	/// <returns>추가된 태스크의 고유 아이디(`ulong`)를 반환합니다.</returns>
	public ulong AddTask(CrontabSchedule crontab, Action<CancellationToken> action)
	{
		var task = new CrontabTask(crontab, action);
		InternalAddTask(task);
		return task.Id;
	}

	/// <summary>
	/// 크론 표현식 문자열을 사용해 동기 작업 태스크를 추가합니다.
	/// </summary>
	/// <param name="expression">크론 표현식 문자열입니다.</param>
	/// <param name="action">실행될 동기 작업입니다.</param>
	/// <param name="withSecond">초 단위를 포함하는 표현식인지 여부입니다.</param>
	/// <returns>추가된 태스크의 고유 아이디입니다.</returns>
	public ulong AddTask(string expression, Action<CancellationToken> action, bool withSecond = false)
		=> AddTask(CrontabSchedule.Parse(expression, withSecond), action);

	/// <summary>
	/// 비동기 작업을 실행하는 태스크를 추가합니다.
	/// </summary>
	/// <param name="crontab">작업의 실행 스케줄입니다.</param>
	/// <param name="func">실행될 비동기 함수로, 취소 토큰을 받아 비동기 작업을 수행합니다.</param>
	/// <returns>추가된 태스크의 고유 아이디입니다.</returns>
	public ulong AddTask(CrontabSchedule crontab, Func<CancellationToken, Task> func)
	{
		var task = new CrontabTask(crontab, func);
		InternalAddTask(task);
		return task.Id;
	}

	/// <summary>
	/// 크론 표현식을 사용해 비동기 태스크를 추가합니다.
	/// </summary>
	/// <param name="expression">크론 표현식 문자열입니다.</param>
	/// <param name="func">실행될 비동기 함수입니다.</param>
	/// <param name="withSecond">초 단위를 포함하는 표현식인지 여부입니다.</param>
	/// <returns>추가된 태스크의 고유 아이디입니다.</returns>
	public ulong AddTask(string expression, Func<CancellationToken, Task> func, bool withSecond = false)
		=> AddTask(CrontabSchedule.Parse(expression, withSecond), func);

	//
	private void InternalAddTask(CrontabTask task)
	{
		_logger?.AddTask(task.Id, task.Schedule);

		lock (_lock)
		{
			_tasks.Add(task);

			if (IsRunning)
				InternalReset();
		}
	}
	#endregion

	#region 태스크 관리
	/// <summary>
	/// 태스크 개수
	/// </summary>
	/// <remarks>스레드 안 세이프티</remarks>
	public int TaskCount => _tasks.Count;

	/// <summary>
	/// 지정한 아이디와 일치하는 태스크를 검색합니다.
	/// </summary>
	/// <param name="id">검색할 태스크의 고유 아이디입니다.</param>
	/// <returns>아이디에 해당하는 <see cref="CrontabTask"/>가 존재하면 반환하고, 없으면 <c>null</c>을 반환합니다.</returns>
	public CrontabTask? FindTask(ulong id)
	{
		lock (_lock)
			return _tasks.SingleOrDefault(x => x.Id == id);
	}

	/// <summary>
	/// 지정된 태스크 인스턴스를 제거합니다.
	/// </summary>
	/// <param name="task">제거하려는 <see cref="CrontabTask"/> 인스턴스입니다. <c>null</c>이면 아무 작업도 하지 않습니다.</param>
	/// <returns>제거에 성공하면 <c>true</c>, 태스크가 없거나 <c>null</c>이면 <c>false</c>를 반환합니다.</returns>
	public bool RemoveTask(CrontabTask? task)
	{
		if (task == null)
			return false;

		_logger?.RemoveTask(task.Id, task.Schedule);

		lock (_lock)
		{
			var ret = _tasks.Remove(task);

			if (IsRunning)
				InternalReset();

			return ret;
		}
	}

	/// <summary>
	/// 여러 태스크를 한 번에 제거합니다.
	/// </summary>
	/// <param name="tasks">제거할 태스크들의 배열입니다.</param>
	/// <returns>제거된 태스크의 개수를 반환합니다.</returns>
	public int RemoveTask(params CrontabTask[] tasks)
	{
		lock (_lock)
		{
			var count = tasks.Count(t => _tasks.Remove(t));

			if (IsRunning)
				InternalReset();

			_logger?.RemoveTask(count);

			return count;
		}
	}

	/// <summary>
	/// 태스크 제거
	/// </summary>
	/// <param name="id"></param>
	/// <returns></returns>
	public bool RemoveTask(ulong id)
		=> RemoveTask(FindTask(id));

	/// <summary>
	/// 여러 아이디를 기준으로 태스크를 제거합니다.
	/// </summary>
	/// <param name="ids">제거할 태스크의 아이디 목록입니다.</param>
	/// <returns>제거된 태스크의 수를 반환합니다.</returns>
	public int RemoveTask(params ulong[] ids)
	{
		lock (_lock)
		{
			var tasks = _tasks.Where(x => ids.Contains(x.Id)).ToArray();
			foreach (var t in tasks)
				_tasks.Remove(t);

			if (IsRunning)
				InternalReset();

			_logger?.RemoveTask(tasks.Length);

			return tasks.Length;
		}
	}

	/// <summary>
	/// 모든 태스크 제거
	/// </summary>
	public void RemoveTaskAll()
	{
		lock (_lock)
		{
			var count = _tasks.Count;

			_tasks.Clear();

			if (IsRunning)
				InternalReset();

			_logger?.RemoveTaskAll(count);
		}
	}
	#endregion

	#region 발생일 관련
	//
	private record NextOcPair(DateTime Issue, CrontabTask Task);

	//
	private static IEnumerable<NextOcPair> InternalGetNextOccurrences(IEnumerable<CrontabTask> tasks, DateTime begin,
		DateTime end)
	{
		var ret = tasks.SelectMany(t =>
			t.Schedule.GetNextOccurrences(begin, end).Select(i => new NextOcPair(i, t)));
		return ret;
	}

	//
	private static IEnumerable<NextOcPair> InternalGetNextOccurrence(IEnumerable<CrontabTask> tasks, DateTime begin)
	{
		var ret = tasks.Select(t => new NextOcPair(t.Schedule.GetNextOccurrence(begin), t));
		return ret;
	}

	//
	private static IEnumerable<CrontabNextOccurrencesPair> InternalGenerateNextOccurrencesPair(
		IEnumerable<NextOcPair> pairs)
	{
		var ret = pairs.GroupBy(g => g.Issue).Select(u => new CrontabNextOccurrencesPair(u.Key, u.Select(p => p.Task)));
		return ret;
	}

	/// <summary>
	/// 이 런에 등록된 태스크들에 대해 지정한 기준 시간 기준의 다음 발생일과 해당 태스크 목록을 반환합니다.
	/// </summary>
	/// <param name="begin">다음 발생일 계산의 기준이 되는 시작 시간입니다.</param>
	/// <returns>발생일(이슈 시간)별로 묶인 태스크 목록을 나타내는 <see cref="CrontabNextOccurrencesPair"/> 열거입니다.</returns>
	/// <see cref="CrontabSchedule.GetNextOccurrence(DateTime)"/>
	public IEnumerable<CrontabNextOccurrencesPair> GetNextOccurrence(DateTime begin)
	{
		IEnumerable<NextOcPair> pairs;
		lock (_lock)
			pairs = InternalGetNextOccurrence(_tasks, begin);

		return InternalGenerateNextOccurrencesPair(pairs);
	}

	/// <summary>
	/// 런이 갖고 있는 태스크에서 지금 시간 기준으로 다음 발생일 얻기
	/// </summary>
	/// <returns></returns>
	/// <see cref="CrontabSchedule.GetNextOccurrence(DateTime)"/>
	public IEnumerable<CrontabNextOccurrencesPair> GetNextOccurrence()
		=> GetNextOccurrence(DateTime.Now);

	/// <summary>
	/// 이 런에 등록된 태스크들에 대해 지정한 기간(begin ~ end) 동안 발생하는 모든 발생일과 해당 태스크 목록을 반환합니다.
	/// </summary>
	/// <param name="begin">기간의 시작 시간입니다.</param>
	/// <param name="end">기간의 종료 시간입니다.</param>
	/// <returns>기간 내의 발생일별 태스크 묶음을 나타내는 <see cref="CrontabNextOccurrencesPair"/> 열거입니다.</returns>
	/// <see cref="CrontabSchedule.GetNextOccurrences(DateTime, DateTime)"/>
	public IEnumerable<CrontabNextOccurrencesPair> GetNextOccurrences(DateTime begin, DateTime end)
	{
		IEnumerable<NextOcPair> pairs;
		lock (_lock)
			pairs = InternalGetNextOccurrences(_tasks, begin, end);

		return InternalGenerateNextOccurrencesPair(pairs);
	}
	#endregion

	#region 새 태스크 아이디
	/// <summary>
	/// 새 태스크 아이디를 얻어요
	/// </summary>
	/// <returns></returns>
	public static ulong NewTaskId()
	{
		return Interlocked.Increment(ref _sTaskId);
	}
	#endregion
}

/// <summary>크론 발생일 기준 태스크 목록</summary>
public record CrontabNextOccurrencesPair
{
	/// <summary>이슈 날짜</summary>
	public DateTime Issue { get; init; }

	/// <summary>태스크 목록</summary>
	public IEnumerable<CrontabTask> Tasks { get; init; }

	/// <summary>새 인스턴스를 만들어요</summary>
	/// <param name="issue">이슈 날짜</param>
	/// <param name="tasks">태스크 목록</param>
	public CrontabNextOccurrencesPair(DateTime issue, IEnumerable<CrontabTask> tasks)
	{
		Issue = issue;
		Tasks = tasks;
	}

	/// <inheritdoc/>
	public override string ToString()
	{
		return $"{Issue}: {Tasks.Count()}";
	}
}
