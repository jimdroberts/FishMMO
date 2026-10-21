#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using FishMMO.Shared.Biomes;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Lays vines (2026-10-12: "vines and ivy in the world … Cave mouths, Ruins and old walls, Cliffs and outcrops,
	/// Trees") as baked props: over cave mouths (<see cref="DrapeCaveMouth(PointOfInterestSiteContext, Vector3, Vector3, Vector3, float, VineBiomes.Habitat, DeterministicRNG, Func{Vector3, bool})"/>,
	/// called by the cave's own mouth builder), off crags (<see cref="HangFromCrags"/>, from CliffPlacer), on the walls beside
	/// waterfalls (<see cref="DrapeFalls"/>, from FallLedges), up trees (<see cref="ClimbTrees"/>, from the tree bake) and over
	/// the walls of ruins (PointOfInterestVines). What goes where is
	/// <see cref="VineRules"/>; which biome grows what, its template's vines (<see cref="BiomeTemplateArt.Vines"/>).
	/// </summary>
	/// <remarks>
	/// <b>Visual only</b> (2026-10-12: "Moss and vines should be visual only, no colliders"). A vine prefab has no
	/// collider, so the prop bake gives it no collision (ScenePropBaker.PrototypeCollision finds none) and the NavMesh,
	/// built from the ground and the props' collision, never sees it: a curtain over a cave's mouth is walked through.
	/// </remarks>
	public static class VinePlacement
	{
		private static readonly Dictionary<string, GameObject> prefabs = new Dictionary<string, GameObject>();

		/// <summary>A spot's generated prefab, or null when the art has not been generated.</summary>
		public static GameObject PrefabFor(in VineSpot spot)
		{
			string name = spot.Prefab;
			// Looked up again when the cached one went Unity-null (an asset refresh releases it).
			if (!prefabs.TryGetValue(name, out GameObject prefab) || prefab == null)
			{
				prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ProceduralArtCatalogue.PrefabPath(name));
				prefabs[name] = prefab;
			}
			return prefab;
		}

		/// <summary>A spot's rotation: turned to its heading, leant back by its tilt.</summary>
		public static Quaternion RotationOf(in VineSpot spot) => Quaternion.Euler(-spot.Tilt, spot.Yaw, 0f);

		/// <summary>The ground's normal at a point, from four heights half a metre round it.</summary>
		private static Vector3 GroundNormal(Func<float, float, float> groundAt, float x, float z)
		{
			float dx = groundAt(x + 0.5f, z) - groundAt(x - 0.5f, z);
			float dz = groundAt(x, z + 0.5f) - groundAt(x, z - 0.5f);
			return new Vector3(-dx, 1f, -dz).normalized;
		}

		/// <summary>A spot laid on the ground: at the ground's height, turned to its slope; false where it is too steep to lie on.</summary>
		private static bool OnGround(ref VineSpot spot, Func<float, float, float> groundAt, out Quaternion rotation)
		{
			rotation = RotationOf(in spot);
			if (!spot.OnGround || groundAt == null)
			{
				return true;
			}
			Vector3 n = GroundNormal(groundAt, spot.Position.x, spot.Position.z);
			if (n.y < 0.7f)
			{
				return false;
			}
			spot.Position.y = groundAt(spot.Position.x, spot.Position.z) - 0.02f;
			rotation = Quaternion.FromToRotation(Vector3.up, n) * Quaternion.Euler(0f, spot.Yaw, 0f);
			return true;
		}

		// ── Cave mouths ─────────────────────────────────────────

		/// <summary>
		/// Overgrows one cave mouth (world positions): curtains along the lintel from <paramref name="lintelLeft"/> to
		/// <paramref name="lintelRight"/> (the opening's top corners, where its roof meets the rock face) with headroom kept
		/// under the middle, longer at the sides and down both jambs, now and then a curtain across the whole opening;
		/// growth over the lintel and on the rock round the opening when <paramref name="solidAt"/> says where the rock is;
		/// spill over the ground at the jambs' feet, off the way in, the trodden ways and the water. How many props it laid.
		/// </summary>
		/// <param name="outward">Out of the cave (only its horizontal part is used).</param>
		/// <param name="openingHeight">From the floor to the middle of the lintel, metres.</param>
		/// <param name="habitat">The vines of the biome at the mouth, what grows there and how thickly (its template's,
		/// <see cref="BiomeTemplateArt.Vines"/>); null where none grows.</param>
		/// <param name="rng">The caller's seeded generator: the same draws for the same site on every cut.</param>
		/// <param name="solidAt">True inside rock (world); null to grow only the lintel, the jambs and the ground.</param>
		/// <param name="lintelAt">The lintel's height over a point between its ends (an arch's underside); null for a straight one.</param>
		public static int DrapeCaveMouth(PointOfInterestSiteContext context, Vector3 lintelLeft, Vector3 lintelRight, Vector3 outward, float openingHeight,
			VineBiomes.Habitat habitat, DeterministicRNG rng, Func<Vector3, bool> solidAt = null, Func<Vector3, float> lintelAt = null)
		{
			if (context == null || habitat == null)
			{
				return 0;
			}
			List<VineSpot> spots = VineRules.CaveMouth(lintelLeft, lintelRight, outward, openingHeight, habitat, rng, solidAt, context.GroundAt, out VineRules.CaveGrowth growth, lintelAt);
			var ways = context.Points != null && context.Points.Paths != null ? new PathExclusion(context.Points.Paths) : null;
			// The terrain keeps the hillside's heights under a cut hole: a ground piece seated there would hang in the opening.
			Func<float, float, bool> holed = context.Terrains != null ? PointOfInterestTerrain.HoleQuery(context.Terrains, context.Tiles) : null;
			Vector3 o = outward;
			o.y = 0f;
			o = o.sqrMagnitude > 1e-6f ? o.normalized : Vector3.forward;
			Vector3 mid = (lintelLeft + lintelRight) * 0.5f;
			if (lintelAt != null)
			{
				mid.y = lintelAt(mid);
			}
			Vector3 a = lintelRight - lintelLeft;
			a.y = 0f;
			float half = a.magnitude * 0.5f;
			a = a.sqrMagnitude > 1e-6f ? a.normalized : new Vector3(o.z, 0f, -o.x);
			int placed = 0, missing = 0;
			for (int i = 0; i < spots.Count; i++)
			{
				VineSpot spot = spots[i];
				if (spot.OnGround)
				{
					// Not in the way in (as wide as the opening, out a dozen metres), on a trodden way, in water or a stream.
					Vector3 d = spot.Position - mid;
					float along = Vector3.Dot(d, o), side = Mathf.Abs(Vector3.Dot(d, a));
					if (along > -0.6f && along < 12f && side < half * 0.7f)
					{
						continue;
					}
					if (ways != null && ways.Details(spot.Position.x, spot.Position.z))
					{
						continue;
					}
					if (holed != null && holed(spot.Position.x, spot.Position.z))
					{
						continue;
					}
					if (context.Water != null && context.Water.KindAt(spot.Position.x, spot.Position.z) != WaterKind.None)
					{
						continue;
					}
				}
				else if (solidAt != null && context.GroundAt != null && spot.Form == VineForm.Mat)
				{
					// A face is only a face with air in front of it: one the ground has buried is not grown on.
					Vector3 front = spot.Position + Quaternion.Euler(0f, spot.Yaw, 0f) * Vector3.forward * 0.4f + Vector3.up * 0.5f;
					if (context.GroundAt(front.x, front.z) > front.y)
					{
						continue;
					}
				}
				if (!OnGround(ref spot, context.GroundAt, out Quaternion rotation))
				{
					continue;
				}
				GameObject prefab = PrefabFor(in spot);
				if (prefab == null)
				{
					missing++;
					continue;
				}
				context.AddProp(prefab, spot.Position, rotation, spot.Scale);
				placed++;
			}
			if (placed > 0 || missing > 0)
			{
				PointOfInterestFeatureKit.Note(context, $"cave mouth vines: {placed} piece(s) of {growth.Species}"
					+ (growth.Curtained ? growth.Parted ? ", the opening curtained (parted)" : ", the opening curtained" : string.Empty)
					+ (missing > 0 ? $"; {missing} left out (no vine art: Generate Biome Art)" : string.Empty) + ".");
			}
			return placed;
		}

		/// <summary>
		/// <see cref="DrapeCaveMouth(PointOfInterestSiteContext, Vector3, Vector3, Vector3, float, VineBiomes.Habitat, DeterministicRNG, Func{Vector3, bool})"/>
		/// for one of a cave system's mouths, its lintel found where the hillside closes over the opening (<see cref="CaveLintel"/>),
		/// and its rock — the shell round the opening and the hillside it comes up through — from the system's rock field and the
		/// ground when <paramref name="ground"/> is given (<see cref="CaveShaper.GroundAsCut"/>: the ground and holes the cave was
		/// meshed against, or the shell is found where the mesh has none). Laid nothing (0) when no lintel was found.
		/// </summary>
		public static int DrapeCaveMouth(PointOfInterestSiteContext context, CaveSystem system, CaveMouth mouth, VineBiomes.Habitat habitat, DeterministicRNG rng, CaveGround ground = null)
		{
			if (system == null || context == null || context.GroundAt == null
				|| !CaveLintel(system, mouth, context.GroundAt, out Vector3 left, out Vector3 right, out Vector3 outward, out float height, ground))
			{
				return 0;
			}
			Func<Vector3, bool> solidAt = null;
			if (ground != null)
			{
				Vector3 origin = system.Origin;
				Func<float, float, float> groundAt = context.GroundAt;
				// The terrain's heights stand on under the cut holes, so under the ground is rock only outside the cave's voids.
				solidAt = p => system.Field(p - origin, ground) < 0f || (p.y < groundAt(p.x, p.z) && system.Void(p - origin) >= 0f);
			}
			// The tops set into the lip's rock, not touching its underside: the opening's height measured to them too.
			return DrapeCaveMouth(context, left, right, outward, height + Embed, habitat, rng, solidAt, p => LintelHeight(system, mouth, p) + Embed);
		}

		/// <summary>
		/// The underside of a cave mouth's arch over a world point of its lintel (<see cref="CaveLintel"/>): the roof the cave's
		/// field finds over the mouth's floor there, world height; the point's own height where there is none.
		/// </summary>
		public static float LintelHeight(CaveSystem system, CaveMouth mouth, Vector3 point)
		{
			Vector3 local = point - system.Origin;
			local.y = mouth.Floor.y;
			return PointOfInterestCaveMouths.RoofOver(system, local, 3.2f * mouth.Radius + 4f, out float roof) ? system.Origin.y + roof : point.y;
		}

		/// <summary>How far up into the lip's rock a cave curtain's top is set, metres: hung from the rock, not near it.</summary>
		private const float Embed = 0.15f;

		/// <summary>How far in from the brow a lintel looks for rock over its roof, metres.</summary>
		private const float SeatReach = 2f;

		/// <summary>
		/// A cave mouth's lintel (world): across the brow of the opening, where the hillside comes down to the crown of the
		/// passage's roof — walked in along the way in, from out in front, until the ground (<paramref name="groundAt"/>: the
		/// terrain keeps the hillside's heights under the holes cut for the mouth) stands as high as the roof the cave's field
		/// finds over the floor (PointOfInterestCaveMouths.RoofOver). Its two ends (where the arch comes down to a metre and a
		/// half over the floor; <see cref="LintelHeight"/> is its underside between them), the way out and the opening's height
		/// under its middle. With <paramref name="ground"/> (<see cref="CaveShaper.GroundAsCut"/>) it is set back from the brow
		/// to where the meshed rock stands over the roof: the mesh rounds its lip back from the hillside, and a lintel at the
		/// brow itself hung its curtains just short of it (2026-10-19). False when no roof stands over the mouth or the
		/// hillside never closes over it.
		/// </summary>
		/// <remarks>
		/// It used to walk OUT from the mouth's floor point for as long as the field still had a roof, which found the front of
		/// the rock outcrop a mouth was set in. With the outcrop gone and the way in graded (2026-10-18), the roof out there is
		/// only the rounded end of the first passage standing in the open air, so the curtains hung from nothing, metres in
		/// front of the hole (2026-10-19).
		/// </remarks>
		public static bool CaveLintel(CaveSystem system, CaveMouth mouth, Func<float, float, float> groundAt, out Vector3 left, out Vector3 right, out Vector3 outward, out float openingHeight,
			CaveGround ground = null)
		{
			left = right = outward = Vector3.zero;
			openingHeight = 0f;
			float r = mouth.Yaw * Mathf.Deg2Rad;
			var o = new Vector3(Mathf.Sin(r), 0f, Mathf.Cos(r));
			var across = new Vector3(o.z, 0f, -o.x);
			float reach = 3.2f * mouth.Radius + 4f;
			if (!Brow(system, mouth.Floor, o, groundAt, reach, 1.5f * mouth.Radius + 2f, -(3f * mouth.Radius + 8f), out float brow, out float middle))
			{
				return false;
			}
			float seat = brow;
			if (ground != null)
			{
				// In from the brow until the meshed rock stands just over the roof: the lip itself.
				for (float d = brow; d >= brow - SeatReach; d -= 0.1f)
				{
					Vector3 p = mouth.Floor + o * d;
					if (PointOfInterestCaveMouths.RoofOver(system, p, reach, out float roof) && system.Field(new Vector3(p.x, roof + Embed, p.z), ground) < 0f)
					{
						seat = d;
						middle = roof;
						break;
					}
				}
			}
			// Across, at the crown's lip: the run of roof that stands at least a metre and a half over the floor. Not each side
			// at its own brow: the hillside meets the arch's low flanks further out than its crown, and a lintel drawn between
			// those ends ran out in front of the crown, in the air again.
			Vector3 at = mouth.Floor + o * seat;
			float uLeft = 0f, uRight = 0f, yLeft = middle, yRight = middle;
			for (int side = -1; side <= 1; side += 2)
			{
				for (float u = 0.25f; u <= 1.6f * mouth.Radius + 1f; u += 0.25f)
				{
					if (!PointOfInterestCaveMouths.RoofOver(system, at + across * (side * u), reach, out float roof) || roof - mouth.Floor.y < 1.5f)
					{
						break;
					}
					if (side < 0)
					{
						uLeft = u;
						yLeft = roof;
					}
					else
					{
						uRight = u;
						yRight = roof;
					}
				}
			}
			if (uLeft + uRight < 0.5f)
			{
				return false;
			}
			left = system.Origin + new Vector3(at.x, 0f, at.z) - across * uLeft + Vector3.up * yLeft;
			right = system.Origin + new Vector3(at.x, 0f, at.z) + across * uRight + Vector3.up * yRight;
			outward = o;
			openingHeight = middle - mouth.Floor.y;
			return true;
		}

		/// <summary>
		/// Where, along a line through <paramref name="start"/> (system frame) in the way out <paramref name="o"/>, the hillside
		/// closes over the cave: walked in from <paramref name="from"/> to <paramref name="to"/> (metres along <paramref name="o"/>,
		/// negative in), the first point where the ground stands as high as the roof over the floor; how far along it is and the
		/// roof's height there (system frame). Between two steps it is interpolated.
		/// </summary>
		private static bool Brow(CaveSystem system, Vector3 start, Vector3 o, Func<float, float, float> groundAt, float reach, float from, float to, out float along, out float roofY)
		{
			along = roofY = 0f;
			const float step = 0.25f;
			Vector3 origin = system.Origin;
			bool open = false;
			float lastD = 0f, lastGap = 0f, lastRoof = 0f;
			for (float d = from; d >= to; d -= step)
			{
				Vector3 p = start + o * d;
				if (!PointOfInterestCaveMouths.RoofOver(system, p, reach, out float roof))
				{
					// No passage under this point (or no roof over it): nothing to close over yet.
					open = false;
					continue;
				}
				float gap = roof - (groundAt(origin.x + p.x, origin.z + p.z) - origin.y);
				if (gap <= 0f)
				{
					if (open)
					{
						float t = lastGap / (lastGap - gap);
						along = Mathf.Lerp(lastD, d, t);
						roofY = Mathf.Lerp(lastRoof, roof, t);
					}
					else
					{
						// Closed from the first roof on: the hillside stands over the passage where it starts.
						along = d;
						roofY = roof;
					}
					return true;
				}
				open = true;
				lastD = d;
				lastGap = gap;
				lastRoof = roof;
			}
			return false;
		}

		// ── Crags ───────────────────────────────────────────────

		/// <summary>
		/// Curtains hung off cliff crags (CliffPlacer, before its props are written): a share of the crags in each biome that
		/// grows vines (<see cref="VineBiomes.Habitat.Cliffs"/>, seeded by the crag's place), each with one to three curtains
		/// off its near-vertical edges facing downhill, as long as the drop under them (<see cref="VineRules.CragCurtains"/>).
		/// Talus and backdrop walls carry none. Added to the cliffs' own props, so a re-placing clears them with the rocks.
		/// </summary>
		/// <param name="meshOf">The crag's seating (collision-level) mesh, in its own frame; null to skip it.</param>
		public static int HangFromCrags(List<PlacedCliffRock> rocks, Func<PlacedCliffRock, MeshBuilder> meshOf, SceneBiomeField field, ICliffRockGround ground,
			uint seed, List<ScenePropSet.Prototype> prototypes, List<ScenePropSet.Prop> props, List<string> notes)
		{
			if (rocks == null || field == null || meshOf == null || prototypes == null || props == null)
			{
				return 0;
			}
			var scratch = new float[Mathf.Max(1, field.Biomes.Count)];
			VineBiomes.Habitat[] habitats = HabitatsOf(field);
			var prototypeOf = new Dictionary<GameObject, int>();
			float GroundAt(float x, float z) => ground != null && ground.TryHeight(x, z, out float h) ? h : float.NegativeInfinity;
			int placed = 0, crags = 0, missing = 0;
			var positions = new List<Vector3>();
			foreach (PlacedCliffRock rock in rocks)
			{
				if (rock.Talus || rock.Backdrop)
				{
					continue;
				}
				int b = field.DominantIndexAt(rock.Position.x, rock.Position.z, scratch);
				VineBiomes.Habitat habitat = b >= 0 && b < habitats.Length ? habitats[b] : null;
				if (habitat == null || habitat.Cliffs <= 0f || VineRules.Hash(rock.Position, (int)(seed & 0x7FFFFFFF) ^ 0x51C) >= habitat.Cliffs * 0.5f)
				{
					continue;
				}
				MeshBuilder mesh = meshOf(rock);
				if (mesh == null || mesh.VertexCount == 0)
				{
					continue;
				}
				positions.Clear();
				foreach (Vector3 p in mesh.Positions)
				{
					positions.Add(rock.World(p));
				}
				var triangles = new List<int>();
				foreach (List<int> sub in mesh.Submeshes)
				{
					triangles.AddRange(sub);
				}
				// Downhill: the way the ground falls under the crag.
				Vector3 c = rock.Position;
				float gx = GroundAt(c.x + 3f, c.z) - GroundAt(c.x - 3f, c.z), gz = GroundAt(c.x, c.z + 3f) - GroundAt(c.x, c.z - 3f);
				if (float.IsNaN(gx) || float.IsInfinity(gx) || float.IsNaN(gz) || float.IsInfinity(gz) || gx * gx + gz * gz < 1e-4f)
				{
					continue;
				}
				var downhill = new Vector3(-gx, 0f, -gz);
				var rng = new DeterministicRNG(ProceduralNoise.SeedFor($"cragvine/{Mathf.RoundToInt(c.x * 4f)}/{Mathf.RoundToInt(c.z * 4f)}", (int)seed));
				string species = habitat.Pick(rng.NextFloat());
				List<VineSpot> spots = VineRules.CragCurtains(positions, triangles, downhill, (x, z) => GroundAt(x, z), species, rng, rng.Range(1, 4));
				if (spots.Count == 0)
				{
					continue;
				}
				crags++;
				foreach (VineSpot spot in spots)
				{
					GameObject prefab = PrefabFor(in spot);
					if (prefab == null)
					{
						missing++;
						continue;
					}
					if (!prototypeOf.TryGetValue(prefab, out int prototype))
					{
						prototype = prototypes.Count;
						prototypes.Add(new ScenePropSet.Prototype { Prefab = prefab, Layer = -1 });
						prototypeOf[prefab] = prototype;
					}
					props.Add(new ScenePropSet.Prop { Prototype = prototype, Position = spot.Position, Rotation = RotationOf(in spot), Scale = spot.Scale });
					placed++;
				}
			}
			notes?.Add($"Cliffs: {placed:N0} vine curtain(s) hung off {crags:N0} crag(s){(missing > 0 ? $"; {missing:N0} left out (no vine art: Generate Biome Art)" : string.Empty)}.");
			return placed;
		}

		// ── Waterfalls ──────────────────────────────────────────

		/// <summary>
		/// Vines on the wet walls beside and round each waterfall (FallLedges.Place, before its props are written): mats on the
		/// faces flanking the curtain and round the plunge pool and curtains hung from the walls' rims, in the biomes that grow
		/// vines, as thickly as <see cref="VineRules.FallShare"/> says (<see cref="VineRules.FallWalls"/>). Seeded by the river
		/// and the fall's lip, so a re-cut grows the same. The walls are the finished ground and the fall's own ledges and
		/// overhang (<paramref name="rocks"/>), so no vine is laid through one. Added to the falls' own props, so a re-placing
		/// clears them with the rock; no collider, so no collision.
		/// </summary>
		/// <param name="groundAt">The finished ground at (east, north), scene metres.</param>
		/// <param name="keptOut">Ground no vine is laid on (under a bridge, in a riverside site's footprint); null for none.</param>
		public static int DrapeFalls(SceneWater water, List<FallLedge> rocks, Func<float, float, float> groundAt, SceneBiomeField field, uint seed,
			Func<float, float, bool> keptOut, List<ScenePropSet.Prototype> prototypes, List<ScenePropSet.Prop> props, List<string> notes)
		{
			if (water == null || groundAt == null || field == null || prototypes == null || props == null)
			{
				return 0;
			}
			var settings = new FallLedgeSettings();
			var scratch = new float[Mathf.Max(1, field.Biomes.Count)];
			VineBiomes.Habitat[] habitats = HabitatsOf(field);
			var prototypeOf = new Dictionary<GameObject, int>();
			for (int p = 0; p < prototypes.Count; p++)
			{
				if (prototypes[p].Prefab != null)
				{
					prototypeOf[prototypes[p].Prefab] = p;
				}
			}
			int placed = 0, falls = 0, missing = 0;
			var boxes = new List<(Vector3 centre, Quaternion inverse, Vector3 half)>();
			foreach (RiverPath river in water.Rivers)
			{
				foreach (FallLedges.FallSpan span in FallLedges.FindFalls(river, settings.MinDropMetres, settings.PoolLevelMetres))
				{
					if (!FallFrameOf(river, span, out VineRules.FallFrame frame))
					{
						continue;
					}
					int b = field.DominantIndexAt(frame.Lip.x, frame.Lip.z, scratch);
					VineBiomes.Habitat habitat = b >= 0 && b < habitats.Length ? habitats[b] : null;
					if (VineRules.FallShare(habitat) <= 0f)
					{
						continue;
					}
					// The fall's own ledges and overhang stand out of its face: rock as much as the ground is.
					boxes.Clear();
					if (rocks != null)
					{
						foreach (FallLedge rock in rocks)
						{
							if (rock.Kind != FallRockKind.LipBoulder && rock.River == river.Id && rock.Lip == span.Lip)
							{
								boxes.Add((rock.Position, Quaternion.Inverse(rock.Rotation), rock.Size * 0.5f));
							}
						}
					}
					bool SolidAt(Vector3 p)
					{
						if (p.y < groundAt(p.x, p.z))
						{
							return true;
						}
						foreach ((Vector3 centre, Quaternion inverse, Vector3 half) in boxes)
						{
							Vector3 local = inverse * (p - centre);
							if (Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z)
							{
								return true;
							}
						}
						return false;
					}
					var rng = new DeterministicRNG(ProceduralNoise.SeedFor($"fallvine/{river.Id}/{span.Lip}", (int)seed));
					List<VineSpot> spots = VineRules.FallWalls(in frame, habitat, rng, SolidAt, water.SurfaceAt, out _);
					int before = placed;
					foreach (VineSpot spot in spots)
					{
						if (keptOut != null && keptOut(spot.Position.x, spot.Position.z))
						{
							continue;
						}
						GameObject prefab = PrefabFor(in spot);
						if (prefab == null)
						{
							missing++;
							continue;
						}
						if (!prototypeOf.TryGetValue(prefab, out int prototype))
						{
							prototype = prototypes.Count;
							prototypes.Add(new ScenePropSet.Prototype { Prefab = prefab, Layer = -1 });
							prototypeOf[prefab] = prototype;
						}
						props.Add(new ScenePropSet.Prop { Prototype = prototype, Position = spot.Position, Rotation = RotationOf(in spot), Scale = spot.Scale });
						placed++;
					}
					if (placed > before)
					{
						falls++;
					}
				}
			}
			notes?.Add($"Waterfalls: {placed:N0} vine(s) on the walls of {falls:N0} fall(s){(missing > 0 ? $"; {missing:N0} left out (no vine art: Generate Biome Art)" : string.Empty)}.");
			return placed;
		}

		/// <summary>
		/// A fall as <see cref="VineRules.FallWalls"/> takes it: the lip's middle on the water and the way the water leaves it
		/// (the lip to the point after, as FallLedges reads it), the lip's width, the pool's level, how far out the water may
		/// land (the plunge point, or its throw if that is further), and the pool's middle and radius as the runtime sizes the
		/// pool (InlandWaterRenderer.FindFalls). False when the lip has no direction.
		/// </summary>
		public static bool FallFrameOf(RiverPath river, FallLedges.FallSpan span, out VineRules.FallFrame frame)
		{
			frame = default;
			int lip = span.Lip, foot = span.Foot;
			int next = Math.Min(foot, lip + 1);
			float tx = river.X[next] - river.X[lip], tz = river.Z[next] - river.Z[lip];
			float tl = Mathf.Sqrt(tx * tx + tz * tz);
			if (tl < 1e-4f)
			{
				return false;
			}
			tx /= tl;
			tz /= tl;
			float width = Mathf.Max(1f, river.Width[lip]);
			float poolWidth = width;
			for (int k = lip; k <= foot; k++)
			{
				poolWidth = Mathf.Max(poolWidth, river.Width[k]);
			}
			float drop = span.Drop;
			float radius = Mathf.Max(3f, 0.9f * poolWidth + 0.35f * drop);
			int plunge = Mathf.Clamp(span.Plunge, lip, foot);
			float plungeAlong = (river.X[plunge] - river.X[lip]) * tx + (river.Z[plunge] - river.Z[lip]) * tz;
			float q = river.Discharge != null && lip < river.Discharge.Length ? river.Discharge[lip] / width : 1f;
			float thrown = VineRules.FallThrow(q, drop);
			// The basin's middle: the plunge point, or out under the falling water where it is thrown well clear of the face.
			float basin = Mathf.Clamp(Mathf.Max(plungeAlong, 0.6f * thrown), 1.5f, 0.8f * radius + 1f);
			var lipPoint = new Vector3(river.X[lip], river.Surface[lip], river.Z[lip]);
			var downstream = new Vector3(tx, 0f, tz);
			frame = new VineRules.FallFrame
			{
				Lip = lipPoint,
				Downstream = downstream,
				Width = width,
				Pool = river.Surface[foot],
				Landing = Mathf.Max(plungeAlong, thrown),
				Basin = lipPoint + downstream * basin,
				PoolRadius = radius,
			};
			return true;
		}

		// ── Trees ───────────────────────────────────────────────

		/// <summary>
		/// Climbers on the scatter's trees as they are baked into props (ScenePropBaker.BakeTerrainTrees): each tree a vine
		/// climbs (ProceduralArtCatalogue.VineHosts) carries one where its biome's share for trees says (seeded by where it
		/// stands), as a prop with the tree's own place, turn and scale — the climber is built on that tree's trunk and the
		/// vegetation shader varies the two alike, so the ivy stays on the bark. No collision (the prefab has no collider).
		/// </summary>
		public static int ClimbTrees(Scene scene, List<ScenePropSet.Prototype> prototypes, List<ScenePropSet.Prop> props, List<string> notes)
		{
			if (prototypes == null || props == null || props.Count == 0)
			{
				return 0;
			}
			Func<Vector3, VineBiomes.Habitat> habitatAt = HabitatLookup(scene);
			if (habitatAt == null)
			{
				notes?.Add("Trees: no climbers (the scene has no biome map yet).");
				return 0;
			}
			// Each tree prototype's species, and the vines it carries.
			int originalPrototypes = prototypes.Count;
			var hostOf = new string[originalPrototypes];
			var vinesOf = new string[originalPrototypes][];
			for (int p = 0; p < originalPrototypes; p++)
			{
				GameObject prefab = prototypes[p].Prefab;
				string tree = prefab != null && prefab.name.StartsWith(ProceduralArtCatalogue.TreePrefab(string.Empty), StringComparison.Ordinal)
					? prefab.name.Substring(ProceduralArtCatalogue.TreePrefab(string.Empty).Length) : null;
				hostOf[p] = tree;
				vinesOf[p] = tree != null ? ProceduralArtCatalogue.VinesOn(tree) : Array.Empty<string>();
			}
			var climberOf = new Dictionary<string, int>();
			int count = props.Count, placed = 0, missing = 0;
			for (int i = 0; i < count; i++)
			{
				ScenePropSet.Prop tree = props[i];
				if (tree.Prototype < 0 || tree.Prototype >= originalPrototypes || vinesOf[tree.Prototype].Length == 0)
				{
					continue;
				}
				if (!VineRules.Climbs(habitatAt(tree.Position), vinesOf[tree.Prototype], tree.Position, out string species, out int variant))
				{
					continue;
				}
				string name = ProceduralArtCatalogue.VinePrefab(ProceduralArtCatalogue.VineClimberName(species, hostOf[tree.Prototype], variant));
				if (!climberOf.TryGetValue(name, out int prototype))
				{
					var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ProceduralArtCatalogue.PrefabPath(name));
					prototype = -1;
					if (prefab != null)
					{
						prototype = prototypes.Count;
						// Drawn on the layer its tree is.
						prototypes.Add(new ScenePropSet.Prototype { Prefab = prefab, Layer = prototypes[tree.Prototype].Layer });
					}
					climberOf[name] = prototype;
				}
				if (prototype < 0)
				{
					missing++;
					continue;
				}
				props.Add(new ScenePropSet.Prop { Prototype = prototype, Position = tree.Position, Rotation = tree.Rotation, Scale = tree.Scale });
				placed++;
			}
			notes?.Add($"Trees: {placed:N0} carry ivy, creeper or lianas{(missing > 0 ? $"; {missing:N0} left bare (no climber art: Generate Biome Art)" : string.Empty)}.");
			return placed;
		}

		/// <summary>Each of a scene's biomes' vines (its template's, <see cref="BiomeTemplateArt.Vines"/>), by the field's biome index.</summary>
		private static VineBiomes.Habitat[] HabitatsOf(SceneBiomeField field)
		{
			var habitats = new VineBiomes.Habitat[field.Biomes.Count];
			for (int i = 0; i < habitats.Length; i++)
			{
				habitats[i] = BiomeTemplateArt.Vines(field.Biomes[i]);
			}
			return habitats;
		}

		/// <summary>
		/// The vines' habitat under a world position, from the scene's biome map: the one in the folder the bake writes to (a
		/// cut or a repaint), else the scene settings'. Each id the map holds is its template's (the registry's, else the
		/// project's, so it answers in a batch run whose biome registry is empty), and the vines its template's. Null with no map.
		/// </summary>
		private static Func<Vector3, VineBiomes.Habitat> HabitatLookup(Scene scene)
		{
			SceneBiomeMap map = null;
			if (!string.IsNullOrEmpty(ScenePropBaker.BakeFolder) && AssetDatabase.IsValidFolder(ScenePropBaker.BakeFolder))
			{
				foreach (string guid in AssetDatabase.FindAssets("t:" + nameof(SceneBiomeMap), new[] { ScenePropBaker.BakeFolder }))
				{
					map = AssetDatabase.LoadAssetAtPath<SceneBiomeMap>(AssetDatabase.GUIDToAssetPath(guid));
					if (map != null)
					{
						break;
					}
				}
			}
			if (map == null && scene.IsValid())
			{
				foreach (GameObject root in scene.GetRootGameObjects())
				{
					WorldSceneSettings settings = root.GetComponentInChildren<WorldSceneSettings>(true);
					if (settings != null && settings.BiomeMap != null)
					{
						map = settings.BiomeMap;
						break;
					}
				}
			}
			if (map == null)
			{
				return null;
			}
			// Each biome's vines once (its template's, BiomeTemplateArt.Vines), by the id the map holds: the registry's template,
			// else the project's of that id (a batch run's registry may be empty).
			var byId = new Dictionary<int, VineBiomes.Habitat>();
			return p =>
			{
				int id = map.IDAt(p);
				if (id == 0)
				{
					return null;
				}
				if (!byId.TryGetValue(id, out VineBiomes.Habitat h))
				{
					h = BiomeTemplateArt.Vines(BiomeTemplateArt.TemplateById(id));
					byId[id] = h;
				}
				return h;
			};
		}
	}
}
#endif
