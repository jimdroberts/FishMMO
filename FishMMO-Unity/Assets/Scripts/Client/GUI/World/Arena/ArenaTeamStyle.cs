using UnityEngine;
using UnityEngine.UIElements;
using FishMMO.Shared;

namespace FishMMO.Client
{
	/// <summary>
	/// Paints a UI Toolkit element in an arena team's colour.
	/// </summary>
	/// <remarks>
	/// Team colours are data — authored per <see cref="ArenaTemplate"/> — not theme tokens, so they
	/// cannot come from a stylesheet and are written as inline styles, the same way runtime theme
	/// overrides are. The colour itself is the border and the text; the fill is the same colour at
	/// low alpha, so a badge stays legible over the panel's dark ground.
	/// </remarks>
	public static class ArenaTeamStyle
	{
		/// <summary>Colours a badge or chip: tinted fill, solid border and text.</summary>
		public static void Apply(VisualElement element, Color teamColor)
		{
			if (element == null)
			{
				return;
			}

			Color fill = new Color(teamColor.r, teamColor.g, teamColor.b, 0.18f);
			element.style.backgroundColor = fill;
			element.style.borderTopColor = teamColor;
			element.style.borderBottomColor = teamColor;
			element.style.borderLeftColor = teamColor;
			element.style.borderRightColor = teamColor;
			element.style.color = teamColor;
		}

		/// <summary>Colours text only.</summary>
		public static void ApplyText(VisualElement element, Color teamColor)
		{
			if (element != null)
			{
				element.style.color = teamColor;
			}
		}
	}
}
