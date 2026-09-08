using System;
using UnityEngine;

namespace FishMMO.Shared.Core
{
	/// <summary>
	/// One row of an overhead nameplate.
	/// </summary>
	/// <remarks>
	/// A value type held in a list on <see cref="Nameplate"/>: rows are written far more often
	/// than they are created — a guild name arriving, a status clearing — and a struct in a list
	/// means those writes allocate nothing and the renderer can walk the rows without chasing
	/// references.
	/// <para>
	/// Colour is stored as a colour plus a flag rather than as a nullable, because the plate's
	/// style is what decides an uncoloured row's colour and that decision has to be re-made
	/// whenever the style or the faction tint changes — long after the row was written.
	/// </para>
	/// </remarks>
	[Serializable]
	public struct NameplateLine
	{
		/// <summary>
		/// The row's identity: a <see cref="NameplateSlot"/> value, or a developer's own key.
		/// </summary>
		/// <remarks>
		/// Writing a row replaces the row with the same key, which is what lets a caller push a
		/// name or a status repeatedly without checking whether it is already there.
		/// </remarks>
		public int Key;

		/// <summary>Stacking order, ascending from the top of the plate.</summary>
		public int Order;

		/// <summary>The row's text. Rich-text markup is permitted.</summary>
		public string Text;

		/// <summary>The row's colour, when <see cref="HasColor"/> is set.</summary>
		public Color Color;

		/// <summary>True when <see cref="Color"/> overrides the colour the style would give.</summary>
		public bool HasColor;

		/// <summary>Font scale relative to the plate's resolved font size.</summary>
		public float Scale;

		/// <summary>The slot this row occupies, for rows written through a slot.</summary>
		public NameplateSlot Slot => (NameplateSlot)Key;

		/// <summary>
		/// The colour this row is drawn in.
		/// </summary>
		/// <param name="style">The plate's style.</param>
		/// <param name="allianceTint">The plate's current faction-standing tint.</param>
		/// <returns>The row's own colour, or the colour the style gives its kind of row.</returns>
		public Color ResolveColor(in NameplateStyle style, Color allianceTint)
		{
			if (HasColor)
			{
				return Color;
			}
			return NameplateSlots.UsesNameColor(Slot)
				? style.ResolveNameColor(allianceTint)
				: style.SecondaryColor;
		}
	}
}
