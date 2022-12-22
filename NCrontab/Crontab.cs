using Du.NCrontab.Suppliment;
using Du.Properties;
using Microsoft.Extensions.Logging;

namespace Du.NCrontab;

/// <summary>
/// 크론을 실행해요
/// </summary>
public class Crontab : IDisposable
{
	private static ulong s_task_id;

	private readonly ILogger? _lg;
	private readonly object _lock = new();

	private readonly List<CrontabTask> _tasks = new();
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
	/// <summary>
	/// 컨스트럭터
	/// </summary>
	/// <param name="logger"></param>
	public Crontab(ILogger? logger = null)
	{
		_lg = logger;
	}

	/// <inheritdoc/>
	public void Dispose()
	{
		Dispose(true);
		GC.SuppressFinalize(this);
	}

	/// <summary>
	/// Dispose 패턴 작업
	/// </summary>
	/// <param name="disposing"></param>
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
	/// 시작
	/// </summary>
	/// <param name="ct"></param>
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
	/// 시작 / 비동기
	/// </summary>
	/// <param name="ct"></param>
	/// <returns></returns>
	/// <exception cref="InvalidOperationException"></exception>
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

		_lg?.CrontabStart();

		try
		{
			LoopCount = 0;

			while (true)
			{
				if (ct.IsCancellationRequested || !IsRunning)
					break;

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
							tasks.Add(t);
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
					_lg?.CancelOnLoop();
					break;
				}

				if (tasks.Count == 0)
					continue;

				var begin = DateTime.Now;
				var ids = tasks.Select(x => x.Id).ToArray();
				Enter?.Invoke(this, new CrontabEnterEventArg(begin, ids));

				foreach (var i in tasks.TakeWhile(_ => !_cts.IsCancellationRequested))
				{
					_lg?.TaskSignaled(i.Id);

					try
					{
						if (!i.IsAsync)
							i.Invoke(ct);
						else
						{
							if (IsWaitAsyncTask)
								await i.AsyncInvoke(ct);
							else
								_ = Task.Run(async () => await i.AsyncInvoke(ct), ct);
						}
					}
					catch (Exception ex)
					{
						if (IsThrowException)
							throw;

						// 태스크 안에서 오류 처리 안하면 여기서 다 걍 무시해버림
						_lg?.TaskException(i.Id, ex.InnerException?.Message ?? ex.Message);
					}
				}

				Leave?.Invoke(this, new CrontabLeaveEventArg(begin, DateTime.Now, ids));

				LoopCount++;
				_lg?.TaskLoopComplete(LoopCount, ids.Length);
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
		_lg?.CrontabStop();

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
		_lg?.RefreshTask();

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
	/// 태스크 추가
	/// </summary>
	/// <param name="cronTask"></param>
	public void AddTask(CrontabTask cronTask) => InternalAddTask(cronTask);

	/// <summary>
	/// 태스크 추가
	/// </summary>
	/// <param name="crontab"></param>
	/// <param name="action"></param>
	/// <returns></returns>
	public ulong AddTask(CrontabSchedule crontab, Action<CancellationToken> action)
	{
		var task = new CrontabTask(crontab, action);
		InternalAddTask(task);
		return task.Id;
	}

	/// <summary>
	/// 태스크 추가
	/// </summary>
	/// <param name="expression"></param>
	/// <param name="action"></param>
	/// <param name="withSecond"></param>
	/// <returns></returns>
	public ulong AddTask(string expression, Action<CancellationToken> action, bool withSecond = false)
		=> AddTask(CrontabSchedule.Parse(expression, withSecond), action);

	/// <summary>
	/// 비동기 태스크 추가
	/// </summary>
	/// <param name="crontab"></param>
	/// <param name="func"></param>
	/// <returns></returns>
	public ulong AddTask(CrontabSchedule crontab, Func<CancellationToken, Task> func)
	{
		var task = new CrontabTask(crontab, func);
		InternalAddTask(task);
		return task.Id;
	}

	/// <summary>
	/// 비동기 태스크 추가
	/// </summary>
	/// <param name="expression"></param>
	/// <param name="func"></param>
	/// <param name="withSecond"></param>
	/// <returns></returns>
	public ulong AddTask(string expression, Func<CancellationToken, Task> func, bool withSecond = false)
		=> AddTask(CrontabSchedule.Parse(expression, withSecond), func);

	//
	private void InternalAddTask(CrontabTask task)
	{
		_lg?.AddTask(task.Id, task.Schedule);

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
	/// 아이디로 태스크 찾기
	/// </summary>
	/// <param name="id"></param>
	/// <returns></returns>
	public CrontabTask? FindTask(ulong id)
	{
		lock (_lock)
			return _tasks.SingleOrDefault(x => x.Id == id);
	}

	/// <summary>
	/// 태스크 제거
	/// </summary>
	/// <param name="task"></param>
	/// <returns></returns>
	public bool RemoveTask(CrontabTask? task)
	{
		if (task == null)
			return false;

		_lg?.RemoveTask(task.Id, task.Schedule);

		lock (_lock)
		{
			var ret = _tasks.Remove(task);

			if (IsRunning)
				InternalReset();

			return ret;
		}
	}

	/// <summary>
	/// 태스크 목록 제거
	/// </summary>
	/// <param name="tasks"></param>
	/// <returns></returns>
	public int RemoveTask(params CrontabTask[] tasks)
	{
		lock (_lock)
		{
			var count = tasks.Count(t => _tasks.Remove(t));

			if (IsRunning)
				InternalReset();

			_lg?.RemoveTask(count);

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
	/// 태스크 목록 제거
	/// </summary>
	/// <param name="ids"></param>
	/// <returns></returns>
	public int RemoveTask(params ulong[] ids)
	{
		lock (_lock)
		{
			var tasks = _tasks.Where(x => ids.Contains(x.Id)).ToArray();
			foreach (var t in tasks)
				_tasks.Remove(t);

			if (IsRunning)
				InternalReset();

			_lg?.RemoveTask(tasks.Length);

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

			_lg?.RemoveTaskAll(count);
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
	/// 런이 갖고 있는 태스크에서 지정 시간을 기준으로 다음 발생일 얻기
	/// </summary>
	/// <param name="begin"></param>
	/// <returns></returns>
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
	/// 런이 갖고 있는 태스크에서 지정 기간 동안 다음 발생일 얻기
	/// </summary>
	/// <param name="begin"></param>
	/// <param name="end"></param>
	/// <returns></returns>
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
		return Interlocked.Increment(ref s_task_id);
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
