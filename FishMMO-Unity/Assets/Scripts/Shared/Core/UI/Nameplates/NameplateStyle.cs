using System;
using UnityEngine;

namespace FishMMO.Shared.Core
{
	/// <summary>
	/// Where a colour comes from: the style itself, or the viewer's standing with the plate's owner.
	/// </summary>
	public enum NameplateTint : byte
	{
		/// <summary>The colour authored in the style is used as-is.</summary>
		Fixed = 0,

		/// <summary>
		/// The authored colour is blended toward the faction-standing colour the client resolved
		/// for this plate — green for an ally, blue for a neutral, red for an enemy, or an arena
		/// team's colour.
		/// </summary>
		Alliance = 1,
	}

	/// <summary>
	/// How a nameplate is drawn: its type, its background, its border and its box.
	/// </summary>
	/// <remarks>
	/// <para>A value type, so a style read from an asset cannot be mutated through the plate that
	/// borrowed it, and so a plate that authors its own style carries it inline rather than in a
	/// second object to keep alive.</para>
	///
	/// <para><b>Everything is authored in points at <see cref="ReferenceFontSize"/>.</b> A plate is
	/// drawn at whatever size perspective gives it, so a padding authored in absolute points would
	/// swallow a distant plate and vanish on a near one. The renderer scales every measurement here
	/// by the plate's resolved font size over the reference, which keeps a plate's proportions the
	/// same at every distance — the one property a nameplate must have, since its whole job is to
	/// be recognised at a glance from wherever the player happens to be standing.</para>
	///
	/// <para><b>Alliance tinting is a blend, not a replacement.</b> A background painted in the raw
	/// standing colour is a solid red or green box that fights the text it sits behind; a dark
	/// background nudged a third of the way toward that colour reads as hostile at a glance and
	/// still leaves the name legible. <see cref="BackgroundBlend"/> and <see cref="BorderBlend"/>
	/// are how far that nudge goes.</para>
	/// </remarks>
	[Serializable]
	public struct NameplateStyle
	{
		/// <summary>
		/// The font size, in points, that every other measurement in this style is authored against.
		/// </summary>
		/// <remarks>
		/// Fourteen points is a nameplate at conversational distance with the default settings, so
		/// authored values read as the sizes a developer sees while standing in front of the thing
		/// they are tuning.
		/// </remarks>
		public const float ReferenceFontSize = 14.0f;

		// ── Text ────────────────────────────────────────────────────

		/// <summary>
		/// The name row's size in WORLD units; the renderer converts it to points by distance.
		/// </summary>
		/// <remarks>
		/// World units rather than points for the same reason <see cref="WorldLabel.fontSize"/> is:
		/// a plate shrinks with distance exactly as a piece of geometry over the character's head
		/// would, and a point size cannot express that.
		/// </remarks>
		public float FontSize;

		/// <summary>Where the name row's colour comes from.</summary>
		public NameplateTint NameTint;

		/// <summary>The name row's colour, or the base of its alliance blend.</summary>
		public Color NameColor;

		/// <summary>
		/// How far the name colour moves toward the alliance tint when <see cref="NameTint"/> is
		/// <see cref="NameplateTint.Alliance"/>. One is the standing colour itself.
		/// </summary>
		public float NameBlend;

		/// <summary>The colour of every row that is neither the name nor coloured by its writer.</summary>
		public Color SecondaryColor;

		/// <summary>
		/// The height of a row's box, as a multiple of that row's own font size.
		/// </summary>
		/// <remarks>
		/// A row left to size itself gets UI Toolkit's line box, which is roughly 1.3x the font
		/// size — leading that is right for a paragraph and far too loose for three words stacked
		/// over someone's head. Setting the height explicitly and centring the text in it is the
		/// only handle on that: UI Toolkit has no line-height property. Values below about 0.95
		/// start clipping descenders on the tallest rows.
		/// </remarks>
		public float LineHeight;

		/// <summary>The gap between rows, in points at <see cref="ReferenceFontSize"/>.</summary>
		/// <remarks>
		/// With <see cref="LineHeight"/> at 1 this IS the gap a reader sees — four points at the
		/// reference size, growing with the plate like every other measurement here. It is worth
		/// having the two fields separate rather than folding the gap into the row height: the
		/// height decides where a row's text sits, the gap decides how far the next one is, and a
		/// single number would make "tighter" also mean "off-centre".
		/// </remarks>
		public float LineSpacing;

		// ── Background ──────────────────────────────────────────────

		/// <summary>Whether the plate paints a background behind its rows.</summary>
		public bool ShowBackground;

		/// <summary>Where the background colour comes from.</summary>
		public NameplateTint BackgroundTint;

		/// <summary>The background colour, or the base of its alliance blend.</summary>
		public Color BackgroundColor;

		/// <summary>How far the background moves toward the alliance tint, 0 to 1.</summary>
		public float BackgroundBlend;

		/// <summary>The background's final alpha, 0 to 1.</summary>
		public float BackgroundOpacity;

		// ── Border ──────────────────────────────────────────────────

		/// <summary>
		/// Whether the plate draws a border. This is the "this one matters" switch: a boss, a rare
		/// spawn, a quest giver.
		/// </summary>
		public bool ShowBorder;

		/// <summary>Where the border colour comes from.</summary>
		public NameplateTint BorderTint;

		/// <summary>The border colour, or the base of its alliance blend.</summary>
		public Color BorderColor;

		/// <summary>How far the border moves toward the alliance tint, 0 to 1.</summary>
		public float BorderBlend;

		/// <summary>Border thickness, in points at <see cref="ReferenceFontSize"/>.</summary>
		public float BorderWidth;

		// ── Box ─────────────────────────────────────────────────────

		/// <summary>Corner radius, in points at <see cref="ReferenceFontSize"/>.</summary>
		public float CornerRadius;

		/// <summary>Padding left and right of the rows, in points at <see cref="ReferenceFontSize"/>.</summary>
		public float PaddingHorizontal;

		/// <summary>Padding above and below the rows, in points at <see cref="ReferenceFontSize"/>.</summary>
		public float PaddingVertical;

		/// <summary>
		/// Smallest plate width, in points at <see cref="ReferenceFontSize"/>. Zero fits the rows.
		/// </summary>
		/// <remarks>
		/// A floor rather than a fixed width: a set of plates that are all the same width reads as
		/// a UI, and a set that each hug their own text reads as labels on the world. This exists
		/// so a one-letter name still gets a plate somebody can aim at.
		/// </remarks>
		public float MinWidth;

		/// <summary>
		/// Vertical distance in WORLD units between the anchor point and the bottom of the plate.
		/// </summary>
		/// <remarks>
		/// Authored in world units so the gap over a character's head stays the same size as the
		/// character at every distance, which a point offset would not.
		/// </remarks>
		public float AnchorGap;

		/// <summary>
		/// The style a plate uses when it authors none of its own and references no asset.
		/// </summary>
		/// <remarks>
		/// A translucent near-black plate, the name in the standing colour, a background nudged a
		/// third of the way toward the same colour, and no border. That reproduces what the old
		/// stacked <see cref="WorldLabel"/> nameplates looked like — coloured name, coloured guild
		/// line, nothing behind them — with the background added, and it is what every character in
		/// the game gets until somebody says otherwise.
		/// </remarks>
		public static NameplateStyle Default => new NameplateStyle
		{
			FontSize = 0.25f,
			NameTint = NameplateTint.Alliance,
			NameColor = Color.white,
			NameBlend = 1.0f,
			SecondaryColor = new Color(0.82f, 0.82f, 0.86f, 1.0f),
			LineHeight = 1.0f,
			LineSpacing = 4.0f,

			ShowBackground = true,
			BackgroundTint = NameplateTint.Alliance,
			BackgroundColor = new Color(0.04f, 0.04f, 0.06f, 1.0f),
			BackgroundBlend = 0.3f,
			BackgroundOpacity = 0.55f,

			ShowBorder = false,
			BorderTint = NameplateTint.Alliance,
			BorderColor = new Color(0.85f, 0.85f, 0.9f, 1.0f),
			BorderBlend = 0.75f,
			BorderWidth = 1.0f,

			CornerRadius = 4.0f,
			PaddingHorizontal = 6.0f,
			PaddingVertical = 2.0f,
			MinWidth = 0.0f,
			AnchorGap = 0.0f,
		};

		/// <summary>
		/// The colour the name row is drawn in.
		/// </summary>
		/// <param name="allianceTint">The plate's current faction-standing tint.</param>
		public Color ResolveNameColor(Color allianceTint)
		{
			return Blend(NameColor, allianceTint, NameTint, NameBlend, 1.0f);
		}

		/// <summary>
		/// The colour the background is painted in, alpha included.
		/// </summary>
		/// <param name="allianceTint">The plate's current faction-standing tint.</param>
		public Color ResolveBackgroundColor(Color allianceTint)
		{
			return Blend(BackgroundColor, allianceTint, BackgroundTint, BackgroundBlend, BackgroundOpacity);
		}

		/// <summary>
		/// The colour the border is drawn in.
		/// </summary>
		/// <param name="allianceTint">The plate's current faction-standing tint.</param>
		public Color ResolveBorderColor(Color allianceTint)
		{
			return Blend(BorderColor, allianceTint, BorderTint, BorderBlend, BorderColor.a);
		}

		/// <summary>
		/// Blends a base colour toward a tint and applies an explicit alpha.
		/// </summary>
		/// <remarks>
		/// The alliance tint's own alpha is deliberately dropped: it is a standing colour, and
		/// letting it carry opacity would make a plate's transparency depend on who is looking
		/// at it.
		/// </remarks>
		private static Color Blend(Color baseColor, Color tint, NameplateTint source, float blend, float alpha)
		{
			Color result = baseColor;
			if (source == NameplateTint.Alliance)
			{
				float t = Mathf.Clamp01(blend);
				result = new Color(
					Mathf.Lerp(baseColor.r, tint.r, t),
					Mathf.Lerp(baseColor.g, tint.g, t),
					Mathf.Lerp(baseColor.b, tint.b, t),
					baseColor.a);
			}
			result.a = Mathf.Clamp01(alpha);
			return result;
		}
	}
}
