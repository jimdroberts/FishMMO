#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// How tall a race stands, metres, and so how big the buildings of its settlements are (2026-10-11: "1 unit = 1 meter
	/// and a 6-7 foot tall character is realistic. Giants will exist too and need their own house sizes. Other races too.").
	/// </summary>
	/// <remarks>
	/// <para>
	/// The structure kit is built for <see cref="Reference"/>: doors 2.25 m and more, rooms 2.8 m high, beds 2.2 m long, a
	/// 6–7 ft person's. A race's site is built at <see cref="BuildingScale"/> of that — a hill giant's longhouse two and a
	/// half times as big, a halfling's cottage smaller, down to MinScale — doors, rooms, furniture and steps with it.
	/// </para>
	/// <para>
	/// The race templates carry no height of their own yet (every <c>DefaultProportions.Height</c> is 1), so the statures
	/// are tabled here by name, from the folklore and game canon each race comes from: giants of each kind, ogres and
	/// trolls, the small folk and the fey. A race not tabled stands at <see cref="Reference"/>.
	/// </para>
	/// </remarks>
	public static class RaceStature
	{
		/// <summary>The stature the kit is built for: a tall human, 6 ft 5 in.</summary>
		public const float Reference = 1.95f;
		/// <summary>
		/// The smallest a site's buildings are scaled: 0.85 of the kit's, doors 1.9 m. Small folk build smaller, but never
		/// smaller than a player of any race can walk into (a halfling's 0.56 made 1.35 m doors no player could pass).
		/// </summary>
		public const float MinScale = 0.85f, MaxScale = 4f;

		private static readonly Dictionary<string, float> metres = new Dictionary<string, float>
		{
			// Giants (the Giant category): canon heights, 16–26 ft.
			["hillgiant"] = 4.9f, ["stonegiant"] = 5.5f, ["frostgiant"] = 6.4f, ["firegiant"] = 5.5f, ["cloudgiant"] = 7.3f,
			["stormgiant"] = 7.9f, ["forestgiant"] = 5.8f, ["mountaingiant"] = 7f, ["eldergiant"] = 7.6f, ["seagiant"] = 6.7f,
			["sandgiant"] = 5.2f, ["boggiant"] = 5f, ["ettin"] = 4.3f, ["cyclops"] = 4.5f,
			// The big folk.
			["ogre"] = 2.9f, ["troll"] = 2.8f, ["cavetroll"] = 3f, ["snowtroll"] = 2.9f, ["bogtroll"] = 2.7f, ["seatroll"] = 3f,
			["minotaur"] = 2.6f, ["centaur"] = 2.4f, ["bugbear"] = 2.2f, ["treefolk"] = 4f,
			// The small folk.
			["dwarf"] = 1.45f, ["halfling"] = 1.1f, ["gnome"] = 1.05f, ["deepgnome"] = 1.05f, ["goblin"] = 1.1f, ["boggoblin"] = 1.05f,
			["hillgoblin"] = 1.15f, ["kobold"] = 0.95f, ["ratfolk"] = 1.2f, ["frogfolk"] = 1.1f, ["poisonfrogfolk"] = 1f,
			// The fey's least.
			["pixie"] = 0.45f, ["sprite"] = 0.5f, ["frostsprite"] = 0.5f, ["brownie"] = 0.6f,
		};

		/// <summary>A race's stature, metres (a tabled race's, else <see cref="Reference"/>).</summary>
		public static float HeightMetres(string race)
		{
			string key = Key(race);
			return key.Length > 0 && metres.TryGetValue(key, out float m) ? m : Reference;
		}

		/// <summary>How big a race's buildings are against the kit's (1 for a race of the kit's stature), clamped.</summary>
		public static float BuildingScale(string race) => Mathf.Clamp(HeightMetres(race) / Reference, MinScale, MaxScale);

		/// <summary>"Hill Giant", "hill-giant", "hillgiant" → "hillgiant".</summary>
		private static string Key(string race)
		{
			if (string.IsNullOrEmpty(race))
			{
				return string.Empty;
			}
			var sb = new StringBuilder(race.Length);
			foreach (char c in race)
			{
				if (char.IsLetter(c))
				{
					sb.Append(char.ToLowerInvariant(c));
				}
			}
			return sb.ToString();
		}
	}
}
#endif
