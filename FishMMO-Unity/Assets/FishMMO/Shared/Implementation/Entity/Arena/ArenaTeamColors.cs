using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// The colours arena teams are drawn in when a template does not author its own.
	/// </summary>
	/// <remarks>
	/// Chosen to stay distinct from the faction colours the open world uses — green for ally, red
	/// for enemy, sky blue for neutral — so an arena never looks like a faction fight: team one is
	/// a saturated blue, team two an orange-red, and the rest are spaced around the wheel. A
	/// template lists its own <c>TeamColors</c> to override any of these.
	/// </remarks>
	public static class ArenaTeamColors
	{
		/// <summary>Palette by team index. Indices past the end wrap.</summary>
		public static readonly Color[] Defaults =
		{
			new Color(0.30f, 0.55f, 1.00f), // team 1: blue
			new Color(1.00f, 0.40f, 0.30f), // team 2: orange-red
			new Color(0.55f, 0.90f, 0.35f), // team 3: lime
			new Color(1.00f, 0.85f, 0.25f), // team 4: gold
			new Color(0.80f, 0.45f, 1.00f), // team 5: violet
			new Color(0.30f, 0.90f, 0.90f), // team 6: cyan
			new Color(1.00f, 0.55f, 0.80f), // team 7: pink
			new Color(0.85f, 0.85f, 0.85f), // team 8: silver
		};

		/// <summary>The default colour for a team index. Negative indices are neutral grey.</summary>
		public static Color Default(int team)
		{
			if (team < 0)
			{
				return new Color(0.6f, 0.6f, 0.6f);
			}
			return Defaults[team % Defaults.Length];
		}

		/// <summary>A CSS-style hex string for rich text, e.g. <c>#4C8CFF</c>.</summary>
		public static string ToHex(Color color)
		{
			return "#" + ColorUtility.ToHtmlStringRGB(color);
		}
	}
}
