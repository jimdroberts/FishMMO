#if UNITY_EDITOR
using System.Collections.Generic;
using FishMMO.Shared.Biomes;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>The hues a cave system's glow is drawn in (indices into the glow art's hues), and what of it grows there.</summary>
	public struct CaveGlowPalette
	{
		public int Crystal, CrystalAccent, Fungus, FungusAccent, Ore, OreAccent, GlowOre;
		/// <summary>Whether luminous fungi grow here, and glow-worms hang.</summary>
		public bool Fungi, Worms;
		/// <summary>
		/// Whether the lining's crystals are the biome's own glowing crystals (<see cref="Mineral"/>, <see cref="MineralAccent"/>)
		/// in place of the glow art's: where its template grows a kind that glows (2026-10-16, I6c).
		/// </summary>
		public bool Minerals;
		public BiomeCaveCrystalKind Mineral, MineralAccent;
	}

	/// <summary>
	/// The one place a cave system's glow palette is drawn from its seed (2026-10-20): the cave dressing lays its crystals, fungi
	/// and ore in it, and the run-time cave cover glows in the same colours (CaveCoverSpecies), so the moss under a glowcap
	/// glows in the glowcap's hue.
	/// </summary>
	/// <remarks>
	/// The draws are the dressing's since 2026-10-13, in the same order on the same streams: changing them moves every glowing
	/// piece of every cave on its next dressing.
	/// </remarks>
	public static class CaveGlowPalettes
	{
		/// <summary>The palette of a system of a form and seed in a biome (null: none of the biome's own crystals).</summary>
		public static CaveGlowPalette For(CaveForm form, int seed, BiomeTemplate biome)
		{
			var random = new DeterministicRNG(unchecked(seed * 0x5D3B + 0x7A21));
			int crystals = CaveArt.HueCount(CaveGlowKind.Crystal), fungi = CaveArt.HueCount(CaveGlowKind.Fungus);
			var p = new CaveGlowPalette
			{
				Crystal = random.Range(0, crystals),
				Fungus = random.Range(0, fungi),
				Fungi = true,
				Worms = true,
			};
			p.CrystalAccent = (p.Crystal + random.Range(1, crystals)) % crystals;
			p.FungusAccent = (p.Fungus + random.Range(1, fungi)) % fungi;
			// Iron and copper the common ores, silver less so, gold rare.
			string[] ores = { "Iron", "Copper", "Silver", "Gold" };
			float roll = random.NextFloat();
			int ore = roll < 0.35f ? 0 : roll < 0.7f ? 1 : roll < 0.88f ? 2 : 3;
			p.Ore = HueOf(CaveGlowKind.OreVein, ores[ore], 0);
			p.OreAccent = HueOf(CaveGlowKind.OreVein, ores[(ore + 1 + random.Range(0, 3)) % 4], 1);
			p.GlowOre = HueOf(CaveGlowKind.GlowOreVein, random.NextFloat() < 0.6f ? "Starmetal" : "Emberstone", 0);
			switch (form)
			{
				case CaveForm.IceCave:
					p.Crystal = p.CrystalAccent = HueOf(CaveGlowKind.Crystal, "IceBlue", 0);
					p.Fungi = p.Worms = false;
					break;
				case CaveForm.LavaTube:
					p.Crystal = p.CrystalAccent = HueOf(CaveGlowKind.Crystal, "Amber", 3);
					p.GlowOre = HueOf(CaveGlowKind.GlowOreVein, "Emberstone", 1);
					p.Fungi = p.Worms = false;
					break;
				case CaveForm.Mine:
					p.Worms = false;
					break;
			}
			// A natural cave whose glow was drawn by chance (not an ice cave's ice, a lava tube's amber or a mine) where the biome
			// grows crystals that glow: those line its ways, a lead and an accent by weight from a stream of their own (so the
			// draws above stand as they were), and the giant crystals take the lead's and the accent's hue.
			if (form == CaveForm.Cave || form == CaveForm.Grotto || form == CaveForm.SeaCave)
			{
				IReadOnlyList<BiomeCaveCrystal> grown = BiomeTemplateArt.CaveCrystals(biome);
				if (BiomeCaveCrystals.Total(grown, glowingOnly: true) > 0f)
				{
					var pick = new DeterministicRNG(unchecked(seed * 0x4C1F + 0x2B95));
					BiomeCaveCrystals.TryPick(grown, pick.NextFloat(), out p.Mineral, glowingOnly: true);
					BiomeCaveCrystals.TryPick(grown, pick.NextFloat(), out p.MineralAccent, glowingOnly: true);
					p.Minerals = true;
					p.Crystal = CrystalHueOf(p.Mineral, p.Crystal);
					p.CrystalAccent = CrystalHueOf(p.MineralAccent, p.CrystalAccent);
				}
			}
			return p;
		}

		/// <summary>A kind's hue by name, or the fallback when the art has none of that name.</summary>
		public static int HueOf(CaveGlowKind kind, string name, int fallback)
		{
			int i = CaveArt.HueIndex(kind, name);
			return i >= 0 ? i : fallback;
		}

		/// <summary>The glow art's crystal hue nearest a glowing crystal's light (violet, ice-blue, green or amber by its light's hue bits).</summary>
		public static int CrystalHueOf(BiomeCaveCrystalKind kind, int fallback)
		{
			switch (ProceduralArtCatalogue.CaveMineralOf(kind).LightHue)
			{
				case 0: return HueOf(CaveGlowKind.Crystal, "Violet", fallback);
				case 1: return HueOf(CaveGlowKind.Crystal, "IceBlue", fallback);
				case 2: return HueOf(CaveGlowKind.Crystal, "Green", fallback);
				case 3: return HueOf(CaveGlowKind.Crystal, "Amber", fallback);
				default: return fallback;
			}
		}
	}
}
#endif
