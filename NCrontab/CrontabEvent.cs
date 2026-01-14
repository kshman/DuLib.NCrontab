namespace Du.NCrontab;

/// <summary>
/// Crontab 태스크 실행하기 전에 발생하는 이벤트
/// </summary>
/// <remarks>
/// 새 인스턴스르 만들어요
/// </remarks>
/// <param name="enter"></param>
/// <param name="ids"></param>
public class CrontabEnterEventArg(DateTime enter, ulong[] ids) : 
	EventArgs
{
	/// <summary>
	/// 발생 시간
	/// </summary>
	public DateTime Enter { get; init; } = enter;

	/// <summary>
	/// 발생한 태스크 아이디
	/// </summary>
	public ulong[] Tasks { get; init; } = ids;
}

/// <summary>
/// Crontab 태스크 실행한 다음 발생하는 이벤트
/// </summary>
/// <remarks>
/// 새 인스턴스를 만들어요
/// </remarks>
/// <param name="enter"></param>
/// <param name="leave"></param>
/// <param name="ids"></param>
public class CrontabLeaveEventArg(DateTime enter, DateTime leave, ulong[] ids) : 
	CrontabEnterEventArg(enter, ids)
{
	/// <summary>
	/// 나올 때 시간
	/// </summary>
	public DateTime Leave { get; init; } = leave;

	/// <summary>
	/// 이슈가 처리된 시간
	/// </summary>
	public TimeSpan Duration { get; init; } = leave - enter;
}
