using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace FishMMO.Shared.Biomes
{
	/// <summary>
	/// How a cave biome's caves of one form stand in the water table (<see cref="BiomeTemplate.CaveFloods"/>, 2026-10-25, phase
	/// I2n-c "drowned galleries", decision 86 (a)): a natural cave whose mouth opens on wet ground is DROWNED in that share of
	/// systems, its galleries laid level at the water table with their floors up to <see cref="Depth"/> under the water, as a
	/// grotto's floors have always lain under its pool. A grotto is awash by its form; its entry sets only how deep.
	/// </summary>
	[Serializable]
	public struct BiomeCaveFlood
	{
		[Tooltip("The cave's form (a natural cave or a grotto; the other forms are never drowned).")]
		public BiomeCaveForm Form;
		[Tooltip("How far under the water the drowned floors may lie, metres (a grotto's has always been 1.5). Kept at most so a tall person's head clears the roof by the air gap.")]
		[Min(0f)] public float Depth;
		[Tooltip("The share of the form's caves on wet ground that are drowned, 0..1 (a grotto is awash whatever it is).")]
		[Range(0f, 1f)] public float Share;
		[Tooltip("How thickly marsh-gas beads rise from the drowned muck, 0 none (a client presentation on the shared clock).")]
		[Min(0f)] public float Bubbles;
		[Tooltip("How many leeches swim in the cave's still water, 0 none (a client presentation: they swim toward whoever wades in, as each viewer sees it; nothing on the wire).")]
		[Min(0f)] public float Leeches;

		public BiomeCaveFlood(BiomeCaveForm form, float depth, float share, float bubbles = 0f, float leeches = 0f)
		{
			Form = form;
			Depth = depth;
			Share = share;
			Bubbles = bubbles;
			Leeches = leeches;
		}

		/// <summary>
		/// The entry as the authoring marks and reports it: "Form=depth|share", the bubbles after a third '|' when either they or the
		/// leeches are set, the leeches after a fourth only when set (an entry authored before either marks as it did).
		/// </summary>
		public override string ToString()
			=> $"{Form}={F(Depth)}|{F(Share)}" + (Bubbles > 0f || Leeches > 0f ? "|" + F(Bubbles) : string.Empty) + (Leeches > 0f ? "|" + F(Leeches) : string.Empty);

		private static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);
	}

	/// <summary>The roots a cave's roof lets down (<see cref="BiomeCaveHanging.Roots"/>).</summary>
	/// <remarks>Append only; the ordinal is stored in template assets.</remarks>
	public enum BiomeCaveRootArt : byte
	{
		/// <summary>The hanging roots and tap roots every cave took until 2026-10-25: down to a dry floor, or a tangle over a way.</summary>
		Hanging = 0,
		/// <summary>
		/// Water roots (decision 86 (d)): curtains of dark alder and willow roots with red root-hair tassels trailing in the water,
		/// hung over the cave's water first, and tap roots ending at the water's surface where the floor is flooded.
		/// </summary>
		WaterRoots = 1,
	}

	/// <summary>
	/// What hangs from a cave biome's caves' roofs, by form (<see cref="BiomeTemplate.CaveHangings"/>, 2026-10-25, phase I2n-c,
	/// decision 86 (d)): the cave dressing's own per-form lookups until then (CaveHanging.For, CaveFungusPlan.RootsNearMouth),
	/// authored as they were, with the root art and the root windows (decision 86 (f)) on top.
	/// </summary>
	[Serializable]
	public struct BiomeCaveHanging
	{
		[Tooltip("The cave's form.")]
		public BiomeCaveForm Form;
		[Tooltip("How thickly glow-worm silk lines the damp roof joints in the dark (1 a limestone cave's, 0 none).")]
		[Min(0f)] public float Silk;
		[Tooltip("How readily tree roots come down through a thin roof in the dark where trees grow over it (1 a limestone cave's, 0 none).")]
		[Min(0f)] public float DeepRoots;
		[Tooltip("How readily tree roots come down through a thin roof near a mouth, before the land over it is weighed (1 a limestone cave's).")]
		[Min(0f)] public float RootsNearMouth;
		[Tooltip("How readily guano heaps under a roost (0 none).")]
		[Min(0f)] public float Guano;
		[Tooltip("The roots' art: the dry caves' hanging and tap roots, or water roots trailing their tassels in the cave's water.")]
		public BiomeCaveRootArt Roots;
		[Tooltip("The most root windows a system opens (small openings up through a thin roof under a wooded, wet host, screened by its trees' roots), 0 none.")]
		[Range(0, 3)] public int RootWindows;

		public BiomeCaveHanging(BiomeCaveForm form, float silk, float deepRoots, float rootsNearMouth, float guano,
			BiomeCaveRootArt roots = BiomeCaveRootArt.Hanging, int rootWindows = 0)
		{
			Form = form;
			Silk = silk;
			DeepRoots = deepRoots;
			RootsNearMouth = rootsNearMouth;
			Guano = guano;
			Roots = roots;
			RootWindows = rootWindows;
		}

		/// <summary>
		/// The entry as the authoring marks and reports it: "Form=silk|deep roots|roots near mouth|guano", then the root art and the
		/// windows only when set (the marks of every entry authored as the dressing chose are these four numbers).
		/// </summary>
		public override string ToString()
		{
			string s = $"{Form}={F(Silk)}|{F(DeepRoots)}|{F(RootsNearMouth)}|{F(Guano)}";
			if (Roots != BiomeCaveRootArt.Hanging || RootWindows > 0)
			{
				s += $"|{Roots}|{RootWindows}";
			}
			return s;
		}

		private static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);
	}

	/// <summary>
	/// The tea ring a cave biome's water leaves on its caves' walls, by form (<see cref="BiomeTemplate.CaveStains"/>, 2026-10-25,
	/// phase I2n-c, decision 86 (g)): from the cave's water up to the highest flood, a band of the water's stain with a sharp top
	/// edge, drawn over the HOST's rock by the cave rock shader (FishCaveRock.hlsl). None until a template names one.
	/// </summary>
	[Serializable]
	public struct BiomeCaveStain
	{
		[Tooltip("The cave's form.")]
		public BiomeCaveForm Form;
		[Tooltip("The stain's colour; its alpha how strongly the band takes it (0 none).")]
		public Color Colour;
		[Tooltip("How far over the water the highest flood stood, metres: the band's top.")]
		[Min(0f)] public float Rise;
		[Tooltip("How sharp the band's top edge is: the metres it fades over.")]
		[Min(0.005f)] public float Edge;

		public BiomeCaveStain(BiomeCaveForm form, Color colour, float rise, float edge)
		{
			Form = form;
			Colour = colour;
			Rise = rise;
			Edge = edge;
		}

		/// <summary>The entry as the authoring marks and reports it: "Form=#rrggbbaa|rise|edge".</summary>
		public override string ToString()
			=> $"{Form}=#{ColorUtility.ToHtmlStringRGBA(Colour)}|{F(Rise)}|{F(Edge)}";

		private static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);
	}

	/// <summary>Where a cave biome's glow-worm colonies hang (<see cref="BiomeCaveGlow.WormMount"/>, decision 86 (e)).</summary>
	/// <remarks>Append only; the ordinal is stored in template assets.</remarks>
	public enum BiomeCaveWormMount : byte
	{
		/// <summary>From the high roofs of the halls and over the chasms (every cave's until 2026-10-25).</summary>
		Roof = 0,
		/// <summary>Threaded along the roots hanging through the roof, with little roof silk of their own.</summary>
		Roots = 1,
		/// <summary>On drowned and fallen wood: the logs the dressing lays in the cave's water and on its floors, with little roof silk of their own.</summary>
		Wood = 2,
	}

	/// <summary>The drowned galleries' rules that do not depend on a template asset (pure, 2026-10-25, phase I2n-c).</summary>
	public static class BiomeCaveWaters
	{
		// ── The water table (a) ───────────────────────────────────────

		/// <summary>How far under a drowned cave's mouth floor its water stands, metres: the threshold stays dry, the gallery wades.</summary>
		public const float MouthDrop = 0.3f;
		/// <summary>The least air a drowned gallery keeps between its water and its roof, metres (decision 86).</summary>
		public const float AirGap = 0.6f;
		/// <summary>The headroom a cave's narrowest bore keeps over its flat floor, metres (CaveSystem.MinBore's 2.3 m).</summary>
		public const float Headroom = 2.3f;
		/// <summary>The deepest a drowned floor may lie, metres: the headroom less the air gap.</summary>
		public const float MaxDepth = Headroom - AirGap;
		/// <summary>How far before a mouth water makes its host's ground wet, metres (decision 86: host water within 60 m).</summary>
		public const float WetReach = 60f;

		/// <summary>The entry a list gives a form (its first), or false.</summary>
		public static bool TryFlood(IReadOnlyList<BiomeCaveFlood> list, BiomeCaveForm form, out BiomeCaveFlood flood)
		{
			flood = default;
			if (list != null)
			{
				for (int i = 0; i < list.Count; i++)
				{
					if (list[i].Form == form)
					{
						flood = list[i];
						return true;
					}
				}
			}
			return false;
		}

		/// <summary>True for the forms the water table drowns (a natural cave) or deepens (a grotto); never a sea cave, ice, lava or a mine.</summary>
		public static bool Floods(BiomeCaveForm form) => form == BiomeCaveForm.Cave || form == BiomeCaveForm.Grotto;

		/// <summary>An entry's depth as the plan takes it: within 0 and <see cref="MaxDepth"/>.</summary>
		public static float DepthOf(in BiomeCaveFlood flood) => Mathf.Clamp(flood.Depth, 0f, MaxDepth);

		/// <summary>
		/// Whether a natural cave is drowned: its host's ground before the mouth is wet (<paramref name="wet"/>) and its seed's own
		/// roll (<paramref name="roll"/>, 0..1) falls in the entry's share. A grotto never asks (awash by its form).
		/// </summary>
		public static bool Drowns(in BiomeCaveFlood flood, bool wet, float roll) => wet && flood.Depth > 0f && roll < Mathf.Clamp01(flood.Share);

		/// <summary>
		/// Whether a host's ground before a cave is wet (decision 86): water within <see cref="WetReach"/> of the mouth, or a land whose
		/// habitat is a wetland's.
		/// </summary>
		public static bool WetHost(bool waterNear, BiomeHabitat hostHabitat) => waterNear || (hostHabitat & BiomeHabitat.Wetland) != 0;

		/// <summary>A drowned cave's water level from its mouth's floor, world y.</summary>
		public static float LevelUnder(float mouthFloor) => mouthFloor - MouthDrop;

		// ── The interior's water look (b) ─────────────────────────────

		/// <summary>
		/// The biome a cave's water takes its look from, by id (SceneHydrology's LookBiome; 0: by its shores, as before): the linked
		/// interior's, where it has a look of its own (not the inland water's own, <paramref name="materialLook"/>). Every cave biome's
		/// look is the material's until its round gives it one, so no cave's water changes until then.
		/// </summary>
		public static int LookBiome(bool linked, int interiorId, BiomeWaterLook interiorLook, BiomeWaterLook materialLook)
		{
			if (!linked || interiorId == 0 || !interiorLook.Defined || interiorLook.Approximately(materialLook))
			{
				return 0;
			}
			return interiorId;
		}

		// ── What hangs (d) ────────────────────────────────────────────

		/// <summary>The entry a list gives a form (its first), or false.</summary>
		public static bool TryHanging(IReadOnlyList<BiomeCaveHanging> list, BiomeCaveForm form, out BiomeCaveHanging hanging)
		{
			hanging = default;
			if (list != null)
			{
				for (int i = 0; i < list.Count; i++)
				{
					if (list[i].Form == form)
					{
						hanging = list[i];
						return true;
					}
				}
			}
			return false;
		}

		/// <summary>The trees per 100 m² a host's land must hold, as CaveHanging.Wooded's share, for a root window to open under it.</summary>
		public const float RootWindowWoods = 0.25f;
		/// <summary>The most rock and soil over a root window's vault, metres (decision 86 (f): a roof of 4 m or less).</summary>
		public const float RootWindowCover = 4f;
		/// <summary>A root window's opening radius, metres, at least and at most: 2–4.8 m across, under a skylight's least (2.5 m radius).</summary>
		public const float RootWindowMinRadius = 1f, RootWindowMaxRadius = 2.4f;
		/// <summary>The least headroom where a root window opens, metres: a gallery's roof, not a hall's vault.</summary>
		public const float RootWindowMinVault = 2.6f;

		/// <summary>How many root windows a system tries for (1 to the entry's most, by its seed's roll), 0 for none.</summary>
		public static int RootWindowsWanted(int most, float roll) => most <= 0 ? 0 : 1 + Mathf.Min(most - 1, Mathf.FloorToInt(Mathf.Clamp01(roll) * most));

		/// <summary>Whether a host's land opens root windows over its caves: wooded enough and wet (<see cref="WetHost"/>).</summary>
		public static bool OpensRootWindows(float wooded, bool wet) => wet && wooded >= RootWindowWoods;

		/// <summary>True for a skylight radius that is a root window's (under a skylight's least).</summary>
		public static bool IsRootWindowRadius(float radius, float skylightMinRadius) => radius < skylightMinRadius;

		// ── The stain (g) ─────────────────────────────────────────────

		/// <summary>The entry a list gives a form (its first, of any strength), or false.</summary>
		public static bool TryStain(IReadOnlyList<BiomeCaveStain> list, BiomeCaveForm form, out BiomeCaveStain stain)
		{
			stain = default;
			if (list != null)
			{
				for (int i = 0; i < list.Count; i++)
				{
					if (list[i].Form == form)
					{
						stain = list[i];
						return stain.Colour.a > 0f && stain.Rise > 0f;
					}
				}
			}
			return false;
		}

		/// <summary>
		/// The ring's strength at a height <paramref name="above"/> metres over the cave's water (the shader's FishCaveFloodRing, kept
		/// in step): full from under the water up to the band's top, fading over <paramref name="edge"/> there; none above it.
		/// </summary>
		public static float RingAt(float above, float rise, float edge)
		{
			float e = Mathf.Max(0.005f, edge);
			float t = Mathf.Clamp01((rise - above) / e);
			return t * t * (3f - 2f * t);
		}

		// ── Glow-worms (e) ────────────────────────────────────────────

		/// <summary>The share of its own roof colonies a cave keeps where its glow-worms hang on roots or wood (decision 86: "little roof silk").</summary>
		public const float RootMountRoofShare = 0.25f;

		/// <summary>How a mount weighs a cave's roof colonies' chance (1 as it was, the roof's).</summary>
		public static float RoofShare(BiomeCaveWormMount mount) => mount == BiomeCaveWormMount.Roof ? 1f : RootMountRoofShare;

		/// <summary>The most logs a system's wood-mounted glow-worms are given (drowned in its water first, then fallen on its floors).</summary>
		public const int MostLogs = 12;

		/// <summary>How many logs a system tries for (decision 86 (e) WormMount Wood): about one a 40 m of its ways, at the biome's glow-worm weight, capped.</summary>
		public static int LogsWanted(float wayMetres, float worms) => worms <= 0f || !(wayMetres > 0f) ? 0 : Mathf.Clamp(Mathf.RoundToInt(wayMetres / 40f * Mathf.Min(2f, worms)), 1, MostLogs);

		// ── The root windows' peat lip (f) ───────────────────────────

		/// <summary>How far past a root window's throat the peat it rotted through shows on the rock, metres (its lip and the vault round it).</summary>
		public const float PeatLipReach = 1.4f;
		/// <summary>How far under the vault the peat lip hangs, metres.</summary>
		public const float PeatLipDrop = 0.8f;
		/// <summary>How dark with peat the lip is at most (the habitat's Organic, 0..1).</summary>
		public const float PeatLipOrganic = 0.9f;

		/// <summary>
		/// How much of a root window's peat lip a point shows, 0..1 (decision 86 (f): "a peat lip"): full on the throat's rock between the
		/// vault (<paramref name="vault"/>, less <see cref="PeatLipDrop"/>) and the opening (<paramref name="top"/>) out to its radius,
		/// fading over <see cref="PeatLipReach"/> beyond (<paramref name="fromAxis"/> the point's distance from the throat's axis).
		/// </summary>
		public static float PeatLip(float fromAxis, float y, float radius, float vault, float top)
		{
			if (!(y >= vault - PeatLipDrop) || y > top + 0.5f)
			{
				return 0f;
			}
			float out_ = Mathf.Clamp01((fromAxis - radius) / PeatLipReach);
			float band = Mathf.Clamp01((y - (vault - PeatLipDrop)) / 0.3f);
			return (1f - out_ * out_ * (3f - 2f * out_)) * band;
		}
	}
}
