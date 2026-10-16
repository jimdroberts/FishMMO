#if UNITY_EDITOR
using System.Collections.Generic;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.Celestial;
using FishMMO.Shared.NameGeneration;
using UnityEditor;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// The boats of a place on the water (2026-10-12: "We need docks (dockyards should be Points of interest), accurate
	/// ships and boats"): cogs and smacks lying at a port's piers and in its roads, a fishing camp's boats off its beach and
	/// one turned over on the shingle, a cove's ship at anchor.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Afloat, they ride the tide.</b> Each floating boat is its own object moved by <see cref="FloatingBoats"/> (a static
	/// prop would stand clear of the water at low tide and be swamped at high): a root the server keeps, with the hull's
	/// collision (<see cref="ScenePropBaker.PrototypeCollision"/>) on it so nobody swims through it, and the drawn hull under
	/// it as a client-only child. A boat hauled up the beach is an ordinary prop, with its collision.
	/// </para>
	/// <para>
	/// <b>Where one floats.</b> Over its own water (one level under the whole hull) at least its draft deep, more at sea where
	/// the tide goes out a metre or two; lying along a pier where the site has one, else along the shore; clear of every
	/// prop the sites have laid and of the other boats. The deepest-drawing go first. Where it would touch the bottom at low
	/// water, it settles there heeled over (<see cref="FloatingBoats.Moored.Floor"/>).
	/// </para>
	/// <para>Seeded by the site: a re-cut moors the same boats.</para>
	/// </remarks>
	[InitializeOnLoad]
	public sealed class PointOfInterestHarbour : IPointOfInterestSiteBuilder
	{
		static PointOfInterestHarbour() => PointOfInterestSiteBuilders.Register(new PointOfInterestHarbour());

		public const string ObjectName = "Boats";
		/// <summary>Water under the keel at mean sea level, metres, for the tide to go out a way before it grounds.</summary>
		private const float SeaKeelRoom = 1.2f;
		/// <summary>Water under the keel on a river or lake, which has no tide.</summary>
		private const float InlandKeelRoom = 0.25f;
		/// <summary>The most the water's level may differ under one hull, metres: more, and it straddles two waters.</summary>
		private const float OneWater = 0.3f;
		/// <summary>The room kept between a hull and anything else, metres.</summary>
		private const float Berth = 1.0f;
		private const int Tries = 48;

		public bool Handles(PointOfInterestRecord record)
		{
			switch (record.Kind)
			{
				case POIType.Port:
				case POIType.Dockyard:
				case POIType.FishingCamp:
				case POIType.PirateCove:
				case POIType.SmugglersCove:
				case POIType.Village:
				case POIType.Town:
				case POIType.City:
				case POIType.Capital:
				case POIType.StiltVillage:
					return true;
			}
			return false;
		}

		/// <summary>One boat wanted: the piece and its variant, afloat or hauled up.</summary>
		private readonly struct Wanted
		{
			public readonly string Piece;
			public readonly int Variant;
			public readonly bool Beached;

			public Wanted(string piece, int variant, bool beached = false)
			{
				Piece = piece;
				Variant = variant;
				Beached = beached;
			}
		}

		/// <summary>A hull's plan: the line of its keel and its half beam.</summary>
		private struct Hull
		{
			public Vector2 A, B;
			public float Radius;
		}

		/// <summary>A laid prop near the site, as a circle in plan.</summary>
		private struct Obstacle
		{
			public Vector2 Centre;
			public float Radius;
			public bool Pier;
			/// <summary>A pier's run (its forward axis), for a boat to lie along.</summary>
			public Vector2 Along;
		}

		public void Build(PointOfInterestSiteContext context)
		{
			PointOfInterestRecord record = context.Record;
			DeterministicRNG random = context.Random;
			if (context.GroundAt == null)
			{
				return;
			}
			float sea = SeaOf(context);
			List<Wanted> fleet = FleetFor(record, random);
			if (fleet.Count == 0)
			{
				return;
			}
			var site = new Vector2(record.Position.x, record.Position.z);
			bool harbour = record.Kind == POIType.Port || record.Kind == POIType.Dockyard;
			float reach = record.Radius + (harbour ? 160f : 60f);
			List<Obstacle> obstacles = ObstaclesNear(context, site, reach + 30f);
			bool piers = obstacles.Exists(o => o.Pier);

			var hulls = new List<Hull>();
			var moored = new List<FloatingBoats.Moored>();
			GameObject holder = null;
			int afloat = 0, beached = 0, unplaced = 0;
			foreach (Wanted wanted in fleet)
			{
				GameObject prefab = StructureKit.Prefab(wanted.Piece, wanted.Variant);
				if (prefab == null || !StructurePieces.TryHull(wanted.Piece, out float length, out float beam, out float draft))
				{
					unplaced++;
					continue;
				}
				if (wanted.Beached)
				{
					if (TryBeach(context, random, site, record.Radius + 30f, sea, length, beam, obstacles, hulls, out Vector3 at, out float yaw))
					{
						context.AddProp(prefab, at, Quaternion.Euler(0f, yaw, 0f), Vector3.one);
						beached++;
					}
					else
					{
						unplaced++;
					}
					continue;
				}
				if (!TryMoor(context, random, site, record.Radius, reach, sea, length, beam, draft, piers, obstacles, hulls, out FloatingBoats.Moored berth))
				{
					unplaced++;
					continue;
				}
				if (holder == null)
				{
					// Kept by the server: the boats' colliders are under it.
					holder = context.AddChild(ObjectName);
				}
				berth.Boat = Moor(prefab, wanted.Piece, wanted.Variant, holder.transform, berth.Position, Quaternion.Euler(0f, berth.Yaw, 0f), out berth.Visual, context.Palette);
				berth.Phase = random.NextFloat();
				moored.Add(berth);
				afloat++;
			}
			if (holder != null)
			{
				holder.AddComponent<FloatingBoats>().Configure(moored.ToArray(), null, null);
			}
			if (afloat + beached > 0 || harbour)
			{
				context.Notes?.Add($"{record.Name}: {afloat} boat(s) moored, {beached} hauled up{(unplaced > 0 ? $", {unplaced} found no water to lie in" : string.Empty)}.");
			}
		}

		/// <summary>The scene's sea level when its sea is water (never lava), else negative infinity.</summary>
		private static float SeaOf(PointOfInterestSiteContext context)
		{
			SceneGenerationRequest request = context.Request;
			if (request != null && request.Body != null && SurfaceLiquids.For(SolarSystemProfile.Resolve(request.Body), request.Body, out _) == SurfaceLiquid.Lava)
			{
				return float.NegativeInfinity;
			}
			return PropsFeature.SeaLevelOf(context);
		}

		/// <summary>What a kind keeps on its water.</summary>
		private static List<Wanted> FleetFor(PointOfInterestRecord record, DeterministicRNG random)
		{
			var fleet = new List<Wanted>();
			void Add(string piece, int min, int max, int variant = 0, bool beached = false)
			{
				int n = random.Range(min, max + 1);
				for (int i = 0; i < n; i++)
				{
					fleet.Add(new Wanted(piece, variant, beached));
				}
			}
			int size = Mathf.Clamp(record.SizeClass, 0, 2);
			switch (record.Kind)
			{
				case POIType.Port:
					Add("Cog", 1 + size, 2 + size);
					Add("Longship", 0, 1, 1);
					Add("FishingBoat", 1, 3);
					Add("Rowboat", 2, 4);
					break;
				case POIType.Dockyard:
					Add("Cog", 0, 1);
					Add("FishingBoat", 1, 1);
					Add("Rowboat", 1, 2);
					Add("Rowboat", 0, 1, 1, true);
					break;
				case POIType.FishingCamp:
					Add("FishingBoat", 0, 2);
					Add("Rowboat", 1, 2);
					Add("Rowboat", 1, 2, 1, true);
					break;
				case POIType.PirateCove:
					// At anchor: a cog's sail furled (v0), a longship's oars shipped and sail furled (v1).
					if (random.NextFloat() < 0.5f)
					{
						Add("Cog", 1, 1);
					}
					else
					{
						Add("Longship", 1, 1, 1);
					}
					Add("Rowboat", 1, 2);
					Add("Rowboat", 0, 1, 1, true);
					break;
				case POIType.SmugglersCove:
					Add("FishingBoat", 1, 1);
					Add("Rowboat", 1, 2);
					Add("Rowboat", 1, 1, 1, true);
					break;
				case POIType.City:
				case POIType.Capital:
					Add("Cog", 0, 1);
					Add("FishingBoat", 1, 2);
					Add("Rowboat", 1, 3);
					Add("Rowboat", 0, 2, 1, true);
					break;
				default:
					// A village or town by the water: its fishermen's boats, where it has water.
					Add("FishingBoat", 0, 1);
					Add("Rowboat", 1, 2);
					Add("Rowboat", 0, 2, 1, true);
					break;
			}
			// The deepest-drawing berths are the scarcest: they choose first.
			fleet.Sort((a, b) => Draft(b).CompareTo(Draft(a)));
			return fleet;
		}

		private static float Draft(Wanted wanted)
			=> wanted.Beached ? -1f : StructurePieces.TryHull(wanted.Piece, out _, out _, out float draft) ? draft : 0f;

		/// <summary>Every prop laid near the site so far (every site's: the sink is the scene's), as circles.</summary>
		private static List<Obstacle> ObstaclesNear(PointOfInterestSiteContext context, Vector2 site, float reach)
		{
			var near = new List<Obstacle>();
			PointOfInterestPropSink sink = context.Sink;
			if (sink == null)
			{
				return near;
			}
			var radius = new float[sink.Prototypes.Count];
			var pier = new bool[sink.Prototypes.Count];
			for (int p = 0; p < radius.Length; p++)
			{
				GameObject prefab = sink.Prototypes[p].Prefab;
				radius[p] = PlanRadius(prefab);
				pier[p] = prefab != null && StructurePieces.TryParsePrefab(prefab.name, out StructurePiece piece, out _) && piece.Id == "Pier";
			}
			foreach (ScenePropSet.Prop prop in sink.Props)
			{
				var at = new Vector2(prop.Position.x, prop.Position.z);
				if ((at - site).sqrMagnitude > reach * reach)
				{
					continue;
				}
				Vector3 forward = prop.Rotation * Vector3.forward;
				near.Add(new Obstacle
				{
					Centre = at,
					Radius = radius[prop.Prototype] * Mathf.Max(Mathf.Abs(prop.Scale.x), Mathf.Abs(prop.Scale.z)),
					Pier = pier[prop.Prototype],
					Along = new Vector2(forward.x, forward.z).normalized,
				});
			}
			return near;
		}

		private static readonly Dictionary<GameObject, float> planRadius = new Dictionary<GameObject, float>();

		/// <summary>The radius of a prefab's footprint in plan, metres: the half diagonal of its meshes' box across x and z.</summary>
		private static float PlanRadius(GameObject prefab)
		{
			if (prefab == null)
			{
				return 0f;
			}
			if (planRadius.TryGetValue(prefab, out float r))
			{
				return r;
			}
			Bounds box = default;
			bool any = false;
			foreach (MeshFilter filter in prefab.GetComponentsInChildren<MeshFilter>(true))
			{
				if (filter.sharedMesh == null)
				{
					continue;
				}
				Bounds local = filter.sharedMesh.bounds;
				Matrix4x4 m = prefab.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
				for (int c = 0; c < 8; c++)
				{
					Vector3 corner = local.center + Vector3.Scale(local.extents, new Vector3((c & 1) != 0 ? 1 : -1, (c & 2) != 0 ? 1 : -1, (c & 4) != 0 ? 1 : -1));
					Vector3 p = m.MultiplyPoint3x4(corner);
					if (!any)
					{
						box = new Bounds(p, Vector3.zero);
						any = true;
					}
					else
					{
						box.Encapsulate(p);
					}
				}
			}
			r = any ? new Vector2(Mathf.Max(Mathf.Abs(box.min.x), Mathf.Abs(box.max.x)), Mathf.Max(Mathf.Abs(box.min.z), Mathf.Abs(box.max.z))).magnitude : 1f;
			planRadius[prefab] = r;
			return r;
		}

		/// <summary>The water's surface over a point (river, lake, or sea over ground under it); negative infinity on dry land.</summary>
		private static float WaterAt(PointOfInterestSiteContext context, float x, float z, float sea)
		{
			float level = context.Water != null ? context.Water.SurfaceAt(x, z) : float.NegativeInfinity;
			if (!float.IsNegativeInfinity(sea) && context.GroundAt(x, z) < sea)
			{
				level = Mathf.Max(level, sea);
			}
			return level;
		}

		/// <summary>A heading, degrees, that lays a hull's length (its +x) along a direction in plan.</summary>
		private static float YawAlong(Vector2 direction) => Mathf.Atan2(-direction.y, direction.x) * Mathf.Rad2Deg;

		private static Vector2 AxisOf(float yaw)
		{
			float a = yaw * Mathf.Deg2Rad;
			return new Vector2(Mathf.Cos(a), -Mathf.Sin(a));
		}

		private static bool TryMoor(PointOfInterestSiteContext context, DeterministicRNG random, Vector2 site, float radius, float reach, float sea,
			float length, float beam, float draft, bool piers, List<Obstacle> obstacles, List<Hull> hulls, out FloatingBoats.Moored berth)
		{
			berth = default;
			float best = float.PositiveInfinity;
			for (int attempt = 0; attempt < Tries; attempt++)
			{
				float a = random.NextFloat() * Mathf.PI * 2f;
				float r = Mathf.Lerp(radius * 0.3f, reach, Mathf.Sqrt(random.NextFloat()));
				Vector2 at = site + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
				float level = WaterAt(context, at.x, at.y, sea);
				if (float.IsNegativeInfinity(level))
				{
					continue;
				}
				bool tidal = !float.IsNegativeInfinity(sea) && Mathf.Abs(level - sea) < OneWater;
				float need = draft + (tidal ? SeaKeelRoom : InlandKeelRoom);
				if (level - context.GroundAt(at.x, at.y) < need)
				{
					continue;
				}

				// Its heading: along the nearest pier where there is one at hand, else along the shore (across the water's slope).
				float yaw;
				float pierGap = float.PositiveInfinity;
				int nearestPier = -1;
				for (int o = 0; o < obstacles.Count; o++)
				{
					if (!obstacles[o].Pier)
					{
						continue;
					}
					float gap = Vector2.Distance(obstacles[o].Centre, at) - obstacles[o].Radius;
					if (gap < pierGap)
					{
						pierGap = gap;
						nearestPier = o;
					}
				}
				if (nearestPier >= 0 && pierGap < beam + 6f)
				{
					yaw = YawAlong(obstacles[nearestPier].Along) + (random.NextFloat() < 0.5f ? 0f : 180f);
				}
				else
				{
					float dx = Depth(context, at.x + 4f, at.y, level) - Depth(context, at.x - 4f, at.y, level);
					float dz = Depth(context, at.x, at.y + 4f, level) - Depth(context, at.x, at.y - 4f, level);
					var shore = new Vector2(-dz, dx);
					yaw = (shore.sqrMagnitude > 1e-4f ? YawAlong(shore.normalized) : random.NextFloat() * 360f)
						+ (random.NextFloat() < 0.5f ? 0f : 180f) + random.Range(-15f, 15f);
				}
				Vector2 axis = AxisOf(yaw);
				var hull = new Hull { A = at - axis * (length * 0.5f), B = at + axis * (length * 0.5f), Radius = beam * 0.5f };

				// Afloat along its whole length on the one water, and nowhere on the bottom but where the tide can leave it.
				bool fits = true;
				float floor = float.NegativeInfinity;
				for (int k = -2; k <= 2 && fits; k++)
				{
					for (int s = -1; s <= 1 && fits; s++)
					{
						Vector2 p = at + axis * (length * 0.45f * k / 2f) + new Vector2(-axis.y, axis.x) * (hull.Radius * 0.8f * s);
						float here = WaterAt(context, p.x, p.y, sea);
						float ground = context.GroundAt(p.x, p.y);
						float keel = draft * (s == 0 ? 1f : 0.55f) * (Mathf.Abs(k) == 2 ? 0.6f : 1f);
						fits = !float.IsNegativeInfinity(here) && Mathf.Abs(here - level) < OneWater && here - ground >= keel + (tidal ? 0.3f : InlandKeelRoom);
						if (s == 0)
						{
							floor = Mathf.Max(floor, ground + keel);
						}
					}
				}
				if (!fits || !Clear(hull, obstacles, hulls))
				{
					continue;
				}

				// The best: along a pier a metre or two off it; else the nearest to the site that floats it.
				float score = piers && pierGap < float.PositiveInfinity ? Mathf.Abs(pierGap - hull.Radius - 1.5f) + 0.01f * r : 0.05f * r;
				if (score < best)
				{
					best = score;
					berth = new FloatingBoats.Moored
					{
						Position = new Vector3(at.x, level, at.y),
						Yaw = yaw,
						Floor = tidal ? floor : float.NegativeInfinity,
						Heel = (random.NextFloat() < 0.5f ? -1f : 1f) * random.Range(5f, 11f),
						Length = length,
					};
				}
			}
			if (float.IsPositiveInfinity(best))
			{
				return false;
			}
			Vector2 berthAxis = AxisOf(berth.Yaw);
			var at2 = new Vector2(berth.Position.x, berth.Position.z);
			hulls.Add(new Hull { A = at2 - berthAxis * (berth.Length * 0.5f), B = at2 + berthAxis * (berth.Length * 0.5f), Radius = beam * 0.5f });
			return true;
		}

		private static float Depth(PointOfInterestSiteContext context, float x, float z, float level) => level - context.GroundAt(x, z);

		/// <summary>A hull clear of the laid props and the boats already moored.</summary>
		private static bool Clear(Hull hull, List<Obstacle> obstacles, List<Hull> hulls)
		{
			foreach (Obstacle o in obstacles)
			{
				// A prop's circle is its box's corner reach: a long pier module's is generous, so only most of it counts.
				if (DistanceToSegment(o.Centre, hull.A, hull.B) < hull.Radius + o.Radius * 0.7f + Berth * 0.5f)
				{
					return false;
				}
			}
			foreach (Hull other in hulls)
			{
				if (SegmentDistance(hull.A, hull.B, other.A, other.B) < hull.Radius + other.Radius + Berth)
				{
					return false;
				}
			}
			return true;
		}

		/// <summary>An upturned boat on the beach: dry above the highest tide, gently sloped, its bow to the water a few metres off.</summary>
		private static bool TryBeach(PointOfInterestSiteContext context, DeterministicRNG random, Vector2 site, float reach, float sea,
			float length, float beam, List<Obstacle> obstacles, List<Hull> hulls, out Vector3 at, out float yaw)
		{
			at = default;
			yaw = 0f;
			float highWater = float.IsNegativeInfinity(sea) ? float.NegativeInfinity : sea + 2.6f;
			for (int attempt = 0; attempt < Tries; attempt++)
			{
				float a = random.NextFloat() * Mathf.PI * 2f;
				Vector2 p = site + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * Mathf.Lerp(4f, reach, Mathf.Sqrt(random.NextFloat()));
				float ground = context.GroundAt(p.x, p.y);
				if (!float.IsNegativeInfinity(WaterAt(context, p.x, p.y, sea)) || ground < highWater
					|| (context.SlopeAt != null && context.SlopeAt(p.x, p.y) > 12f))
				{
					continue;
				}
				// The water near: the first wet point in eight directions within a dozen metres.
				Vector2 toWater = Vector2.zero;
				for (int d = 0; d < 8 && toWater == Vector2.zero; d++)
				{
					var dir = new Vector2(Mathf.Cos(d * Mathf.PI * 0.25f), Mathf.Sin(d * Mathf.PI * 0.25f));
					for (float t = 3f; t <= 12f; t += 1.5f)
					{
						Vector2 q = p + dir * t;
						if (!float.IsNegativeInfinity(WaterAt(context, q.x, q.y, sea)))
						{
							toWater = dir;
							break;
						}
					}
				}
				if (toWater == Vector2.zero)
				{
					continue;
				}
				float heading = YawAlong(toWater) + random.Range(-25f, 25f);
				Vector2 axis = AxisOf(heading);
				var hull = new Hull { A = p - axis * (length * 0.5f), B = p + axis * (length * 0.5f), Radius = beam * 0.5f };
				if (!Clear(hull, obstacles, hulls))
				{
					continue;
				}
				hulls.Add(hull);
				at = new Vector3(p.x, ground - 0.05f, p.y);
				yaw = heading;
				return true;
			}
			return false;
		}

		/// <summary>
		/// A moored boat's objects: a root with the piece's collision (one mesh collider, on its colliders' layer) that the
		/// server keeps, and under it the drawn hull, client-only, for <see cref="FloatingBoats"/> to bob. The sea lanes'
		/// ships are built the same way (PointOfInterestSeaTraffic).
		/// </summary>
		/// <remarks>The drawn hull is laid in <paramref name="palette"/> (its home biome's structure palette; null: the kit's own materials).</remarks>
		internal static Transform Moor(GameObject prefab, string piece, int variant, Transform parent, Vector3 position, Quaternion rotation, out Transform visual,
			FishMMO.Shared.Biomes.BiomeStructurePalette palette = null)
		{
			var root = new GameObject(prefab.name);
			root.transform.SetParent(parent, false);
			root.transform.SetPositionAndRotation(position, rotation);
			Mesh collision = ScenePropBaker.PrototypeCollision(prefab, out int layer);
			if (collision != null)
			{
				root.layer = layer;
				var collider = root.AddComponent<MeshCollider>();
				collider.convex = false;
				collider.sharedMesh = collision;
			}
			visual = Spawn(prefab, piece, variant, root.transform, position, rotation, palette);
			visual.gameObject.AddComponent<ClientOnlyObject>();
			return root.transform;
		}

		/// <summary>The water lid's child under a drawn hull (<see cref="AddWaterLid"/>).</summary>
		public const string WaterLidName = "Water Lid";

		/// <summary>
		/// A drawn hull's own object: the piece's renderers, unlinked from its prefab and with no collider, and over its open
		/// top its water lid (<see cref="AddWaterLid"/>), so the sea is not drawn inside it.
		/// </summary>
		internal static Transform Spawn(GameObject prefab, string piece, int variant, Transform parent, Vector3 position, Quaternion rotation,
			FishMMO.Shared.Biomes.BiomeStructurePalette palette = null)
		{
			GameObject boat = Object.Instantiate(prefab, position, rotation, parent);
			boat.name = prefab.name;
			StructurePaletteDressing.Dress(boat, prefab, palette);
			foreach (Collider collider in boat.GetComponentsInChildren<Collider>(true))
			{
				Object.DestroyImmediate(collider);
			}
			foreach (Transform t in boat.GetComponentsInChildren<Transform>(true))
			{
				GameObjectUtility.SetStaticEditorFlags(t.gameObject, 0);
			}
			AddWaterLid(boat.transform, piece, variant);
			return boat.transform;
		}

		/// <summary>
		/// A sheet across a floating hull's open top, just under its gunwale (<see cref="StructurePieces.TryWaterLid"/>),
		/// drawn by FishMMO/Water/Hull Lid: invisible, it marks the stencil where it is seen and the water passes skip
		/// those pixels, so no sea is drawn inside the planking (every hull's floor is under the waterline: they took on the sea). A child of the drawn hull, so it bobs, rolls and heels with it, and
		/// client-only with it. Its mesh is the scene's own (a few dozen vertices); no collider, no shadow.
		/// </summary>
		internal static void AddWaterLid(Transform hull, string piece, int variant)
		{
			if (!StructurePieces.TryWaterLid(piece, variant, out Vector3[] port, out Vector3[] starboard))
			{
				return;
			}
			Material material = WaterLidMaterial();
			if (material == null)
			{
				Debug.LogWarning($"{hull.name}: no HullWaterLid material (FishMMO Water) found; the hull has no water lid and the sea shows inside it.");
				return;
			}
			int stations = port.Length;
			var vertices = new Vector3[2 * stations];
			var triangles = new int[6 * (stations - 1)];
			for (int i = 0; i < stations; i++)
			{
				vertices[2 * i] = port[i];
				vertices[2 * i + 1] = starboard[i];
				if (i > 0)
				{
					int a = 2 * (i - 1), t = 6 * (i - 1);
					triangles[t] = a;
					triangles[t + 1] = a + 2;
					triangles[t + 2] = a + 1;
					triangles[t + 3] = a + 1;
					triangles[t + 4] = a + 2;
					triangles[t + 5] = a + 3;
				}
			}
			var mesh = new Mesh { name = $"{piece} {variant} water lid", vertices = vertices, triangles = triangles };
			mesh.RecalculateBounds();
			var lid = new GameObject(WaterLidName);
			lid.transform.SetParent(hull, false);
			lid.AddComponent<MeshFilter>().sharedMesh = mesh;
			var renderer = lid.AddComponent<MeshRenderer>();
			renderer.sharedMaterial = material;
			renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
			renderer.receiveShadows = false;
			renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
			renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
			renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
			renderer.allowOcclusionWhenDynamic = false;
		}

		private static Material waterLidMaterial;

		/// <summary>The water plugin's lid material, found by name (the plugin's folder may move).</summary>
		private static Material WaterLidMaterial()
		{
			if (waterLidMaterial != null)
			{
				return waterLidMaterial;
			}
			foreach (string guid in AssetDatabase.FindAssets("HullWaterLid t:Material"))
			{
				Material found = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
				if (found != null && found.name == "HullWaterLid")
				{
					return waterLidMaterial = found;
				}
			}
			return null;
		}

		private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
		{
			Vector2 ab = b - a;
			float t = ab.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
			return Vector2.Distance(p, a + ab * t);
		}

		private static float SegmentDistance(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
		{
			// Closest of the four ends to the other segment: exact unless they cross, and crossing hulls are refused below.
			float best = Mathf.Min(Mathf.Min(DistanceToSegment(a, c, d), DistanceToSegment(b, c, d)), Mathf.Min(DistanceToSegment(c, a, b), DistanceToSegment(d, a, b)));
			return Crosses(a, b, c, d) ? 0f : best;
		}

		private static bool Crosses(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
		{
			float Cross(Vector2 o, Vector2 p, Vector2 q) => (p.x - o.x) * (q.y - o.y) - (p.y - o.y) * (q.x - o.x);
			float d1 = Cross(c, d, a), d2 = Cross(c, d, b), d3 = Cross(a, b, c), d4 = Cross(a, b, d);
			return ((d1 > 0f) != (d2 > 0f)) && ((d3 > 0f) != (d4 > 0f));
		}
	}
}
#endif
