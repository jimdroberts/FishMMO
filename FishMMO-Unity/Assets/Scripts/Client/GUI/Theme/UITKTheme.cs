using UnityEngine;
using FishMMO.Shared;

namespace FishMMO.Client
{
	/// <summary>
	/// The set of colours a player may override, parsed once from configuration.
	/// </summary>
	/// <remarks>
	/// The thirteen colour names and their storage format — <c>{Name}ColorR/G/B/A</c> as bytes —
	/// are inherited from the theme the Canvas UI used, so a config file written by an older
	/// client still themes this one. Nothing about those keys was Canvas-specific; they name
	/// colours in the game, not components in a scene.
	///
	/// What did not carry over is the rest of that theme: layout spacing, padding, grid spacing,
	/// scroll sensitivity, elasticity and font auto-size bounds. Those existed because the old UI
	/// had no stylesheet — geometry could only be themed by walking components and writing
	/// fields. That is what a USS file is for now, and re-exposing them as runtime config would
	/// mean re-implementing the cascade by hand.
	///
	/// <see cref="IsOverridden"/> distinguishes "the player chose this colour" from "no config
	/// entry exists". Without it a fresh install reads zeros and repaints the entire UI
	/// transparent-black, which is what an unguarded <c>TryGetByte</c> returns when the key is
	/// missing.
	/// </remarks>
	public sealed class UITKTheme
	{
		/// <summary>Primary UI element color (e.g., backgrounds, normal states).</summary>
		public readonly Color Primary;

		/// <summary>Secondary UI element color (e.g., pressed/selected states).</summary>
		public readonly Color Secondary;

		/// <summary>Highlight color for hovered/focused UI elements.</summary>
		public readonly Color Highlight;

		/// <summary>General background color.</summary>
		public readonly Color Background;

		/// <summary>Default text color.</summary>
		public readonly Color Text;

		/// <summary>Health bar color.</summary>
		public readonly Color Health;

		/// <summary>Mana bar color.</summary>
		public readonly Color Mana;

		/// <summary>Stamina bar color.</summary>
		public readonly Color Stamina;

		/// <summary>Crosshair color.</summary>
		public readonly Color Crosshair;

		/// <summary>Tooltip title/header color.</summary>
		public readonly Color TooltipTitle;

		/// <summary>Tooltip label/description color.</summary>
		public readonly Color TooltipLabel;

		/// <summary>Tooltip value/positive-number color.</summary>
		public readonly Color TooltipValue;

		/// <summary>Tooltip stat/general-number color.</summary>
		public readonly Color TooltipStat;

		/// <summary>
		/// True when at least one colour was actually present in configuration.
		/// </summary>
		/// <remarks>
		/// A theme with nothing overridden is applied as a no-op, leaving every panel on the
		/// values authored in FishMMO-Theme.uss.
		/// </remarks>
		public readonly bool IsOverridden;

		/// <summary>Per-colour flags recording which names were present in configuration.</summary>
		private readonly bool[] present = new bool[ColorNames.Length];

		/// <summary>
		/// Configuration key prefixes, in the order <see cref="present"/> stores them.
		/// </summary>
		public static readonly string[] ColorNames =
		{
			"Primary",
			"Secondary",
			"Highlight",
			"Background",
			"Text",
			"Health",
			"Mana",
			"Stamina",
			"Crosshair",
			"TooltipTitle",
			"TooltipLabel",
			"TooltipValue",
			"TooltipStat",
		};

		/// <summary>
		/// Builds a theme by parsing every colour entry from configuration once.
		/// </summary>
		/// <param name="configuration">Configuration to read from. May be null.</param>
		public UITKTheme(Configuration configuration)
		{
			bool any = false;

			Primary      = Parse(configuration, 0, out bool p0);  any |= p0;
			Secondary    = Parse(configuration, 1, out bool p1);  any |= p1;
			Highlight    = Parse(configuration, 2, out bool p2);  any |= p2;
			Background   = Parse(configuration, 3, out bool p3);  any |= p3;
			Text         = Parse(configuration, 4, out bool p4);  any |= p4;
			Health       = Parse(configuration, 5, out bool p5);  any |= p5;
			Mana         = Parse(configuration, 6, out bool p6);  any |= p6;
			Stamina      = Parse(configuration, 7, out bool p7);  any |= p7;
			Crosshair    = Parse(configuration, 8, out bool p8);  any |= p8;
			TooltipTitle = Parse(configuration, 9, out bool p9);  any |= p9;
			TooltipLabel = Parse(configuration, 10, out bool p10); any |= p10;
			TooltipValue = Parse(configuration, 11, out bool p11); any |= p11;
			TooltipStat  = Parse(configuration, 12, out bool p12); any |= p12;

			IsOverridden = any;
		}

		/// <summary>
		/// Whether the colour at the given index was present in configuration.
		/// </summary>
		/// <param name="index">Index into <see cref="ColorNames"/>.</param>
		/// <returns>True when the player has set this colour.</returns>
		public bool HasOverride(int index)
		{
			return index >= 0 && index < present.Length && present[index];
		}

		/// <summary>
		/// Whether the named colour was present in configuration.
		/// </summary>
		/// <param name="name">One of <see cref="ColorNames"/>.</param>
		/// <returns>True when the player has set this colour.</returns>
		public bool HasOverride(string name)
		{
			return HasOverride(System.Array.IndexOf(ColorNames, name));
		}

		/// <summary>
		/// Reads one <c>{name}ColorR/G/B/A</c> group.
		/// </summary>
		/// <param name="configuration">Configuration to read from. May be null.</param>
		/// <param name="index">Index into <see cref="ColorNames"/>.</param>
		/// <param name="found">Set to true when the entry exists.</param>
		/// <returns>The parsed colour, or white when absent.</returns>
		private Color Parse(Configuration configuration, int index, out bool found)
		{
			found = false;
			if (configuration == null)
			{
				return Color.white;
			}

			string name = ColorNames[index];

			/* The R channel decides presence for the whole group. A partially written group is
			 * not something the writer can produce — Save writes all four together — so probing
			 * one key is enough, and treating a group as absent unless every channel is present
			 * would discard a legitimately black-with-zero-red colour. */
			if (!configuration.TryGetByte($"{name}ColorR", out byte r))
			{
				return Color.white;
			}
			configuration.TryGetByte($"{name}ColorG", out byte g);
			configuration.TryGetByte($"{name}ColorB", out byte b);
			if (!configuration.TryGetByte($"{name}ColorA", out byte a))
			{
				// Alpha absent means an older config; fully opaque is the sane reading.
				a = 255;
			}

			found = true;
			present[index] = true;
			return new Color32(r, g, b, a);
		}

		/// <summary>
		/// Writes a colour to configuration in the <c>{name}ColorR/G/B/A</c> format.
		/// </summary>
		/// <param name="configuration">Configuration to write to.</param>
		/// <param name="name">One of <see cref="ColorNames"/>.</param>
		/// <param name="color">The colour to store.</param>
		public static void Write(Configuration configuration, string name, Color color)
		{
			if (configuration == null || string.IsNullOrEmpty(name))
			{
				return;
			}

			Color32 c = color;
			configuration.Set($"{name}ColorR", c.r);
			configuration.Set($"{name}ColorG", c.g);
			configuration.Set($"{name}ColorB", c.b);
			configuration.Set($"{name}ColorA", c.a);
		}

		/// <summary>
		/// Removes a colour override from configuration, returning it to the stylesheet default.
		/// </summary>
		/// <param name="configuration">Configuration to write to.</param>
		/// <param name="name">One of <see cref="ColorNames"/>.</param>
		/// <remarks>
		/// Configuration has no delete, so "no override" is expressed as an empty value — which
		/// <c>TryGetByte</c> fails to parse, and a failed parse is exactly how <see cref="Parse"/>
		/// already detects absence.
		/// </remarks>
		public static void Clear(Configuration configuration, string name)
		{
			if (configuration == null || string.IsNullOrEmpty(name))
			{
				return;
			}

			configuration.Set($"{name}ColorR", string.Empty);
			configuration.Set($"{name}ColorG", string.Empty);
			configuration.Set($"{name}ColorB", string.Empty);
			configuration.Set($"{name}ColorA", string.Empty);
		}
	}
}
