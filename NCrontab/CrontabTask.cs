namespace Du.NCrontab;

/// <summary>
/// Crontab 태스크
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

    //
    private CrontabTask(ulong id, CrontabSchedule shedule, object task)
    {
        Id = id;
        Schedule = shedule;
        _task = task;
    }

    /// <summary>
    /// 새 인스턴스를 만들어요
    /// </summary>
    /// <param name="shedule"></param>
    /// <param name="action"></param>
    public CrontabTask(CrontabSchedule shedule, Action<CancellationToken> action)
        : this(Crontab.NewTaskId(), shedule, action) { }

    /// <summary>
    /// 새 인스턴스를 만들어요
    /// </summary>
    /// <param name="expression"></param>
    /// <param name="action"></param>
    public CrontabTask(string expression, Action<CancellationToken> action)
        : this(Crontab.NewTaskId(), CrontabSchedule.Parse(expression), action) { }

    /// <summary>
    /// 새 인스턴스를 만들어요
    /// </summary>
    /// <param name="shedule"></param>
    /// <param name="func"></param>
    public CrontabTask(CrontabSchedule shedule, Func<CancellationToken, Task> func)
        : this(Crontab.NewTaskId(), shedule, func) { }

    /// <summary>
    /// 새 인스턴스를 만들어요
    /// </summary>
    /// <param name="expression"></param>
    /// <param name="func"></param>
    public CrontabTask(string expression, Func<CancellationToken, Task> func)
        : this(Crontab.NewTaskId(), CrontabSchedule.Parse(expression), func) { }

    /// <summary>
    /// 메소드(Action)를 동기로 실행해요
    /// </summary>
    /// <param name="ct"></param>
    public void Invoke(CancellationToken ct)
    {
        var action = _task as Action<CancellationToken>;
        action!.Invoke(ct);
    }

    /// <summary>
    /// 메소드(Function)를 비동기로 실행해요
    /// </summary>
    /// <param name="ct"></param>
    /// <returns></returns>
    public Task AsyncInvoke(CancellationToken ct)
    {
        var func = _task as Func<CancellationToken, Task>;
        return func!.Invoke(ct);
    }

    /// <summary>
    /// 비동기 태스트인가
    /// </summary>
    public bool IsAsync => _task is Func<CancellationToken, Task>;

    /// <inheritdoc/>
    public override string ToString()
    {
        return $"{Id} ({Schedule})";
    }
}
