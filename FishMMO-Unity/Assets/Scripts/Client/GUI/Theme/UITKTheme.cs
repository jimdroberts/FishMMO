using UnityEngine;
using FishMMO.Shared;

namespace FishMMO.Client
{
	/// <summary>
	/// The set of colours a player may override, parsed once from configuration.
	/// </summary>
	/// <remarks>
	/// The colour names and their storage format — <c>{Name}ColorR/G/B/A</c> as bytes — are
	/// inherited from the theme the Canvas UI used, so a config file written by an older client
	/// still themes this one.
	///
	/// <b>But only half of it does.</b> That inheritance was taken on the reading that the keys
	/// name colours in the game rather than components in a scene, and for
	/// <c>Text</c>, <c>Health</c>, <c>Mana</c>, <c>Stamina</c>, <c>Crosshair</c> and
	/// <c>TooltipLabel</c> that holds — a health bar is red in any skin. It does not hold for the
	/// four surface colours. <c>Primary</c>, <c>Secondary</c>, <c>Highlight</c> and
	/// <c>Background</c> named Canvas widgets and carry that skin's greys, and applying them to
	/// this one repaints every panel in the palette of a UI that no longer exists. A player who
	/// once set what that panel called "Window Background" gets a grey client, having asked for
	/// nothing of the sort.
	///
	/// So the surface four are honoured only from a config this skin has written, marked by
	/// <see cref="VersionKey"/>. Older entries are left on disk and ignored; setting any colour
	/// in the options panel stamps the version and brings the whole set back into force. The
	/// stamp exists because there is no other way to tell a value this skin was given from one
	/// it merely inherited — both are four bytes under the same key.
	///
	/// <b>This was dormant until the store got an owner.</b> <c>Configuration.GlobalSettings</c>
	/// used to be created by whichever of two unrelated places asked for it first, and in a
	/// client started past the launcher neither did — so the theme parsed nothing and every panel
	/// kept its stylesheet colours. Making <see cref="ClientSettings"/> the single owner is what
	/// first put these keys into effect, which is why a config years old could turn a UI grey the
	/// day the loading order was fixed.
	///
	/// Three of that set are gone: <c>TooltipTitle</c>, <c>TooltipValue</c> and
	/// <c>TooltipStat</c>. Every colour here reaches the screen by being written onto elements
	/// carrying a particular USS class, and no element in the client carries
	/// <c>fish-tooltip__title</c> or <c>fish-tooltip__stat</c> — the tooltip is a single label
	/// whose internal emphasis comes from rich-text tags emitted by shared code, which cannot
	/// reach a client-side theme. <c>TooltipValue</c> never had a class at all. All three were
	/// listed in the options panel as editable colours that changed nothing when edited. A
	/// config file that still carries their keys is simply ignored.
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

		/// <summary>Tooltip label/description color.</summary>
		public readonly Color TooltipLabel;

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
		/// Whether the surface colours in this configuration were written by this skin.
		/// </summary>
		private readonly bool surfacesHonoured;

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
			"TooltipLabel",
		};

		/// <summary>
		/// Configuration key marking which skin last wrote the colour set.
		/// </summary>
		public const string VersionKey = "UIThemeVersion";

		/// <summary>
		/// The current skin's version. Absent or lower means the surface colours in the file
		/// belong to a skin this one shares no palette with.
		/// </summary>
		public const int Version = 1;

		/// <summary>
		/// How many leading entries of <see cref="ColorNames"/> paint UI surfaces rather than
		/// things in the game. These are the ones a pre-<see cref="Version"/> config cannot
		/// meaningfully set, and they are deliberately first in the array so the test is a
		/// comparison rather than a lookup.
		/// </summary>
		private const int SurfaceColorCount = 4;

		/// <summary>
		/// Builds a theme by parsing every colour entry from configuration once.
		/// </summary>
		/// <param name="configuration">Configuration to read from. May be null.</param>
		public UITKTheme(Configuration configuration)
		{
			bool any = false;

			/* Read before any colour is: it decides whether the surface four are read at all. A
			 * file with no stamp is either pre-UITK or has never had a colour set, and neither
			 * has an opinion about this skin's surfaces worth honouring. */
			int version = 0;
			configuration?.TryGetInt(VersionKey, out version, 0);
			surfacesHonoured = version >= Version;

			Primary      = Parse(configuration, 0, out bool p0);  any |= p0;
			Secondary    = Parse(configuration, 1, out bool p1);  any |= p1;
			Highlight    = Parse(configuration, 2, out bool p2);  any |= p2;
			Background   = Parse(configuration, 3, out bool p3);  any |= p3;
			Text         = Parse(configuration, 4, out bool p4);  any |= p4;
			Health       = Parse(configuration, 5, out bool p5);  any |= p5;
			Mana         = Parse(configuration, 6, out bool p6);  any |= p6;
			Stamina      = Parse(configuration, 7, out bool p7);  any |= p7;
			Crosshair    = Parse(configuration, 8, out bool p8);  any |= p8;
			TooltipLabel = Parse(configuration, 9, out bool p9);  any |= p9;

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

			/* Ignored rather than erased. The player may have a client on another machine that
			 * still reads them, and a value this one declines to apply is not a value it is
			 * entitled to delete. */
			if (index < SurfaceColorCount && !surfacesHonoured)
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

			/* Stamped on every write, not just a surface one. The stamp says "this skin owns the
			 * colours in this file", and a player who sets a health bar colour has just made that
			 * true of the whole set. */
			configuration.Set(VersionKey, Version);

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
		/// The keys are removed rather than emptied. Both read back as "not overridden" —
		/// <see cref="Parse"/> detects absence by <c>TryGetByte</c> failing, and an empty value
		/// fails to parse just as a missing key does — but only one of them leaves the file
		/// clean. Emptying wrote four dead lines per colour, so a player who reset their theme,
		/// or loaded a shared profile that sets no colours, ended up with forty entries in
		/// Configuration.cfg that exist only to say nothing.
		/// </remarks>
		public static void Clear(Configuration configuration, string name)
		{
			if (configuration == null || string.IsNullOrEmpty(name))
			{
				return;
			}

			configuration.Remove($"{name}ColorR");
			configuration.Remove($"{name}ColorG");
			configuration.Remove($"{name}ColorB");
			configuration.Remove($"{name}ColorA");
		}
	}
}
