#if UNITY_EDITOR
using FishMMO.Shared.Biomes;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// The run-time cave cover's choice for a cave (2026-10-20): which <see cref="CaveCoverType"/>s it grows, how thickly, how
	/// strongly its glowing cover glows and in which colours. Decided at the cut, where the cave's biome and form are known, and
	/// written onto the cave's client-only Visual as a <see cref="CaveCoverSite"/> (<see cref="Attach"/>), which the client's
	/// cover renderer reads: at run time the client knows neither.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>One lookup.</b> Every per-biome and per-form choice the cover makes is in <see cref="For"/>, read from the biome's
	/// existing template data (its climate envelope, the crystals its caves grow) with code defaults otherwise, so the biome
	/// suite's cave-interior phase can move them into template fields later.
	/// </para>
	/// <para>
	/// <b>The dressing's palette.</b> The glow colours are the dressing's for the same system (<see cref="CaveGlowPalettes.For"/>,
	/// which both call), so the moss and caps glow in the hue of the cave's glowcaps and the glints in its crystals'.
	/// </para>
	/// </remarks>
	public static class CaveCoverSpecies
	{
		/// <summary>The cover of a cave of a form in a biome (null: no biome, the code defaults), its colours from the system's seed.</summary>
		public static CaveCoverSite.Choice For(BiomeTemplate biome, CaveForm form, int seed) => For(new CaveBiome(biome, null), form, seed);

		/// <summary>
		/// The cover of a cave by its cave biomes (2026-10-25, I2n): its climate envelope the HOST's (the land over a cave is its air
		/// and water), its glow colours the dressing's for the same system, which reads the biome dressing the inside
		/// (<see cref="CaveGlowPalettes.For(CaveForm, int, in CaveBiome)"/>).
		/// </summary>
		public static CaveCoverSite.Choice For(in CaveBiome caves, CaveForm form, int seed)
		{
			BiomeTemplate biome = caves.Host;
			CaveCoverSite.Choice choice = CaveCoverSite.Default;
			choice.Form = (byte)form;
			switch (form)
			{
				case CaveForm.IceCave:
					choice.Types = CaveCoverTypes.Ice;
					choice.Glow = 0.8f;
					break;
				case CaveForm.SeaHall:
					// The ledge's rime and frost (2026-10-26, I2n-b); the flooded floor's growth is the sea's (PointOfInterestCaveDressing.SeaHall.cs).
					choice.Types = CaveCoverTypes.Ice;
					choice.Glow = 0.4f;
					break;
				case CaveForm.LavaTube:
					choice.Types = CaveCoverTypes.Lava;
					choice.Glow = 0.9f;
					break;
				case CaveForm.Mine:
					choice.Types = CaveCoverTypes.Mine;
					choice.Glow = 0.7f;
					break;
				default:
					choice.Types = CaveCoverTypes.Natural;
					choice.Glow = 1f;
					break;
			}
			if (biome != null)
			{
				Climate(biome, form, ref choice);
			}
			Palette(in caves, form, seed, ref choice);
			return choice;
		}

		/// <summary>The biome's climate envelope: an arid biome's caves have no moss, pearls or mud and less of everything; a wet one's more; a cold one's natural caves frost.</summary>
		private static void Climate(BiomeTemplate biome, CaveForm form, ref CaveCoverSite.Choice choice)
		{
			float humidity = 0.5f * (biome.MinHumidity + biome.MaxHumidity);
			if (humidity < -0.35f)
			{
				choice.Types &= ~CaveCoverTypes.Mask(CaveCoverType.MossCushion, CaveCoverType.CavePearls, CaveCoverType.MudSheen);
				choice.Density *= 0.8f;
			}
			else if (humidity > 0.35f)
			{
				choice.Density *= 1.15f;
			}
			bool natural = form == CaveForm.Cave || form == CaveForm.Grotto || form == CaveForm.SeaCave;
			if (natural && biome.MaxTemperature < -0.25f)
			{
				choice.Types |= CaveCoverTypes.Bit(CaveCoverType.FrostCrystals);
			}
		}

		/// <summary>The glow colours: the dressing's lead and accent fungus hues and its crystal hue for the system (see the remarks).</summary>
		private static void Palette(in CaveBiome caves, CaveForm form, int seed, ref CaveCoverSite.Choice choice)
		{
			CaveGlowPalette p = CaveGlowPalettes.For(form, seed, in caves);
			CaveGlowHue[] crystals = ProceduralArtCatalogue.CrystalHues, fungi = ProceduralArtCatalogue.FungusHues;
			choice.Crystal = crystals[p.Crystal % crystals.Length].Emission;
			if (p.Minerals)
			{
				// The ways are lined with the biome's own glowing crystal: its own light, where it has one.
				Color glow = ProceduralArtCatalogue.CaveMineralOf(p.Mineral).Hue.Emission;
				if (glow.maxColorComponent > 0f)
				{
					choice.Crystal = glow;
				}
			}
			if (p.Fungi)
			{
				choice.Fungus = fungi[p.Fungus % fungi.Length].Emission;
				choice.Accent = fungi[p.FungusAccent % fungi.Length].Emission;
			}
			else if (form == CaveForm.LavaTube)
			{
				// No fungi: the glowing cover is the tube's emberstone.
				choice.Fungus = choice.Accent = HueNamed(ProceduralArtCatalogue.GlowOreHues, "Emberstone", choice.Crystal);
			}
			else
			{
				// No fungi (an ice cave): the glowing cover is its crystals' light.
				choice.Fungus = choice.Accent = choice.Crystal;
			}
		}

		private static Color HueNamed(CaveGlowHue[] hues, string name, Color fallback)
		{
			foreach (CaveGlowHue hue in hues)
			{
				if (hue.Name == name && hue.Emission.maxColorComponent > 0f)
				{
					return hue.Emission;
				}
			}
			return fallback;
		}

		/// <summary>
		/// Writes a cave's choice onto its Visual (the <see cref="CaveVisibility"/> under the cave's rock that
		/// CaveShaper.Build made): the one call the cut makes. Returns the rock it was given.
		/// </summary>
		public static GameObject Attach(GameObject caveRock, CaveSystem system, BiomeTemplate biome) => Attach(caveRock, system, new CaveBiome(biome, null));

		/// <summary><see cref="Attach(GameObject, CaveSystem, BiomeTemplate)"/> by the cave's biomes (2026-10-25, I2n: host climate, interior palette).</summary>
		public static GameObject Attach(GameObject caveRock, CaveSystem system, in CaveBiome caves)
		{
			if (caveRock == null || system == null)
			{
				return caveRock;
			}
			CaveVisibility visual = caveRock.GetComponentInChildren<CaveVisibility>(true);
			if (visual == null)
			{
				return caveRock;
			}
			CaveCoverSite site = visual.GetComponent<CaveCoverSite>();
			if (site == null)
			{
				site = visual.gameObject.AddComponent<CaveCoverSite>();
			}
			site.Configure(For(in caves, system.Form, system.Seed));
			return caveRock;
		}
	}
}
#endif
