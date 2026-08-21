using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace FishMMO.WebServers.Signing
{
	/// <summary>
	/// Emits the exact JSON bytes a signed manifest is made of.
	/// </summary>
	/// <remarks>
	/// <para>
	/// This exists instead of <c>System.Text.Json.JsonSerializer</c> because the signature is
	/// over the literal bytes the client receives, and ASP.NET's serialiser is not a stable part
	/// of that contract: its naming policy, escaping table and spacing are framework
	/// configuration, and a future <c>AddJsonOptions</c> call somewhere in <c>Program.cs</c>
	/// would change what goes on the wire without anyone connecting that to "patching stopped
	/// working". Owning the emitter makes the wire format a property of this file.
	/// </para>
	/// <para>
	/// The format is fixed and minimal: <c>"key": value</c>, pairs separated by <c>", "</c>. The
	/// <c>": "</c> spacing is one of the two the client verifier accepts (it tries <c>": "</c>
	/// first), and it is the one the blanked-signature placeholder is written in, so the
	/// emitted document and the canonical message differ in exactly one place — the signature
	/// value — by construction.
	/// </para>
	/// <para>
	/// Insertion order is preserved and never sorted. The client does not care about key order
	/// (it substring-searches for one field), but a stable order keeps the ETag, the logs and a
	/// human diff meaningful.
	/// </para>
	/// </remarks>
	public sealed class ManifestJsonWriter
	{
		private readonly StringBuilder builder = new StringBuilder(256);
		private bool hasField;

		/// <summary>Appends a string field. The value is JSON-escaped.</summary>
		public ManifestJsonWriter AddString(string name, string? value)
		{
			BeginField(name);
			if (value == null)
			{
				builder.Append("null");
			}
			else
			{
				AppendEscapedString(builder, value);
			}
			return this;
		}

		/// <summary>Appends a boolean field.</summary>
		public ManifestJsonWriter AddBool(string name, bool value)
		{
			BeginField(name);
			builder.Append(value ? "true" : "false");
			return this;
		}

		/// <summary>Appends an integer field. Always invariant-culture, never grouped.</summary>
		public ManifestJsonWriter AddNumber(string name, long value)
		{
			BeginField(name);
			builder.Append(value.ToString(CultureInfo.InvariantCulture));
			return this;
		}

		/// <summary>
		/// The document body: the field list WITHOUT the enclosing braces and WITHOUT a trailing
		/// separator, ready for <see cref="ManifestSigning.SignDocument"/> to wrap and sign.
		/// </summary>
		public string Build() => builder.ToString();

		private void BeginField(string name)
		{
			if (hasField)
			{
				builder.Append(", ");
			}
			hasField = true;
			AppendEscapedString(builder, name);
			builder.Append(": ");
		}

		/// <summary>
		/// Writes a JSON string literal, escaping the characters RFC 8259 requires.
		/// </summary>
		/// <remarks>
		/// Control characters are escaped as <c>\uXXXX</c> rather than dropped. Dropping would
		/// mean two different inputs could produce the same signed bytes, which is exactly the
		/// kind of ambiguity a canonical form exists to prevent. Non-ASCII is emitted as UTF-8
		/// rather than escaped: the transport is UTF-8, the client reads UTF-8, and escaping
		/// would only add a second representation of the same value.
		/// </remarks>
		internal static void AppendEscapedString(StringBuilder target, string value)
		{
			target.Append('"');
			foreach (char c in value)
			{
				switch (c)
				{
					case '"': target.Append("\\\""); break;
					case '\\': target.Append("\\\\"); break;
					case '\b': target.Append("\\b"); break;
					case '\f': target.Append("\\f"); break;
					case '\n': target.Append("\\n"); break;
					case '\r': target.Append("\\r"); break;
					case '\t': target.Append("\\t"); break;
					default:
						if (c < 0x20 || c == 0x7F)
						{
							target.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
						}
						else
						{
							target.Append(c);
						}
						break;
				}
			}
			target.Append('"');
		}

		/// <summary>
		/// Re-emits an already-parsed JSON object in this writer's format, minus the enclosing
		/// braces. Used by the offline signing tool, which is handed a document somebody else
		/// wrote and must normalise it before it can be signed.
		/// </summary>
		public static string BuildBody(JsonObject obj)
		{
			var sb = new StringBuilder(256);
			bool first = true;
			foreach (var pair in obj)
			{
				if (!first) sb.Append(", ");
				first = false;
				AppendEscapedString(sb, pair.Key);
				sb.Append(": ");
				AppendNode(sb, pair.Value);
			}
			return sb.ToString();
		}

		private static void AppendNode(StringBuilder sb, JsonNode? node)
		{
			switch (node)
			{
				case null:
					sb.Append("null");
					break;
				case JsonObject nested:
					sb.Append('{').Append(BuildBody(nested)).Append('}');
					break;
				case JsonArray array:
					sb.Append('[');
					for (int i = 0; i < array.Count; i++)
					{
						if (i > 0) sb.Append(", ");
						AppendNode(sb, array[i]);
					}
					sb.Append(']');
					break;
				default:
					// JsonValue. Strings go through our escaper so the tool and the server agree
					// on escaping; numbers, booleans and null keep System.Text.Json's literal,
					// which is already round-trip-exact and culture-invariant.
					if (node.AsValue().TryGetValue(out string? s))
					{
						AppendEscapedString(sb, s!);
					}
					else
					{
						sb.Append(node.ToJsonString());
					}
					break;
			}
		}
	}
}
