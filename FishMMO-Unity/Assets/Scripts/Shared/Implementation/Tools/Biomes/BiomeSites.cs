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

		/// <summary>
		/// A template's weight in a biome from its variant weight and its style's (pure): the two multiplied, except that a variant the
		/// biome names by weight (<paramref name="named"/>) keeps its weight under a style weighed 0 (2026-10-27). A style at 0 says the
		/// country does not build that way; a named variant is the biome's own word for that one template, which the style's blanket
		/// 0 must not silence (Alpine Meadow names MonsterDen "Bones" at 0.4 and weighs Earth 0, and every bone den is in earth).
		/// </summary>
		public static float Apply(float variantWeight, bool named, float styleWeight)
			=> named && styleWeight <= 0f ? Mathf.Max(0f, variantWeight) : Mathf.Max(0f, variantWeight) * Mathf.Max(0f, styleWeight);

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
		[Tooltip("A coat the GPU draws over the biome's buildings where they stand (2026-10-25: lichen, rime, salt bloom, soot, a tide-mark…; BiomeStructureCoats). None draws them as before.")]
		public BiomeStructureCoat Coat;
		[Tooltip("The share of pieces the coat lies on (0..1), each piece by its position.")]
		[Range(0f, 1f)] public float CoatCover;
		[Tooltip("How far the coat lies where it lies (0 none … 1 the recipe's full coat).")]
		[Range(0f, 1f)] public float CoatStrength;
		[Tooltip("The biome's own tint of the coat's main colour (alpha: how far it is pulled to it; 0 the recipe's colour).")]
		public Color CoatTint;

		public BiomeWeathering(BiomeStructureFinish finish, float chance, float chancePerDecay)
		{
			Finish = finish;
			Chance = chance;
			ChancePerDecay = chancePerDecay;
			Coat = BiomeStructureCoat.None;
			CoatCover = 0f;
			CoatStrength = 0f;
			CoatTint = default;
		}

		/// <summary>True when the weathering lays a coat that draws (a coat, some cover, some strength).</summary>
		public bool Coats => Coat != BiomeStructureCoat.None && CoatCover > 0f && CoatStrength > 0f;

		/// <summary>A copy that also lays <paramref name="coat"/> on <paramref name="cover"/> of the pieces at <paramref name="strength"/> (tint alpha 0: the recipe's colour).</summary>
		public BiomeWeathering WithCoat(BiomeStructureCoat coat, float cover, float strength = 1f, Color tint = default)
		{
			BiomeWeathering copy = this;
			copy.Coat = coat;
			copy.CoatCover = Mathf.Clamp01(cover);
			copy.CoatStrength = Mathf.Clamp01(strength);
			copy.CoatTint = tint;
			return copy;
		}

		/// <summary>The share of pieces of this decay finished (before a ruin's floor for moss).</summary>
		public float ChanceAt(float decay) => Chance + ChancePerDecay * decay;

		/// <summary>Every value, exactly: the authoring's mark.</summary>
		/// <remarks>The coat is appended only when there is one ("|coat Lichen|cover|strength|tint rgba"), so every mark written before coats reads the same.</remarks>
		public string Signature() => Finish + "|" + Chance.ToString("R", CultureInfo.InvariantCulture) + "|" + ChancePerDecay.ToString("R", CultureInfo.InvariantCulture)
			+ (Coat == BiomeStructureCoat.None ? string.Empty : "|coat " + Coat + "|" + CoatCover.ToString("R", CultureInfo.InvariantCulture) + "|"
				+ CoatStrength.ToString("R", CultureInfo.InvariantCulture) + "|" + CoatTint.r.ToString("R", CultureInfo.InvariantCulture) + ","
				+ CoatTint.g.ToString("R", CultureInfo.InvariantCulture) + "," + CoatTint.b.ToString("R", CultureInfo.InvariantCulture) + ","
				+ CoatTint.a.ToString("R", CultureInfo.InvariantCulture));

		public override string ToString() => (Finish == BiomeStructureFinish.None
			? "none"
			: $"{Finish} {Chance.ToString("0.##", CultureInfo.InvariantCulture)} + {ChancePerDecay.ToString("0.##", CultureInfo.InvariantCulture)} × decay")
			+ (Coat == BiomeStructureCoat.None ? string.Empty
				: $", coat {Coat} on {CoatCover.ToString("0.##", CultureInfo.InvariantCulture)} at {CoatStrength.ToString("0.##", CultureInfo.InvariantCulture)}");
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

	/// <summary>The field-crop rules that do not depend on a template asset (pure).</summary>
	public static class BiomeFieldCrops
	{
		/// <summary>
		/// Whether a biome's fields sow nothing at all (decision 25, 2026-10-25), so its settlements lay no fields: only a list
		/// authored so (<paramref name="authored"/>, the template's mark: a list never authored grows wheat) in which no entry
		/// weighs anything, with no orchard planted instead. An entry with a weight but no prefab (its art missing) still counts
		/// as sown: the fields are laid and left unsown, as before, rather than vanishing because a prefab was lost.
		/// </summary>
		public static bool SowsNothing(bool authored, IReadOnlyList<BiomeFieldCrop> crops, int orchards)
		{
			if (!authored || orchards > 0)
			{
				return false;
			}
			if (crops != null)
			{
				for (int i = 0; i < crops.Count; i++)
				{
					if (crops[i].Weight > 0f)
					{
						return false;
					}
				}
			}
			return true;
		}
	}

	/// <summary>
	/// What stands in a stronghold's moat in a biome (<see cref="BiomeTemplate.MoatWater"/>): open water, ice, or nothing (a dry
	/// ditch). Append-only (stored by value).
	/// </summary>
	/// <remarks>
	/// Until 2026-10-16 (phase I5b) the moat shaper read it from the biome's climate envelope: a dry ditch where the envelope's
	/// mean humidity is under −0.3, else water, frozen where its mean temperature is under −0.5 (<see cref="BiomeMoatWaters.FromClimate"/>).
	/// The biome art authoring writes each biome what that rule gave it. A moat of molten rock (a volcanic stronghold's lava
	/// ditch, <see cref="Lava"/>, 2026-10-25, phase I2r) is dug as a dry ditch and drawn as a lava ring along its floor by the client
	/// (BiomeLavaChannels.MoatLines): no water, no hydrology. Append-only (serialised on the templates).
	/// </remarks>
	public enum BiomeMoatWater : byte
	{
		/// <summary>Open water, a little under the lower bank.</summary>
		Wet = 0,
		/// <summary>A dry ditch: no water at all.</summary>
		Dry = 1,
		/// <summary>Water frozen over.</summary>
		Frozen = 2,
		/// <summary>Molten lava along the ditch's floor (decision 95, Volcanic Temple): dug dry, drawn molten (2026-10-25, phase I2r).</summary>
		Lava = 3,
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
	/// 2026-10-16, I6c); 7–12 are the lights added with the per-biome content (I6c), each its own kit piece; 13 on are the
	/// way and cave lights the biome suite's drafts name (2026-10-26). How a light is set (on the ground, on a wall, from a
	/// roof) is <see cref="BiomeWayLights.MountOf"/>, its placement class <see cref="BiomeWayLights.ClassOf"/>, what it gives
	/// off <see cref="BiomeWayLights.EmitterOf"/>.
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
		// The way and cave lights the biome suite's drafts name (2026-10-26, NEW-LIGHTS): each its own kit piece
		// (StructurePieces.WayLights.cs), its placement class, emitter and standing twin in BiomeWayLights.
		/// <summary>A beacon on a tower top: an iron fire basket of logs on a stand, dark by day and kindled at dusk (a Pyre).</summary>
		Beacon = 13,
		/// <summary>A signal pyre: a log crib on a slab-stone hearth, kindled at dusk (a Pyre).</summary>
		SignalPyre = 14,
		/// <summary>A wart fire: a hill beacon's turf-walled fire pit on a cairn, kindled at dusk (a Pyre).</summary>
		WartFire = 15,
		/// <summary>A blubber pyre: an iron pot of rendered fat over a stone ring, kindled at dusk (a Pyre).</summary>
		BlubberPyre = 16,
		/// <summary>A beacon of five fire pits in a row on a rammed platform, kindled at dusk (a Pyre).</summary>
		FivePits = 17,
		/// <summary>A horn lantern with amber panes on a driftwood post (a Lamp).</summary>
		AmberLamp = 18,
		/// <summary>A squat lantern of soft tuff pierced with stars and crosses, on a tuff post (a Lamp).</summary>
		CarvedLantern = 19,
		/// <summary>A stone lantern: base, post, a paper-screened firebox and a curled cap (a Lamp).</summary>
		StoneLantern = 20,
		/// <summary>A round paper lantern on a bamboo bracket from a wall (a Lamp; PaperLanternPost where no wall is near).</summary>
		PaperLantern = 21,
		/// <summary>A round paper lantern hung from a bamboo post's arm (a Lamp).</summary>
		PaperLanternPost = 22,
		/// <summary>A clay oil lamp in a niche built out from a wall, a soot tongue above it (a Lamp; NicheLampPost where no wall is near).</summary>
		NicheLamp = 23,
		/// <summary>A carved post with a lamp niche in its head (a Lamp).</summary>
		NicheLampPost = 24,
		/// <summary>A small bronze bell on a weed-hung post, its mouth glowing (a cold Glow).</summary>
		Bellglow = 25,
		/// <summary>A lamp pillar: a stone shaft with tiers of lamp brackets (a Torch at its middle tier).</summary>
		LampPillar = 26,
		/// <summary>A ship's stern lantern: a tall hexagonal iron frame of horn panes on an iron stand (a Lamp).</summary>
		Fanal = 27,
		/// <summary>A terracotta lamp-jar pierced with triangles, hung from a wall hook (a Lamp; PiercedJarPillar where no wall is near).</summary>
		PiercedJar = 28,
		/// <summary>A pierced lamp-jar set on a stacked-slab pillar (a Lamp).</summary>
		PiercedJarPillar = 29,
		/// <summary>A waist-high stepped stone altar with a fire bowl on top (a Fire).</summary>
		FireAltar = 30,
		/// <summary>An iron double cruse lamp hung from a wall hook (a Lamp; IronCrusePost where no wall is near).</summary>
		IronCruse = 31,
		/// <summary>An iron double cruse lamp hung from a hook on a short post (a Lamp).</summary>
		IronCrusePost = 32,
		/// <summary>A fire in a stone bowl on a pillar of stacked slabs (a Fire).</summary>
		SlabHearth = 33,
		/// <summary>A splinter-light: an iron fork holding a burning splinter on a post over an ember trough (a Torch).</summary>
		Svetets = 34,
		/// <summary>A hollow ice cylinder, frosted, on a stone block, a candle inside (a Lamp).</summary>
		IceLantern = 35,
		/// <summary>A crystal lens on a stone post that gives back the day after sunset (a cold Glow, amber).</summary>
		SunLens = 36,
		/// <summary>An iron fat-lamp cup hung on a hook from a carved post (a Torch).</summary>
		SergeLamp = 37,
		/// <summary>A soapstone cup-lamp on an iron spike driven into the ground or the ice (a Torch).</summary>
		KolaSpike = 38,
		/// <summary>A tall pole with a crescent finial and hanging tassels (a cold Glow, silver).</summary>
		CrescentStandard = 39,
		/// <summary>A many-spouted clay oil lamp in a wall niche (a Lamp; ClayLampPost where no wall is near).</summary>
		ClayLamp = 40,
		/// <summary>A many-spouted clay oil lamp on a carved post (a Lamp).</summary>
		ClayLampPost = 41,
		/// <summary>A sunken lamp: a green-glowing jar in a weed-grown iron cage on a stake (a cold Glow, green).</summary>
		DrownedLamp = 42,
		/// <summary>A resin-heart pine torch wedged in a wall's crack, a soot tongue above it (a Torch; a torch post where it cannot be wedged).</summary>
		TeaTorch = 43,
		/// <summary>A ring of stones round a small fire on the ground (a Fire).</summary>
		HearthRing = 44,
		/// <summary>A ring of glass oil-cups hung from a post's iron crook (a Lamp).</summary>
		Polycandelon = 45,
		/// <summary>A clay saucer lamp under a little hood on a wall bracket (a Lamp; CastorLampPost where no wall is near).</summary>
		CastorLamp = 46,
		/// <summary>A clay saucer lamp under a little hood on a palm-trunk post (a Lamp).</summary>
		CastorLampPost = 47,
		/// <summary>A flat iron oil dish with a wick spout hung from a twisted hook on a staff (a Torch).</summary>
		Kriseilu = 48,
		/// <summary>An iron wheel crown on a stone post, an oil lamp hung inside its spokes (a Lamp).</summary>
		WheelLamp = 49,
		/// <summary>A tilted metal mirror disc in a ring frame on a stone post (a cold Glow, white).</summary>
		VolvaMirror = 50,
		/// <summary>A square post whose arm hangs a glass plumb-bob dead still on a wire (a cold Glow, white).</summary>
		Plummet = 51,
		/// <summary>An open iron oil lamp hung from a hook driven into a wall (a Lamp; a lantern stand where no wall is near).</summary>
		FrogLamp = 52,
		/// <summary>A flagstone niche with a horn-paned shutter, built out from a gable or a dyke end (a Lamp; HornBolePillar where no wall is near).</summary>
		HornBole = 53,
		/// <summary>A flagstone pillar with a horn-paned lamp niche in its head (a Lamp).</summary>
		HornBolePillar = 54,
		/// <summary>A stone lamp-bowl in a wall niche (a Lamp; ChiraghPillar where no wall is near).</summary>
		Chiragh = 55,
		/// <summary>A stone lamp-bowl on a waist-high rubble pillar (a Lamp).</summary>
		ChiraghPillar = 56,
		/// <summary>An oil wick in a hollowed block of rock salt in a wall niche, the salt glowing (a Lamp; SaltLampCairn where no wall is near).</summary>
		SaltLamp = 57,
		/// <summary>An oil wick in a hollowed block of rock salt on a cairn post (a Lamp).</summary>
		SaltLampCairn = 58,
		/// <summary>A carved soapstone fire bowl on a waist-high pillar (a Fire).</summary>
		SoapstoneBowl = 59,
		/// <summary>A pine-knot fire in a clay-plastered corner niche with a flue hole (a Fire; KokeulPillar where no wall is near).</summary>
		Kokeul = 60,
		/// <summary>A squat clay pillar with a pine-knot fire niche and a flue hole (a Fire).</summary>
		KokeulPillar = 61,
		/// <summary>A fire in a shallow painted clay tray on a wooden tripod (a Fire).</summary>
		TrayFire = 62,
		/// <summary>A candlenut torch: kernels skewered on a leaf midrib lashed to a pole (a Torch).</summary>
		TuituiTorch = 63,
		/// <summary>A rolled birch-bark torch in a split stake (a Torch).</summary>
		BarkTorch = 64,
		/// <summary>A cauldron-brazier on three hoof-shaped legs (a Fire).</summary>
		KazanFire = 65,
		/// <summary>A clay cup of sulphur and oil in a wall niche, burning low and blue (a blue Pyre; BrimLampPost where no wall is near).</summary>
		BrimLamp = 66,
		/// <summary>A clay cup of sulphur and oil on a rammed-earth post, burning low and blue (a blue Pyre).</summary>
		BrimLampPost = 67,
		/// <summary>An iron fork-basket of resinous pine splinters on a driven stake (a Torch).</summary>
		PishaStake = 68,
		/// <summary>A three-wick tinned-copper oil lamp hung from a tripod (a Lamp).</summary>
		CopperKandil = 69,
		/// <summary>A tall tripod candelabrum, its oil cup at the top (a Torch).</summary>
		Candelabrum = 70,
		/// <summary>A hollow nodule shell on a rib post, a glowing chip inside (a cold Glow, green).</summary>
		NoduleLamp = 71,
		/// <summary>A wrought-iron cage on a rib or stone post holding a glowing lure-orb (a cold Glow, blue).</summary>
		Lurelight = 72,
		/// <summary>A perforated clay lantern on a pitch-daubed reed-bundle pillar (a Torch).</summary>
		NaphthaLamp = 73,
		/// <summary>A seated stone figure bearing a fire bowl on its head (a Fire).</summary>
		Hearthwarden = 74,
		/// <summary>Three carved hearthstones under a clay griddle, a fire between them (a Fire).</summary>
		Tenamaztli = 75,
		/// <summary>Candlenut kernels strung on a midrib in a leaf sheath, set in a stone socket (a Torch).</summary>
		KukuiTorch = 76,
		/// <summary>A stone bowl lamp of nut oil on the ground (a Lamp).</summary>
		PohoLamp = 77,
		/// <summary>A bronze many-wick oil lamp in a limewashed wall niche (a Lamp; LucernaPost where no wall is near).</summary>
		Lucerna = 78,
		/// <summary>A bronze many-wick oil lamp on a squared post with a little roof (a Lamp).</summary>
		LucernaPost = 79,
		/// <summary>One candle in a squat stone hood on a cut post (a Lamp).</summary>
		VigilLamp = 80,
		/// <summary>A great shell holding oil and a floating wick, on a short stand (a Lamp).</summary>
		Shellwick = 81,
		/// <summary>A wrought-iron pan holding a glowing ember-stone, hung from a stake's arm (a cold Glow, amber).</summary>
		Gledpan = 82,
		/// <summary>A pale wisp caged in a bog-iron basket on a crooked post (a cold Glow, faerie).</summary>
		WispLantern = 83,
		/// <summary>A stone pillar with a gabled niche, a candle in a tin lantern inside (a Lamp).</summary>
		Alminha = 84,
		/// <summary>A candle in a pierced gourd hung from a crooked pole (a Lamp).</summary>
		GourdLantern = 85,
		/// <summary>A crescent stone oil lamp on a waist-high cairn, a low wide flame (a Torch).</summary>
		LampCairn = 86,
		/// <summary>A candle lantern in a small gabled shrine-box on a post (a Lamp).</summary>
		WaysideLantern = 87,
		/// <summary>A tall hollow stone column with an open lantern chamber under a pyramid cap (a Lamp).</summary>
		LanterneDesMorts = 88,
		/// <summary>Candles in a niche built out from a wall, a soot fan above (a Lamp; a lantern stand where no wall is near).</summary>
		CandleNiche = 89,
	}

	/// <summary>
	/// What shape of light a kind is, which decides where it can be laid (<see cref="BiomeWayLights.ClassOf"/>, 2026-10-26): a
	/// post, a wall light, a niche built out from a wall, a bowl or hearth on the ground, a hanging light, a tower's beacon.
	/// Stored by value nowhere (read from the kind); append-only all the same.
	/// </summary>
	public enum BiomeWayLightClass : byte
	{
		/// <summary>Stands on its own post, pillar, stake or stand (a ground light).</summary>
		Post = 0,
		/// <summary>Hung on a wall by its back plate (at z = 0, standing out along −z): a sconce, a bracket, a hook.</summary>
		Wall = 1,
		/// <summary>A niche built out from a wall, the lamp inside it (mounted as a wall light).</summary>
		Niche = 2,
		/// <summary>A bowl, a hearth or a brazier on the ground or a low base (a ground light).</summary>
		Bowl = 3,
		/// <summary>Hangs from a roof or a beam (a roof light).</summary>
		Hanging = 4,
		/// <summary>A beacon: a fire laid on a tower's top (its lookout) or a hill, kindled at dusk (a ground light).</summary>
		Beacon = 5,
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
					// Where the dressing stands its lights on the floor (a route's marks, a camp's lamp, a stope's light), a suite
					// light that is hung on a wall stands as its own twin (2026-10-26: a niche lamp's carved post, a tea torch on a pole).
					if (StandsOnFloor(place) && BiomeWayLights.StandsInStead(light.Light))
					{
						light.Light = BiomeWayLights.PostFor(light.Light);
					}
					return true;
				}
			}
			return false;
		}

		/// <summary>True for the places whose lights the cave dressing stands on the floor (a route, a camp, a stope).</summary>
		public static bool StandsOnFloor(BiomeCaveLightPlace place)
			=> place == BiomeCaveLightPlace.Route || place == BiomeCaveLightPlace.Camp || place == BiomeCaveLightPlace.MineStope;

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

		/// <summary>One kind's row of <see cref="Rows"/>: its placement class, what it gives off, and its standing twin.</summary>
		private readonly struct Row
		{
			public readonly BiomeWayLightClass Class;
			public readonly SceneEmitterKind Emitter;
			public readonly BiomeWayLightKind Post;

			public Row(BiomeWayLightClass @class, SceneEmitterKind emitter, BiomeWayLightKind post)
			{
				Class = @class;
				Emitter = emitter;
				Post = post;
			}
		}

		/// <summary>
		/// Every kind's placement class, the emitter its piece gives off (the kit's pieces are held to it by the tests), and the
		/// ground light laid in its stead where it cannot be hung (itself for a ground light), in kind order. The cold Glow
		/// kinds are what an airless or unbreathable world is lit with (decisions 41/62, <see cref="BiomeGlowLights.Glows"/>).
		/// </summary>
		private static readonly Row[] Rows =
		{
			new Row(BiomeWayLightClass.Post, SceneEmitterKind.Lamp, BiomeWayLightKind.LanternPost), // LanternPost
			new Row(BiomeWayLightClass.Post, SceneEmitterKind.Torch, BiomeWayLightKind.TorchPost), // TorchPost
			new Row(BiomeWayLightClass.Bowl, SceneEmitterKind.Fire, BiomeWayLightKind.Brazier), // Brazier
			new Row(BiomeWayLightClass.Post, SceneEmitterKind.Lamp, BiomeWayLightKind.LanternStand), // LanternStand
			new Row(BiomeWayLightClass.Wall, SceneEmitterKind.Torch, BiomeWayLightKind.TorchPost), // WallTorch
			new Row(BiomeWayLightClass.Hanging, SceneEmitterKind.Lamp, BiomeWayLightKind.LanternPost), // HangingLantern
			new Row(BiomeWayLightClass.Wall, SceneEmitterKind.Lamp, BiomeWayLightKind.LanternPost), // WallLantern
			new Row(BiomeWayLightClass.Post, SceneEmitterKind.Fire, BiomeWayLightKind.Cresset), // Cresset
			new Row(BiomeWayLightClass.Post, SceneEmitterKind.Lamp, BiomeWayLightKind.CrookLantern), // CrookLantern
			new Row(BiomeWayLightClass.Post, SceneEmitterKind.Glow, BiomeWayLightKind.Glowjar), // Glowjar
			new Row(BiomeWayLightClass.Post, SceneEmitterKind.Glow, BiomeWayLightKind.CrystalPost), // CrystalPost
			new Row(BiomeWayLightClass.Post, SceneEmitterKind.Glow, BiomeWayLightKind.RuneStone), // RuneStone
			new Row(BiomeWayLightClass.Wall, SceneEmitterKind.Lamp, BiomeWayLightKind.CrookLantern), // HangingBracket
			new Row(BiomeWayLightClass.Beacon, SceneEmitterKind.Pyre, BiomeWayLightKind.Beacon), // Beacon
			new Row(BiomeWayLightClass.Beacon, SceneEmitterKind.Pyre, BiomeWayLightKind.SignalPyre), // SignalPyre
			new Row(BiomeWayLightClass.Beacon, SceneEmitterKind.Pyre, BiomeWayLightKind.WartFire), // WartFire
			new Row(BiomeWayLightClass.Beacon, SceneEmitterKind.Pyre, BiomeWayLightKind.BlubberPyre), // BlubberPyre
			new Row(BiomeWayLightClass.Beacon, SceneEmitterKind.Pyre, BiomeWayLightKind.FivePits), // FivePits
			new Row(BiomeWayLightClass.Post, SceneEmitterKind.Lamp, BiomeWayLightKind.AmberLamp), // AmberLamp
			new Row(BiomeWayLightClass.Post, SceneEmitterKind.Lamp, BiomeWayLightKind.CarvedLantern), // CarvedLantern
			new Row(BiomeWayLightClass.Post, SceneEmitterKind.Lamp, BiomeWayLightKind.StoneLantern), // StoneLantern
			new Row(BiomeWayLightClass.Wall, SceneEmitterKind.Lamp, BiomeWayLightKind.PaperLanternPost), // PaperLantern
			new Row(BiomeWayLightClass.Post, SceneEmitterKind.Lamp, BiomeWayLightKind.PaperLanternPost), // PaperLanternPost
			new Row(BiomeWayLightClass.Niche, SceneEmitterKind.Lamp, BiomeWayLightKind.NicheLampPost), // NicheLamp
			new Row(BiomeWayLightClass.Post, SceneEmitterKind.Lamp, BiomeWayLightKind.NicheLampPost), // NicheLampPost
			new Row(BiomeWayLightClass.Post, SceneEmitterKind.Glow, BiomeWayLightKind.Bellglow), // Bellglow
			new Row(BiomeWayLightClass.Post, SceneEmitterKind.Torch, BiomeWayLightKind.LampPillar), // LampPillar
			new Row(BiomeWayLightClass.Post, SceneEmitterKind.Lamp, BiomeWayLightKind.Fanal), // Fanal
			new Row(BiomeWayLightClass.Wall, SceneEmitterKind.Lamp, BiomeWayLightKind.PiercedJarPillar), // PiercedJar
			new Row(BiomeWayLightClass.Post, SceneEmitterKind.Lamp, BiomeWayLightKind.PiercedJarPillar), // PiercedJarPillar
			new Row(BiomeWayLightClass.Bowl, SceneEmitterKind.Fire, BiomeWayLightKind.FireAltar), // FireAltar
			new Row(BiomeWayLightClass.Wall, SceneEmitterKind.Lamp, BiomeWayLightKind.IronCrusePost), // IronCruse
			new Row(BiomeWayLightClass.Post, SceneEmitterKind.Lamp, BiomeWayLightKind.IronCrusePost), // IronCrusePost
			new Row(BiomeWayLightClass.Bowl, SceneEmitterKind.Fire, BiomeWayLightKind.SlabHearth), // SlabHearth
			new Row(BiomeWayLightClass.Post, SceneEmitterKind.Torch, BiomeWayLightKind.Svetets), // Svetets
			new Row(BiomeWayLightClass.Post, SceneEmitterKind.Lamp, BiomeWayLightKind.IceLantern), // IceLantern
			new Row(BiomeWayLightClass.Post, SceneEmitterKind.Glow, BiomeWayLightKind.SunLens), // SunLens
			new Row(BiomeWayLightClass.Post, SceneEmitterKind.Torch, BiomeWayLightKind.SergeLamp), // SergeLamp
			new Row(BiomeWayLightClass.Post, SceneEmitterKind.Torch, BiomeWayLightKind.KolaSpike), // KolaSpike
			new Row(BiomeWayLightClass.Post, SceneEmitterKind.Glow, BiomeWayLightKind.CrescentStandard), // CrescentStandard
			new Row(BiomeWayLightClass.Niche, SceneEmitterKind.Lamp, BiomeWayLightKind.ClayLampPost), // ClayLamp
			new Row(BiomeWayLightClass.Post, SceneEmitterKind.Lamp, BiomeWayLightKind.ClayLampPost), // ClayLampPost
			new Row(BiomeWayLightClass.Post, SceneEmitterKind.Glow, BiomeWayLightKind.DrownedLamp), // DrownedLamp
			new Row(BiomeWayLightClass.Wall, SceneEmitterKind.Torch, BiomeWayLightKind.TorchPost), // TeaTorch
			new Row(BiomeWayLightClass.Bowl, SceneEmitterKind.Fire, BiomeWayLightKind.HearthRing), // HearthRing
			new Row(BiomeWayLightClass.Post, SceneEmitterKind.Lamp, BiomeWayLightKind.Polycandelon), // Polycandelon
			new Row(BiomeWayLightClass.Wall, SceneEmitterKind.Lamp, BiomeWayLightKind.CastorLampPost), // CastorLamp
			new Row(BiomeWayLightClass.Post, SceneEmitterKind.Lamp, BiomeWayLightKind.CastorLampPost), // CastorLampPost
			new Row(BiomeWayLightClass.Post, SceneEmitterKind.Torch, BiomeWayLightKind.Kriseilu), // Kriseilu
			new Row(BiomeWayLightClass.Post, SceneEmitterKind.Lamp, BiomeWayLightKind.WheelLamp), // WheelLamp
			new Row(BiomeWayLightClass.Post, SceneEmitterKind.Glow, BiomeWayLightKind.VolvaMirror), // VolvaMirror
			new Row(BiomeWayLightClass.Post, SceneEmitterKind.Glow, BiomeWayLightKind.Plummet), // Plummet
			new Row(BiomeWayLightClass.Wall, SceneEmitterKind.Lamp, BiomeWayLightKind.LanternStand), // FrogLamp
			new Row(BiomeWayLightClass.Niche, SceneEmitterKind.Lamp, BiomeWayLightKind.HornBolePillar), // HornBole
			new Row(BiomeWayLightClass.Post, SceneEmitterKind.Lamp, BiomeWayLightKind.HornBolePillar), // HornBolePillar
			new Row(BiomeWayLightClass.Niche, SceneEmitterKind.Lamp, BiomeWayLightKind.ChiraghPillar), // Chiragh
			new Row(BiomeWayLightClass.Post, SceneEmitterKind.Lamp, BiomeWayLightKind.ChiraghPillar), // ChiraghPillar
			new Row(BiomeWayLightClass.Niche, SceneEmitterKind.Lamp, BiomeWayLightKind.SaltLampCairn), // SaltLamp
			new Row(BiomeWayLightClass.Post, SceneEmitterKind.Lamp, BiomeWayLightKind.SaltLampCairn), // SaltLampCairn
			new Row(BiomeWayLightClass.Bowl, SceneEmitterKind.Fire, BiomeWayLightKind.SoapstoneBowl), // SoapstoneBowl
			new Row(BiomeWayLightClass.Niche, SceneEmitterKind.Fire, BiomeWayLightKind.KokeulPillar), // Kokeul
			new Row(BiomeWayLightClass.Post, SceneEmitterKind.Fire, BiomeWayLightKind.KokeulPillar), // KokeulPillar
			new Row(BiomeWayLightClass.Bowl, SceneEmitterKind.Fire, BiomeWayLightKind.TrayFire), // TrayFire
			new Row(BiomeWayLightClass.Post, SceneEmitterKind.Torch, BiomeWayLightKind.TuituiTorch), // TuituiTorch
			new Row(BiomeWayLightClass.Post, SceneEmitterKind.Torch, BiomeWayLightKind.BarkTorch), // BarkTorch
			new Row(BiomeWayLightClass.Bowl, SceneEmitterKind.Fire, BiomeWayLightKind.KazanFire), // KazanFire
			new Row(BiomeWayLightClass.Niche, SceneEmitterKind.Pyre, BiomeWayLightKind.BrimLampPost), // BrimLamp
			new Row(BiomeWayLightClass.Post, SceneEmitterKind.Pyre, BiomeWayLightKind.BrimLampPost), // BrimLampPost
			new Row(BiomeWayLightClass.Post, SceneEmitterKind.Torch, BiomeWayLightKind.PishaStake), // PishaStake
			new Row(BiomeWayLightClass.Post, SceneEmitterKind.Lamp, BiomeWayLightKind.CopperKandil), // CopperKandil
			new Row(BiomeWayLightClass.Post, SceneEmitterKind.Torch, BiomeWayLightKind.Candelabrum), // Candelabrum
			new Row(BiomeWayLightClass.Post, SceneEmitterKind.Glow, BiomeWayLightKind.NoduleLamp), // NoduleLamp
			new Row(BiomeWayLightClass.Post, SceneEmitterKind.Glow, BiomeWayLightKind.Lurelight), // Lurelight
			new Row(BiomeWayLightClass.Post, SceneEmitterKind.Torch, BiomeWayLightKind.NaphthaLamp), // NaphthaLamp
			new Row(BiomeWayLightClass.Bowl, SceneEmitterKind.Fire, BiomeWayLightKind.Hearthwarden), // Hearthwarden
			new Row(BiomeWayLightClass.Bowl, SceneEmitterKind.Fire, BiomeWayLightKind.Tenamaztli), // Tenamaztli
			new Row(BiomeWayLightClass.Post, SceneEmitterKind.Torch, BiomeWayLightKind.KukuiTorch), // KukuiTorch
			new Row(BiomeWayLightClass.Bowl, SceneEmitterKind.Lamp, BiomeWayLightKind.PohoLamp), // PohoLamp
			new Row(BiomeWayLightClass.Niche, SceneEmitterKind.Lamp, BiomeWayLightKind.LucernaPost), // Lucerna
			new Row(BiomeWayLightClass.Post, SceneEmitterKind.Lamp, BiomeWayLightKind.LucernaPost), // LucernaPost
			new Row(BiomeWayLightClass.Post, SceneEmitterKind.Lamp, BiomeWayLightKind.VigilLamp), // VigilLamp
			new Row(BiomeWayLightClass.Bowl, SceneEmitterKind.Lamp, BiomeWayLightKind.Shellwick), // Shellwick
			new Row(BiomeWayLightClass.Post, SceneEmitterKind.Glow, BiomeWayLightKind.Gledpan), // Gledpan
			new Row(BiomeWayLightClass.Post, SceneEmitterKind.Glow, BiomeWayLightKind.WispLantern), // WispLantern
			new Row(BiomeWayLightClass.Post, SceneEmitterKind.Lamp, BiomeWayLightKind.Alminha), // Alminha
			new Row(BiomeWayLightClass.Post, SceneEmitterKind.Lamp, BiomeWayLightKind.GourdLantern), // GourdLantern
			new Row(BiomeWayLightClass.Post, SceneEmitterKind.Torch, BiomeWayLightKind.LampCairn), // LampCairn
			new Row(BiomeWayLightClass.Post, SceneEmitterKind.Lamp, BiomeWayLightKind.WaysideLantern), // WaysideLantern
			new Row(BiomeWayLightClass.Post, SceneEmitterKind.Lamp, BiomeWayLightKind.LanterneDesMorts), // LanterneDesMorts
			new Row(BiomeWayLightClass.Niche, SceneEmitterKind.Lamp, BiomeWayLightKind.LanternStand), // CandleNiche
		};

		/// <summary>The first of the biome suite's own lights (2026-10-26): every kind from here on is the drafts'.</summary>
		public const BiomeWayLightKind FirstSuiteLight = BiomeWayLightKind.Beacon;

		/// <summary>The number of kinds there are (the enum's members, 0 .. Count − 1).</summary>
		public static int Count => Rows.Length;

		/// <summary>A kind's row; a number no kind has (a bad asset's) reads as a lantern post's.</summary>
		private static Row RowOf(BiomeWayLightKind light) => (int)light < Rows.Length ? Rows[(int)light] : Rows[0];

		/// <summary>What shape of light a kind is: a post, a wall light, a niche, a bowl, a hanging light, a beacon.</summary>
		public static BiomeWayLightClass ClassOf(BiomeWayLightKind light) => RowOf(light).Class;

		/// <summary>The emitter a kind's piece gives off: a lamp's, a torch's, a fire's, a cold Glow, a way light's own fire (Pyre).</summary>
		public static SceneEmitterKind EmitterOf(BiomeWayLightKind light) => RowOf(light).Emitter;

		/// <summary>How a light is set: the wall lights and the niches on a wall, the hanging lights from a roof, everything else on the ground.</summary>
		public static BiomeWayLightMount MountOf(BiomeWayLightKind light)
		{
			switch (ClassOf(light))
			{
				case BiomeWayLightClass.Wall:
				case BiomeWayLightClass.Niche:
					return BiomeWayLightMount.Wall;
				case BiomeWayLightClass.Hanging:
					return BiomeWayLightMount.Roof;
				default:
					return BiomeWayLightMount.Ground;
			}
		}

		/// <summary>
		/// True for a suite light (2026-10-26) that cannot stand on a floor and has a standing twin the cave dressing lays in its
		/// stead where it only stands lights on the floor (a route, a camp, a stope: <see cref="BiomeCaveLights.TryFor"/>). The
		/// older wall and hanging kinds keep their old handling there (noted as unfit, not laid).
		/// </summary>
		public static bool StandsInStead(BiomeWayLightKind light) => light >= FirstSuiteLight && MountOf(light) != BiomeWayLightMount.Ground;

		/// <summary>
		/// The light that stands on the ground in a light's stead where it cannot be hung (2026-10-18, I6g: a street with no wall
		/// near a light's place): a wall torch's torch on a pole, a wall lantern's lantern post, a hanging bracket's lantern on a
		/// crook post, the hanging lantern's lantern post; a suite light's own twin (a niche lamp's carved post, a paper lantern's
		/// bamboo post…); a light that stands on the ground is its own.
		/// </summary>
		public static BiomeWayLightKind PostFor(BiomeWayLightKind light) => (int)light < Rows.Length ? Rows[(int)light].Post : light;
	}
}
