using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Npgsql;

namespace FishMMO.Database.Npgsql.Services
{
	/// <summary>
	/// Non-generic helper for building safe exception diagnostic strings.
	/// Extracted from <see cref="BaseService{TEntity}"/> so callers that don't
	/// inherit from BaseService (e.g. <see cref="UnitOfWorkService"/>) can also
	/// produce sanitised, informative error messages.
	/// </summary>
	internal static class ExceptionDiagnosticHelper
	{
		/// <summary>
		/// Matches the .NET 5+ trailing parameter annotation pattern appended to exception messages,
		/// e.g. <c>"Value cannot be null. (Parameter 'name')"</c>. Stripping this prevents leaking
		/// method parameter names to remote callers.
		/// </summary>
		private static readonly Regex SanitizePatternRegex =
			new Regex(@"\s*\(Parameter\s+'[^']*'\)\s*$", RegexOptions.Compiled);

		/// <summary>
		/// Strips parameter names and internal details from .NET exception messages to prevent
		/// leaking implementation details (method parameter names, internal variable names) to
		/// remote callers via <see cref="DatabaseResult.ErrorMessage" />.
		/// </summary>
		/// <para>
		/// Handles both .NET 5+ format (<c>(Parameter 'paramName')</c>) and .NET Framework format
		/// (<c>Parameter name: paramName</c>), including the trailing actual-value line.
		/// </para>
		public static string SanitizeExceptionMessage(string message)
		{
			if (string.IsNullOrEmpty(message))
				return message;

			// Strip .NET 5+ trailing parameter annotation: " (Parameter 'paramName')"
			message = SanitizePatternRegex.Replace(message, string.Empty);

			// Strip .NET Framework "Parameter name: xxx" and optional "Actual value was yyy." lines
			int paramNameIdx = message.IndexOf("Parameter name: ", StringComparison.Ordinal);
			if (paramNameIdx >= 0)
			{
				message = message.Substring(0, paramNameIdx).TrimEnd();
			}

			// Strip newline trailing from Framework format if present after stripping
			return message.TrimEnd();
		}

		/// <summary>
		/// Builds a safe, human-readable diagnostic string from an exception chain for inclusion
		/// in <see cref="DatabaseResult.ErrorMessage"/>.
		/// </summary>
		/// <remarks>
		/// Walks the exception chain up to 3 levels deep and emits:
		/// <list type="bullet">
		///   <item>CLR exception type names — safe, never contain SQL/data</item>
		///   <item><c>PostgresException.SqlState</c> — safe, 5-char protocol-level code</item>
		/// </list>
		/// This method explicitly NEVER emits exception messages, stack traces, or data values.
		/// Messages from Npgsql/PostgresException can contain column names, constraint names,
		/// SQL fragments, and connection details that must not be exposed to clients.
		/// </remarks>
		/// <param name="ex">The outermost exception.</param>
		/// <returns>
		/// A diagnostic string like <c>"InvalidCastException → NpgsqlException → PostgresException[42703]"</c>
		/// or just the outer type name for single-level exceptions.
		/// </returns>
		public static string BuildSafeExceptionDiagnostic(Exception ex)
		{
			const int maxDepth = 3;
			var parts = new List<string>(maxDepth + 1);
			var seen = new HashSet<Type>();

			Exception? current = ex;
			for (int depth = 0; depth <= maxDepth && current != null; depth++)
			{
				var type = current.GetType();
				if (!seen.Add(type))
				{
					// Cycle detected — stop walking.
					break;
				}

				var typeName = type.Name;
				if (current is PostgresException pgEx && !string.IsNullOrEmpty(pgEx.SqlState))
				{
					parts.Add($"{typeName}[{pgEx.SqlState}]");
				}
				else
				{
					parts.Add(typeName);
				}

				current = current.InnerException;
			}

			return parts.Count > 0
				? string.Join(" → ", parts)
				: ex.GetType().Name;
		}
	}
}
