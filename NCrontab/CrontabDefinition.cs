using System.ComponentModel;

namespace Du.NCrontab;

/// <summary>
/// 크론탭 필드 종류
/// </summary>
public enum CrontabFieldKind
{
    /// <summary>초</summary>
    [Description("초")]
    Second = 0, // Keep in order of appearance in expression
    /// <summary>분</summary>
    [Description("분")]
    Minute = 1,
    /// <summary>시</summary>
    [Description("시")]
    Hour = 2,
    /// <summary>일</summary>
    [Description("일")]
    Day = 3,
    /// <summary>월</summary>
    [Description("월")]
    Month = 4,
    /// <summary>요일</summary>
    [Description("요일")]
    DayOfWeek = 5
}
