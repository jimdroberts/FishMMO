using System.Collections.Generic;
using System.Text;

public static class JsonHelper
{
    public static string ToJson(this Dictionary<string, object> source)
    {
        if (source == null)
            return "";

        var sb = new StringBuilder();
        sb.Append("{");

        var firstIteration = true;
        foreach (var pair in source)
        {
            if (!firstIteration)
                sb.Append(",");
            else
                firstIteration = false;

            sb.Append($"\"{EscapeJsonString(pair.Key)}\":{ValueToJson(pair.Value)}");
        }

        sb.Append("}");
        return sb.ToString();
    }

    private static string EscapeJsonString(string value)
    {
        return value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\b", "\\b")
            .Replace("\f", "\\f")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t");
    }

    private static string ValueToJson(object value)
    {
        if (value == null)
            return "null";

        if (value is string)
            return $"\"{EscapeJsonString(value.ToString())}\"";

        return value.ToString();
    }
}