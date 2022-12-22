using System.ComponentModel;

namespace Du.NCrontab.Suppliment;

internal static class Attr
{
	public static TA? GetAttribute<TA, TT>(this TT type)
		where TA : Attribute
		where TT : class, Enum
	{
		var fld = type.GetType().GetField(type.ToString());
		return (fld?.GetCustomAttributes(typeof(TA), false) as TA[])?.FirstOrDefault();
	}

	public static string GetDescription<T>(this T type) where T : class, Enum
	{
		var attr = type.GetAttribute<DescriptionAttribute, T>();
		return attr?.Description ?? type.ToString();
	}

	public static string GetDescription(this Enum e)
		=> GetDescription<Enum>(e);
}
