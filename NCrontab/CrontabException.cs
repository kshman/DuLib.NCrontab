using System.Runtime.Serialization;
using Du.Properties;

namespace Du.NCrontab;

/// <summary>
/// 크론탭 예외
/// </summary>
[Serializable]
public class CrontabException : Exception
{
    /// <summary>
    /// 새 인스턴스르 만들어요
    /// </summary>
    public CrontabException()
        : base(Resources.CrontabError) { }

    /// <summary>
    /// 새 인스턴스르 만들어요
    /// </summary>
    public CrontabException(string? message) : base(message) { }

    /// <summary>
    /// 새 인스턴스르 만들어요
    /// </summary>
    public CrontabException(string? message, Exception? innerException) : base(message, innerException) { }

    /// <summary>
    /// 새 인스턴스르 만들어요
    /// </summary>
    protected CrontabException(SerializationInfo info, StreamingContext context) : base(info, context) { }
}

/// <summary>
/// 크론탭 예외 정보를 제공할 때 쓰는 대리자
/// </summary>
/// <returns></returns>
public delegate Exception CrontabExceptionProvider();
