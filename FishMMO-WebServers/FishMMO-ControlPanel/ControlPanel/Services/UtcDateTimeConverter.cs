using System.Text.Json;
using System.Text.Json.Serialization;

namespace FishMMO.ControlPanel.Services
{
	/// <summary>
	/// Serializes every <see cref="DateTime"/> with an explicit UTC designator.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Every timestamp column in this schema is <c>timestamp without time zone</c> holding a UTC
	/// value — the columns are even named for it, <c>occurred_utc</c>, <c>last_saved</c>,
	/// <c>time_deleted</c>. EF reads them back as <see cref="DateTimeKind.Unspecified"/>, and
	/// <c>System.Text.Json</c> then writes an unspecified kind with no designator at all:
	/// <c>"2026-09-11T02:10:00"</c>.
	/// </para>
	/// <para>
	/// <b>A browser parses that as local time.</b> Not as UTC, and not as an error — every date
	/// in the panel was silently shifted by the operator's offset, which on this developer's
	/// machine is seven hours. The failure is invisible precisely where it matters: a character
	/// "last saved 7 hours ago" that was in fact saved a minute ago reads as a stale row, and an
	/// audit entry appears to have happened in the future.
	/// </para>
	/// <para>
	/// Stamping UTC on the way out is a statement of fact rather than a guess: the value in the
	/// column is UTC. Reading accepts either form, so a client that does send a designator is
	/// not broken by this.
	/// </para>
	/// </remarks>
	public sealed class UtcDateTimeConverter : JsonConverter<DateTime>
	{
		/// <inheritdoc/>
		public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
		{
			DateTime value = reader.GetDateTime();

			// A client may send either form. Both mean UTC here, because every column this is
			// bound for is UTC; converting rather than assuming keeps an offset-bearing value
			// correct instead of reinterpreting its wall-clock digits.
			return value.Kind switch
			{
				DateTimeKind.Utc => value,
				DateTimeKind.Local => value.ToUniversalTime(),
				_ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
			};
		}

		/// <inheritdoc/>
		public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
		{
			DateTime utc = value.Kind == DateTimeKind.Local
				? value.ToUniversalTime()
				: DateTime.SpecifyKind(value, DateTimeKind.Utc);

			// Round-trip format: "2026-09-11T02:10:00.0000000Z".
			writer.WriteStringValue(utc.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
		}
	}

	/// <summary>The nullable counterpart, since a null timestamp must stay null.</summary>
	public sealed class NullableUtcDateTimeConverter : JsonConverter<DateTime?>
	{
		private static readonly UtcDateTimeConverter Inner = new UtcDateTimeConverter();

		/// <inheritdoc/>
		public override DateTime? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
		{
			if (reader.TokenType == JsonTokenType.Null)
			{
				return null;
			}
			return Inner.Read(ref reader, typeof(DateTime), options);
		}

		/// <inheritdoc/>
		public override void Write(Utf8JsonWriter writer, DateTime? value, JsonSerializerOptions options)
		{
			if (!value.HasValue)
			{
				writer.WriteNullValue();
				return;
			}
			Inner.Write(writer, value.Value, options);
		}
	}
}
