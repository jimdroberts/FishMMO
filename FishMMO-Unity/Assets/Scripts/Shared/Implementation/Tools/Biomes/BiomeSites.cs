using System;
using System.Collections.Generic;
using System.Globalization;
using FishMMO.Shared.NameGeneration;
using UnityEngine;

namespace FishMMO.Shared.Biomes
{
	/// <summary>
	/// One kind of point of interest a biome hosts and how gladly (<see cref="BiomeTemplate.SiteKinds"/>): the planner sites the
	/// kind on this biome's ground at <see cref="Weight"/> against its other ground (1 as gladly as anywhere, 0 never). A kind a
	/// biome does not list is never sited on it.
	/// </summary>
	/// <remarks>
	/// Until 2026-10-16 (phase I5) each kind named its biomes itself (the point-of-interest catalogue's rows: a swamp kind
	/// "only in" the swamps, settlements weighted by how habitable the ground is, nothing built on the sea floor, by the biomes'
	/// names). The biome art authoring writes each biome the weight those rows gave it for every kind, so nothing changes until a
	/// template is edited.
	/// </remarks>
	[Serializable]
	public struct BiomeSiteKind
	{
		[Tooltip("The point-of-interest kind.")]
		public POIType Kind;
		[Tooltip("How gladly the kind is sited on this biome's ground: 1 as anywhere, more to favour it, 0 never.")]
		[Min(0f)] public float Weight;
		/// <remarks>
		/// Decision 39 (2026-10-18, the Crater round): a kind people live in or keep (a village, a shrine, a herb garden) is not
		/// sited on a world whose air is not breathable (thin or none, or of the wrong make-up such as a methane or acid sky:
		/// BiomeAir.IsBreathable, decision 62), so a biome painted on an airless moon places only natural sites and relics there.
		/// Written by the authoring from BiomeSiteAir.NeedsAirByDefault unless the biome's file says otherwise; off (every entry
		/// before 2026-10-18) the kind is sited on every world.
		/// </remarks>
		[Tooltip("Only sited where the world's air is breathable (Earth-like or thick, and not a methane, nitrogen, acid or molten-rock world): settlements, camps, kept shrines, living groves. Off: on every world, natural sites and relics.")]
		public bool NeedsBreathableAir;
		/// <remarks>
		/// Decision 68 (2026-10-25, the Ice Shelf round): a kind that works open water (a fishing camp at a lead, a harbour) is not
		/// sited on a world with no liquid water on its surface (BiomeWorldConditions.HasLiquidWater, the world flag
		/// BiomeWorldRequirement.LiquidWater), such as the raised frozen sea of an ice world. Off by default for every kind, so
		/// nothing changes until a biome's file sets it (BiomeArtSpec.Entry.Water).
		/// </remarks>
		[Tooltip("Only sited where the world has liquid water on its surface. Off: on every world.")]
		public bool NeedsLiquidWater;

		public BiomeSiteKind(POIType kind, float weight, bool needsBreathableAir = false, bool needsLiquidWater = false)
		{
			Kind = kind;
			Weight = weight;
			NeedsBreathableAir = needsBreathableAir;
			NeedsLiquidWater = needsLiquidWater;
		}

		/// <summary>
		/// The entry as the authoring marks and reports it: "Kind×weight" (the weight round-tripped exactly), "×air" after it when it
		/// needs breathable air and "×water" when it needs liquid water (so every mark written before either flag existed still
		/// reads the same).
		/// </summary>
		public override string ToString() => Kind + "×" + Weight.ToString("R", CultureInfo.InvariantCulture) + (NeedsBreathableAir ? "×air" : string.Empty)
			+ (NeedsLiquidWater ? "×water" : string.Empty);
	}

	/// <summary>
	/// The weight a biome gives one variant of a point-of-interest kind's templates (<see cref="BiomeTemplate.SiteVariants"/>):
	/// in this biome the kind's template of that variant ("Village" "Stone", "Camp" "Hide Tents") weighs <see cref="Weight"/>
	/// against the kind's other variants in place of the template's own weight elsewhere. A variant the biome does not list
	/// keeps that elsewhere weight.
	/// </summary>
	/// <remarks>
	/// The variant is the template's variant name (PointOfInterestTemplate.Variant, the part of its asset name after the kind:
	/// "Village - Stone" → "Stone"; the size runs an asset is split into, "(Small)", share it). Until 2026-10-16 each template
	/// named its biomes (stone villages in the arid and highland biomes, longhouses in the cold, yurts on the steppe); the
	/// biome art authoring writes each biome the variants whose weight there differed from the variant's elsewhere weight.
	/// </remarks>
	[Serializable]
	public struct BiomeSiteVariant
	{
		[Tooltip("The point-of-interest kind whose templates the weight is for.")]
		public POIType Kind;
		[Tooltip("The template variant's name: its asset name after the kind (\"Village - Stone\" → Stone), size runs aside.")]
		public string Variant;
		[Tooltip("The variant's weight in this biome against the kind's other variants (in place of its weight elsewhere). 0: never built here.")]
		[Min(0f)] public float Weight;

		public BiomeSiteVariant(POIType kind, string variant, float weight)
		{
			Kind = kind;
			Variant = variant;
			Weight = weight;
		}

		/// <summary>
		/// The weight a list gives a kind's variant (variant by name, ignoring case): the first entry naming both, never below 0;
		/// false when none does.
		/// </summary>
		public static bool TryWeight(IReadOnlyList<BiomeSiteVariant> entries, POIType kind, string variant, out float weight)
		{
			weight = 0f;
			if (entries == null || string.IsNullOrEmpty(variant))
			{
				return false;
			}
			for (int i = 0; i < entries.Count; i++)
			{
				if (entries[i].Kind == kind && string.Equals(entries[i].Variant, variant, StringComparison.OrdinalIgnoreCase))
				{
					weight = Mathf.Max(0f, entries[i].Weight);
					return true;
				}
			}
			return false;
		}

		/// <summary>The entry as the authoring marks and reports it: "Kind/Variant×weight".</summary>
		public override string ToString() => Kind + "/" + (Variant ?? "<none>") + "×" + Weight.ToString("R", CultureInfo.InvariantCulture);
	}

	/// <summary>
	/// How much a biome favours the point-of-interest templates built in one structure style (<see cref="BiomeTemplate.SiteStyles"/>):
	/// every template of the style weighs that many times its variant weight here. A style not listed weighs 1.
	/// </summary>
	/// <remarks>
	/// The coarse knob over <see cref="BiomeSiteVariant"/> (2026-10-16): "this country builds in stone" without naming every
	/// kind's stone variant. The authoring writes none (every style at 1, as before); a biome's round may set its mix.
	/// </remarks>
	[Serializable]
	public struct BiomeSiteStyle
	{
		[Tooltip("The structure style (the template's Style: timber, stone, hide, iron, earth).")]
		public BiomeStructureStyle Style;
		[Tooltip("How many times its variant weight every template of this style weighs in this biome. 0: never built here.")]
		[Min(0f)] public float Weight;

		public BiomeSiteStyle(BiomeStructureStyle style, float weight)
		{
			Style = style;
			Weight = weight;
		}

		/// <summary>
		/// The weight a list gives a style: the first entry naming it, never below 0; 1 when none does, or for a template with
		/// no style of its own (null: its site's race chooses).
		/// </summary>
		public static float WeightOf(IReadOnlyList<BiomeSiteStyle> entries, BiomeStructureStyle? style)
		{
			if (entries == null || style == null)
			{
				return 1f;
			}
			for (int i = 0; i < entries.Count; i++)
			{
				if (entries[i].Style == style.Value)
				{
					return Mathf.Max(0f, entries[i].Weight);
				}
			}
			return 1f;
		}

		/// <summary>"timber" → Timber (ignoring case); null for anything that is not a style name (a race key, empty).</summary>
		public static BiomeStructureStyle? Parse(string style)
		{
			if (string.IsNullOrWhiteSpace(style))
			{
				return null;
			}
			foreach (BiomeStructureStyle value in (BiomeStructureStyle[])Enum.GetValues(typeof(BiomeStructureStyle)))
			{
				if (string.Equals(value.ToString(), style.Trim(), StringComparison.OrdinalIgnoreCase))
				{
					return value;
				}
			}
			return null;
		}

		public override string ToString() => Style + "×" + Weight.ToString("R", CultureInfo.InvariantCulture);
	}

	/// <summary>
	/// A material treatment a structure piece is laid in: the runtime mirror of the structure kit's StructureFinish (the kit is
	/// editor code), member for member and in the same order (a test holds them equal).
	/// </summary>
	public enum BiomeStructureFinish : byte
	{
		/// <summary>Weathered but kept.</summary>
		None,
		/// <summary>Moss in the hollows and on the tops.</summary>
		Mossy,
		/// <summary>Burnt.</summary>
		Charred,
		/// <summary>A dark algal film.</summary>
		Algae,
	}

	/// <summary>
	/// How the weather of a biome finishes the buildings and stones of its points of interest (<see cref="BiomeTemplate.Weathering"/>):
	/// a piece laid in the template's automatic finish takes <see cref="Finish"/> with the chance
	/// <see cref="Chance"/> + <see cref="ChancePerDecay"/> × its decay (0 whole … 1 rubble).
	/// </summary>
	/// <remarks>
	/// <para>
	/// What does not depend on the biome stays the site's: a piece under water is always algae-filmed, half a battlefield
	/// charred, and a template that forces a finish lays it whatever the biome says. Long-abandoned sites (ruins and the dead)
	/// are older than their decay shows: where the biome's finish is moss, they are mossed at least 0.55 + 0.4 × decay
	/// (2026-10-11: ruins looked freshly built).
	/// </para>
	/// <para>
	/// Until 2026-10-16 (phase I5) the finish was read from keywords in the biome's name: algae in wet country (0.3 + 0.6 ×
	/// decay), nothing in dry country, moss elsewhere (1 × decay in damp country, 0.5 × decay in the rest). The biome art
	/// authoring writes each biome what its name gave, so nothing changes until a template is edited.
	/// </para>
	/// </remarks>
	[Serializable]
	public struct BiomeWeathering
	{
		[Tooltip("The finish the weather lays on this biome's buildings and stones: Mossy, Algae, Charred, or None (dry country keeps them clean).")]
		public BiomeStructureFinish Finish;
		[Tooltip("The share of pieces finished when whole (0..1).")]
		[Range(0f, 1f)] public float Chance;
		[Tooltip("The share added for each unit of a piece's decay (0 whole … 1 rubble): the more worn, the more finished.")]
		[Range(0f, 1f)] public float ChancePerDecay;

		public BiomeWeathering(BiomeStructureFinish finish, float chance, float chancePerDecay)
		{
			Finish = finish;
			Chance = chance;
			ChancePerDecay = chancePerDecay;
		}

		/// <summary>The share of pieces of this decay finished (before a ruin's floor for moss).</summary>
		public float ChanceAt(float decay) => Chance + ChancePerDecay * decay;

		/// <summary>Every value, exactly: the authoring's mark.</summary>
		public string Signature() => Finish + "|" + Chance.ToString("R", CultureInfo.InvariantCulture) + "|" + ChancePerDecay.ToString("R", CultureInfo.InvariantCulture);

		public override string ToString() => Finish == BiomeStructureFinish.None
			? "none"
			: $"{Finish} {Chance.ToString("0.##", CultureInfo.InvariantCulture)} + {ChancePerDecay.ToString("0.##", CultureInfo.InvariantCulture)} × decay";
	}

	/// <summary>
	/// One crop a biome's farmland grows (<see cref="BiomeTemplate.FieldCrops"/>): a detail prefab sown edge to edge in a crop
	/// field, drawn against the biome's other crops by <see cref="Weight"/>.
	/// </summary>
	/// <remarks>Until 2026-10-16 every field in every biome grew wheat (Detail_GrassWheat); the authoring writes that crop.</remarks>
	[Serializable]
	public struct BiomeFieldCrop
	{
		[Tooltip("The crop's detail prefab (a grass-blade prefab such as Detail_GrassWheat).")]
		public GameObject Prefab;
		[Tooltip("How often a field here grows this crop against the biome's other crops.")]
		[Min(0f)] public float Weight;
		[Tooltip("How its fields are laid on the ground: Flat (sown edge to edge), Lazy Bed (narrow sown ridges with bare trenches between), Ridge And Furrow (the open field's broad ridges), Paddy (flooded behind a bund), Terraced (benches stepped into a slope), Corn Hills (mounds on a grid), Baulatura (wide cambered beds between ditches), Marcita (a water meadow's beds with a rill along each crown) or Raised Beds (a garden's wattle-edged beds).")]
		public BiomeFieldSurface Surface;
		[Tooltip("How its terraced fields' steps are faced (2026-10-18): Drystone, Earth Bund (a grassed lip) or Grass Bank. A Flat field or a Paddy with a riser is terraced with it on slopes; a Terraced field with none is drystone.")]
		public BiomeTerraceRiser Riser;
		[Tooltip("Sow it in rows this many metres apart along the field (vines, tea, cabbages: 0 edge to edge, as a grain).")]
		[Min(0f)] public float Rows;

		public BiomeFieldCrop(GameObject prefab, float weight, BiomeFieldSurface surface = BiomeFieldSurface.Flat, BiomeTerraceRiser riser = BiomeTerraceRiser.None,
			float rows = 0f)
		{
			Prefab = prefab;
			Weight = weight;
			Surface = surface;
			Riser = riser;
			Rows = rows;
		}

		/// <summary>
		/// The entry as the authoring marks and reports it: "Prefab×weight", and "|Surface" when it is not flat, "|Riser" when it has
		/// one, "|rows N" when it is sown in rows (so the marks written before fields had a surface, a riser or rows still read as the
		/// tool's).
		/// </summary>
		public override string ToString() => (Prefab != null ? Prefab.name : "<none>") + "×" + Weight.ToString("R", CultureInfo.InvariantCulture)
			+ (Surface == BiomeFieldSurface.Flat ? string.Empty : "|" + Surface)
			+ (Riser == BiomeTerraceRiser.None ? string.Empty : "|" + Riser)
			+ (Rows > 0f ? "|rows " + Rows.ToString("R", CultureInfo.InvariantCulture) : string.Empty);
	}

	/// <summary>
	/// What stands in a stronghold's moat in a biome (<see cref="BiomeTemplate.MoatWater"/>): open water, ice, or nothing (a dry
	/// ditch). Append-only (stored by value).
	/// </summary>
	/// <remarks>
	/// Until 2026-10-16 (phase I5b) the moat shaper read it from the biome's climate envelope: a dry ditch where the envelope's
	/// mean humidity is under −0.3, else water, frozen where its mean temperature is under −0.5 (<see cref="BiomeMoatWaters.FromClimate"/>).
	/// The biome art authoring writes each biome what that rule gave it. A moat of molten rock (a volcanic stronghold's lava
	/// ditch) is not offered: the scene's water has no lava lakes yet (a hydrology/generator task for a later phase).
	/// </remarks>
	public enum BiomeMoatWater : byte
	{
		/// <summary>Open water, a little under the lower bank.</summary>
		Wet = 0,
		/// <summary>A dry ditch: no water at all.</summary>
		Dry = 1,
		/// <summary>Water frozen over.</summary>
		Frozen = 2,
	}

	/// <summary>The moat rules that do not depend on a template asset.</summary>
	public static class BiomeMoatWaters
	{
		/// <summary>The mean humidity of a climate envelope under which a moat is a dry ditch.</summary>
		public const float DryBelowHumidity = -0.3f;
		/// <summary>The mean temperature of a climate envelope under which a moat's water is frozen.</summary>
		public const float FrozenBelowTemperature = -0.5f;

		/// <summary>
		/// What the moat shaper decided from a biome's climate envelope until 2026-10-16: Dry where the mean humidity is under
		/// <see cref="DryBelowHumidity"/>, else Frozen where the mean temperature is under <see cref="FrozenBelowTemperature"/>,
		/// else Wet. Each biome's default (the spec) and the fallback for a template never authored with its own.
		/// </summary>
		public static BiomeMoatWater FromClimate(float minHumidity, float maxHumidity, float minTemperature, float maxTemperature)
		{
			if (0.5f * (minHumidity + maxHumidity) < DryBelowHumidity)
			{
				return BiomeMoatWater.Dry;
			}
			return 0.5f * (minTemperature + maxTemperature) < FrozenBelowTemperature ? BiomeMoatWater.Frozen : BiomeMoatWater.Wet;
		}
	}

	/// <summary>
	/// The light a settlement's streets are lit with: the structure kit's free-standing lights (by piece). Append-only.
	/// </summary>
	/// <remarks>
	/// Every member's name is the kit piece that is laid (<see cref="BiomeWayLights.PieceQuery"/>). The first three are the
	/// street lights the kit built before; the cave kit's lamps (3–6) light caves and mines (<see cref="BiomeCaveLight"/>,
	/// 2026-10-16, I6c); the rest (7–12) are the lights added with the per-biome content (I6c), each its own kit piece. How a
	/// light is set (on the ground, on a wall, from a roof) is <see cref="BiomeWayLights.MountOf"/>.
	/// </remarks>
	public enum BiomeWayLightKind : byte
	{
		/// <summary>A post with an iron lantern on a bracket (piece LanternPost).</summary>
		LanternPost = 0,
		/// <summary>A pitch torch or a cresset on a pole (piece TorchPost).</summary>
		TorchPost = 1,
		/// <summary>A fire bowl: an iron brazier on three legs, or a stone bowl on a pedestal (piece Brazier).</summary>
		Brazier = 2,
		/// <summary>A lantern hung from a timber stand or an iron crook driven into the ground (piece LanternStand): a cave route's mark, a camp's lamp.</summary>
		LanternStand = 3,
		/// <summary>A torch in an iron sconce on a wall (piece WallTorch): a mine drift's light.</summary>
		WallTorch = 4,
		/// <summary>An iron lantern on a chain from a roof or a beam (piece HangingLantern); on a wall bracket where the roof is too low.</summary>
		HangingLantern = 5,
		/// <summary>A lantern on an iron bracket from a wall (piece WallLantern).</summary>
		WallLantern = 6,
		/// <summary>A cresset: an iron fire basket of burning pine knots high on a pole (piece Cresset).</summary>
		Cresset = 7,
		/// <summary>An iron lantern hung from a bent shepherd's-crook post of oak (piece CrookLantern).</summary>
		CrookLantern = 8,
		/// <summary>A glowjar: faerie light in a stoppered jar hung from a crooked stake (piece Glowjar).</summary>
		Glowjar = 9,
		/// <summary>A crystal of the biome's own kind in an iron cradle on a stone or timber post (piece CrystalPost).</summary>
		CrystalPost = 10,
		/// <summary>A standing stone carved with runes that glow (piece RuneStone).</summary>
		RuneStone = 11,
		/// <summary>A wrought-iron bracket from a wall with a lantern hung from its scrolled arm (piece HangingBracket).</summary>
		HangingBracket = 12,
	}

	/// <summary>How a light is set where it stands: on the ground, against a wall, or from a roof.</summary>
	public enum BiomeWayLightMount : byte
	{
		/// <summary>Stands on the ground (a post, a stand, a bowl, a stone).</summary>
		Ground = 0,
		/// <summary>Fixed to a wall by its back plate (at z = 0, standing out along −z).</summary>
		Wall = 1,
		/// <summary>Hung from a roof or a beam by its fixing (hanging down −y).</summary>
		Roof = 2,
	}

	/// <summary>
	/// Where in a cave or a mine a light stands (<see cref="BiomeCaveLight"/>). Append-only (stored by value).
	/// </summary>
	public enum BiomeCaveLightPlace : byte
	{
		/// <summary>A natural cave's adventurers' route: beside a bridge's or a crossing's ends and a climb's head (a ground light).</summary>
		Route = 0,
		/// <summary>An explorers' or miners' camp's own lamp, its arm over the camp (a ground light).</summary>
		Camp = 1,
		/// <summary>A mine's drifts, on the rock at head height every <see cref="BiomeCaveLight.Spacing"/> metres or so (a wall light; a ground light stands at the wall's foot).</summary>
		MineDrift = 2,
		/// <summary>A light on a pole where the work was, in each of a mine's stopes (a ground light).</summary>
		MineStope = 3,
		/// <summary>
		/// A mine's lamps where its ways meet, in its stopes and at its shaft heads: hung from the roof where it clears a tall
		/// person's head, else on the nearer wall (a roof light); a wall light only on the wall; a ground light at the wall's foot.
		/// </summary>
		MineLamp = 4,
	}

	/// <summary>
	/// How a biome lights one place in its caves and mines (<see cref="BiomeTemplate.CaveLights"/>, 2026-10-16, I6c): the light
	/// laid there and, along a mine's drifts, the metres between lights. A place a biome does not list is not lit.
	/// </summary>
	/// <remarks>
	/// Until 2026-10-16 the cave dressing decided it alone, the same in every biome (<see cref="BiomeCaveLights.Default"/>):
	/// lanterns on stands marking a cave's route and lighting its camps, torches in sconces along a mine's drifts every 14–20 m,
	/// a torch on a pole in each stope, and hung lanterns (on a wall bracket where the roof is low) where a mine's ways meet.
	/// The biome art authoring writes each biome that list.
	/// </remarks>
	[Serializable]
	public struct BiomeCaveLight
	{
		[Tooltip("Where in a cave or mine the light stands: a cave's route marks or its camps, a mine's drifts, stopes or lamps.")]
		public BiomeCaveLightPlace Place;
		[Tooltip("The light laid there. A ground light (lantern stand, torch post, cresset, crystal post…) stands on the floor; a wall light (wall torch, wall lantern, hanging bracket) goes on the rock; a hanging lantern hangs from the roof.")]
		public BiomeWayLightKind Light;
		[Tooltip("Along a mine's drifts: the mean metres between lights (each gap 14/17 to 20/17 of it). Unused elsewhere.")]
		[Min(0f)] public float Spacing;

		public BiomeCaveLight(BiomeCaveLightPlace place, BiomeWayLightKind light, float spacing = 0f)
		{
			Place = place;
			Light = light;
			Spacing = spacing;
		}

		/// <summary>The structure kit query for the light's piece ("=LanternStand").</summary>
		public string PieceQuery => BiomeWayLights.PieceQuery(Light);

		/// <summary>The entry as the authoring marks and reports it: "Place=Light@spacing".</summary>
		public override string ToString() => Place + "=" + Light + "@" + Spacing.ToString("R", CultureInfo.InvariantCulture);
	}

	/// <summary>The cave-light rules that do not depend on a template asset.</summary>
	public static class BiomeCaveLights
	{
		/// <summary>The mean metres between a mine drift's torches until 2026-10-16 (each gap 14–20 m).</summary>
		public const float DriftSpacing = 17f;

		/// <summary>The least mean spacing along a drift, metres (a closer entry is read as this).</summary>
		public const float MinDriftSpacing = 4f;

		/// <summary>
		/// What every biome lit its caves and mines with until 2026-10-16 (the cave dressing's own rule): a lantern on a stand
		/// on a cave's route and in its camps, torches in sconces along a mine's drifts every 14–20 m, a torch post in each
		/// stope, hung lanterns where the mine's ways meet. A fresh list each call, in place order.
		/// </summary>
		public static List<BiomeCaveLight> Default() => new List<BiomeCaveLight>
		{
			new BiomeCaveLight(BiomeCaveLightPlace.Route, BiomeWayLightKind.LanternStand),
			new BiomeCaveLight(BiomeCaveLightPlace.Camp, BiomeWayLightKind.LanternStand),
			new BiomeCaveLight(BiomeCaveLightPlace.MineDrift, BiomeWayLightKind.WallTorch, DriftSpacing),
			new BiomeCaveLight(BiomeCaveLightPlace.MineStope, BiomeWayLightKind.TorchPost),
			new BiomeCaveLight(BiomeCaveLightPlace.MineLamp, BiomeWayLightKind.HangingLantern),
		};

		/// <summary>The light a list gives a place: the first entry naming it; false when none does (not lit).</summary>
		public static bool TryFor(IReadOnlyList<BiomeCaveLight> lights, BiomeCaveLightPlace place, out BiomeCaveLight light)
		{
			light = default;
			if (lights == null)
			{
				return false;
			}
			for (int i = 0; i < lights.Count; i++)
			{
				if (lights[i].Place == place)
				{
					light = lights[i];
					if (place == BiomeCaveLightPlace.MineDrift)
					{
						light.Spacing = light.Spacing > 0f ? Mathf.Max(MinDriftSpacing, light.Spacing) : DriftSpacing;
					}
					return true;
				}
			}
			return false;
		}

		/// <summary>
		/// The gap to the next light along a drift, for a mean <paramref name="spacing"/>: between 14/17 and 20/17 of it, so the
		/// old 17 m mean gives exactly the old 14–20 m (the products and quotients are exact in floats for it).
		/// </summary>
		public static void DriftGap(float spacing, out float min, out float max)
		{
			min = spacing * 14f / 17f;
			max = spacing * 20f / 17f;
		}
	}

	/// <summary>
	/// How a biome lights one kind of settlement's streets (<see cref="BiomeTemplate.WayLights"/>): <see cref="Light"/> either side
	/// in turn, every <see cref="Spacing"/> metres along each street. A kind a biome does not list is not lit.
	/// </summary>
	/// <remarks>
	/// Until 2026-10-16 (phase I5b) the props decided it by kind alone: a town's, city's, capital's and port's iron lanterns every
	/// 13 m, a village's torches on poles every 20 m, nothing elsewhere (<see cref="BiomeWayLights.Default"/>). The biome art
	/// authoring writes each biome that list. Lights stand only along the streets a settlement's layout lays.
	/// </remarks>
	[Serializable]
	public struct BiomeWayLight
	{
		[Tooltip("The settlement kind whose streets are lit (Village, Town, City, Capital, Port…).")]
		public POIType Kind;
		[Tooltip("The light along its streets.")]
		public BiomeWayLightKind Light;
		[Tooltip("Metres between lights along a street (they alternate sides).")]
		[Min(4f)] public float Spacing;

		public BiomeWayLight(POIType kind, BiomeWayLightKind light, float spacing)
		{
			Kind = kind;
			Light = light;
			Spacing = spacing;
		}

		/// <summary>The structure kit query for the light's piece ("=LanternPost").</summary>
		public string PieceQuery => BiomeWayLights.PieceQuery(Light);

		/// <summary>The entry as the authoring marks and reports it: "Kind=Light@spacing".</summary>
		public override string ToString() => Kind + "=" + Light + "@" + Spacing.ToString("R", CultureInfo.InvariantCulture);
	}

	/// <summary>The way-light rules that do not depend on a template asset.</summary>
	public static class BiomeWayLights
	{
		/// <summary>Metres between a town's iron lanterns, and between a village's torches (the props' spacing until 2026-10-16).</summary>
		public const float LanternSpacing = 13f, TorchSpacing = 20f;

		/// <summary>The least spacing the props lay lights at, metres (a closer list entry is read as this).</summary>
		public const float MinSpacing = 4f;

		/// <summary>
		/// What every biome lit its streets with until 2026-10-16 (the props' rule by kind): iron lanterns every
		/// <see cref="LanternSpacing"/> in towns, cities, capitals and ports, torches every <see cref="TorchSpacing"/> in villages.
		/// A fresh list each call, in kind order.
		/// </summary>
		public static List<BiomeWayLight> Default() => new List<BiomeWayLight>
		{
			new BiomeWayLight(POIType.Village, BiomeWayLightKind.TorchPost, TorchSpacing),
			new BiomeWayLight(POIType.Town, BiomeWayLightKind.LanternPost, LanternSpacing),
			new BiomeWayLight(POIType.City, BiomeWayLightKind.LanternPost, LanternSpacing),
			new BiomeWayLight(POIType.Capital, BiomeWayLightKind.LanternPost, LanternSpacing),
			new BiomeWayLight(POIType.Port, BiomeWayLightKind.LanternPost, LanternSpacing),
		};

		/// <summary>The light a list gives a kind's streets: the first entry naming it; false when none does (not lit).</summary>
		public static bool TryFor(IReadOnlyList<BiomeWayLight> lights, POIType kind, out BiomeWayLight light)
		{
			light = default;
			if (lights == null)
			{
				return false;
			}
			for (int i = 0; i < lights.Count; i++)
			{
				if (lights[i].Kind == kind)
				{
					light = lights[i];
					light.Spacing = Mathf.Max(MinSpacing, light.Spacing);
					return true;
				}
			}
			return false;
		}

		/// <summary>The structure kit query that names a light's piece ("=LanternPost", "=TorchPost", "=Brazier").</summary>
		public static string PieceQuery(BiomeWayLightKind light) => "=" + light;

		/// <summary>The kit piece id of a light (its name: "LanternPost", "Cresset"…).</summary>
		public static string PieceId(BiomeWayLightKind light) => light.ToString();

		/// <summary>How a light is set: the wall pieces on a wall, the hanging lantern from a roof, everything else on the ground.</summary>
		public static BiomeWayLightMount MountOf(BiomeWayLightKind light)
		{
			switch (light)
			{
				case BiomeWayLightKind.WallTorch:
				case BiomeWayLightKind.WallLantern:
				case BiomeWayLightKind.HangingBracket:
					return BiomeWayLightMount.Wall;
				case BiomeWayLightKind.HangingLantern:
					return BiomeWayLightMount.Roof;
				default:
					return BiomeWayLightMount.Ground;
			}
		}

		/// <summary>
		/// The light that stands on the ground in a light's stead where it cannot be hung (2026-10-18, I6g: a street with no wall
		/// near a light's place): a wall torch's torch on a pole, a wall lantern's lantern post, a hanging bracket's lantern on a
		/// crook post, the hanging lantern's lantern post; a light that stands on the ground is its own.
		/// </summary>
		public static BiomeWayLightKind PostFor(BiomeWayLightKind light)
		{
			switch (light)
			{
				case BiomeWayLightKind.WallTorch: return BiomeWayLightKind.TorchPost;
				case BiomeWayLightKind.WallLantern: return BiomeWayLightKind.LanternPost;
				case BiomeWayLightKind.HangingBracket: return BiomeWayLightKind.CrookLantern;
				case BiomeWayLightKind.HangingLantern: return BiomeWayLightKind.LanternPost;
				default: return light;
			}
		}
	}
}
