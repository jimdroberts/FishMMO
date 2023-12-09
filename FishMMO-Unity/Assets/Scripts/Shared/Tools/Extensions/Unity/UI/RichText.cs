using Cysharp.Text;

namespace FishMMO.Shared
{
	public static class RichText
	{
		public static string Format(string valueName, float value, bool appendLine = false, string hexColor = null, string size = null)
		{
			if (value == 0.0f)
			{
				return "";
			}

			using (var sb = ZString.CreateStringBuilder())
			{
				if (appendLine)
				{
					sb.AppendLine();
				}
				if (!string.IsNullOrWhiteSpace(size))
				{
					sb.Append("<size=" + size + ">");
				}
				if (!string.IsNullOrWhiteSpace(hexColor))
				{
					sb.Append("<color=#" + hexColor + ">");
				}
				if (!string.IsNullOrWhiteSpace(valueName))
				{
					sb.Append(valueName);
					sb.Append(": ");
				}
				if (value < 0)
				{
					sb.Append("-");
				}
				else if (value > 0)
				{
					sb.Append("+");
				}
				sb.Append(value);
				if (!string.IsNullOrWhiteSpace(hexColor))
				{
					sb.Append("</color>");
				}
				if (!string.IsNullOrWhiteSpace(size))
				{
					sb.Append("</size>");
				}
				return sb.ToString();
			}
		}

		public static string Format(string value, bool appendLine = false, string hexColor = null, string size = null)
		{
			if (string.IsNullOrWhiteSpace(value))
			{
				return "";
			}

			using (var sb = ZString.CreateStringBuilder())
			{
				if (!string.IsNullOrWhiteSpace(value))
				{
					if (appendLine)
					{
						sb.AppendLine();
					}
					if (!string.IsNullOrWhiteSpace(size))
					{
						sb.Append("<size=" + size + ">");
					}
					if (!string.IsNullOrWhiteSpace(hexColor))
					{
						sb.Append("<color=#" + hexColor + ">");
					}
					sb.Append(value);
					if (!string.IsNullOrWhiteSpace(hexColor))
					{
						sb.Append("</color>");
					}
					if (!string.IsNullOrWhiteSpace(size))
					{
						sb.Append("</size>");
					}
				}
				return sb.ToString();
			}
		}
	}
}