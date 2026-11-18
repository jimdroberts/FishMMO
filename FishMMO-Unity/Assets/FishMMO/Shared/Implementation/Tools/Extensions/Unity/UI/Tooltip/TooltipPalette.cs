using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// The colours a tooltip's tones and row kinds resolve to.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Producers never name a colour. They say what a row <i>is</i> — a stat, a requirement, an
	/// improvement, a penalty — and this maps that to the theme. The old system had every template
	/// type reaching for a hex constant while it concatenated its tooltip, which is why the same
	/// idea came out a different colour depending on which system wrote it, and why nothing could
	/// be re-themed without editing every producer in the game.
	/// </para>
	/// <para>
	/// The client overwrites these from the loaded UI theme at boot; the defaults are the theme's
	/// own values so a server, a test, or an unthemed editor session still produces sensible text.
	/// </para>
	/// </remarks>
	public static class TooltipPalette
	{
		/// <summary>Names, headings, and the tooltip title. Abyss-100.</summary>
		public static Color Title { get; private set; } = new Color32(199, 220, 234, 255);

		/// <summary>Descriptions and secondary text. Abyss-200.</summary>
		public static Color Label { get; private set; } = new Color32(127, 163, 184, 255);

		/// <summary>Stat values. Signal blue highlight.</summary>
		public static Color Stat { get; private set; } = new Color32(88, 179, 241, 255);

		/// <summary>An improvement. The stamina green.</summary>
		public static Color Good { get; private set; } = new Color32(55, 168, 107, 255);

		/// <summary>A penalty, or something the character cannot satisfy. The danger red.</summary>
		public static Color Bad { get; private set; } = new Color32(200, 56, 74, 255);

		/// <summary>Background detail. Abyss-300.</summary>
		public static Color Muted { get; private set; } = new Color32(63, 109, 135, 255);

		/// <summary>Worth noticing without being good or bad. The brand accent.</summary>
		public static Color Accent { get; private set; } = new Color32(42, 151, 220, 255);

		/// <summary>
		/// Replaces the palette, normally from the loaded UI theme.
		/// </summary>
		public static void Initialize(Color title, Color label, Color stat, Color good, Color bad, Color muted, Color accent)
		{
			Title = title;
			Label = label;
			Stat = stat;
			Good = good;
			Bad = bad;
			Muted = muted;
			Accent = accent;
		}

		/// <summary>
		/// The colour a row resolves to, from its tone first and its kind second.
		/// </summary>
		/// <param name="kind">What the row is.</param>
		/// <param name="tone">How it reads.</param>
		public static Color Resolve(TooltipRowKind kind, TooltipTone tone)
		{
			switch (tone)
			{
				case TooltipTone.Good: return Good;
				case TooltipTone.Bad:
				case TooltipTone.Unmet: return Bad;
				case TooltipTone.Muted: return Muted;
				case TooltipTone.Accent: return Accent;
			}

			switch (kind)
			{
				case TooltipRowKind.Title:
				case TooltipRowKind.Header:
					return Title;
				case TooltipRowKind.Subtitle:
				case TooltipRowKind.Body:
				case TooltipRowKind.Hint:
					return Label;
				case TooltipRowKind.Stat:
				case TooltipRowKind.Requirement:
				case TooltipRowKind.Effect:
					return Stat;
				default:
					return Label;
			}
		}

		/// <summary>The rich-text hex string for a row's resolved colour.</summary>
		public static string ColorFor(TooltipRowKind kind, TooltipTone tone)
		{
			return ToHex(Resolve(kind, tone));
		}

		/// <summary>Formats a colour as the <c>#RRGGBBAA</c> rich text understands.</summary>
		public static string ToHex(Color color)
		{
			Color32 c = color;
			return $"#{c.r:X2}{c.g:X2}{c.b:X2}{c.a:X2}";
		}
	}
}
