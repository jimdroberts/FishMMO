#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.NameGeneration;
using UnityEditor;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// The farmland round a settlement (2026-10-11): fields of wheat, ploughed plots and fenced pasture on the gentle,
	/// dry ground outside it, clear of its ways, the water and every other site.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Crops are grown, not placed.</b> A field's ground has its other details cleared and its crop's detail painted in,
	/// edge to edge but a headland at its rim, so the procedural grass draws the crop with the same wind, light and season as
	/// the meadow beside it. The crop is one of its biome's (BiomeTemplate.FieldCrops, 2026-10-16), drawn by the site and the
	/// field when the biome grows more than one; until then every field grew wheat (Detail_GrassWheat, the grass blades'
	/// hand-masked crop), which the authoring writes as every biome's. A ploughed plot is cleared and laid as bare yard earth
	/// (the path overlay's tilled soil); pasture keeps its grass and is only fenced.
	/// </para>
	/// <para>
	/// <b>Fenced</b> with the kit's field fence, post and rails, a gap left in the side facing the settlement for the gate.
	/// An abandoned farm's fields are fallow (their grass let back in) and its fences broken.
	/// </para>
	/// <para>
	/// <b>Ridge and furrow is carved</b> (2026-10-18): its broad ridges raised into the terrain (<see cref="CarveRaise"/>,
	/// PointOfInterestFieldCarve), its furrows painted; lazy beds are painted only.
	/// </para>
	/// <para>
	/// <b>Never over another site's fields</b> (2026-10-18): the fields a site laid claim their ground (PointOfInterestFieldClaims),
	/// and a later site's field that would lie over one is shrunk about its middle or left out, deterministically, nothing else moved.
	/// </para>
	/// <para>Seeded by the site: a re-cut lays the same fields.</para>
	/// </remarks>
	[InitializeOnLoad]
	public sealed class PointOfInterestFields : IPointOfInterestSiteBuilder
	{
		static PointOfInterestFields() => PointOfInterestSiteBuilders.Register(new PointOfInterestFields());

		/// <summary>The steepest ground a field is ploughed on, degrees.</summary>
		public const float MaxSlopeDegrees = 7f;
		/// <summary>A crop's coverage in a field (0..255, the terrain's coverage mode).</summary>
		public const int WheatCoverage = 235;
		/// <summary>The unsown rim of a crop field, metres.</summary>
		private const float Headland = 1.5f;

		private enum FieldKind { Wheat, Ploughed, Pasture, Fallow }

		private struct Field
		{
			public Vector2 Centre;
			public float Yaw;
			public Vector2 Size;
			public FieldKind Kind;
			/// <summary>Its crop is laid on terraces (2026-10-18, I2j-b): it may stand on a slope a flat field may not.</summary>
			public bool Terraced;
			/// <summary>As laid (2026-10-18, I2j-b): its benches (null for an untouched field), the metres between its crop's rows (0 edge to edge), and whether its crop is a plant drawn by the detail scatter (not blades).</summary>
			public PointOfInterestFieldTerraces.Plan Terraces;
			public float Rows;
			public bool Mesh;
			/// <summary>The share of its crop's coverage it is painted at (2026-10-19: a reaped field's stubble, PointOfInterestFarmsteads); 0 the whole crop.</summary>
			public float Cover;
		}

		public bool Handles(PointOfInterestRecord record)
		{
			switch (record.Kind)
			{
				case POIType.Village:
				case POIType.Town:
				case POIType.City:
				case POIType.Capital:
				case POIType.Monastery:
				case POIType.AbandonedFarm:
				case POIType.Windmill:
					return true;
				default:
					return false;
			}
		}

		private static (int min, int max) CountFor(POIType kind)
		{
			switch (kind)
			{
				case POIType.Town: return (5, 9);
				case POIType.City: return (8, 12);
				case POIType.Capital: return (10, 14);
				case POIType.Monastery: return (2, 4);
				case POIType.AbandonedFarm: return (2, 3);
				// The fields a post mill grinds for, round its mound.
				case POIType.Windmill: return (3, 5);
				default: return (3, 6);
			}
		}

		public void Build(PointOfInterestSiteContext context)
		{
			PointOfInterestRecord record = context.Record;
			DeterministicRNG random = context.Random;
			(int min, int max) = CountFor(record.Kind);
			int wanted = min + (int)(random.NextDouble() * (max - min + 1));
			bool abandoned = record.Kind == POIType.AbandonedFarm;

			var ways = context.Points != null && context.Points.Paths != null ? new PathExclusion(context.Points.Paths) : null;
			var site = new Vector2(record.Position.x, record.Position.z);
			BiomeTemplate siteBiome = context.BiomeAt != null ? context.BiomeAt(site.x, site.y) : null;
			/* A biome authored to sow nothing (decision 25, 2026-10-25: its Field Crops written empty, or every crop weighed 0, and no
			 * orchard) lays no fields at all: no plots, pasture, fences or farmsteads. Only its old terraces, a channel of their own on
			 * draws of their own. A template never authored with crops still grows wheat (BiomeTemplateArt.FieldCrops). */
			if (BiomeTemplateArt.SowsNothing(siteBiome))
			{
				int none = 0, alone = 0;
				int lone = PointOfInterestFieldKinds.LayAbandonedTerraces(context, ways, ref none, ref alone);
				context.Notes?.Add($"{record.Name}: its biome sows nothing, so no field is laid round it" +
					(lone > 0 ? $"; {lone} flight(s) of old terraces round it, carved into {none:N0} heightmap sample(s)." : "."));
				return;
			}
			var fields = new List<Field>();
			/* How the site's biome lays its fields out (2026-10-18, I2j-b): kept to the bottomland near its rivers and lakes, set
			 * square to a centuriation grid. Neither (every biome until a round names them): fields are drawn as they always were. */
			BiomeFieldLayout layout = BiomeTemplateArt.FieldLayout(siteBiome);
			int sceneSeed = layout.Centuriation > 0f && context.Request != null ? unchecked((int)SceneGenerator.SceneSeed(context.Request)) : 0;
			for (int attempt = 0; attempt < wanted * 8 && fields.Count < wanted; attempt++)
			{
				float angle = (float)(random.NextDouble() * Math.PI * 2.0);
				// Out from the site's own edge that way (its outline's reach), not a disc's: a town drawn long down its valley has
				// its fields beyond its houses on every side.
				float edge = PointOfInterestOutline.ReachToward(record.Outline, record.Radius, angle * Mathf.Rad2Deg);
				float distance = edge * 1.15f + 12f + (float)random.NextDouble() * Mathf.Max(30f, record.Radius * 1.6f);
				var field = new Field
				{
					Centre = site + new Vector2(Mathf.Sin(angle), Mathf.Cos(angle)) * distance,
					Yaw = record.Yaw + (random.NextDouble() < 0.5 ? 0f : 90f) + (float)(random.NextDouble() - 0.5) * 16f,
					Size = new Vector2(Mathf.Lerp(28f, 72f, (float)random.NextDouble()), Mathf.Lerp(18f, 44f, (float)random.NextDouble())),
				};
				double roll = random.NextDouble();
				field.Kind = abandoned ? FieldKind.Fallow : roll < 0.55 ? FieldKind.Wheat : roll < 0.75 ? FieldKind.Ploughed : FieldKind.Pasture;
				// After every draw, so the site's sequence runs as before whatever the layout makes of the field.
				if (layout.Centuriation > 0f)
				{
					BiomeCenturiation.Snap(field.Centre, field.Yaw, field.Size, sceneSeed, layout.Centuriation, out field.Centre, out field.Yaw, out field.Size);
				}
				// Bottomland first: three quarters of the attempts keep to the rivers' and lakes' reach, the rest take any ground.
				if (layout.Bottomland > 0f && attempt < wanted * 6 && !NearWater(context, field.Centre, layout.Bottomland))
				{
					continue;
				}
				field.Terraced = TerracedAt(context, field, fields.Count);
				if (Fits(context, field, fields, ways))
				{
					fields.Add(field);
				}
			}
			if (fields.Count == 0)
			{
				// Steep country where no field fits may still have its old terraces (2026-10-18, I2j-b).
				int none = 0, alone = 0;
				int lone = PointOfInterestFieldKinds.LayAbandonedTerraces(context, ways, ref none, ref alone);
				if (lone > 0)
				{
					context.Notes?.Add($"{record.Name}: no field fits round it; {lone} flight(s) of old terraces round it, carved into {none:N0} heightmap sample(s).");
				}
				return;
			}

			/* Off every other site's fields (2026-10-18, I2k, PointOfInterestFieldClaims): a field that would lie over one is shrunk
			 * about its middle until it clears, inside the ground already found fit, or left out. Tested after every field was drawn,
			 * as they always were, so this site's other fields lie where they did; the fences' draws are kept in step below. */
			var laid = new Field?[fields.Count];
			int shrunk = 0, left = 0;
			for (int f = 0; f < fields.Count; f++)
			{
				Field drawn = fields[f];
				if (!PointOfInterestFieldClaims.Fit(context.FieldClaims, record.Id, drawn.Centre, drawn.Yaw, drawn.Size, out Vector2 fitted))
				{
					left++;
					continue;
				}
				// A garden of raised beds is laid at a garden's size about its middle (2026-10-18, I2j-b).
				Vector2 claimed = fitted;
				fitted = GardenSize(context, drawn, f, fitted);
				Field field = drawn;
				field.Size = fitted;
				if (fitted != drawn.Size && !FitsGround(context, field, ways))
				{
					left++;
					continue;
				}
				shrunk += claimed != drawn.Size ? 1 : 0;
				laid[f] = field;
				context.FieldClaims?.Add(record.Id, field.Centre, field.Yaw, field.Size);
			}

			List<Terrain> terrains = TerrainsOf(context);
			int crops = 0, carved = 0, paddies = 0, terraced = 0, orchards = 0, pieces = 0;
			// Every field as laid, for the farmsteads and the dressing laid after them all (2026-10-19, PointOfInterestFarmsteads).
			var laidFields = new List<PointOfInterestFarmsteads.LaidField>();
			for (int f = 0; f < fields.Count; f++)
			{
				Field drawn = fields[f];
				if (!laid[f].HasValue)
				{
					// Left out: its fence's draws still made, so every later field's fence is as it was.
					if (!WouldFlood(context, drawn, f))
					{
						Fence(context, drawn, site, abandoned, context.Random, dry: true);
					}
					continue;
				}
				Field field = laid[f].Value;
				// Shrunk: whether the field as drawn would have been fenced, asked of the ground before this one changes it.
				bool same = field.Size == drawn.Size;
				bool drawnFenced = !same && !WouldFlood(context, drawn, f);
				bool flooded = false;
				bool benched = false;
				// What it is used for, for the farmsteads and the dressing (2026-10-19): a grain crop is found below as it is sown.
				PointOfInterestFarmsteads.FieldUse use = field.Kind == FieldKind.Pasture ? PointOfInterestFarmsteads.FieldUse.Pasture
					: field.Kind == FieldKind.Fallow ? PointOfInterestFarmsteads.FieldUse.Fallow
					: field.Kind == FieldKind.Ploughed ? PointOfInterestFarmsteads.FieldUse.Ploughed : PointOfInterestFarmsteads.FieldUse.Crop;
				bool reaped = false, inOrchard = false;
				// Whether its details were laid as its crop's (below); else it is kept as turf (2026-10-27, PointOfInterestEnclosureClearance).
				bool painted = false;
				if (field.Kind == FieldKind.Wheat || field.Kind == FieldKind.Ploughed)
				{
					// The crop is one of the biome's its field lies in, and takes that biome's look (2026-10-16).
					CropOf(context, field, f, out BiomeTemplate biome, out GameObject crop, out BiomeFieldSurface surface, out BiomeFieldCrop picked, out BiomeOrchard? orchard);
					/* Terraces (2026-10-18, I2j-b): benches stepped into the slope along its contours, carved from the restored ground as
					 * ridge and furrow is; a terraced paddy's every bench dug, bunded and flooded. Laid flat where the ground is too gentle
					 * to step (a paddy then as a paddy). Its steps faced in its biome's wall style or left as turfed banks. */
					BiomeTerraceRiser riser = BiomeTerraces.RiserOf(surface, picked.Riser);
					if (riser != BiomeTerraceRiser.None)
					{
						PointOfInterestFieldTerraces.Plan plan = PointOfInterestFieldTerraces.Make(field.Centre, field.Yaw, field.Size, riser, BiomeFieldSurfaces.Flooded(surface), context.GroundAt);
						List<SceneHydrology.Lake> lakes = plan.Paddy ? TreadLakes(context, plan, f) : null;
						if (plan.Terraced && (!plan.Paddy || lakes.Count > 0))
						{
							carved += PointOfInterestFieldTerraces.Carve(context, plan);
							if (plan.Paddy)
							{
								WriteLakes(context, lakes);
								flooded = true;
								paddies++;
							}
							pieces += PointOfInterestFieldTerraces.FaceSteps(context, biome, plan, unchecked((int)PointOfInterestPlanner.Hash(record.SiteSeed, 0x544552, f, 0)));
							field.Terraces = plan;
							benched = true;
							terraced++;
							surface = BiomeFieldSurface.Flat;
						}
						else
						{
							surface = BiomeTerraces.TreadSurface(surface);
						}
					}
					/* Ridge and furrow is in the ground too (2026-10-18): carved from the ground as it lay before any field was,
					 * which the repaint and Regenerate POIs put back first (PointOfInterestFieldCarve), so it is the same on every
					 * run. Before the crop, the furrows and the fence are laid, so each stands on the ground as carved. */
					if (BiomeFieldSurfaces.Carved(surface))
					{
						carved += PointOfInterestFieldCarve.Carve(context, field.Centre, field.Yaw, field.Size, surface);
					}
					/* A paddy (2026-10-18, I2i): dug level and bunded from the same restored ground, then flooded with a lake of the
					 * scene's (PointOfInterestPaddies); sown flat where the ground would need too deep a cut or fill. */
					if (BiomeFieldSurfaces.Flooded(surface))
					{
						flooded = LayPaddy(context, field, f, ref carved);
						if (!flooded)
						{
							surface = BiomeFieldSurface.Flat;
						}
						paddies += flooded ? 1 : 0;
					}
					/* An orchard (2026-10-18, I2j-b): its understorey sown edge to edge (none: the meadow left as it grows), its trees in
					 * rows; a non-grass crop drawn by the detail scatter, in rows where its entry sows it so (2026-10-18, I2j-b). */
					if (orchard.HasValue)
					{
						crop = orchard.Value.Understorey;
					}
					field.Rows = orchard.HasValue ? 0f : picked.Rows;
					field.Mesh = crop != null && !BiomeGrassDefaults.IsBlade(crop.name);
					/* A grain crop the blades draw is reaped in some fields (2026-10-19: "fenced areas but they aren't really farm land"):
					 * painted as its stubble, a share of the crop, and stood in stooks after every field (PointOfInterestFarmsteads.Dress),
					 * by a hash of its own, so nothing else changes. */
					if (field.Kind == FieldKind.Wheat && crop != null && !field.Mesh && !orchard.HasValue && !flooded && !benched)
					{
						use = PointOfInterestFarmsteads.FieldUse.Grain;
						reaped = PointOfInterestFarmsteads.Reaped(record.SiteSeed, f);
						field.Cover = reaped ? PointOfInterestFarmsteads.StubbleCover : 0f;
					}
					if (!orchard.HasValue || crop != null)
					{
						PaintDetails(terrains, field, crop, biome, surface);
						painted = true;
					}
					if (BiomeFieldSurfaces.Ridged(surface))
					{
						AddFurrows(context, field, surface);
					}
					pieces += PointOfInterestFieldKinds.LaySurface(context, biome, field.Centre, field.Yaw, field.Size, surface, record.SiteSeed, f, Headland, ref carved);
					if (orchard.HasValue)
					{
						pieces += PointOfInterestFieldKinds.LayOrchard(context, biome, orchard.Value, field.Centre, field.Yaw, field.Size, field.Terraces, record.SiteSeed, f, Headland);
						orchards++;
						inOrchard = true;
					}
					crops++;
				}
				if (field.Kind == FieldKind.Ploughed)
				{
					AddPloughedGround(context, field);
				}
				// Shrunk: the drawn field's fence's draws made, so every later fence is as it was; its own fence from its own seed.
				if (drawnFenced)
				{
					Fence(context, drawn, site, abandoned, context.Random, dry: true);
				}
				// A paddy's bund is its bound: no fence round it.
				if (!flooded && benched)
				{
					// Its steps are its bounds (2026-10-18, I2j-b): no edge style, the kit fence's draws still made (dry), as a lined field's.
					Fence(context, field, site, abandoned, same ? context.Random : new DeterministicRNG(unchecked((int)PointOfInterestPlanner.Hash(record.SiteSeed, 0x46454E, f, 0))), dry: true);
				}
				else if (!flooded)
				{
					/* Bounded in its biome's field-edge style (2026-10-18, I2f: a hedge, a wall, a row of trees…, PointOfInterestLinearFeatures)
					 * where it names one the field's draw admits, on draws of its own; the kit's fence's draws are still made (dry), so
					 * every later field's fence lies as it did. */
					bool lined = PointOfInterestLinearFeatures.LayFieldEdges(context, field.Centre, field.Yaw, field.Size, site, abandoned, f);
					Fence(context, field, site, abandoned, same ? context.Random : new DeterministicRNG(unchecked((int)PointOfInterestPlanner.Hash(record.SiteSeed, 0x46454E, f, 0))), dry: lined);
				}
				laidFields.Add(new PointOfInterestFarmsteads.LaidField { Centre = field.Centre, Yaw = field.Yaw, Size = field.Size, Use = use, Reaped = reaped, Index = f, Orchard = inOrchard });
				/* Its ground cleared of the scatter's trees, bushes and boulders once every site is built (2026-10-27: "Trees, bushes, and
				 * other details spawn inside it"), a pasture's, a fallow's or an unsown orchard's wild details with them, its grass kept. */
				context.FieldClaims?.Enclose(field.Centre, field.Yaw, field.Size, turf: !painted);
			}
			/* The old terraces round a hamlet (2026-10-18, I2j-b): flights of benches on the slopes about it, carved as a terraced field's,
			 * nothing sown, their walls tumbled; on draws of their own after every field, so the fields lie as before. */
			int flights = PointOfInterestFieldKinds.LayAbandonedTerraces(context, ways, ref carved, ref pieces);
			int kept = fields.Count - left;
			context.Notes?.Add($"{record.Name}: {kept} field(s) laid round it ({crops} sown or ploughed)" +
				(paddies > 0 ? $", {paddies} of them flooded paddies" : string.Empty) +
				(terraced > 0 ? $", {terraced} terraced" : string.Empty) +
				(orchards > 0 ? $", {orchards} orchard(s)" : string.Empty) +
				(flights > 0 ? $", {flights} flight(s) of old terraces round it" : string.Empty) +
				(pieces > 0 ? $", {pieces} wall, tree and edging piece(s)" : string.Empty) +
				(left + shrunk > 0 ? $", {shrunk} shrunk and {left} left out off another site's fields" : string.Empty) +
				(carved > 0 ? $", ridge and furrow, terraces or paddy bunds carved into {carved:N0} heightmap sample(s)." : "."));
			/* Worked farmland (2026-10-19, PointOfInterestFarmsteads): stooks on the reaped corn, ricks, scarecrows and troughs, then the
			 * farmsteads beside the fields, each on draws of its own after everything above, so the fields, fences and terraces lie as
			 * they did; then the outskirts' lone holdings, wayside crosses, gallows and cemetery (PointOfInterestOutskirts), after those. */
			PointOfInterestFarmsteads.Dress(context, laidFields, abandoned);
			PointOfInterestFarmsteads.Lay(context, laidFields);
			PointOfInterestOutskirts.Lay(context, laidFields);
		}

		/// <summary>
		/// The crop a sown field grows and how it lies: one of the biome its middle lies in (with that biome's look, 2026-10-16), drawn
		/// by the site and the field's index; none for a ploughed plot or a biome growing none, when it lies flat.
		/// </summary>
		private static void CropOf(PointOfInterestSiteContext context, Field field, int index, out BiomeTemplate biome, out GameObject crop, out BiomeFieldSurface surface)
			=> CropOf(context, field, index, out biome, out crop, out surface, out _, out _);

		/// <summary>
		/// The crop (or orchard, 2026-10-18, I2j-b) a sown field grows, with its whole entry: the biome's orchards are drawn in the same
		/// draw as its crops, after them (<see cref="BiomeOrchards.AsCrop"/>), so a biome planting none draws exactly as before. For an
		/// orchard <paramref name="crop"/> is its tree and <paramref name="orchard"/> the orchard.
		/// </summary>
		private static void CropOf(PointOfInterestSiteContext context, Field field, int index, out BiomeTemplate biome, out GameObject crop, out BiomeFieldSurface surface,
			out BiomeFieldCrop picked, out BiomeOrchard? orchard)
		{
			biome = context.BiomeAt != null ? context.BiomeAt(field.Centre.x, field.Centre.y) : null;
			crop = null;
			picked = default;
			orchard = null;
			// How the crop's fields lie (2026-10-16): flat as every field was, or in lazy beds or ridge and furrow.
			surface = BiomeFieldSurface.Flat;
			if (field.Kind == FieldKind.Wheat)
			{
				List<BiomeFieldCrop> grown = BiomeTemplateArt.FieldCrops(biome);
				int crops = grown.Count;
				List<BiomeOrchard> orchards = BiomeTemplateArt.Orchards(biome);
				grown.AddRange(orchards.ConvertAll(BiomeOrchards.AsCrop));
				int pick = CropIndex(grown.ConvertAll(c => c.Weight), context.Record.SiteSeed, index);
				crop = pick >= 0 ? grown[pick].Prefab : null;
				surface = pick >= 0 ? grown[pick].Surface : BiomeFieldSurface.Flat;
				picked = pick >= 0 ? grown[pick] : default;
				orchard = pick >= crops ? orchards[pick - crops] : (BiomeOrchard?)null;
			}
			if (crop == null)
			{
				surface = BiomeFieldSurface.Flat;
				picked = default;
				orchard = null;
			}
		}

		/// <summary>True when a field as drawn would be laid on terraces (2026-10-18, I2j-b): its crop's riser, at the field's index when drawn.</summary>
		private static bool TerracedAt(PointOfInterestSiteContext context, Field field, int index)
		{
			if (field.Kind != FieldKind.Wheat)
			{
				return false;
			}
			CropOf(context, field, index, out _, out _, out BiomeFieldSurface surface, out BiomeFieldCrop picked, out _);
			return BiomeTerraces.RiserOf(surface, picked.Riser) != BiomeTerraceRiser.None;
		}

		/// <summary>A garden of raised beds' size (2026-10-18, I2j-b): no larger than <see cref="BiomeFieldSurfaces.RaisedBedGarden"/>, its long side along the field's; any other field's as fitted.</summary>
		private static Vector2 GardenSize(PointOfInterestSiteContext context, Field field, int index, Vector2 fitted)
		{
			if (field.Kind != FieldKind.Wheat)
			{
				return fitted;
			}
			CropOf(context, field, index, out _, out _, out BiomeFieldSurface surface);
			if (surface != BiomeFieldSurface.RaisedBeds)
			{
				return fitted;
			}
			Vector2 most = fitted.x >= fitted.y ? BiomeFieldSurfaces.RaisedBedGarden : new Vector2(BiomeFieldSurfaces.RaisedBedGarden.y, BiomeFieldSurfaces.RaisedBedGarden.x);
			return new Vector2(Mathf.Min(fitted.x, most.x), Mathf.Min(fitted.y, most.y));
		}

		/// <summary>
		/// True when a river or a lake lies within <paramref name="reach"/> metres of a point (2026-10-18, I2j-b; the bottomland a biome
		/// keeps its fields to): searched outward on an 8 m lattice, ring by ring, stopping at the first.
		/// </summary>
		private static bool NearWater(PointOfInterestSiteContext context, Vector2 p, float reach)
		{
			if (context.Water == null)
			{
				return true;
			}
			return PointOfInterestFieldKinds.NearWater((x, z) =>
			{
				WaterKind kind = context.Water.KindAt(x, z);
				return kind == WaterKind.River || kind == WaterKind.Lake;
			}, p, reach);
		}

		/// <summary>A terraced paddy's lakes, their ids the site's and the field's (PointOfInterestFieldTerraces.LakeId).</summary>
		private static List<SceneHydrology.Lake> TreadLakes(PointOfInterestSiteContext context, PointOfInterestFieldTerraces.Plan plan, int index)
			=> PointOfInterestFieldTerraces.TreadLakes(plan, piece => PointOfInterestFieldTerraces.LakeId(PointOfInterestFieldTerraces.TreadLakeKind, context.Record.Id, index, piece));

		/// <summary>Writes lakes into the scene's hydrology (a note once where there is none to hold them).</summary>
		private static void WriteLakes(PointOfInterestSiteContext context, List<SceneHydrology.Lake> lakes)
		{
			foreach (SceneHydrology.Lake lake in lakes)
			{
				if (!PointOfInterestPaddies.WriteWater(context, lake))
				{
					PointOfInterestFeatureKit.Note(context, "a terraced paddy's water has no hydrology to go in (a harness, or a LOCAL copy, which shares the committed scene's water)");
					return;
				}
			}
		}

		/// <summary>True when a field as drawn would be laid as a flooded paddy (and so fenced by none): its crop's, and its ground's, say.</summary>
		private static bool WouldFlood(PointOfInterestSiteContext context, Field field, int index)
		{
			if (field.Kind != FieldKind.Wheat)
			{
				return false;
			}
			CropOf(context, field, index, out _, out _, out BiomeFieldSurface surface, out BiomeFieldCrop picked, out _);
			if (!BiomeFieldSurfaces.Flooded(surface))
			{
				return false;
			}
			// A terraced paddy (2026-10-18, I2j-b) as its build lays it: flooded where its benches are cut and hold water, else as a paddy.
			BiomeTerraceRiser riser = BiomeTerraces.RiserOf(surface, picked.Riser);
			if (riser != BiomeTerraceRiser.None)
			{
				PointOfInterestFieldTerraces.Plan plan = PointOfInterestFieldTerraces.Make(field.Centre, field.Yaw, field.Size, riser, true, context.GroundAt);
				if (plan.Terraced && TreadLakes(context, plan, index).Count > 0)
				{
					return true;
				}
			}
			return PaddyFits(context, field, index, out _, out _);
		}

		/// <summary>
		/// Whether a paddy can be laid in a field (no side effect): its level from the ground round it, no other water round it, and
		/// a lake cell under its floor.
		/// </summary>
		private static bool PaddyFits(PointOfInterestSiteContext context, Field field, int index, out float water, out SceneHydrology.Lake lake)
		{
			lake = null;
			water = 0f;
			if (context.GroundAt == null || !PointOfInterestPaddies.Level(field.Centre, field.Yaw, field.Size, context.GroundAt, out water))
			{
				return false;
			}
			// Not where the ground round it runs down to other water (a shore, a river's bank): its level would be that water's.
			foreach (Vector2 p in PointOfInterestPaddies.Ring(field.Centre, field.Yaw, field.Size))
			{
				if (context.IsWet(p.x, p.y, BiomeFieldSurfaces.PaddyBundRise))
				{
					return false;
				}
			}
			lake = PointOfInterestPaddies.Lake(PointOfInterestPaddies.LakeId(context.Record.Id, index), field.Centre, field.Yaw, field.Size, water);
			return lake != null;
		}

		/// <summary>
		/// Digs, bunds and floods one paddy (PointOfInterestPaddies): its level from the ground round it, the carve recorded for the
		/// repaint to put back, its water written as a lake of the scene's. False, touching nothing, when its ground would need too
		/// deep a cut or fill (it is then sown flat) or the field gets no water cell.
		/// </summary>
		private static bool LayPaddy(PointOfInterestSiteContext context, Field field, int index, ref int carved)
		{
			if (!PaddyFits(context, field, index, out float water, out SceneHydrology.Lake lake))
			{
				return false;
			}
			Vector2 centre = field.Centre, size = field.Size;
			float yaw = field.Yaw;
			carved += PointOfInterestFieldCarve.CarveTo(context, PointOfInterestPaddies.CarveArea(centre, yaw, size),
				(x, z, ground) => PointOfInterestPaddies.Offset(centre, yaw, size, water, x, z, ground));
			if (!PointOfInterestPaddies.WriteWater(context, lake))
			{
				PointOfInterestFeatureKit.Note(context, "a paddy's water has no hydrology to go in (a harness, or a LOCAL copy, which shares the committed scene's water)");
			}
			return true;
		}

		// ── Siting ────────────────────────────────────────────────────

		/// <summary>How far apart a field's ground is tested, metres: closer than a shore can slip between two tests.</summary>
		private const float SampleSpacing = 4f;

		/// <summary>The least the ground of a field stands over the water's highest level, metres.</summary>
		private const float Freeboard = 0.4f;

		/// <summary>
		/// The field's ground on a lattice no coarser than <see cref="SampleSpacing"/>, corners and edges included, world xz.
		/// Its corners, edge middles and centre alone let a coast run between them: fields were laid out over the sea.
		/// </summary>
		private static IEnumerable<Vector2> Samples(Field field)
		{
			float r = field.Yaw * Mathf.Deg2Rad;
			var x = new Vector2(Mathf.Cos(r), -Mathf.Sin(r));
			var z = new Vector2(Mathf.Sin(r), Mathf.Cos(r));
			int nx = Mathf.Max(2, Mathf.CeilToInt(field.Size.x / SampleSpacing));
			int nz = Mathf.Max(2, Mathf.CeilToInt(field.Size.y / SampleSpacing));
			for (int i = 0; i <= nx; i++)
			{
				for (int j = 0; j <= nz; j++)
				{
					yield return field.Centre + x * ((i / (float)nx - 0.5f) * field.Size.x) + z * ((j / (float)nz - 0.5f) * field.Size.y);
				}
			}
		}

		private static bool Fits(PointOfInterestSiteContext context, Field field, List<Field> placed, PathExclusion ways)
		{
			foreach (Field other in placed)
			{
				if (PointOfInterestLayouts.Overlaps(field.Centre, field.Yaw, field.Size + Vector2.one * 6f, other.Centre, other.Yaw, other.Size + Vector2.one * 6f))
				{
					return false;
				}
			}
			return FitsGround(context, field, ways);
		}

		/// <summary>True when every sample of a field's ground is dry, gentle, open ground in the scene, clear of the ways and the keep-outs.</summary>
		private static bool FitsGround(PointOfInterestSiteContext context, Field field, PathExclusion ways)
		{
			float slopes = 0f;
			int count = 0;
			foreach (Vector2 p in Samples(field))
			{
				if (!context.InScene(p.x, p.y))
				{
					return false;
				}
				float ground = context.GroundAt(p.x, p.y);
				if (float.IsNaN(ground) || float.IsInfinity(ground) || (context.SkyAt != null && context.SkyAt(p.x, p.y)))
				{
					return false;
				}
				// A terraced field stands on a slope a flat one may not (2026-10-18, I2j-b), its mean no steeper than benches are cut.
				if (context.SlopeAt != null && context.SlopeAt(p.x, p.y) > (field.Terraced ? BiomeTerraces.MaxSlopeDegrees : MaxSlopeDegrees))
				{
					return false;
				}
				if (field.Terraced && context.SlopeAt != null)
				{
					slopes += context.SlopeAt(p.x, p.y);
					count++;
				}
				if (context.Water != null && context.Water.KindAt(p.x, p.y) != WaterKind.None)
				{
					return false;
				}
				// The sea and every standing water by level, not only the grid's rivers and lakes.
				if (context.IsWet(p.x, p.y, Freeboard))
				{
					return false;
				}
				if (context.Points != null && context.Points.IsKeptOut(p.x, p.y))
				{
					return false;
				}
				if (ways != null && ways.Trees(p.x, p.y))
				{
					return false;
				}
			}
			return count == 0 || slopes / count <= BiomeTerraces.MaxMeanSlopeDegrees;
		}

		// ── The ground ────────────────────────────────────────────────

		private static List<Terrain> TerrainsOf(PointOfInterestSiteContext context)
		{
			var terrains = new List<Terrain>();
			if (context.Scene.IsValid())
			{
				foreach (GameObject root in context.Scene.GetRootGameObjects())
				{
					terrains.AddRange(root.GetComponentsInChildren<Terrain>(true));
				}
			}
			return terrains;
		}

		/// <summary>
		/// Which of a biome's crops a field grows (pure): the only one without a draw, else a draw by the crop weights seeded by
		/// the site and the field's index (never the site's random sequence, so the fields lie as before); -1 for none.
		/// </summary>
		public static int CropIndex(IReadOnlyList<float> weights, int siteSeed, int field)
		{
			if (weights == null || weights.Count == 0)
			{
				return -1;
			}
			if (weights.Count == 1)
			{
				return weights[0] > 0f ? 0 : -1;
			}
			float total = 0f;
			foreach (float weight in weights)
			{
				total += Mathf.Max(0f, weight);
			}
			if (!(total > 0f))
			{
				return -1;
			}
			float roll = PointOfInterestPlanner.Unit(PointOfInterestPlanner.Hash(siteSeed, 0x43524F, field, 0)) * total;
			int last = -1;
			for (int i = 0; i < weights.Count; i++)
			{
				float weight = Mathf.Max(0f, weights[i]);
				if (weight <= 0f)
				{
					continue;
				}
				last = i;
				roll -= weight;
				if (roll < 0f)
				{
					return i;
				}
			}
			return last;
		}

		/// <summary>
		/// Clears every detail inside the field and, for a crop, paints it in (a headland left round it), on every tile the
		/// field crosses. The crop goes into the tile's prototype of it for <paramref name="biome"/>, or one the field adds and
		/// records for it (PointOfInterestHerbGrove.SiteLayers), whose look of the crop its blades then take.
		/// </summary>
		/// <param name="crop">The crop's detail prefab; null for a ploughed plot (cleared only) or a biome growing none.</param>
		/// <param name="surface">How the crop lies: on a ridged field it is sown along the ridges only (<see cref="SownCoverage"/>).</param>
		private static void PaintDetails(List<Terrain> terrains, Field field, GameObject crop, BiomeTemplate biome, BiomeFieldSurface surface = BiomeFieldSurface.Flat)
		{
			float r = field.Yaw * Mathf.Deg2Rad;
			var axisX = new Vector2(Mathf.Cos(r), -Mathf.Sin(r));
			var axisZ = new Vector2(Mathf.Sin(r), Mathf.Cos(r));
			float halfX = field.Size.x * 0.5f, halfZ = field.Size.y * 0.5f;
			float reach = new Vector2(halfX, halfZ).magnitude;
			foreach (Terrain terrain in terrains)
			{
				TerrainData data = terrain != null ? terrain.terrainData : null;
				if (data == null || data.detailResolution <= 0)
				{
					continue;
				}
				Vector3 origin = terrain.transform.position;
				Vector3 size = data.size;
				int res = data.detailResolution;
				float texel = size.x / res;
				int x0 = Mathf.Clamp(Mathf.FloorToInt((field.Centre.x - reach - origin.x) / texel), 0, res - 1);
				int x1 = Mathf.Clamp(Mathf.CeilToInt((field.Centre.x + reach - origin.x) / texel), 0, res - 1);
				int z0 = Mathf.Clamp(Mathf.FloorToInt((field.Centre.y - reach - origin.z) / texel), 0, res - 1);
				int z1 = Mathf.Clamp(Mathf.CeilToInt((field.Centre.y + reach - origin.z) / texel), 0, res - 1);
				if (x1 <= x0 || z1 <= z0)
				{
					continue;
				}
				// Every layer is cleared inside the field (the crop of another biome's prototype too), so the field's own
				// biome's prototype of its crop simply takes it.
				int wheatLayer = crop != null ? CropLayer(terrain, crop, biome) : -1;
				int layers = data.detailPrototypes.Length;
				/* The fields laid as before take the old path below to the bit; terraced, rowed, corn-hill, raised-bed and scatter-drawn
				 * crops (2026-10-18, I2j-b) their share of the texel (CropShare), a scatter plant's in the scatter's units. */
				bool plain = !field.Mesh && field.Rows <= 0f && field.Terraces == null && surface != BiomeFieldSurface.CornHills && surface != BiomeFieldSurface.RaisedBeds;
				float unit = field.Mesh ? Mathf.Max(1, data.maxDetailScatterPerRes) / 255f : 1f;
				int maxValue = field.Mesh ? Mathf.Max(1, data.maxDetailScatterPerRes) : int.MaxValue;
				bool turf = field.Terraces != null && field.Terraces.Terraced && BiomeTerraces.KeepsTurf(field.Terraces.Riser);
				int w = x1 - x0 + 1, h = z1 - z0 + 1;
				for (int layer = 0; layer < layers; layer++)
				{
					int[,] map = data.GetDetailLayer(x0, z0, w, h, layer);
					bool changed = false;
					for (int j = 0; j < h; j++)
					{
						for (int i = 0; i < w; i++)
						{
							var p = new Vector2(origin.x + (x0 + i + 0.5f) * texel, origin.z + (z0 + j + 0.5f) * texel) - field.Centre;
							float u = Vector2.Dot(p, axisX), v = Vector2.Dot(p, axisZ);
							if (Mathf.Abs(u) > halfX || Mathf.Abs(v) > halfZ)
							{
								continue;
							}
							bool sown = layer == wheatLayer && Mathf.Abs(u) < halfX - Headland && Mathf.Abs(v) < halfZ - Headland;
							int value = !sown ? 0 : surface == BiomeFieldSurface.Flat ? WheatCoverage : SownCoverage(surface, field, u, v, texel);
							if (!plain)
							{
								Vector2 world = field.Centre + p;
								// An earth bund's or grass bank's step and lip keep the turf that grew there.
								if (turf && !field.Terraces.Sown(world.x, world.y, 0f))
								{
									continue;
								}
								float share = sown ? CropShare(surface, field.Size, u, v, texel, field.Rows, field.Terraces, world) : 0f;
								value = Mathf.Clamp(Mathf.RoundToInt(WheatCoverage * share * unit), 0, maxValue);
							}
							// A reaped field's stubble (2026-10-19): a share of the standing crop.
							if (field.Cover > 0f && field.Cover < 1f)
							{
								value = Mathf.RoundToInt(value * field.Cover);
							}
							if (map[j, i] != value)
							{
								map[j, i] = value;
								changed = true;
							}
						}
					}
					if (changed)
					{
						data.SetDetailLayer(x0, z0, layer, map);
					}
				}
				EditorUtility.SetDirty(data);
			}
		}

		/// <summary>
		/// A crop's detail layer on a tile for a field in <paramref name="biome"/>: the tile's prototype of the crop for that
		/// biome, else one the field adds and records for it (a copy of the tile's prototype of the crop for another biome, or a
		/// new one copying a grass prototype's settings when the tile has none).
		/// </summary>
		private static int CropLayer(Terrain terrain, GameObject crop, BiomeTemplate biome)
		{
			// A crop the detail scatter draws (enset, tea, vines, cabbages: no blades; 2026-10-18, I2j-b) takes a scattered plant's prototype.
			if (!BiomeGrassDefaults.IsBlade(crop.name))
			{
				return PointOfInterestHerbGrove.LayersOf(terrain, crop, biome).Own;
			}
			foreach (DetailPrototype prototype in terrain.terrainData.detailPrototypes)
			{
				if (prototype?.prototype != null && prototype.prototype.name == crop.name)
				{
					crop = prototype.prototype;
					break;
				}
			}
			return PointOfInterestHerbGrove.SiteLayers(terrain, crop, biome, NewWheat).Own;
		}

		/// <summary>
		/// The ridges' frame of a field (pure): the ridges run its length, so across them is its short side; the band is its
		/// sown width across them (inside the headland), and <paramref name="across"/> how far a point at (<paramref name="u"/>,
		/// <paramref name="v"/>) along the field's own axes lies across the band from its edge.
		/// </summary>
		public static float RidgeBand(Vector2 size, float u, float v, out float across)
		{
			bool alongX = size.x >= size.y;
			float band = Mathf.Max(0f, (alongX ? size.y : size.x) - 2f * Headland);
			across = (alongX ? v : u) + 0.5f * band;
			return band;
		}

		/// <summary>
		/// A sown texel's crop coverage on a ridged field (pure): the crop's coverage times the share of the texel, a
		/// <paramref name="texel"/> metres wide across the ridges, that lies on a ridge (<see cref="BiomeFieldSurfaces.SownShare"/>);
		/// the whole coverage on a flat field.
		/// </summary>
		public static int SownCoverage(BiomeFieldSurface surface, Vector2 size, float u, float v, float texel)
		{
			if (!BiomeFieldSurfaces.Ridged(surface))
			{
				return WheatCoverage;
			}
			float band = RidgeBand(size, u, v, out float across);
			float share = BiomeFieldSurfaces.SownShare(surface, band, across - 0.5f * texel, across + 0.5f * texel);
			return Mathf.RoundToInt(WheatCoverage * share);
		}

		private static int SownCoverage(BiomeFieldSurface surface, Field field, float u, float v, float texel) => SownCoverage(surface, field.Size, u, v, texel);

		/// <summary>The share of a rowed crop's spacing each row takes (2026-10-18, I2j-b): a clipped tea row 1.8 m apart is about a metre thick.</summary>
		public const float RowShare = 0.6f;

		/// <summary>
		/// How much of a texel <paramref name="texel"/> metres across at (<paramref name="u"/>, <paramref name="v"/>) along a field's own
		/// axes a crop covers (pure; 2026-10-18, I2j-b): on a terraced field (<paramref name="terraces"/>) its benches' sown ground
		/// (sampled four times in the texel round <paramref name="world"/>), else the ridges' share (<see cref="BiomeFieldSurfaces.SownShare"/>),
		/// a garden's beds along (<see cref="BiomeFieldSurfaces.BedShareAlong"/>) and corn hills (<see cref="BiomeFieldSurfaces.HillShare"/>);
		/// times its rows' share where it is sown in rows <paramref name="rows"/> apart (along the field, or along each bench).
		/// </summary>
		public static float CropShare(BiomeFieldSurface surface, Vector2 size, float u, float v, float texel, float rows, PointOfInterestFieldTerraces.Plan terraces, Vector2 world)
		{
			float share;
			float across;
			if (terraces != null && terraces.Terraced)
			{
				int on = 0;
				for (int k = 0; k < 4; k++)
				{
					float dx = ((k & 1) - 0.5f) * 0.5f * texel, dz = ((k >> 1) - 0.5f) * 0.5f * texel;
					on += terraces.Sown(world.x + dx, world.y + dz, Headland) ? 1 : 0;
				}
				share = on / 4f;
				across = terraces.AcrossBench(world.x, world.y);
			}
			else
			{
				float band = RidgeBand(size, u, v, out across);
				share = BiomeFieldSurfaces.SownShare(surface, band, across - 0.5f * texel, across + 0.5f * texel);
				float along = (size.x >= size.y ? u : v) + 0.5f * Mathf.Max(size.x, size.y) - Headland;
				share *= BiomeFieldSurfaces.BedShareAlong(surface, along - 0.5f * texel, along + 0.5f * texel);
				if (surface == BiomeFieldSurface.CornHills)
				{
					share *= BiomeFieldSurfaces.HillShare(u, v, texel);
				}
			}
			if (rows > 0f)
			{
				share *= RowCover(across, rows, texel);
			}
			return share;
		}

		/// <summary>How much of a stretch a texel wide at <paramref name="across"/> metres lies in a row of a crop sown <paramref name="rows"/> apart (pure): rows <see cref="RowShare"/> of the spacing wide, centred on each spacing's middle.</summary>
		public static float RowCover(float across, float rows, float texel)
		{
			if (!(rows > 0f))
			{
				return 1f;
			}
			const int samples = 8;
			int on = 0;
			for (int i = 0; i < samples; i++)
			{
				float a = across + texel * ((i + 0.5f) / samples - 0.5f);
				float t = a / rows - Mathf.Floor(a / rows);
				on += Mathf.Abs(t - 0.5f) <= 0.5f * RowShare ? 1 : 0;
			}
			return on / (float)samples;
		}

		/// <summary>
		/// How far a carved field raises the ground at world (<paramref name="x"/>, <paramref name="z"/>) over the ground it was
		/// laid on, metres (pure): its ridges' profile across them (<see cref="BiomeFieldSurfaces.CarvedHeight"/>, averaged over
		/// <paramref name="footprint"/> metres, one heightmap sample's ground) in the frame its crop is sown in (<see cref="RidgeBand"/>),
		/// each ridge dying into the headland at its ends (<see cref="BiomeFieldSurfaces.RidgeEnd"/>). Only raised: the furrows lie
		/// at the ground's own level, as do the headland and the fence line, so the field meets the ground round it with no step.
		/// 0 outside the sown band and for a surface not carved.
		/// </summary>
		public static float CarveRaise(Vector2 centre, float yaw, Vector2 size, BiomeFieldSurface surface, float x, float z, float footprint)
		{
			if (!BiomeFieldSurfaces.Carved(surface))
			{
				return 0f;
			}
			float r = yaw * Mathf.Deg2Rad;
			var axisX = new Vector2(Mathf.Cos(r), -Mathf.Sin(r));
			var axisZ = new Vector2(Mathf.Sin(r), Mathf.Cos(r));
			var p = new Vector2(x, z) - centre;
			float u = Vector2.Dot(p, axisX), v = Vector2.Dot(p, axisZ);
			if (Mathf.Abs(u) > 0.5f * size.x || Mathf.Abs(v) > 0.5f * size.y)
			{
				return 0f;
			}
			float band = RidgeBand(size, u, v, out float across);
			float along = size.x >= size.y ? u : v;
			float fromEnd = 0.5f * Mathf.Max(size.x, size.y) - Headland - Mathf.Abs(along);
			if (!(fromEnd > 0f) || across < 0f || across > band)
			{
				return 0f;
			}
			return BiomeFieldSurfaces.CarvedHeight(surface, band, across, footprint) * BiomeFieldSurfaces.RidgeEnd(fromEnd);
		}

		/// <summary>
		/// The bare furrows between a ridged field's ridges, as worn yard earth the path overlay draws (as a ploughed plot's): one
		/// straight strip the length of the sown field down each furrow, its own width, square at the headland. The ground
		/// shader draws them crisply, a texel finer than the crop's coverage, and the blades keep out of them.
		/// </summary>
		/// <remarks>
		/// Painted on every ridged field: the terrain's samples (about two metres apart) cannot hold a metre-wide furrow, nor a
		/// lazy bed's 1.8 m ridges at all. Ridge and furrow's broad ridges are carved into the ground besides (2026-10-18,
		/// <see cref="CarveRaise"/>, PointOfInterestFieldCarve), from the ground as it was before, so a repaint carves the same.
		/// </remarks>
		private static void AddFurrows(PointOfInterestSiteContext context, Field field, BiomeFieldSurface surface)
		{
			if (context.Points == null)
			{
				return;
			}
			bool alongX = field.Size.x >= field.Size.y;
			float band = RidgeBand(field.Size, 0f, 0f, out _);
			List<float> furrows = BiomeFieldSurfaces.Furrows(surface, band, out float width);
			if (furrows.Count == 0 || !(width > 0f))
			{
				return;
			}
			int id = 0;
			foreach (ScenePath existing in context.Points.Paths)
			{
				id = Math.Max(id, existing != null ? existing.Id : 0);
			}
			float r = field.Yaw * Mathf.Deg2Rad;
			var axisX = new Vector2(Mathf.Cos(r), -Mathf.Sin(r));
			var axisZ = new Vector2(Mathf.Sin(r), Mathf.Cos(r));
			Vector2 along = alongX ? axisX : axisZ, across = alongX ? axisZ : axisX;
			float reach = 0.5f * Mathf.Max(field.Size.x, field.Size.y) - Headland;
			float half = 0.5f * width;
			foreach (float centre in furrows)
			{
				Vector2 middle = field.Centre + across * (centre - 0.5f * band);
				Vector2 a = middle - along * reach, b = middle + along * reach;
				context.Points.Paths.Add(new ScenePath
				{
					Id = ++id,
					Class = ScenePathClass.Yard,
					Points = new[] { new Vector3(a.x, context.GroundAt(a.x, a.y), a.y), new Vector3(b.x, context.GroundAt(b.x, b.y), b.y) },
					HalfWidth = new[] { half, half },
					Wear = new[] { 0.85f, 0.85f },
					Surface = new[] { (byte)ScenePathSurface.Earth, (byte)ScenePathSurface.Earth },
					Flags = new[] { (byte)(ScenePathPointFlags.InSite | ScenePathPointFlags.Squared), (byte)(ScenePathPointFlags.InSite | ScenePathPointFlags.Squared) },
				});
			}
		}

		/// <summary>A crop's prototype for a tile that grows none of it, copying a grass prototype's settings.</summary>
		private static DetailPrototype NewWheat(DetailPrototype[] prototypes, GameObject wheat)
		{
			int grass = -1;
			for (int i = 0; i < prototypes.Length; i++)
			{
				GameObject prefab = prototypes[i].prototype;
				if (grass < 0 && prefab != null && prefab.name.StartsWith("Detail_Grass", StringComparison.Ordinal))
				{
					grass = i;
				}
			}
			DetailPrototype copy = grass >= 0 ? prototypes[grass] : new DetailPrototype { usePrototypeMesh = true, renderMode = DetailRenderMode.VertexLit, useInstancing = true };
			return new DetailPrototype
			{
				prototype = wheat,
				usePrototypeMesh = true,
				useInstancing = copy.useInstancing,
				renderMode = copy.renderMode,
				minWidth = 0.9f,
				maxWidth = 1.1f,
				minHeight = 0.9f,
				maxHeight = 1.2f,
				noiseSpread = copy.noiseSpread,
				healthyColor = Color.white,
				dryColor = Color.white,
				density = copy.density,
				alignToGround = copy.alignToGround,
				positionJitter = copy.positionJitter,
				useDensityScaling = copy.useDensityScaling,
			};
		}

		/// <summary>A ploughed plot: bare tilled earth over the whole field, as the path overlay draws worn ground.</summary>
		private static void AddPloughedGround(PointOfInterestSiteContext context, Field field)
		{
			if (context.Points == null)
			{
				return;
			}
			int id = 0;
			foreach (ScenePath existing in context.Points.Paths)
			{
				id = Math.Max(id, existing != null ? existing.Id : 0);
			}
			float r = field.Yaw * Mathf.Deg2Rad;
			bool alongX = field.Size.x >= field.Size.y;
			Vector2 axis = alongX ? new Vector2(Mathf.Cos(r), -Mathf.Sin(r)) : new Vector2(Mathf.Sin(r), Mathf.Cos(r));
			float shortest = Mathf.Min(field.Size.x, field.Size.y), longest = Mathf.Max(field.Size.x, field.Size.y);
			// Squared, end midpoint to end midpoint, half a metre inside the fence all round.
			Vector2 a = field.Centre - axis * (longest * 0.5f - 0.5f), b = field.Centre + axis * (longest * 0.5f - 0.5f);
			float half = shortest * 0.5f - 0.5f;
			context.Points.Paths.Add(new ScenePath
			{
				Id = id + 1,
				Class = ScenePathClass.Yard,
				Points = new[] { new Vector3(a.x, context.GroundAt(a.x, a.y), a.y), new Vector3(b.x, context.GroundAt(b.x, b.y), b.y) },
				HalfWidth = new[] { half, half },
				Wear = new[] { 0.9f, 0.9f },
				Surface = new[] { (byte)ScenePathSurface.Earth, (byte)ScenePathSurface.Earth },
				// The plot is ploughed to its fence's corners, not rounded off inside them.
				Flags = new[] { (byte)(ScenePathPointFlags.InSite | ScenePathPointFlags.Squared), (byte)(ScenePathPointFlags.InSite | ScenePathPointFlags.Squared) },
			});
		}

		/// <summary>
		/// The fence round a field: module by module along each side, a gap in the side nearest the settlement, its modules drawn
		/// from <paramref name="random"/>. <paramref name="dry"/> makes every draw and lays nothing: a field left out or shrunk off
		/// another site's keeps the site's sequence where it was for the fences after it (2026-10-18).
		/// </summary>
		/// <remarks>
		/// Joined up (2026-10-19: "fences aren't fully connecting in some cases"; PointOfInterestLinearFeatures.Joins): each module is laid
		/// between its two ends on the ground, pitched to the chord (it was laid level at its middle's ground, so on a slope its rails
		/// stepped at every post), round the field the same way so each corner takes exactly one module's post (one corner had none, the
		/// opposite two), a closing post wherever a run stops with no post (a module left in the water, a break, the gate's sides when
		/// it cannot stand), and the field gateway in the gate's gap, swung into the field. The draws are as they were, module by module.
		/// </remarks>
		private static void Fence(PointOfInterestSiteContext context, Field field, Vector2 site, bool broken, DeterministicRNG random, bool dry = false)
		{
			GameObject[] prefabs =
			{
				PointOfInterestPieceResolver.Resolve(new PointOfInterestPieceRequest("=FieldFence", "timber", 0)),
				PointOfInterestPieceResolver.Resolve(new PointOfInterestPieceRequest("=FieldFence", "timber", 1)),
			};
			if (prefabs[0] == null && prefabs[1] == null)
			{
				return;
			}
			const float module = 3f;
			float r = field.Yaw * Mathf.Deg2Rad;
			var axisX = new Vector2(Mathf.Cos(r), -Mathf.Sin(r));
			var axisZ = new Vector2(Mathf.Sin(r), Mathf.Cos(r));
			Vector2 toSite = (site - field.Centre).normalized;
			// Each side: its middle, its direction along, its length, and its outward normal.
			var sides = new[]
			{
				(field.Centre + axisZ * (field.Size.y * 0.5f), axisX, field.Size.x, axisZ),
				(field.Centre - axisZ * (field.Size.y * 0.5f), axisX, field.Size.x, -axisZ),
				(field.Centre + axisX * (field.Size.x * 0.5f), axisZ, field.Size.y, axisX),
				(field.Centre - axisX * (field.Size.x * 0.5f), axisZ, field.Size.y, -axisX),
			};
			int gateSide = 0;
			float best = float.NegativeInfinity;
			for (int s = 0; s < 4; s++)
			{
				float facing = Vector2.Dot(sides[s].Item4, toSite);
				if (facing > best)
				{
					best = facing;
					gateSide = s;
				}
			}
			var joins = dry ? null : new PointOfInterestLinearFeatures.Joins();
			GameObject gatePiece = dry ? null : StructureKit.Prefab("LinearGate", 0);
			for (int s = 0; s < 4; s++)
			{
				(Vector2 middle, Vector2 along, float length, Vector2 outward) = sides[s];
				int count = Mathf.Max(1, Mathf.RoundToInt(length / module));
				float step = length / count;
				int gate = s == gateSide ? count / 2 : -1;
				// Laid round the field with the field on the left (its outward normal on the right), whichever way the draws count.
				Vector2 round = Vector2.Dot(PointOfInterestLinearFeatures.Left(outward), along) > 0f ? along : -along;
				for (int k = 0; k < count; k++)
				{
					Vector2 p = middle + along * ((k + 0.5f) * step - length * 0.5f);
					Vector2 a = p - round * (step * 0.5f), b = p + round * (step * 0.5f);
					if (k == gate)
					{
						// The gateway in its gap, as wide as the gap; no draw, as before.
						if (joins != null && gatePiece != null && !context.IsWet(p.x, p.y, 0f))
						{
							joins.Gate(context, context.Palette, gatePiece, PointOfInterestLinearFeatures.Joint(context, a), PointOfInterestLinearFeatures.Joint(context, b),
								StructurePieces.GateModule);
						}
						continue;
					}
					if (broken && random.NextDouble() < 0.35)
					{
						continue;
					}
					int variant = random.NextDouble() < 0.5 || prefabs[1] == null ? 0 : 1;
					GameObject prefab = prefabs[variant] ?? prefabs[1];
					float tilt = broken ? (float)(random.NextDouble() - 0.5) * 20f : 0f;
					// After the draws, so a module left out of the water moves none of the others.
					if (dry || context.IsWet(p.x, p.y, 0f))
					{
						continue;
					}
					joins.Module(context, context.Palette, prefab, "FieldFence", variant, PointOfInterestLinearFeatures.Joint(context, a), PointOfInterestLinearFeatures.Joint(context, b),
						module, 0f, 1f, tilt);
				}
			}
			joins?.Close();
		}
	}
}
#endif
