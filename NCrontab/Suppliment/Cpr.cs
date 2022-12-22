using Microsoft.Extensions.Logging;

namespace Du.NCrontab.Suppliment;

internal static partial class Cpr
{
    #region 로그 메시지

    [LoggerMessage(Level = LogLevel.Information,
        EventId = 401, Message = "태스크 추가: id={taskid}, sch={schedule}")]
    public static partial void AddTask(this ILogger logger, ulong taskId, CrontabSchedule schedule);
    [LoggerMessage(Level = LogLevel.Information,
        EventId = 402, Message = "태스크 삭제: id={taskid}, sch={schedule}")]
    public static partial void RemoveTask(this ILogger logger, ulong taskId, CrontabSchedule schedule);
    [LoggerMessage(Level = LogLevel.Trace,
        EventId = 403, Message = "태스크 {count}개 삭제")]
    public static partial void RemoveTask(this ILogger logger, int count);
    [LoggerMessage(Level = LogLevel.Trace,
        EventId = 404, Message = "태스크 모두 삭제: {count}개")]
    public static partial void RemoveTaskAll(this ILogger logger, int count);
    [LoggerMessage(Level = LogLevel.Trace,
        EventId = 405, Message = "태스크를 정리했어요")]
    public static partial void RefreshTask(this ILogger logger);
    [LoggerMessage(Level = LogLevel.Trace,
        EventId = 406, Message = "Crontab 시작해요")]
    public static partial void CrontabStart(this ILogger logger);
    [LoggerMessage(Level = LogLevel.Trace,
        EventId = 407, Message = "Crontab 정지해요")]
    public static partial void CrontabStop(this ILogger logger);
    [LoggerMessage(Level = LogLevel.Trace,
        EventId = 408, Message = "루프 실행 중에 사종자가 취소했어요")]
    public static partial void CancelOnLoop(this ILogger logger);
    [LoggerMessage(Level = LogLevel.Information,
        EventId = 409, Message = "태스크를 실행해요: id={taskid}")]
    public static partial void TaskSignaled(this ILogger logger, ulong taskId);
    [LoggerMessage(Level = LogLevel.Error,
        EventId = 410, Message = "태스크 안에서 오류가 있어요: id={taskid}, ex={ex}")]
    public static partial void TaskException(this ILogger logger, ulong taskId, string ex);
    [LoggerMessage(Level = LogLevel.Information,
        EventId = 411, Message = "태스크 루프 완료: {count}번째, 총 작업: {tasks}")]
    public static partial void TaskLoopComplete(this ILogger logger, int count, int tasks);
    #endregion
}
