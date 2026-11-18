#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using FishMMO.Shared.Biomes;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	public sealed partial class PointOfInterestCaveDressing
	{
		private sealed partial class Dresser
		{
			/// <summary>The cave-life spots this system was given (<see cref="CaveLifeBake"/>).</summary>
			public int LifeSpots;

			/// <summary>
			/// Where the system's animals live (2026-10-18: "Caves need critters, bats, lizards, etc."): its flat floors, walls and
			/// roofs, its open air, its pools and its mouths, measured from its field (<see cref="CaveLifeBake"/>) and written to the
			/// scene's cave life for the client's ambient life. Nothing is placed: no prop moves for it.
			/// </summary>
			private void Life()
			{
				if (context.Points == null)
				{
					return;
				}
				List<SceneCaveLifeSpot> spots = CaveLifeBake.Bake(system, context.Record.Id);
				// A linked cave's animals are its interior's (2026-10-25, I2n): each spot carries the interior biome's id, read by the
				// client's ambient life in place of the biome map over it.
				int inside = caves.Linked ? BiomeRegistry.IDOf(caves.Interior) : 0;
				if (inside != 0)
				{
					for (int i = 0; i < spots.Count; i++)
					{
						SceneCaveLifeSpot spot = spots[i];
						spot.Biome = inside;
						spots[i] = spot;
					}
				}
				context.Points.CaveLife ??= new List<SceneCaveLifeSpot>();
				context.Points.CaveLife.AddRange(spots);
				LifeSpots = spots.Count;
			}
		}
	}

	/// <summary>
	/// The places a carved cave system's animals live (<see cref="SceneCaveLifeSpot"/>), measured from its own field at the cut:
	/// patches of floor, wall and roof flat enough to stand or hang on, boxes of air clear of rock all through, the water standing
	/// in it, the straight run in from each mouth, and the still air under its high roofs. Pure: a function of the system alone
	/// (its field and its seed, on a random stream of its own), so a repaint bakes the same spots and nothing else moves for them.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why patches, not the rock.</b> The client has the cave's colliders, but a ray a frame for every animal in every cave is
	/// what this saves: an animal kept within its patch's flat radius, on the patch's plane, stands on the rock to a few
	/// centimetres (the patch was only kept as wide as that held), anywhere on it, with nothing asked of the scene.
	/// </para>
	/// <para>
	/// <b>The dark</b> (<see cref="SceneCaveLifeSpot.Dark"/>) is reckoned from the mouths as the overgrowth reckons the daylight
	/// that lets plants grow in (five bore radii, 8–24 m), the twilight running on a dozen metres past it: what sorts the
	/// entrance's moths, spiders and swallow-like comings and goings from the deep cave's blind white things.
	/// </para>
	/// </remarks>
	public static class CaveLifeBake
	{
		/// <summary>
		/// The most spots one system is given: a great cave's worth, a thousand and more animals' homes. The first
		/// <see cref="MaxSpots"/> − <see cref="MaxLightSpots"/> are the cave's own (its floors, walls, roofs, air, water and mouths);
		/// the rest are kept for the homes of its light-makers, so a great cave that fills the first share still gets them.
		/// </summary>
		public const int MaxSpots = 1120;
		/// <summary>The spots kept for the light-makers' homes (<c>Lights</c>, 2026-10-20): the low glow air and the patches by it.</summary>
		public const int MaxLightSpots = 160;
		/// <summary>The most a floor, wall or roof patch may stray from its plane, metres, anywhere within its radius.</summary>
		public const float Flatness = 0.07f;
		/// <summary>The air kept round every corner and middle of a box of flight, as the field reads it, metres.</summary>
		public const float FlightClearance = 0.35f;

		/// <summary>The spots of a system (world metres), in a fixed order: its chambers', its passages', its mouths'.</summary>
		public static List<SceneCaveLifeSpot> Bake(CaveSystem system, int siteId)
		{
			var spots = new List<SceneCaveLifeSpot>();
			if (system == null)
			{
				return spots;
			}
			new Baker(system, siteId, spots).Run();
			return spots;
		}

		/// <summary>The cave-life form a cave's form is (the same ordinals: a test keeps them so).</summary>
		public static SceneCaveForm FormOf(CaveForm form) => (SceneCaveForm)(byte)form;

		private sealed class Baker
		{
			private readonly CaveSystem system;
			private readonly int siteId;
			private readonly List<SceneCaveLifeSpot> spots;
			private readonly DeterministicRNG random;
			private readonly SceneCaveForm form;
			/// <summary>The water standing in the cave (system frame), NaN for none; and whether it is the sea.</summary>
			private readonly float water;
			private readonly bool salt;
			/// <summary>Limestone forms drip: their deep floors are damp.</summary>
			private readonly bool dripping;
			/// <summary>Where the cave's glow gathers (<see cref="CaveHabitat"/>: the rock, the cover and the dressing agree on it).</summary>
			private readonly CaveHabitat habitat;
			/// <summary>The light-makers' own stream (<see cref="Lights"/>).</summary>
			private readonly DeterministicRNG lights;
			private readonly uint noiseSalt;
			/// <summary>How many spots the current pass may fill the list to.</summary>
			private int cap = MaxSpots - MaxLightSpots;

			public Baker(CaveSystem system, int siteId, List<SceneCaveLifeSpot> spots)
			{
				this.system = system;
				this.siteId = siteId;
				this.spots = spots;
				// Its own stream: the dressing's formations, glow, climbs and camps all draw from theirs, untouched by this.
				random = new DeterministicRNG(unchecked(system.Seed * 0x6C1B + 0x3F57));
				form = FormOf(system.Form);
				water = float.IsNaN(system.WaterLevel) ? float.NaN : system.WaterLevel - system.Origin.y;
				// A mouth under a lake or the sea floods everything below the water outside it (the sump rising from it, and any
				// hollow it opens into at that depth): its water is the cave's too.
				foreach (CaveMouth mouth in system.Mouths)
				{
					if (mouth.Kind == CaveMouthKind.Submerged)
					{
						float level = mouth.Water - system.Origin.y;
						water = float.IsNaN(water) ? level : Mathf.Max(water, level);
					}
				}
				// A sea cave is the sea's; a grotto opening on the sea stands at its mean level (the sea is at 0), an inland one at
				// its pool's.
				salt = system.Form == CaveForm.SeaCave || (system.Form == CaveForm.Grotto && !float.IsNaN(system.WaterLevel) && Mathf.Abs(system.WaterLevel) < 0.05f);
				dripping = system.Form == CaveForm.Cave || system.Form == CaveForm.Grotto || system.Form == CaveForm.SeaCave;
				habitat = CaveHabitat.For(system);
				// The light-makers' homes draw from a stream of their own, so the spots above them bake as they always did.
				lights = new DeterministicRNG(unchecked(system.Seed * 0x2F1D + 0x6A3B));
				noiseSalt = unchecked((uint)system.Seed * 0x9E3779B1u ^ 0x61C8u);
			}

			public void Run()
			{
				foreach (CaveChamber chamber in system.Chambers)
				{
					Chamber(chamber);
				}
				foreach (CavePassage passage in system.Passages)
				{
					Passage(passage);
				}
				for (int i = 0; i < system.Mouths.Count; i++)
				{
					Mouth(system.Mouths[i]);
				}
				// Last, so every spot above keeps its index: the light-makers' homes.
				cap = MaxSpots;
				Lights();
			}

			// ── The light-makers' homes (2026-10-20) ──────────────

			/// <summary>
			/// Homes for the cave's light-makers (glowing millipedes, snails, beetles, newts and worms on the rock; spore motes and wisp
			/// moths in the air), kept low where a person walks (2026-10-19: a colony 19 m up a hall is never seen): boxes of glow air a
			/// metre or two over the floor, each with a patch of floor and of wall by it, in the chambers and every so often along the
			/// ways, and over the pools. Thickest where the cave's glow gathers (<see cref="GlowAt"/>), as the glowing fungus is.
			/// Lava tubes and ice caves get only the patches: their light-makers crawl (ember mites, ice worms).
			/// </summary>
			private void Lights()
			{
				bool air = system.Form != CaveForm.LavaTube && system.Form != CaveForm.IceCave;
				foreach (CaveChamber c in system.Chambers)
				{
					float yaw = c.Yaw * Mathf.Deg2Rad;
					var along = new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));
					var across = new Vector3(along.z, 0f, -along.x);
					float area = Mathf.PI * c.Radii.x * c.Radii.z;
					bool great = c.Kind == CaveChamberKind.Cavern || c.Kind == CaveChamberKind.Hall;
					int tries = Mathf.Clamp(Mathf.RoundToInt(area / 90f), 1, great ? 12 : 3);
					for (int i = 0; i < tries; i++)
					{
						float a = lights.Range(0f, 2f * Mathf.PI), t = Mathf.Sqrt(lights.Range(0.05f, 0.8f));
						float keep = lights.NextFloat();
						Vector3 p = InChamber(c, along, across, a, t);
						LightHome(p, c.Yaw, Mathf.Clamp(0.2f * Mathf.Min(c.Radii.x, c.Radii.z), 1.2f, 2.4f), keep, air);
					}
				}
				foreach (CavePassage passage in system.Passages)
				{
					List<CaveNode> nodes = passage.Nodes;
					if (nodes.Count < 2)
					{
						continue;
					}
					float start = nodes[0].Along, end = nodes[nodes.Count - 1].Along;
					for (float at = start + lights.Range(4f, 12f); at < end - 2f; at += lights.Range(14f, 26f))
					{
						float keep = lights.NextFloat();
						CentreAt(nodes, at, out Vector3 centre, out float floor, out float radius);
						CentreAt(nodes, Mathf.Min(end, at + 0.5f), out Vector3 ahead, out _, out _);
						Vector3 dir = ahead - centre;
						float yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
						LightHome(new Vector3(centre.x, floor, centre.z), yaw, Mathf.Clamp(0.5f * radius, 0.6f, 1.6f), keep, air);
					}
				}
				// Over the water: the glow air a person sees hanging over a still pool, doubled in it.
				if (air)
				{
					int count = spots.Count;
					for (int i = 0; i < count; i++)
					{
						if (spots[i].Kind != SceneCaveLifeKind.Pool)
						{
							continue;
						}
						Vector3 surface = spots[i].Position - system.Origin;
						float half = Mathf.Clamp(spots[i].Extent.x, 0.8f, 2.2f);
						TryBox(SceneCaveLifeKind.Glow, surface + Vector3.up * 1.4f, lights.Range(0f, 360f), new Vector3(half, 0.7f, half), new Vector3(0.6f, 0.4f, 0.6f));
					}
				}
			}

			/// <summary>
			/// One light-makers' home round a point of floor (system frame), kept by the glow there (about one in two where there is
			/// none, all of them where it gathers): a box of glow air one to three metres over the floor, a patch of floor and one of wall.
			/// </summary>
			private void LightHome(Vector3 near, float yaw, float patch, float keep, bool air)
			{
				if (!Ground(near + Vector3.up * 1.2f, 4f, out float floor))
				{
					return;
				}
				var at = new Vector3(near.x, floor, near.z);
				if (keep > Mathf.Lerp(0.45f, 1f, GlowAt(at, Vector3.up)))
				{
					return;
				}
				float rise = lights.Range(1.4f, 2.8f);
				float half = lights.Range(1.1f, 2f);
				Vector3 toWall = Dir(lights.Range(0f, 360f));
				float height = lights.Range(0.3f, 1.4f);
				if (air && Roof(at + Vector3.up * 0.5f, 40f, out float roof) && roof - floor >= 2.6f)
				{
					float top = Mathf.Min(rise, roof - floor - 1.1f);
					TryBox(SceneCaveLifeKind.Glow, at + Vector3.up * Mathf.Max(1.3f, top), yaw, new Vector3(half, 0.7f, half), new Vector3(0.6f, 0.4f, 0.6f));
				}
				TryFloor(at + toWall * (0.5f * patch), patch);
				TryWall(at + Vector3.up * Mathf.Max(0.3f, height), toWall, 12f);
			}

			/// <summary>
			/// How much the cave's glow gathers at a point of its rock, 0..1: the habitat's (<see cref="CaveHabitatSample.Glow"/>, what
			/// the rock's and the cover's glow follow), and a field of the system's own seed where the habitat says nothing (patches
			/// some twenty metres across), so a cave keeps its glowing reaches either way.
			/// </summary>
			private float GlowAt(Vector3 local, Vector3 normal)
			{
				float rock = Mathf.Clamp01(habitat.Sample(local, normal).Glow);
				float own = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 0.75f, ValueNoise(local / 20f, noiseSalt)));
				return Mathf.Max(rock, (rock > 0f ? 0.5f : 1f) * own);
			}

			/// <summary>Smooth value noise, 0..1, from an integer lattice hash (pure: the same on every machine).</summary>
			private static float ValueNoise(Vector3 p, uint salt)
			{
				int x = Mathf.FloorToInt(p.x), y = Mathf.FloorToInt(p.y), z = Mathf.FloorToInt(p.z);
				float fx = p.x - x, fy = p.y - y, fz = p.z - z;
				fx = fx * fx * (3f - 2f * fx);
				fy = fy * fy * (3f - 2f * fy);
				fz = fz * fz * (3f - 2f * fz);
				float Corner(int i, int j, int k)
				{
					unchecked
					{
						uint h = (uint)(x + i) * 73856093u ^ (uint)(y + j) * 19349663u ^ (uint)(z + k) * 83492791u ^ salt;
						h ^= h >> 16;
						h *= 0x7FEB352Du;
						h ^= h >> 15;
						h *= 0x846CA68Bu;
						h ^= h >> 16;
						return (h >> 8) * (1f / 16777216f);
					}
				}
				float a = Mathf.Lerp(Corner(0, 0, 0), Corner(1, 0, 0), fx), b = Mathf.Lerp(Corner(0, 1, 0), Corner(1, 1, 0), fx);
				float c = Mathf.Lerp(Corner(0, 0, 1), Corner(1, 0, 1), fx), d = Mathf.Lerp(Corner(0, 1, 1), Corner(1, 1, 1), fx);
				return Mathf.Lerp(Mathf.Lerp(a, b, fy), Mathf.Lerp(c, d, fy), fz);
			}

			// ── Chambers ──────────────────────────────────────────

			private void Chamber(in CaveChamber c)
			{
				float yaw = c.Yaw * Mathf.Deg2Rad;
				var along = new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));
				var across = new Vector3(along.z, 0f, -along.x);
				float area = Mathf.PI * c.Radii.x * c.Radii.z;
				float small = Mathf.Min(c.Radii.x, c.Radii.z), reach = Mathf.Max(c.Radii.x, c.Radii.z);
				bool great = c.Kind == CaveChamberKind.Cavern || c.Kind == CaveChamberKind.Hall;
				var middle = new Vector3(c.Centre.x, CaveSystem.FloorOf(c, c.Centre), c.Centre.z);
				Ground(middle + Vector3.up * 1.2f, 6f, out float middleFloor);
				float clear = Roof(new Vector3(middle.x, middleFloor + 1f, middle.z), 100f, out float middleRoof) ? middleRoof - middleFloor : 0f;

				// The floor: patches spread over it, one to every 35 m² or so (a great hall's few dozen), so no room is bare.
				int floors = Mathf.Clamp(Mathf.RoundToInt(area / 35f), 2, great ? 40 : 10);
				for (int i = 0; i < floors; i++)
				{
					float a = random.Range(0f, 2f * Mathf.PI), t = Mathf.Sqrt(random.Range(0.02f, 0.75f));
					Vector3 p = InChamber(c, along, across, a, t);
					TryFloor(p, Mathf.Clamp(0.25f * small, 0.8f, 2.4f));
				}
				// The walls: at a reach's height or so, round the room, one to every 10 m of its round.
				float round = 2f * Mathf.PI * Mathf.Sqrt(0.5f * (c.Radii.x * c.Radii.x + c.Radii.z * c.Radii.z));
				int walls = Mathf.Clamp(Mathf.RoundToInt(round / 10f), 2, great ? 24 : 8);
				for (int i = 0; i < walls; i++)
				{
					float a = random.Range(0f, 2f * Mathf.PI), h = random.Range(0.4f, 2.6f);
					Vector3 dir = along * Mathf.Cos(a) + across * Mathf.Sin(a);
					TryWall(new Vector3(middle.x, middleFloor + Mathf.Min(h, Mathf.Max(0.4f, clear - 1f)), middle.z), dir, reach + 3f);
				}
				// The roof: where a colony hangs, only over air a person walks under without touching them.
				if (clear >= 4f)
				{
					int roosts = great ? random.Range(3, 8) : random.Range(1, 3);
					for (int i = 0; i < roosts; i++)
					{
						float a = random.Range(0f, 2f * Mathf.PI), t = Mathf.Sqrt(random.Range(0f, 0.35f));
						Vector3 p = InChamber(c, along, across, a, t);
						TryRoost(new Vector3(p.x, p.y + 1.5f, p.z));
					}
				}
				// The air: a box of the room's middle, if it is clear enough to wheel in; a great hall (to 250 m across since
				// 2026-10-18) a few, each no wider than a colony keeps to (and than a box's 27 points can vouch for: a column or a
				// ramp standing in a hall's middle is missed by a lattice 75 m apart).
				int boxes = great && reach > 30f ? 3 : 1;
				for (int i = 0; i < boxes; i++)
				{
					float a = random.Range(0f, 2f * Mathf.PI), t = i == 0 ? 0f : Mathf.Sqrt(random.Range(0.1f, 0.45f));
					Vector3 p = i == 0 ? middle : InChamber(c, along, across, a, t);
					float floor = middleFloor, room = clear;
					if (i > 0 && !(Ground(p + Vector3.up * 1.2f, 6f, out floor) && Roof(new Vector3(p.x, floor + 1f, p.z), 100f, out float roof) && (room = roof - floor) > 0f))
					{
						continue;
					}
					if (room < 2.5f)
					{
						continue;
					}
					float height = Mathf.Clamp(0.5f * room, 1.2f, 9f);
					var centre = new Vector3(p.x, floor + height, p.z);
					var halves = new Vector3(Mathf.Min(0.6f * c.Radii.x, 18f), Mathf.Min(0.35f * room, 6f), Mathf.Min(0.6f * c.Radii.z, 18f));
					TryBox(SceneCaveLifeKind.Flight, centre, c.Yaw, halves, new Vector3(1.4f, 0.6f, 1.4f));
				}
				// Under a high roof in a limestone cave (the glow-worms' still damp air, as the glow hangs them: 4–30 m up).
				if ((system.Form == CaveForm.Cave || system.Form == CaveForm.Grotto) && reach >= 9f && clear >= 4f && clear <= 30f)
				{
					int glows = great ? 2 : 1;
					for (int i = 0; i < glows; i++)
					{
						float a = random.Range(0f, 2f * Mathf.PI), t = Mathf.Sqrt(random.Range(0f, 0.5f)), drop = random.Range(1.2f, 2.2f);
						Vector3 p = InChamber(c, along, across, a, t);
						if (Ground(new Vector3(p.x, p.y + 1f, p.z), 4f, out float floor) && Roof(new Vector3(p.x, floor + 1f, p.z), 32f, out float roof) && roof - floor >= 4f)
						{
							TryBox(SceneCaveLifeKind.Glow, new Vector3(p.x, roof - drop, p.z), c.Yaw, new Vector3(1.8f, 0.7f, 1.8f), new Vector3(0.8f, 0.4f, 0.8f));
						}
					}
				}
				// The water: where the room's floor goes under it (a grotto's front, a sea cave's tide).
				if (!float.IsNaN(water))
				{
					for (int i = 0; i < 3; i++)
					{
						float t = 0.25f * i;
						Vector3 p = middle - along * (t * c.Radii.x);
						if (TryPool(p))
						{
							break;
						}
					}
				}
			}

			/// <summary>A point in a chamber's plan at a share <paramref name="t"/> of the way out along heading <paramref name="angle"/> (radians), on its floor's plane.</summary>
			private static Vector3 InChamber(in CaveChamber c, Vector3 along, Vector3 across, float angle, float t)
			{
				Vector3 p = c.Centre + along * (Mathf.Cos(angle) * t * c.Radii.x) + across * (Mathf.Sin(angle) * t * c.Radii.z);
				p.y = CaveSystem.FloorOf(c, p);
				return p;
			}

			// ── Passages ──────────────────────────────────────────

			private void Passage(CavePassage passage)
			{
				List<CaveNode> nodes = passage.Nodes;
				if (nodes.Count < 2)
				{
					return;
				}
				float start = nodes[0].Along, end = nodes[nodes.Count - 1].Along;
				bool trunk = passage.Kind == CavePassageKind.Trunk;
				// A trunk's first few metres are the mouth's (its own spot); a branch's or a link's open off a chamber.
				for (float along = start + (trunk ? 3f : 2f); along < end - 1f; along += random.Range(5f, 9f))
				{
					CentreAt(nodes, along, out Vector3 centre, out float floor, out float radius);
					CentreAt(nodes, Mathf.Min(end, along + 0.5f), out Vector3 ahead, out _, out _);
					Vector3 dir = ahead - centre;
					dir.y = 0f;
					dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.forward;
					var across = new Vector3(dir.z, 0f, -dir.x);
					float offset = random.Range(-0.45f, 0.45f) * radius;
					float side = random.NextFloat() < 0.5f ? -1f : 1f;
					float height = random.Range(0.4f, 1.6f);
					bool wall = random.NextFloat() < 0.85f;
					TryFloor(new Vector3(centre.x, floor, centre.z) + across * offset, Mathf.Clamp(0.6f * radius, 0.6f, 1.8f));
					if (wall)
					{
						TryWall(new Vector3(centre.x, floor + height, centre.z), across * side, 2f * radius + 1f);
					}
				}
				// The air just in from a mouth, where the twilight's moths flutter and a colony circles before it goes out.
				if (trunk && system.Mouths.Count > 0)
				{
					CaveMouth mouth = NearestMouth(nodes[0].Centre);
					float light = Daylight(mouth);
					float at = Mathf.Min(start + 0.6f * light, Mathf.Lerp(start, end, 0.5f));
					CentreAt(nodes, at, out Vector3 centre, out float floor, out float radius);
					CentreAt(nodes, Mathf.Min(end, at + 0.5f), out Vector3 ahead, out _, out _);
					Vector3 dir = ahead - centre;
					float yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
					if (Roof(new Vector3(centre.x, floor + 1f, centre.z), 30f, out float roof) && roof - floor >= 2.4f)
					{
						float half = 0.5f * (roof - floor);
						TryBox(SceneCaveLifeKind.Flight, new Vector3(centre.x, floor + half, centre.z), yaw,
							new Vector3(Mathf.Min(6f, 0.25f * (end - start)), 0.6f * half, 0.6f * radius), new Vector3(1.2f, 0.5f, 0.6f));
					}
				}
			}

			/// <summary>The passage's centre line, nominal floor and bore at an arc length along it (system frame), between its nodes.</summary>
			private static void CentreAt(List<CaveNode> nodes, float along, out Vector3 centre, out float floor, out float radius)
			{
				int i = 1;
				while (i < nodes.Count - 1 && nodes[i].Along < along)
				{
					i++;
				}
				CaveNode a = nodes[i - 1], b = nodes[i];
				float t = Mathf.Clamp01((along - a.Along) / Mathf.Max(1e-4f, b.Along - a.Along));
				centre = Vector3.Lerp(a.Centre, b.Centre, t);
				floor = Mathf.Lerp(a.Floor, b.Floor, t);
				radius = Mathf.Lerp(a.Radius, b.Radius, t);
			}

			// ── Mouths ────────────────────────────────────────────

			/// <summary>
			/// The way out: in the mouth's bore a little over its floor, facing out, and as far back in as a straight line stays
			/// clear of the rock (a colony streams out along it at dusk and back before dawn).
			/// </summary>
			private void Mouth(in CaveMouth mouth)
			{
				if (mouth.Kind == CaveMouthKind.Submerged)
				{
					return; // dived into: nothing flies out of it
				}
				if (mouth.Kind == CaveMouthKind.Skylight)
				{
					return; // an opening in a roof: the colony's way out is a passage's, not a climb up a shaft
				}
				Vector3 outward = Dir(mouth.Yaw);
				Vector3 at = mouth.Floor + Vector3.up * Mathf.Max(1.2f, 0.75f * mouth.Radius);
				if (system.Void(at) > -0.2f)
				{
					// The mouth's bore is lower than that: halfway up it.
					at = mouth.Floor + Vector3.up * Mathf.Max(0.8f, 0.5f * mouth.Radius);
					if (system.Void(at) > -0.2f)
					{
						return;
					}
				}
				float run = 0f;
				for (float d = 0.5f; d <= 30f; d += 0.5f)
				{
					if (system.Void(at - outward * d) > -0.4f)
					{
						break;
					}
					run = d;
				}
				if (run < 4f)
				{
					return;
				}
				Add(new SceneCaveLifeSpot
				{
					Kind = SceneCaveLifeKind.Mouth,
					Position = at,
					Normal = outward,
					Extent = new Vector3(mouth.Radius, 0f, run),
					Yaw = mouth.Yaw,
					Dark = 0f,
				});
			}

			// ── Patches ───────────────────────────────────────────

			/// <summary>A patch of floor round a point (system frame), as wide as it is flat; false where there is none.</summary>
			private bool TryFloor(Vector3 near, float maxRadius)
			{
				if (!Ground(near + Vector3.up * 1f, 4f, out float floor))
				{
					return false;
				}
				var centre = new Vector3(near.x, floor, near.z);
				if (!float.IsNaN(water) && floor < water + 0.05f)
				{
					return false; // under the cave's water: the pool's, not the floor's
				}
				if (!Roof(centre + Vector3.up * 0.3f, 40f, out float roof) || roof - floor < 0.6f)
				{
					return false;
				}
				for (float radius = maxRadius; radius >= 0.35f; radius *= 0.6f)
				{
					if (FloorPlane(centre, radius, out Vector3 normal, out float lift))
					{
						Vector3 position = centre + Vector3.up * lift;
						Add(new SceneCaveLifeSpot
						{
							Kind = SceneCaveLifeKind.Floor,
							Flags = FloorFlags(floor),
							Position = position,
							Normal = normal,
							Extent = new Vector3(radius, roof - floor, 0f),
						});
						return true;
					}
				}
				return false;
			}

			/// <summary>
			/// The plane through the floor round a centre: sixteen points on two rings, fitted by least squares; true when every
			/// one lies within <see cref="Flatness"/> of it and it is walkable (under 35°).
			/// </summary>
			private bool FloorPlane(Vector3 centre, float radius, out Vector3 normal, out float lift)
			{
				normal = Vector3.up;
				lift = 0f;
				const int count = 17;
				Span<Vector3> points = stackalloc Vector3[count];
				points[0] = Vector3.zero;
				for (int k = 0; k < 16; k++)
				{
					float a = k * Mathf.PI / 4f + (k >= 8 ? Mathf.PI / 8f : 0f);
					float r = k < 8 ? radius : 0.5f * radius;
					var q = new Vector3(centre.x + Mathf.Cos(a) * r, centre.y, centre.z + Mathf.Sin(a) * r);
					if (!Ground(q + Vector3.up * (0.6f + 0.7f * radius), 1.6f + 1.4f * radius, out float h))
					{
						return false;
					}
					points[k + 1] = new Vector3(q.x - centre.x, h - centre.y, q.z - centre.z);
				}
				if (!FitPlane(points, out float gx, out float gz, out float d) || gx * gx + gz * gz > 0.49f)
				{
					return false;
				}
				for (int k = 0; k < count; k++)
				{
					if (Mathf.Abs(points[k].y - (gx * points[k].x + gz * points[k].z + d)) > Flatness)
					{
						return false;
					}
				}
				normal = new Vector3(-gx, 1f, -gz).normalized;
				lift = d;
				return true;
			}

			/// <summary>A patch of wall where a ray along <paramref name="dir"/> from a point meets it, as wide as it is flat.</summary>
			private bool TryWall(Vector3 from, Vector3 dir, float reach)
			{
				if (!Hit(from, dir, reach, out Vector3 rock))
				{
					return false;
				}
				Vector3 normal = Outward(rock);
				if (Mathf.Abs(normal.y) > 0.45f)
				{
					return false; // the ray met the floor rising, or a roof: not a wall
				}
				return TrySheet(SceneCaveLifeKind.Wall, rock, normal, 1.2f, 0.3f, 20f);
			}

			/// <summary>A patch of roof over a point, high over the floor: where a colony hangs.</summary>
			private bool TryRoost(Vector3 under)
			{
				if (!Ground(under, 6f, out float floor) || !Roof(under, 40f, out float roof) || roof - floor < 3f)
				{
					return false;
				}
				var rock = new Vector3(under.x, roof, under.z);
				Vector3 normal = Outward(rock - Vector3.up * 0.05f);
				if (normal.y > -0.6f)
				{
					return false; // a sloping vault's side: nothing hangs from it
				}
				return TrySheet(SceneCaveLifeKind.Roost, rock, normal, 1.8f, 0.35f, roof - floor);
			}

			/// <summary>
			/// A patch of rock (a wall or a roof) at <paramref name="rock"/> facing <paramref name="normal"/>: kept as wide as eight
			/// points round it on its plane each meet the rock within <see cref="Flatness"/> of it.
			/// </summary>
			private bool TrySheet(SceneCaveLifeKind kind, Vector3 rock, Vector3 normal, float maxRadius, float minRadius, float clearanceReach)
			{
				Vector3 t1 = Vector3.Cross(Mathf.Abs(normal.y) > 0.9f ? Vector3.forward : Vector3.up, normal).normalized;
				Vector3 t2 = Vector3.Cross(normal, t1);
				for (float radius = maxRadius; radius >= minRadius; radius *= 0.6f)
				{
					bool flat = true;
					for (int k = 0; k < 8 && flat; k++)
					{
						float a = k * Mathf.PI / 4f;
						Vector3 q = rock + (t1 * Mathf.Cos(a) + t2 * Mathf.Sin(a)) * radius;
						// From a hand's breadth out of the plane, back along the normal to the rock.
						flat = Hit(q + normal * 0.5f, -normal, 0.5f + 3f * Flatness, out Vector3 met)
							&& Mathf.Abs(Vector3.Dot(met - rock, normal)) <= Flatness;
					}
					if (!flat)
					{
						continue;
					}
					float clearance = 0f;
					for (float d = 0.5f; d <= clearanceReach; d += 0.5f)
					{
						if (system.Void(rock + normal * d) >= 0f)
						{
							break;
						}
						clearance = d;
					}
					if (clearance < 0.5f)
					{
						return false;
					}
					Add(new SceneCaveLifeSpot
					{
						Kind = kind,
						Flags = kind == SceneCaveLifeKind.Wall ? FloorFlags(rock.y - 1f) : SceneCaveLifeFlags.None,
						Position = rock,
						Normal = normal,
						Extent = new Vector3(radius, clearance, 0f),
					});
					return true;
				}
				return false;
			}

			/// <summary>
			/// A box of air (a flight's, a glow's) about a centre, turned to <paramref name="yaw"/>: its corners, faces' middles and
			/// middle each <see cref="FlightClearance"/> clear of rock, shrunk a fifth at a time until they are, no smaller than
			/// <paramref name="least"/>.
			/// </summary>
			private bool TryBox(SceneCaveLifeKind kind, Vector3 centre, float yaw, Vector3 halves, Vector3 least)
			{
				Vector3 along = Dir(yaw), across = new Vector3(along.z, 0f, -along.x);
				for (int attempt = 0; attempt < 6; attempt++)
				{
					if (halves.x < least.x || halves.y < least.y || halves.z < least.z)
					{
						return false;
					}
					bool clear = true;
					for (int i = -1; i <= 1 && clear; i++)
					{
						for (int j = -1; j <= 1 && clear; j++)
						{
							for (int k = -1; k <= 1 && clear; k++)
							{
								Vector3 q = centre + along * (i * halves.x) + Vector3.up * (j * halves.y) + across * (k * halves.z);
								clear = system.Void(q) < -FlightClearance;
							}
						}
					}
					if (clear)
					{
						Add(new SceneCaveLifeSpot
						{
							Kind = kind,
							Flags = kind == SceneCaveLifeKind.Glow ? SceneCaveLifeFlags.Damp : SceneCaveLifeFlags.None,
							Position = centre,
							Normal = Vector3.up,
							Extent = halves,
							Yaw = yaw,
						});
						return true;
					}
					halves *= 0.8f;
				}
				return false;
			}

			/// <summary>The cave's water over a point of floor (system frame), at least a forearm deep: a pool spot as wide as it is so.</summary>
			private bool TryPool(Vector3 near)
			{
				if (system.Void(new Vector3(near.x, water - 0.05f, near.z)) >= 0f || !Ground(new Vector3(near.x, water - 0.05f, near.z), 6f, out float bed) || water - bed < 0.3f)
				{
					return false;
				}
				for (float radius = 3f; radius >= 0.6f; radius *= 0.65f)
				{
					bool deep = true;
					for (int k = 0; k < 8 && deep; k++)
					{
						float a = k * Mathf.PI / 4f;
						var q = new Vector3(near.x + Mathf.Cos(a) * radius, water - 0.05f, near.z + Mathf.Sin(a) * radius);
						deep = Ground(q, 6f, out float under) && water - under >= 0.2f;
					}
					if (deep)
					{
						Add(new SceneCaveLifeSpot
						{
							Kind = SceneCaveLifeKind.Pool,
							Flags = SceneCaveLifeFlags.Damp | (salt ? SceneCaveLifeFlags.Salt : SceneCaveLifeFlags.None),
							Position = new Vector3(near.x, water, near.z),
							Normal = Vector3.up,
							Extent = new Vector3(radius, water - bed, 0f),
						});
						return true;
					}
				}
				return false;
			}

			/// <summary>A floor's damp: by the cave's water, or (a limestone cave's) under its dripping roof, one in two.</summary>
			private SceneCaveLifeFlags FloorFlags(float floor)
			{
				bool wet = !float.IsNaN(water) && floor < water + 2.5f;
				SceneCaveLifeFlags flags = SceneCaveLifeFlags.None;
				if (wet)
				{
					flags |= SceneCaveLifeFlags.Damp | (salt ? SceneCaveLifeFlags.Salt : SceneCaveLifeFlags.None);
				}
				else if (dripping && random.NextFloat() < 0.5f)
				{
					flags |= SceneCaveLifeFlags.Damp;
				}
				return flags;
			}

			// ── The dark ──────────────────────────────────────────

			/// <summary>
			/// How far the daylight carries in at a mouth, metres: five bore radii, 8–24 m (the overgrowth's reach); a mouth under
			/// water lets in only the little the water passes, a third as far.
			/// </summary>
			private static float Daylight(in CaveMouth mouth) => Mathf.Clamp(5f * mouth.Radius, 8f, 24f) * (mouth.Kind == CaveMouthKind.Submerged ? 0.35f : 1f);

			/// <summary>0 in a mouth's light .. 1 a dozen metres past it, from the nearest mouth (system frame).</summary>
			public float DarkAt(Vector3 local)
			{
				float dark = 1f;
				foreach (CaveMouth mouth in system.Mouths)
				{
					float light = Daylight(mouth);
					dark = Mathf.Min(dark, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.5f * light, light + 12f, Vector3.Distance(local, mouth.Floor))));
				}
				return dark;
			}

			private CaveMouth NearestMouth(Vector3 local)
			{
				CaveMouth best = system.Mouths[0];
				float d = float.MaxValue;
				foreach (CaveMouth mouth in system.Mouths)
				{
					float m = (mouth.Floor - local).sqrMagnitude;
					if (m < d)
					{
						d = m;
						best = mouth;
					}
				}
				return best;
			}

			// ── The field ─────────────────────────────────────────

			private void Add(SceneCaveLifeSpot spot)
			{
				if (spots.Count >= cap)
				{
					return;
				}
				// The dark from where the animal is (a mouth is the daylight itself), then into the world's frame.
				if (spot.Kind != SceneCaveLifeKind.Mouth)
				{
					spot.Dark = DarkAt(spot.Position);
				}
				spot.Form = form;
				spot.SiteId = siteId;
				spot.Position += system.Origin;
				spots.Add(spot);
			}

			/// <summary>The floor under a point in the void (system frame), marched down and bisected; false where it is rock or none is in reach.</summary>
			private bool Ground(Vector3 p, float reach, out float floor)
			{
				floor = p.y;
				if (system.Void(p) >= 0f)
				{
					return false;
				}
				const float step = 0.2f;
				float y = p.y;
				while (system.Void(new Vector3(p.x, y - step, p.z)) < 0f)
				{
					y -= step;
					if (y < p.y - reach)
					{
						return false;
					}
				}
				float lo = y - step, hi = y;
				for (int k = 0; k < 5; k++)
				{
					float mid = 0.5f * (lo + hi);
					if (system.Void(new Vector3(p.x, mid, p.z)) < 0f)
					{
						hi = mid;
					}
					else
					{
						lo = mid;
					}
				}
				floor = 0.5f * (lo + hi);
				return true;
			}

			/// <summary>The roof over a point in the void (system frame), as <see cref="Ground"/> marches the floor.</summary>
			private bool Roof(Vector3 p, float reach, out float roof)
			{
				roof = p.y;
				if (system.Void(p) >= 0f)
				{
					return false;
				}
				const float step = 0.25f;
				float y = p.y;
				while (system.Void(new Vector3(p.x, y + step, p.z)) < 0f)
				{
					y += step;
					if (y > p.y + reach)
					{
						return false;
					}
				}
				float lo = y, hi = y + step;
				for (int k = 0; k < 5; k++)
				{
					float mid = 0.5f * (lo + hi);
					if (system.Void(new Vector3(p.x, mid, p.z)) < 0f)
					{
						lo = mid;
					}
					else
					{
						hi = mid;
					}
				}
				roof = 0.5f * (lo + hi);
				return true;
			}

			/// <summary>Where a ray from a point in the void along a unit direction first meets rock, to a few centimetres; false within reach of none.</summary>
			private bool Hit(Vector3 from, Vector3 dir, float reach, out Vector3 rock)
			{
				rock = from;
				if (system.Void(from) >= 0f)
				{
					return false;
				}
				// A tenth of a metre at a hand's reach, coarser across a great hall (a 250 m ray in 0.1 m steps is 2500 reads),
				// bisected to the same few centimetres either way.
				float step = Mathf.Clamp(reach / 60f, 0.1f, 0.5f);
				float last = 0f;
				for (float d = step; d <= reach + 1e-4f; d += step)
				{
					if (system.Void(from + dir * d) >= 0f)
					{
						float lo = last, hi = d;
						for (int k = 0; k < 7; k++)
						{
							float mid = 0.5f * (lo + hi);
							if (system.Void(from + dir * mid) >= 0f)
							{
								hi = mid;
							}
							else
							{
								lo = mid;
							}
						}
						rock = from + dir * (0.5f * (lo + hi));
						return true;
					}
					last = d;
				}
				return false;
			}

			/// <summary>The way out of the rock into the air at a point on it: down the field's slope (the void reads below zero).</summary>
			private Vector3 Outward(Vector3 p)
			{
				const float e = 0.12f;
				var g = new Vector3(
					system.Void(p + Vector3.right * e) - system.Void(p - Vector3.right * e),
					system.Void(p + Vector3.up * e) - system.Void(p - Vector3.up * e),
					system.Void(p + Vector3.forward * e) - system.Void(p - Vector3.forward * e));
				return g.sqrMagnitude > 1e-12f ? -g.normalized : Vector3.up;
			}

			/// <summary>
			/// A plane y = gx·x + gz·z + d through points by least squares (their x and z about the first's). False for a degenerate set.
			/// </summary>
			private static bool FitPlane(Span<Vector3> points, out float gx, out float gz, out float d)
			{
				double sx = 0, sz = 0, sy = 0, sxx = 0, szz = 0, sxz = 0, sxy = 0, szy = 0;
				int n = points.Length;
				for (int i = 0; i < n; i++)
				{
					Vector3 p = points[i];
					sx += p.x; sz += p.z; sy += p.y;
					sxx += p.x * p.x; szz += p.z * p.z; sxz += p.x * p.z;
					sxy += p.x * p.y; szy += p.z * p.y;
				}
				// Centred sums: the normal equations of the slope, then the offset through the mean.
				double mx = sx / n, mz = sz / n, my = sy / n;
				double cxx = sxx - n * mx * mx, czz = szz - n * mz * mz, cxz = sxz - n * mx * mz;
				double cxy = sxy - n * mx * my, czy = szy - n * mz * my;
				double det = cxx * czz - cxz * cxz;
				if (Math.Abs(det) < 1e-9)
				{
					gx = gz = d = 0f;
					return false;
				}
				gx = (float)((cxy * czz - czy * cxz) / det);
				gz = (float)((czy * cxx - cxy * cxz) / det);
				d = (float)(my - gx * mx - gz * mz);
				return true;
			}

			private static Vector3 Dir(float yaw)
			{
				float r = yaw * Mathf.Deg2Rad;
				return new Vector3(Mathf.Sin(r), 0f, Mathf.Cos(r));
			}
		}
	}
}
#endif
