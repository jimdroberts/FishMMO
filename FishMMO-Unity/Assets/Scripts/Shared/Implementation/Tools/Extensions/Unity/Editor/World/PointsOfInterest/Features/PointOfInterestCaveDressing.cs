#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Dresses a carved cave system (2026-10-11: "much more realistic features"): dripstone on its floors and roofs
	/// where water has stood and dripped — stalagmites, stalactites hung over them, columns where the two have met,
	/// flowstone down the walls and gour pools on cavern floors — breakdown blocks fallen from the roof, glowing crystals
	/// in some chambers, and in a mine its timbering, track, an ore tub and lamps.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>By the cave's stone.</b> A limestone cave (cave, grotto, sea cave) takes calcite dripstone and selenite; an ice
	/// cave ice stalagmites, icicles and frost crystals; a lava tube basalt dribble spires and slabs of its fallen lining
	/// (lava stalactites hang from its roof); a mine none of these, only the timbering and the spoil of the ore.
	/// </para>
	/// <para>
	/// <b>Never in the way.</b> Every passage's line is a lane kept clear of anything on the floor and of anything hanging
	/// lower than 2.4 m over it, and the middle of each site's own chamber (where its camp or den goes) is kept clear too;
	/// formations stand in the chambers' outer rings and along the passages' walls. Stalactites hang where the roof is,
	/// found by marching the system's field; nothing is placed where the field says rock.
	/// </para>
	/// <para>
	/// The system's host site dresses all of it (a joined site's chambers included); a joined site dresses nothing.
	/// Props, so instanced, culled and collided with like the rest; formations from the biome art's formation prefabs, the
	/// mine's from the structure kit. Seeded by the system, so a repaint dresses the same.
	/// </para>
	/// </remarks>
	[InitializeOnLoad]
	public sealed class PointOfInterestCaveDressing : IPointOfInterestSiteBuilder
	{
		static PointOfInterestCaveDressing() => PointOfInterestSiteBuilders.Register(new PointOfInterestCaveDressing());

		/// <summary>A lane's half-width either side of a passage's line, metres: a person and a little room.</summary>
		private const float Lane = 1.4f;
		/// <summary>The least clearance over a lane under anything hung from the roof, metres.</summary>
		private const float Headroom = 2.4f;

		public bool Handles(PointOfInterestRecord record) => CaveShaper.TryForm(record.Kind, out _);

		private sealed class Set
		{
			public string Rock;
			public string Floor, Hanging, Wall, Pool, Breakdown, Crystal;
			/// <summary>The crystals' own stone, when not the cave's (selenite in a limestone cave).</summary>
			public string CrystalRock;
			/// <summary>The formations' heights at scale 1 (the shapes' own), metres.</summary>
			public float FloorHeight = 2.6f, WallHeight = 1.6f;
			public float Density = 1f, CrystalChance, BreakdownShare = 0.25f;
			/// <summary>The crystals' light's hue (SceneEmitterKind.Crystal), or −1 for one by chance.</summary>
			public int Hue = -1;
		}

		private static Set SetFor(CaveForm form)
		{
			switch (form)
			{
				case CaveForm.IceCave:
					return new Set { Rock = "WaterIce", Floor = "Stalagmite", Hanging = "Stalagmite", Wall = "FrostFlowers", Crystal = "Crystal", Breakdown = "Lobe",
						FloorHeight = 2.2f, WallHeight = 0.6f, CrystalChance = 0.6f, Hue = 1 };
				case CaveForm.LavaTube:
					return new Set { Rock = "Basalt", Floor = "Dribble", Hanging = "Dribble", Breakdown = "Slabs", FloorHeight = 1.6f, Density = 0.6f, BreakdownShare = 0.5f };
				case CaveForm.SeaCave:
					return new Set { Rock = "Limestone", Floor = "Stalagmite", Hanging = "Stalagmite", Breakdown = "Block", Density = 0.35f, BreakdownShare = 0.6f };
				case CaveForm.Mine:
					return new Set { Rock = "Quartzite", Breakdown = "Scree", Density = 0.3f, BreakdownShare = 1f };
				default:
					return new Set { Rock = "Limestone", Floor = "Stalagmite", Hanging = "Stalagmite", Wall = "Flowstone", Pool = "Gours", Breakdown = "Block",
						Crystal = "Crystal", CrystalRock = "Gypsum", CrystalChance = 0.3f };
			}
		}

		public void Build(PointOfInterestSiteContext context)
		{
			PointOfInterestShape shape = CaveShaper.ShapeFor(context.Points, context.Record.Id);
			if (shape == null || CaveShaper.IsLink(shape.Values, out _))
			{
				return;
			}
			CaveSystem system = CaveSystem.FromValues(shape.Values, shape.Position, shape.Seed);
			if (system == null)
			{
				return;
			}
			var dresser = new Dresser(context, system, SetFor(system.Form), new DeterministicRNG(unchecked(system.Seed * 0x2F6B + 0xD1E5)));
			dresser.Run();
			PointOfInterestFeatureKit.Note(context, $"cave dressed: {dresser.Placed} props, {dresser.Lights} lights" + (dresser.Missing.Count > 0 ? $"; missing art: {string.Join(", ", dresser.Missing)} (Generate Biome Art, then repaint)" : string.Empty));
		}

		private sealed class Dresser
		{
			private readonly PointOfInterestSiteContext context;
			private readonly CaveSystem system;
			private readonly Set set;
			private readonly DeterministicRNG rng;
			private readonly List<(Vector3 a, Vector3 b)> lanes = new List<(Vector3, Vector3)>();
			private readonly List<(Vector3 centre, float radius)> keepClear = new List<(Vector3, float)>();
			public readonly HashSet<string> Missing = new HashSet<string>();
			public int Placed, Lights;

			public Dresser(PointOfInterestSiteContext context, CaveSystem system, Set set, DeterministicRNG rng)
			{
				this.context = context;
				this.system = system;
				this.set = set;
				this.rng = rng;
				foreach (CavePassage passage in system.Passages)
				{
					for (int i = 0; i + 1 < passage.Nodes.Count; i++)
					{
						CaveNode a = passage.Nodes[i], b = passage.Nodes[i + 1];
						lanes.Add((new Vector3(a.Centre.x, a.Floor, a.Centre.z), new Vector3(b.Centre.x, b.Floor, b.Centre.z)));
					}
				}
				// Each site's own chamber's middle is its camp's and a large cave's dungeon entrance's (PointOfInterestChamberFeature).
				foreach (CaveMouth mouth in system.Mouths)
				{
					if (system.TryChamberOf(mouth.SiteId, out CaveChamber own))
					{
						keepClear.Add((new Vector3(own.Centre.x, own.Floor, own.Centre.z), Mathf.Max(4.5f, 0.6f * Mathf.Min(own.Radii.x, own.Radii.z))));
						if (system.Form == CaveForm.Grotto)
						{
							// A grotto's shrine or camp stands on the dry ledge at its back (CaveShaper.TryChamber).
							float back = 0.55f * own.Radii.x, yr = own.Yaw * Mathf.Deg2Rad;
							keepClear.Add((new Vector3(own.Centre.x + Mathf.Sin(yr) * back, own.Floor + own.Slope * back, own.Centre.z + Mathf.Cos(yr) * back), 3f));
						}
					}
				}
			}

			public void Run()
			{
				if (system.Form == CaveForm.Mine)
				{
					Mine();
				}
				foreach (CaveChamber chamber in system.Chambers)
				{
					Chamber(chamber);
				}
				foreach (CavePassage passage in system.Passages)
				{
					Passage(passage);
				}
			}

			// ── Chambers ──────────────────────────────────────────

			private void Chamber(CaveChamber chamber)
			{
				float cy = chamber.Yaw * Mathf.Deg2Rad;
				var along = new Vector3(Mathf.Sin(cy), 0f, Mathf.Cos(cy));
				var across = new Vector3(along.z, 0f, -along.x);
				float area = Mathf.PI * chamber.Radii.x * chamber.Radii.z;
				int count = Mathf.Clamp(Mathf.RoundToInt(area / 14f * set.Density), 0, 40);
				bool cavern = chamber.Kind == CaveChamberKind.Cavern;
				for (int i = 0; i < count; i++)
				{
					// The outer ring, 0.45–0.92 of the way to the wall.
					float a = rng.Range(0f, 2f * Mathf.PI), t = Mathf.Sqrt(rng.Range(0.2f, 0.85f));
					Vector3 p = chamber.Centre + along * (Mathf.Cos(a) * t * chamber.Radii.x) + across * (Mathf.Sin(a) * t * chamber.Radii.z);
					p.y = CaveSystem.FloorOf(chamber, p) + 0.5f;
					float roll = rng.NextFloat();
					if (roll < set.BreakdownShare)
					{
						Breakdown(p);
					}
					else if (set.Pool != null && cavern && roll < set.BreakdownShare + 0.12f)
					{
						FloorPiece(p, set.Pool, 0.5f, rng.Range(0.7f, 1.3f), wall: false);
					}
					else
					{
						Dripstone(p, rng.NextFloat() < 0.45f);
					}
				}
				// Flowstone down the walls: at the wall's foot, its back to the rock.
				if (set.Wall != null)
				{
					int walls = Mathf.Clamp(Mathf.RoundToInt(area / 60f * set.Density), 0, 8);
					for (int i = 0; i < walls; i++)
					{
						float a = rng.Range(0f, 2f * Mathf.PI);
						Vector3 dir = along * Mathf.Cos(a) + across * Mathf.Sin(a);
						Vector3 p = new Vector3(chamber.Centre.x, chamber.Floor + 0.5f, chamber.Centre.z);
						if (WallFoot(p, dir, Mathf.Max(chamber.Radii.x, chamber.Radii.z) + 2f, out Vector3 foot, chamber))
						{
							FloorPiece(foot - dir * 0.6f, set.Wall, set.WallHeight, rng.Range(0.7f, 1.2f), wall: true, facing: -dir);
						}
					}
				}
				// Crystals: a cluster or three in some chambers, glowing.
				if (set.Crystal != null && rng.NextFloat() < set.CrystalChance * (cavern ? 1.5f : 1f))
				{
					int clusters = rng.Range(1, 4);
					for (int i = 0; i < clusters; i++)
					{
						float a = rng.Range(0f, 2f * Mathf.PI);
						Vector3 dir = along * Mathf.Cos(a) + across * Mathf.Sin(a);
						Vector3 p = new Vector3(chamber.Centre.x, chamber.Floor + 0.5f, chamber.Centre.z);
						if (WallFoot(p, dir, Mathf.Max(chamber.Radii.x, chamber.Radii.z) + 2f, out Vector3 foot, chamber)
							&& FloorPiece(foot - dir * 0.5f, set.Crystal, 1f, rng.Range(0.8f, 1.6f), true, -dir, set.CrystalRock, out Vector3 at))
						{
							Glow(at + Vector3.up * 0.8f, Mathf.Clamp(0.4f * Mathf.Max(chamber.Radii.x, chamber.Radii.z), 5f, 10f));
						}
					}
				}
			}

			// ── Passages ──────────────────────────────────────────

			private void Passage(CavePassage passage)
			{
				if (set.Density <= 0f)
				{
					return;
				}
				float next = system.Mouths[0].Zone * 0.6f;
				for (int i = 1; i < passage.Nodes.Count; i++)
				{
					CaveNode node = passage.Nodes[i];
					if (node.Along < next || NearMouth(node.Centre))
					{
						continue;
					}
					next = node.Along + rng.Range(5f, 12f) / set.Density;
					Vector3 dir = node.Centre - passage.Nodes[i - 1].Centre;
					dir.y = 0f;
					dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.forward;
					Vector3 side = new Vector3(dir.z, 0f, -dir.x) * (rng.NextFloat() < 0.5f ? -1f : 1f);
					var p = new Vector3(node.Centre.x, node.Floor + 0.5f, node.Centre.z);
					// Against one wall: as far out as the floor goes, less a formation's reach.
					if (WallFoot(p, side, 2f * node.Radius + 1f, out Vector3 foot))
					{
						Vector3 at = foot - side * 0.7f;
						if (rng.NextFloat() < 0.3f)
						{
							Breakdown(at);
						}
						else
						{
							Dripstone(at, rng.NextFloat() < 0.35f);
						}
					}
					// Straws and stalactites over the passage, short enough over its lane.
					if (set.Hanging != null && rng.NextFloat() < 0.6f)
					{
						Vector3 over = p + side * rng.Range(-0.5f, 0.5f) * node.Radius + dir * rng.Range(-1.5f, 1.5f);
						Hang(over, rng.Range(0.25f, 0.6f));
					}
				}
			}

			// ── Pieces ────────────────────────────────────────────

			/// <summary>A stalagmite on the floor, maybe the stalactite over it, and where they come near enough a column.</summary>
			private void Dripstone(Vector3 near, bool withHanging)
			{
				if (set.Floor == null)
				{
					Breakdown(near);
					return;
				}
				if (!FloorAndRoof(near, out float floor, out float roof))
				{
					return;
				}
				var at = new Vector3(near.x, floor, near.z);
				if (InLane(at, 0.8f))
				{
					return;
				}
				float room = roof - floor;
				// A column where the roof is low enough for the two to have met (a quarter of them).
				bool column = room < 7f && rng.NextFloat() < 0.25f;
				float up = column ? 0.55f * room : Mathf.Min(rng.Range(0.4f, 1.5f) * set.FloorHeight, 0.55f * room);
				float scale = up / set.FloorHeight;
				if (scale < 0.15f)
				{
					return;
				}
				float width = Mathf.Clamp(scale * rng.Range(0.8f, 1.3f), 0.3f, 2.2f);
				Place(Formation(set.Floor), at, Quaternion.Euler(0f, rng.Range(0f, 360f), 0f), new Vector3(width, scale, width));
				if (set.Hanging != null && (withHanging || column))
				{
					float down = column ? 0.5f * room : Mathf.Min(rng.Range(0.3f, 0.9f) * set.FloorHeight, 0.4f * room);
					float s = down / set.FloorHeight;
					Place(Formation(set.Hanging), new Vector3(at.x, roof + 0.05f, at.z), Quaternion.Euler(180f, rng.Range(0f, 360f), 0f), new Vector3(width * 0.8f, s, width * 0.8f));
				}
			}

			/// <summary>A stalactite (or a cluster of straws) hung from the roof over a point, its length a share of the formation's.</summary>
			private void Hang(Vector3 under, float share)
			{
				if (!FloorAndRoof(under, out float floor, out float roof))
				{
					return;
				}
				float down = share * set.FloorHeight;
				// Over a lane, never lower than a tall person's head.
				if (InLane(new Vector3(under.x, floor, under.z), 0f))
				{
					down = Mathf.Min(down, roof - floor - Headroom);
				}
				float s = down / set.FloorHeight;
				if (s < 0.08f)
				{
					return;
				}
				float width = Mathf.Clamp(s * rng.Range(0.6f, 1f), 0.15f, 1.2f);
				Place(Formation(set.Hanging), new Vector3(under.x, roof + 0.05f, under.z), Quaternion.Euler(180f, rng.Range(0f, 360f), 0f), new Vector3(width, s, width));
			}

			/// <summary>A block or slab fallen from the roof, or spoil.</summary>
			private void Breakdown(Vector3 near)
			{
				if (set.Breakdown == null || !FloorAndRoof(near, out float floor, out _))
				{
					return;
				}
				var at = new Vector3(near.x, floor, near.z);
				if (InLane(at, 1f))
				{
					return;
				}
				float s = rng.Range(0.2f, 0.55f);
				Place(Formation(set.Breakdown), at, Quaternion.Euler(rng.Range(-8f, 8f), rng.Range(0f, 360f), rng.Range(-8f, 8f)), new Vector3(s * rng.Range(0.8f, 1.2f), s, s * rng.Range(0.8f, 1.2f)));
			}

			private bool FloorPiece(Vector3 near, string shape, float height, float scale, bool wall, Vector3 facing = default, string rock = null)
				=> FloorPiece(near, shape, height, scale, wall, facing, rock, out _);

			private bool FloorPiece(Vector3 near, string shape, float height, float scale, bool wall, Vector3 facing, string rock, out Vector3 at)
			{
				at = default;
				if (!FloorAndRoof(near, out float floor, out float roof) || roof - floor < height * scale + 0.3f)
				{
					return false;
				}
				at = new Vector3(near.x, floor, near.z);
				if (InLane(at, wall ? 0.6f : 1.6f * scale))
				{
					return false;
				}
				float yaw = facing.sqrMagnitude > 1e-4f ? Mathf.Atan2(facing.x, facing.z) * Mathf.Rad2Deg : rng.Range(0f, 360f);
				return Place(Formation(shape, rock), at, Quaternion.Euler(0f, yaw, 0f), Vector3.one * scale);
			}

			// ── Mines ─────────────────────────────────────────────

			private void Mine()
			{
				// The portal at each mouth, facing out.
				int portal = PortalVariant(context.Template != null ? context.Template.name : null);
				foreach (CaveMouth mouth in system.Mouths)
				{
					float s = mouth.Radius / 2.1f;
					Place(StructureKit.Prefab("MinePortal", portal), mouth.Floor + Dir(mouth.Yaw) * 0.4f, Quaternion.Euler(0f, mouth.Yaw + 180f, 0f), new Vector3(s, Mathf.Max(1f, s), 1f));
				}
				foreach (CavePassage passage in system.Passages)
				{
					float nextSet = 2f, nextLamp = 8f, nextTrack = 0f;
					bool trunk = passage.Kind == CavePassageKind.Trunk;
					for (int i = 1; i < passage.Nodes.Count; i++)
					{
						CaveNode a = passage.Nodes[i - 1], b = passage.Nodes[i];
						float length = b.Along - a.Along;
						if (length <= 1e-3f)
						{
							continue;
						}
						Vector3 dir = b.Centre - a.Centre;
						float yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
						float pitch = -Mathf.Atan2(dir.y, new Vector2(dir.x, dir.z).magnitude) * Mathf.Rad2Deg;
						for (float along = Mathf.Max(a.Along, nextSet); along < b.Along; along = nextSet)
						{
							float t = (along - a.Along) / length;
							float floor = Mathf.Lerp(a.Floor, b.Floor, t);
							Vector3 c = Vector3.Lerp(a.Centre, b.Centre, t);
							float radius = Mathf.Lerp(a.Radius, b.Radius, t);
							// Sized to the bore: its posts inside the floor's width, its cap under the roof.
							float s = radius / 2.1f;
							Place(StructureKit.Prefab("MineSet", rng.NextFloat() < 0.5f ? 1 : 0), new Vector3(c.x, floor, c.z), Quaternion.Euler(0f, yaw, 0f), new Vector3(s, Mathf.Clamp(s, 0.95f, 1.1f), 1f));
							nextSet = along + rng.Range(2.6f, 3.2f);
						}
						for (float along = Mathf.Max(a.Along, nextLamp); along < b.Along; along = nextLamp)
						{
							float t = (along - a.Along) / length;
							Vector3 c = Vector3.Lerp(a.Centre, b.Centre, t);
							Vector3 side = new Vector3(dir.z, 0f, -dir.x).normalized;
							float floor = Mathf.Lerp(a.Floor, b.Floor, t);
							// A torch in its sconce on the rock at head height, leaning into the drift (the piece gives off its flame).
							float hand = rng.NextFloat() < 0.5f ? 1f : -1f;
							if (WallFoot(new Vector3(c.x, floor + 1.8f, c.z), side * hand, 2f * StructurePieces.MineSetHalfWidth + 1f, out Vector3 rock))
							{
								Place(StructureKit.Prefab("WallTorch", rng.NextFloat() < 0.5f ? 1 : 0), rock - side * hand * 0.05f, Quaternion.LookRotation(side * hand, Vector3.up), Vector3.one);
							}
							nextLamp = along + rng.Range(14f, 20f);
						}
						if (trunk)
						{
							for (float along = Mathf.Max(a.Along, nextTrack); along + 1.5f < b.Along; along = nextTrack)
							{
								float t = (along + 1.5f - a.Along) / length;
								Vector3 c = Vector3.Lerp(a.Centre, b.Centre, t);
								float floor = Mathf.Lerp(a.Floor, b.Floor, t);
								Place(StructureKit.Prefab("MineTrack", rng.NextFloat() < 0.15f ? 1 : 0), new Vector3(c.x, floor, c.z), Quaternion.Euler(pitch, yaw, 0f), Vector3.one);
								nextTrack = along + 3f;
							}
						}
					}
					// A tub at the end of the track, by the last stope.
					if (trunk && passage.Nodes.Count > 2)
					{
						CaveNode end = passage.Nodes[passage.Nodes.Count - 2];
						CaveNode last = passage.Nodes[passage.Nodes.Count - 1];
						Vector3 dir = last.Centre - end.Centre;
						Place(StructureKit.Prefab("MineCart", rng.NextFloat() < 0.3f ? 1 : 0), new Vector3(end.Centre.x, end.Floor, end.Centre.z),
							Quaternion.Euler(0f, Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg, 0f), Vector3.one);
					}
				}
				// Every stope lit, and its spoil and the ore's glint along its walls.
				foreach (CaveChamber stope in system.Chambers)
				{
					// A torch on a pole where the work was, by the wall.
					Vector3 post = Dir(rng.Range(0f, 360f));
					if (WallFoot(new Vector3(stope.Centre.x, stope.Floor + 0.5f, stope.Centre.z), post, Mathf.Max(stope.Radii.x, stope.Radii.z) + 2f, out Vector3 postFoot, stope)
						&& FloorAndRoof(postFoot - post * 0.8f, out float postFloor, out float postRoof) && postRoof - postFloor > 3f)
					{
						Vector3 at = postFoot - post * 0.8f;
						Place(StructureKit.Prefab("TorchPost", rng.NextFloat() < 0.5f ? 1 : 0), new Vector3(at.x, postFloor, at.z), Quaternion.Euler(0f, rng.Range(0f, 360f), 0f), Vector3.one);
					}
					int glints = rng.Range(2, 5);
					for (int i = 0; i < glints; i++)
					{
						Vector3 dir = Dir(rng.Range(0f, 360f));
						if (WallFoot(new Vector3(stope.Centre.x, stope.Floor + 0.5f, stope.Centre.z), dir, Mathf.Max(stope.Radii.x, stope.Radii.z) + 2f, out Vector3 foot, stope))
						{
							FloorPiece(foot - dir * 0.4f, "Cubes", 1f, rng.Range(0.5f, 0.9f), true, -dir, "Pyrite");
						}
					}
				}
			}

			/// <summary>The mine's portal to its template: boarded up when abandoned, dressed stone for a delving, timber otherwise.</summary>
			private static int PortalVariant(string template)
			{
				if (string.IsNullOrEmpty(template))
				{
					return 0;
				}
				return template.Contains("Abandoned") ? 1 : template.Contains("Delving") ? 2 : 0;
			}

			// ── Lights ────────────────────────────────────────────

			private void Glow(Vector3 local, float reach)
			{
				uint seed = PointOfInterestPlanner.Hash(system.Seed, Mathf.RoundToInt(local.x * 10f), Mathf.RoundToInt(local.z * 10f), 0xC4);
				if (set.Hue >= 0)
				{
					seed = (seed & ~0x300u) | ((uint)set.Hue << 8);
				}
				Emit(local, SceneEmitterKind.Crystal, seed, reach);
			}

			private void Emit(Vector3 local, SceneEmitterKind kind, uint seed, float reach)
			{
				if (context.Points == null)
				{
					return;
				}
				context.Points.Emitters ??= new List<SceneEmitter>();
				context.Points.Emitters.Add(new SceneEmitter { Position = system.Origin + local, Kind = kind, Seed = seed, Reach = reach });
				Lights++;
			}

			// ── Geometry ──────────────────────────────────────────

			/// <summary>The floor and the roof over a point in the void (system frame), by marching the field; false where it is rock.</summary>
			private bool FloorAndRoof(Vector3 p, out float floor, out float roof)
			{
				floor = roof = p.y;
				if (system.Void(p) >= 0f)
				{
					return false;
				}
				float y = p.y;
				while (system.Void(new Vector3(p.x, y, p.z)) < 0f && y > p.y - 20f)
				{
					y -= 0.05f;
				}
				floor = y;
				y = p.y;
				while (system.Void(new Vector3(p.x, y, p.z)) < 0f && y < p.y + 60f)
				{
					y += 0.1f;
				}
				roof = y;
				return roof - floor > 1f;
			}

			/// <summary>
			/// Where a ray from <paramref name="from"/> meets the wall, its foot on the floor; false where it meets none within
			/// reach. Over a chamber's floor the ray keeps half a metre over its sloping plane, so it meets the wall and not
			/// the floor rising.
			/// </summary>
			private bool WallFoot(Vector3 from, Vector3 dir, float reach, out Vector3 foot, CaveChamber? over = null)
			{
				foot = from;
				if (system.Void(from) >= 0f)
				{
					return false;
				}
				Vector3 last = from;
				for (float d = 0.25f; d <= reach; d += 0.25f)
				{
					Vector3 p = from + dir * d;
					if (over.HasValue)
					{
						p.y = CaveSystem.FloorOf(over.Value, p) + 0.5f;
					}
					if (system.Void(p) >= 0f)
					{
						foot = last;
						return true;
					}
					last = p;
				}
				return false;
			}

			/// <summary>Within <paramref name="margin"/> + a lane of any passage's line on the floor, or in a site's camp ground.</summary>
			private bool InLane(Vector3 p, float margin)
			{
				foreach ((Vector3 centre, float radius) in keepClear)
				{
					if (Mathf.Abs(p.y - centre.y) < 3f && new Vector2(p.x - centre.x, p.z - centre.z).sqrMagnitude < (radius + margin) * (radius + margin))
					{
						return true;
					}
				}
				float reach = Lane + margin;
				foreach ((Vector3 a, Vector3 b) in lanes)
				{
					var ab = new Vector2(b.x - a.x, b.z - a.z);
					var ap = new Vector2(p.x - a.x, p.z - a.z);
					float t = Mathf.Clamp01(Vector2.Dot(ap, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
					if ((ap - ab * t).sqrMagnitude < reach * reach && Mathf.Abs(p.y - Mathf.Lerp(a.y, b.y, t)) < 3f)
					{
						return true;
					}
				}
				return false;
			}

			private bool NearMouth(Vector3 p)
			{
				foreach (CaveMouth mouth in system.Mouths)
				{
					if ((p - mouth.Floor).sqrMagnitude < mouth.Zone * mouth.Zone * 0.36f)
					{
						return true;
					}
				}
				return false;
			}

			private static Vector3 Dir(float yaw)
			{
				float r = yaw * Mathf.Deg2Rad;
				return new Vector3(Mathf.Sin(r), 0f, Mathf.Cos(r));
			}

			private GameObject Formation(string shape, string rock = null)
			{
				string name = RockArtNames.FormationPrefab(rock ?? set.Rock, shape, rng.Range(0, RockTypes.VariantCount));
				GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ProceduralArtCatalogue.PrefabPath(name));
				if (prefab == null)
				{
					Missing.Add($"{rock ?? set.Rock} {shape}");
				}
				return prefab;
			}

			private bool Place(GameObject prefab, Vector3 local, Quaternion rotation, Vector3 scale)
			{
				if (prefab == null)
				{
					return false;
				}
				context.AddProp(prefab, system.Origin + local, rotation, scale);
				Placed++;
				return true;
			}
		}
	}
}
#endif
