using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DreamTeamMobile
{
    public static class JsonHelper
    {
        public static string ToJson(this Dictionary<string, object> source)
        {
            if (source == null)
                return "null";

            var result = $"{{{string.Join(",", source.Select(pair => $"\"{EscapeJsonString(pair.Key)}\":{ValueToJson(pair.Value)}"))}}}";
            return result;
        }

        public static string ToJson(this IEnumerable<object> source)
        {
            if (source == null)
                return "null";

            var result = $"[{string.Join(",", source.Select(i => JsonUtility.ToJson(i)))}]";
            return result;
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

            if (value is IEnumerable<object> valueEnumerable)
                return valueEnumerable.ToJson();

            return value.ToString();
        }
    }
}