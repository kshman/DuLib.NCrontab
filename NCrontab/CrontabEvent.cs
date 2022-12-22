namespace Du.NCrontab;

/// <summary>
/// Crontab 태스크 실행하기 전에 발생하는 이벤트
/// </summary>
public class CrontabEnterEventArg : EventArgs
{
    /// <summary>
    /// 발생 시간
    /// </summary>
    public DateTime Enter { get; init; }

    /// <summary>
    /// 발생한 태스크 아이디
    /// </summary>
    public ulong[] Tasks { get; init; }

    /// <summary>
    /// 새 인스턴스르 만들어요
    /// </summary>
    /// <param name="enter"></param>
    /// <param name="ids"></param>
    public CrontabEnterEventArg(DateTime enter, ulong[] ids)
    {
        Enter = enter;
        Tasks = ids;
    }
}

/// <summary>
/// Crontab 태스크 실행한 다음 발생하는 이벤트
/// </summary>
public class CrontabLeaveEventArg : CrontabEnterEventArg
{
    /// <summary>
    /// 나올 때 시간
    /// </summary>
    public DateTime Leave { get; init; }

    /// <summary>
    /// 이슈가 처리된 시간
    /// </summary>
    public TimeSpan Duration { get; init; }

    /// <summary>
    /// 새 인스턴스를 만들어요
    /// </summary>
    /// <param name="enter"></param>
    /// <param name="leave"></param>
    /// <param name="ids"></param>
    public CrontabLeaveEventArg(DateTime enter, DateTime leave, ulong[] ids)
    : base(enter, ids)
    {
        Leave = leave;
        Duration = leave - enter;
    }
}
