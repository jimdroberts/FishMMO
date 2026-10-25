#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using FishMMO.Shared.Biomes;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// The props every site in a scene lays, collected into one prop set (<see cref="PointOfInterestGenerator.PropSource"/>)
	/// so they draw instanced with streamed collision like the cliffs and boulders.
	/// </summary>
	public sealed class PointOfInterestPropSink
	{
		public readonly List<ScenePropSet.Prototype> Prototypes = new List<ScenePropSet.Prototype>();
		public readonly List<ScenePropSet.Prop> Props = new List<ScenePropSet.Prop>();
		private readonly Dictionary<(GameObject, string), int> prototypeOf = new Dictionary<(GameObject, string), int>();

		public void Add(GameObject prefab, Vector3 position, Quaternion rotation, Vector3 scale) => Add(prefab, position, rotation, scale, null);

		/// <summary>
		/// Adds a prop drawn in <paramref name="materials"/> instead of its prefab's own (a structure piece in its biome's
		/// palette, StructurePaletteDressing): one prototype per prefab and set of materials, so every piece of a site laid in the
		/// same palette shares one. Null materials: the prefab's own, the prototype every scene had before palettes.
		/// </summary>
		public void Add(GameObject prefab, Vector3 position, Quaternion rotation, Vector3 scale, Material[] materials)
		{
			if (prefab == null)
			{
				return;
			}
			string dress = DressKey(materials);
			if (!prototypeOf.TryGetValue((prefab, dress), out int prototype))
			{
				prototype = Prototypes.Count;
				prototypeOf[(prefab, dress)] = prototype;
				Prototypes.Add(new ScenePropSet.Prototype { Prefab = prefab, Layer = -1, Materials = materials != null && materials.Length > 0 ? materials : null });
			}
			Props.Add(new ScenePropSet.Prop { Prototype = prototype, Position = position, Rotation = rotation, Scale = scale });
		}

		private static string DressKey(Material[] materials)
		{
			if (materials == null || materials.Length == 0)
			{
				return string.Empty;
			}
			var key = new System.Text.StringBuilder();
			foreach (Material m in materials)
			{
				key.Append(m != null ? m.GetEntityId().ToString() : "-").Append(',');
			}
			return key.ToString();
		}
	}

	/// <summary>
	/// Everything a <see cref="PointOfInterestFeature"/> (or a registered <see cref="IPointOfInterestSiteBuilder"/>) is
	/// given to build one site with.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Deterministic.</b> <see cref="Random"/> is seeded from the record's <see cref="PointOfInterestRecord.SiteSeed"/>
	/// and fresh for each feature, so a feature's draws do not shift when another feature is added before it.
	/// </para>
	/// <para>
	/// <b>Where things go.</b> Instanced visuals with collision go through <see cref="AddProp"/>; scene objects
	/// (spawners, a waypoint, a region) go under <see cref="Root"/> (the site's <see cref="ScenePointOfInterest"/>), which is
	/// shared by both builds. Client-only visuals that are not props go under a ClientOnlyObject child; colliders never do.
	/// </para>
	/// </remarks>
	public sealed class PointOfInterestSiteContext
	{
		public Scene Scene;
		/// <summary>The site's object, a child of the scene's "Points of Interest" root, at the record's anchor and heading.</summary>
		public Transform Root;
		public PointOfInterestRecord Record;
		/// <summary>The template the site was built from; null for a marker-only site (site builders run for those too).</summary>
		public PointOfInterestTemplate Template;
		/// <summary>Seeded from the site; see remarks.</summary>
		public DeterministicRNG Random;
		/// <summary>The scene's POI asset.</summary>
		public ScenePointsOfInterest Points;
		/// <summary>The scene's request (body, layer, settings).</summary>
		public SceneGenerationRequest Request;
		/// <summary>The scene's terrain folder: where a feature writes any asset it makes.</summary>
		public string TerrainFolder;
		/// <summary>The scene's rivers and lakes; may be null.</summary>
		public SceneWater Water;
		/// <summary>The ground at (east, north), scene metres.</summary>
		public Func<float, float, float> GroundAt;
		/// <summary>The scene's terrain tiles (null in a harness) and their plan, for the ground a building is dug into (<see cref="AddProp"/>).</summary>
		public Terrain[,] Terrains;
		public TerrainTilePlan Tiles;
		/// <summary>
		/// True over open sky: a cloud world's terrain holes, where <see cref="GroundAt"/> still answers (the void is filled
		/// with its islands' rim heights) but nothing can stand. Null where every hole is a cave's or a wall's, not sky.
		/// </summary>
		public Func<float, float, bool> SkyAt;
		/// <summary>The ground's steepness at (east, north), degrees.</summary>
		public Func<float, float, float> SlopeAt;
		/// <summary>The biome at (east, north); may answer null.</summary>
		public Func<float, float, BiomeTemplate> BiomeAt;
		/// <summary>The fields every site laid before this one in this placing (2026-10-18), shared by all of them; null in a harness (no other sites).</summary>
		public PointOfInterestFieldClaims FieldClaims;

		private BiomeStructurePalette palette;
		private bool paletteRead;

		/// <summary>
		/// What the site's biome builds its structures of (its template's structure palette, BiomeTemplateArt.StructurePalette,
		/// 2026-10-16): every kit piece laid through <see cref="AddProp"/> is laid in it (StructurePaletteDressing), and the
		/// features that make scene objects of kit pieces (doors, moored boats) dress them in it. Read once from the biome at the
		/// site's anchor; null (the kit's own materials) without a biome lookup. Settable for a harness.
		/// </summary>
		public BiomeStructurePalette Palette
		{
			get
			{
				if (!paletteRead)
				{
					paletteRead = true;
					palette = BiomeAt != null && Record != null ? BiomeTemplateArt.StructurePalette(BiomeAt(Record.Position.x, Record.Position.z)) : null;
					// A cave site whose template the cave biome inside it named (not its host) builds in that biome's palette (2026-10-25,
					// I2n, decision 51: "pieces take the palette of the template that named their variant").
					if (Record != null && Template != null && CaveShaper.TryForm(Record.Kind, out _))
					{
						CaveBiome caves = CaveInteriorArt.OfSite(Request, Record);
						if (CaveInteriorArt.InteriorNamed(in caves, Template))
						{
							palette = BiomeTemplateArt.StructurePalette(caves.Interior);
						}
					}
				}
				return palette;
			}
			set
			{
				palette = value;
				paletteRead = true;
			}
		}
		private BiomeCaveCrystalKind postCrystal;
		private bool postCrystalRead;

		/// <summary>
		/// The crystal the site's crystal posts carry (2026-10-16, I6c): its biome's (BiomeTemplateArt.PostCrystal, the heaviest
		/// glowing kind of its Cave Crystals, else the heaviest), read once at the site's anchor; selenite without a biome lookup.
		/// Settable for a harness.
		/// </summary>
		public BiomeCaveCrystalKind PostCrystal
		{
			get
			{
				if (!postCrystalRead)
				{
					postCrystalRead = true;
					postCrystal = BiomeTemplateArt.PostCrystal(BiomeAt != null && Record != null ? BiomeAt(Record.Position.x, Record.Position.z) : null);
				}
				return postCrystal;
			}
			set
			{
				postCrystal = value;
				postCrystalRead = true;
			}
		}
		public List<string> Notes;
		internal PointOfInterestPropSink Sink;

		/// <summary>One door of a building laid at the site, in the world: what PointOfInterestDoors makes a door of.</summary>
		public struct PlacedDoor
		{
			public Vector3 Hinge;
			public Vector3 Across;
			public Vector3 Inward;
			public float Width, Height;
			public StructureDoorKind Kind;
			/// <summary>The building's piece, for the door's name.</summary>
			public string Building;
			/// <summary>The building's scale (its race's stature): the leaf's thickness, and whether a person needs steps of their own up to it.</summary>
			public float Scale;
			public StructureDoorMotion Motion;
			public bool StartsOpen;
			/// <summary>The building's origin's height (its ground line), where its own steps up to the door start.</summary>
			public float Base;
		}

		/// <summary>One room of a building laid at the site: the room in the building's frame, where the building stands, and its doors.</summary>
		public struct PlacedRoom
		{
			public StructureRoom Room;
			public Vector3 Position;
			public Quaternion Rotation;
			public float Scale;
			/// <summary>The building's doors, in its own frame (the room keeps their swing and the way in clear).</summary>
			public IReadOnlyList<StructureDoor> Doors;
			public string Building;
		}

		/// <summary>The rooms of the buildings laid so far (<see cref="AddProp"/>), for PointOfInterestInteriors.</summary>
		public List<PlacedRoom> Rooms = new List<PlacedRoom>();

		/// <summary>The doors of the buildings laid so far (<see cref="AddProp"/>), for the site builders after the template's features.</summary>
		public List<PlacedDoor> Doors = new List<PlacedDoor>();

		/// <summary>One prop laid at the site (<see cref="AddProp"/>), as laid.</summary>
		public struct PlacedProp
		{
			public GameObject Prefab;
			public Vector3 Position;
			public Quaternion Rotation;
			public Vector3 Scale;
		}

		/// <summary>
		/// Every prop laid at the site so far (<see cref="AddProp"/>), shared by every feature and builder of the site like
		/// <see cref="Doors"/>: what the site builders after the template's features dress (PointOfInterestVines grows ivy on
		/// the walls laid here).
		/// </summary>
		public List<PlacedProp> Laid = new List<PlacedProp>();

		/// <summary>The style pieces are resolved in: the template's, else the site's race, else empty.</summary>
		public string Style => Template != null && !string.IsNullOrWhiteSpace(Template.Style) ? Template.Style : Record?.Race ?? string.Empty;

		/// <summary>A structure piece's prefab for this site's style, or null (<see cref="PointOfInterestPieces"/>).</summary>
		public GameObject ResolvePiece(string tag, int seed) => PointOfInterestPieces.Resolve(tag, Style, seed);

		/// <summary>
		/// Adds an instanced prop to the scene's POI prop set: a structure kit piece in the site's <see cref="Palette"/>, a house
		/// with the roof its palette's roof material is built at (its roof variant, StructurePaletteDressing.RoofVariant).
		/// </summary>
		public void AddProp(GameObject prefab, Vector3 position, Quaternion rotation, Vector3 scale)
		{
			if (prefab != null)
			{
				prefab = StructurePaletteDressing.RoofVariant(prefab, Palette);
			}
			Sink?.Add(prefab, position, rotation, scale, Sink != null ? StructurePaletteDressing.MaterialsFor(prefab, Palette) : null);
			if (prefab != null)
			{
				Laid?.Add(new PlacedProp { Prefab = prefab, Position = position, Rotation = rotation, Scale = scale });
			}
			// Its doors, where they now stand: each becomes a server-run door (PointOfInterestDoors).
			if (prefab != null && StructurePieces.TryParsePrefab(prefab.name, out StructurePiece doorPiece, out int doorVariant))
			{
				foreach (StructureDoor door in StructurePieces.DoorsOf(doorPiece, doorVariant))
				{
					Doors.Add(new PlacedDoor
					{
						Hinge = position + rotation * Vector3.Scale(door.Hinge, scale),
						Across = (rotation * door.Across).normalized,
						Inward = (rotation * door.Inward).normalized,
						Width = door.Width * Mathf.Abs(scale.x),
						Height = door.Height * Mathf.Abs(scale.y),
						Kind = door.Kind,
						Building = doorPiece.Id,
						Scale = Mathf.Abs(scale.y),
						Motion = door.Motion,
						StartsOpen = door.StartsOpen,
						Base = position.y,
					});
				}
				IReadOnlyList<StructureDoor> pieceDoors = StructurePieces.DoorsOf(doorPiece, doorVariant);
				foreach (StructureRoom room in StructurePieces.RoomsOf(doorPiece, doorVariant))
				{
					Rooms.Add(new PlacedRoom
					{
						Room = room,
						Position = position,
						Rotation = rotation,
						Scale = Mathf.Abs(scale.y),
						Doors = pieceDoors,
						Building = doorPiece.Id,
					});
					// Its floor clear of the ground: a building seated on a slope had the hillside up through its floor.
					if (Terrains != null)
					{
						PointOfInterestTerrain.DigUnder(Terrains, Tiles, room, position, rotation, Mathf.Abs(scale.y));
					}
				}
			}
			// What the piece gives off (a chimney's smoke, a fire, a forge, a lantern), where it now stands.
			StructurePiece piece = null;
			int variant = 0;
			StructureRoofPitch? roof = null;
			bool isPiece = prefab != null && StructurePieces.TryParsePrefab(prefab.name, out piece, out variant, out _, out roof);
			if (Points != null && isPiece)
			{
				// A roof variant's chimney stands clear of its own roof (a flat roof's lower than a thatched one's).
				foreach ((Vector3 at, SceneEmitterKind kind) in StructurePieces.EmittersOf(piece, variant, roof))
				{
					Vector3 world = position + rotation * Vector3.Scale(at, scale);
					Points.Emitters ??= new System.Collections.Generic.List<SceneEmitter>();
					uint seed = PointOfInterestPlanner.Hash(Record != null ? Record.Id : 0, Mathf.RoundToInt(world.x * 10f), Mathf.RoundToInt(world.z * 10f), (int)kind);
					// A cold glow's tint (2026-10-18, I6g): a crystal post's its crystal's hue (the site biome's crystal, 2026-10-16),
					// a rune stone's and a glowjar's their own; it flickers only where the post's crystal does (stormglass,
					// decision 33), else it pulses (the bit always written, never the hash's).
					if (kind == SceneEmitterKind.Glow)
					{
						SceneGlowHue? own = StructurePieces.GlowHueOf(piece.Id);
						bool cradled = !own.HasValue && StructurePieces.TryCrystalSocket(piece, variant, out _, out _);
						CaveMineral postMineral = cradled ? ProceduralArtCatalogue.CaveMineralOf(PostCrystal) : default;
						seed = own.HasValue ? SceneGlow.WithHue(seed, own.Value) : SceneGlow.WithCrystalHue(seed, cradled ? postMineral.LightHue : -1);
						seed = SceneGlow.WithFlicker(seed, cradled && postMineral.Flickers);
					}
					Points.Emitters.Add(new SceneEmitter { Position = world, Kind = kind, Seed = seed });
				}
			}
			// A crystal post's crystal: its biome's own kind, laid in the post's cradle (a kit piece is the same in every biome).
			if (isPiece && StructurePieces.TryCrystalSocket(piece, variant, out Vector3 socket, out float crystalScale))
			{
				GameObject crystal = CaveArt.MineralPrefab(PostCrystal, unchecked((int)PointOfInterestPlanner.Hash(Record != null ? Record.Id : 0,
					Mathf.RoundToInt(position.x * 10f), Mathf.RoundToInt(position.z * 10f), 0x5C)) & 0xFF);
				if (crystal != null)
				{
					AddProp(crystal, position + rotation * Vector3.Scale(socket, scale), rotation, scale * crystalScale);
				}
				else
				{
					PointOfInterestFeatureKit.Note(this, $"crystal post: no art for its {PostCrystal} crystal yet (Generate Biome Art)");
				}
			}
		}

		private float highWater = float.NaN;

		/// <summary>
		/// The highest the water stands at a point, scene metres: an inland surface (river, lake, moat) or the sea at its
		/// highest tide, whichever is higher; negative infinity where there is none. The sea is no WaterKind on the water
		/// grid — its floor is only ground under sea level — so a test of the grid alone finds the ocean dry.
		/// </summary>
		public float WaterLevelAt(float x, float z)
		{
			if (float.IsNaN(highWater))
			{
				float sea = PropsFeature.SeaLevelOf(this);
				highWater = float.IsNegativeInfinity(sea) ? sea : sea + PropsFeature.HighestTideOf(this);
			}
			float inland = Water != null ? Water.SurfaceAt(x, z) : float.NegativeInfinity;
			return Mathf.Max(inland, highWater);
		}

		/// <summary>
		/// Whether the ground at a point lies under water, or within <paramref name="clearance"/> metres of its highest
		/// level: where anything that is not a water prop must not stand.
		/// </summary>
		public bool IsWet(float x, float z, float clearance)
		{
			if (GroundAt == null)
			{
				return false;
			}
			return GroundAt(x, z) < WaterLevelAt(x, z) + clearance;
		}

		/// <summary>
		/// How far in from the scene's edge whatever a site lays out past its own outline (its fields, farmsteads, yards and their
		/// bounds) keeps, metres. <see cref="GroundAt"/> answers past the edge too, where only the backdrop is drawn, so nothing
		/// else stopped a field there (2026-10-22). The site itself keeps further in (PointOfInterestPlanInput.EdgeMarginMetres).
		/// </summary>
		public const float SceneEdgeMarginMetres = 12f;

		/// <summary>Whether (x, z) lies on the scene's terrain at least <paramref name="margin"/> metres in from its edge; true without a tile plan (a harness).</summary>
		public bool InScene(float x, float z, float margin = SceneEdgeMarginMetres)
		{
			if (Tiles.TotalTiles == 0)
			{
				return true;
			}
			return Mathf.Abs(x) <= Tiles.WidthMetres * 0.5f - margin && Mathf.Abs(z) <= Tiles.DepthMetres * 0.5f - margin;
		}

		/// <summary>The world position of an offset in the site's own frame (x right, z ahead along its heading), on the ground.</summary>
		public Vector3 OnGround(Vector2 local)
		{
			Quaternion turn = Quaternion.Euler(0f, Record.Yaw, 0f);
			Vector3 offset = turn * new Vector3(local.x, 0f, local.y);
			float x = Record.Position.x + offset.x, z = Record.Position.z + offset.z;
			return new Vector3(x, GroundAt != null ? GroundAt(x, z) : Record.Position.y, z);
		}

		/// <summary>A new child object of the site, for spawners, waypoints and the like.</summary>
		public GameObject AddChild(string name)
		{
			var child = new GameObject(name);
			child.transform.SetParent(Root, false);
			return child;
		}
	}

	/// <summary>
	/// Something a template carries that is built at each of its sites: props, a waypoint, spawners, a portal.
	/// </summary>
	/// <remarks>
	/// Held by <c>[SerializeReference]</c> on the template, so features defined anywhere in the editor appear in its
	/// list. A feature must be deterministic in <see cref="PointOfInterestSiteContext.Random"/> and must not assume it runs
	/// once: a repaint builds every site again from scratch under a fresh site object.
	/// </remarks>
	[Serializable]
	public abstract class PointOfInterestFeature
	{
		public abstract void Build(PointOfInterestSiteContext context);
	}

	/// <summary>
	/// Built at EVERY site of the kinds it takes, with or without a template: the seam for gameplay that every site of a
	/// kind needs (a waypoint in every settlement, a boss spawner in every lair) whatever it was built from.
	/// </summary>
	public interface IPointOfInterestSiteBuilder
	{
		bool Handles(PointOfInterestRecord record);
		void Build(PointOfInterestSiteContext context);
	}

	/// <summary>The registered site builders (see <see cref="IPointOfInterestSiteBuilder"/>), run in registration order after a site's template features.</summary>
	public static class PointOfInterestSiteBuilders
	{
		private static readonly List<IPointOfInterestSiteBuilder> builders = new List<IPointOfInterestSiteBuilder>();

		public static IReadOnlyList<IPointOfInterestSiteBuilder> All => builders;

		public static void Register(IPointOfInterestSiteBuilder builder)
		{
			if (builder != null && !builders.Contains(builder))
			{
				builders.Add(builder);
			}
		}

		public static void Unregister(IPointOfInterestSiteBuilder builder) => builders.Remove(builder);
	}
}
#endif
