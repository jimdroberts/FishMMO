#if UNITY_EDITOR
using System.Collections.Generic;
using FishMMO.Shared.Biomes;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>How much of what hangs from a cave's roof a cave of a form holds (<see cref="CaveHanging.For"/>), each 1 a limestone cave's.</summary>
	public struct CaveHangingPlan
	{
		/// <summary>How thickly glow-worm silk lines the damp roof joints in the dark (0: none).</summary>
		public float Silk;
		/// <summary>How readily tree roots come down through a thin roof in the dark, where trees grow over it (0: none).</summary>
		public float DeepRoots;
		/// <summary>How readily guano heaps under a roost (0: none): the habitat's guano for the form (<see cref="CaveHabitat.TraitsOf"/>).</summary>
		public float Guano;
	}

	/// <summary>
	/// The ONE lookup of what hangs from a cave's roof and piles under it (2026-10-20, caves livened), and the rules its pieces
	/// keep: pure, so the biome suite can move the choices into template fields later. By form, then by the biome over the roof
	/// (<see cref="Wooded"/>: whether trees grow there to send their roots down).
	/// </summary>
	public static class CaveHanging
	{
		/// <summary>Tree roots come through at most this much rock and soil, metres: most go a few metres down the joints, a few ten.</summary>
		public const float RootCoverMax = 10f;
		/// <summary>Under this much the roof is the soil's own underside, metres (the near-mouth roots' and the cover's).</summary>
		public const float RootCoverMin = 0.3f;
		/// <summary>The trees per 100 m² over a biome's ground that make it a wood for its roots (a forest rule is 1–4).</summary>
		public const float WoodTrees = 2f;
		/// <summary>The room a tap root hangs to the floor across, metres: lower, the near-mouth roots' tangle; higher, it never reaches.</summary>
		public const float TapRoomMin = 2.2f, TapRoomMax = 9f;
		/// <summary>The roof heights glow-worms hang their silk under, metres over the floor: still air, out of the draughts and the reach of feet.</summary>
		public const float SilkRoomMin = 2.6f, SilkRoomMax = 22f;

		/// <summary>
		/// The plan by form: a limestone cave the most; a grotto more silk (its water breeds the midges the larvae catch); a sea
		/// cave little (salt air); an ice cave nothing; a lava tube no silk (dry, nothing glows in it) but the most roots (its roof
		/// is thin and cracked); a mine no silk (as its palette has no glow-worms), roots at its shallow adits, a little guano.
		/// Guano is the habitat's own for the form, so the heaps go where the rock is stained and the cover crusted.
		/// </summary>
		public static CaveHangingPlan For(CaveForm form)
		{
			CaveHangingPlan p;
			switch (form)
			{
				case CaveForm.Grotto: p = new CaveHangingPlan { Silk = 1.25f, DeepRoots = 0.8f }; break;
				case CaveForm.SeaCave: p = new CaveHangingPlan { Silk = 0.15f, DeepRoots = 0.3f }; break;
				case CaveForm.IceCave: p = new CaveHangingPlan(); break;
				case CaveForm.LavaTube: p = new CaveHangingPlan { DeepRoots = 1.3f }; break;
				case CaveForm.Mine: p = new CaveHangingPlan { DeepRoots = 0.6f }; break;
				default: p = new CaveHangingPlan { Silk = 1f, DeepRoots = 1f }; break;
			}
			p.Guano = CaveHabitat.TraitsOf(form).Guano;
			return p;
		}

		/// <summary>
		/// How wooded a biome's ground is, 0..1: the trees its ground layers' scatter rules set per 100 m² (enabled tree rules'
		/// density × probability), a wood at <see cref="WoodTrees"/>. 0 for none (a grassland, a desert, a glacier).
		/// </summary>
		public static float Wooded(BiomeTemplate biome)
		{
			if (ReferenceEquals(biome, null))
			{
				return 0f;
			}
			float trees = Trees(biome.MainTextureLayer);
			if (biome.DetailTextureLayers != null)
			{
				foreach (TerrainTextureLayer layer in biome.DetailTextureLayers)
				{
					trees += Trees(layer);
				}
			}
			return Mathf.Clamp01(trees / WoodTrees);
		}

		private static float Trees(TerrainTextureLayer layer)
		{
			float trees = 0f;
			if (layer == null || layer.prefabSpawnRules == null)
			{
				return trees;
			}
			foreach (PrefabSpawnRule rule in layer.prefabSpawnRules)
			{
				if (rule != null && rule.enableSpawning && rule.spawnChannel == PrefabSpawnChannel.TreeInstance)
				{
					trees += Mathf.Max(0f, rule.densityPer100m2) * Mathf.Max(0f, rule.spawnProbabilityMultiplier);
				}
			}
			return trees;
		}

		/// <summary>
		/// The chance tree roots come through a roof under <paramref name="cover"/> metres of rock and soil, under ground
		/// <paramref name="wooded"/> (0..1): none outside <see cref="RootCoverMin"/>–<see cref="RootCoverMax"/>, falling off the
		/// deeper they would have to go (fewer roots find a joint all the way down).
		/// </summary>
		public static float RootChance(float cover, float wooded, float weight)
		{
			if (!(cover >= RootCoverMin) || cover > RootCoverMax || wooded <= 0f || weight <= 0f)
			{
				return 0f;
			}
			float depth = 1f - cover / RootCoverMax;
			return Mathf.Clamp01(weight * wooded * Mathf.Pow(depth, 1.5f));
		}

		/// <summary>
		/// The y scale that hangs a tap root (<paramref name="artHeight"/> long at scale 1, from its fixing just over the roof) down
		/// onto a floor <paramref name="room"/> metres under the roof, its foot a couple of centimetres into it.
		/// </summary>
		public static float TapScaleY(float room, float artHeight) => (room + 0.05f) / Mathf.Max(0.01f, artHeight);

		/// <summary>
		/// The guano heap under a roost <paramref name="roostRadius"/> across its flat patch and <paramref name="drop"/> metres over
		/// the floor: its variant (a high roost's droppings scatter into a low spread; a wide colony's pile is old and terraced),
		/// and its scale to reach a little past the patch, within 0.5–1.8 of the art.
		/// </summary>
		public static (int variant, float scale) GuanoFor(float roostRadius, float drop, float roll)
		{
			int variant = drop > 12f ? 1 : roostRadius >= 1.1f ? (roll < 0.5f ? 2 : 1) : roostRadius >= 0.7f && roll < 0.35f ? 2 : 0;
			float wanted = roostRadius + 0.25f + 0.02f * Mathf.Clamp(drop, 0f, 30f);
			return (variant, Mathf.Clamp(wanted / CaveGlowMeshes.GuanoRadii[variant], 0.5f, 1.8f));
		}
	}

	/// <summary>
	/// What hangs from a cave's roof and piles under it (2026-10-20, caves livened): glow-worm silk along its damp roof joints in
	/// the dark, tree roots come down through a thin roof where trees grow over it, and guano heaped under its bats' roosts.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Silk.</b> The colonies the glow hangs in the high halls (the dressing's glow-worm patches of short snares)
	/// are joined by long silk fringes along the roof joints: a passage follows its joint, so along its crest; a room's joints
	/// one set of parallel lines across it; and over the still air the cave's life is baked to (its Glow spots, where the
	/// fungus gnats, the glow-worms' grown flies, dance). Only where the habitat says the roof is damp or the glow gathers
	/// (<see cref="CaveHabitat"/>: the run-time cover's glow threads gather there too), in the dark, in the fungi's lead hue as
	/// the cover's threads glow (<see cref="CaveGlowPalette"/>). No lights: the larvae glow by their material.
	/// </para>
	/// <para>
	/// <b>Roots.</b> The near-mouth roots (PointOfInterestCaveDressing.Vines.cs) stop where the daylight does; past it, wherever
	/// the ground over the roof is 0.3–10 m above it and its biome grows trees, roots find the joints down: hanging all the way to
	/// the floor and spreading over it off the ways (<see cref="CaveGlowKind.TapRoots"/>), or over a way a hanging tangle that
	/// keeps a tall person's head clear (<see cref="CaveGlowKind.HangingRoots"/>).
	/// </para>
	/// <para>
	/// <b>Guano.</b> Under each roost the cave's life was baked to (<see cref="SceneCaveLifeKind.Roost"/>), by how dark it is and
	/// the form's guano: a heap a little wider than the colony's patch, and a few smaller ones round it, never on a way or in
	/// water, only on a floor flat enough to hold one.
	/// </para>
	/// <para>Each on its own random stream, after every other pass, so nothing they laid moves.</para>
	/// </remarks>
	public sealed partial class PointOfInterestCaveDressing
	{
		private sealed partial class Dresser
		{
			/// <summary>What the hanging pass laid: silk fringes, tap roots to the floor, short roots over the ways, guano heaps.</summary>
			private int silkHung, tapRoots, shortRoots, guanoHeaps;

			/// <summary>The most of each a system takes: a great cave's worth, so a hall of thin roof is not a thicket (draws are taken all the same).</summary>
			private const int MaxSilk = 160, MaxRoots = 140, MaxGuano = 90;

			/// <summary>What hangs from the roof: glow-worm threads, roots through a thin roof, guano under the roosts (cave-life programme HANGING).</summary>
			private void Hanging()
			{
				CaveHangingPlan plan = CaveHanging.For(system.Form);
				if (plan.Silk > 0f && Colours().Worms)
				{
					SilkThreads(new DeterministicRNG(unchecked(system.Seed * 0x3C91 + 0x5E27)), plan.Silk * CaveFungi.CharacterWeight(system.Character));
				}
				if (plan.DeepRoots > 0f && context.GroundAt != null)
				{
					RootsThroughRoof(new DeterministicRNG(unchecked(system.Seed * 0x6B17 + 0x2D43)), plan.DeepRoots);
				}
				if (plan.Guano > 0f)
				{
					GuanoUnderRoosts(new DeterministicRNG(unchecked(system.Seed * 0x1E8D + 0x7C35)), plan.Guano);
				}
				if (silkHung + tapRoots + shortRoots + guanoHeaps > 0)
				{
					PointOfInterestFeatureKit.Note(context, $"cave hanging: {silkHung} glow-worm silk, {tapRoots} tap roots, {shortRoots} roots over the ways, {guanoHeaps} guano heaps");
				}
			}

			/// <summary>The cave-life spots of this system of a kind, in their baked order (system frame not applied: world metres).</summary>
			private IEnumerable<SceneCaveLifeSpot> OwnLifeSpots(SceneCaveLifeKind kind)
			{
				if (context.Points == null || context.Points.CaveLife == null || context.Record == null)
				{
					yield break;
				}
				foreach (SceneCaveLifeSpot spot in context.Points.CaveLife)
				{
					if (spot.SiteId == context.Record.Id && spot.Kind == kind)
					{
						yield return spot;
					}
				}
			}

			// ── Silk ──────────────────────────────────────────

			/// <summary>Glow-worm silk along the passages' crests, across the rooms' joints, and over the cave life's glow air.</summary>
			private void SilkThreads(DeterministicRNG random, float weight)
			{
				float noise = random.Range(0f, 1000f);
				int hue = Colours().Fungus;
				foreach (CavePassage passage in system.Passages)
				{
					List<CaveNode> nodes = passage.Nodes;
					if (nodes.Count < 2)
					{
						continue;
					}
					float end = nodes[nodes.Count - 1].Along;
					for (float along = nodes[0].Along + random.Range(3f, 10f); along < end; along += random.Range(5f, 12f) / Mathf.Max(0.2f, weight))
					{
						float across = random.Range(-0.5f, 0.5f), roll = random.NextFloat(), pieces = random.NextFloat();
						CentreAt(nodes, along, out Vector3 c, out float nominal);
						CentreAt(nodes, along + 0.5f, out Vector3 ahead, out _);
						Vector3 dir = Flat(ahead - c);
						Vector3 p = c + Vector3.Cross(Vector3.up, dir) * (across * RadiusAt(nodes, along));
						p.y = nominal + 1.2f;
						if (NearMouth(p))
						{
							continue;
						}
						// Kept or skipped by a noise along the way, so the silk comes in stretches where the roof seeps.
						float patch = Mathf.Clamp01(Mathf.PerlinNoise(noise + along * 0.08f, noise * 0.37f) * 1.7f - 0.3f);
						if (!SilkRoof(p, out _, out _, out float damp) || roll >= weight * patch * (0.1f + 0.9f * damp))
						{
							continue;
						}
						SilkJoint(random, p, dir, 1 + Mathf.FloorToInt(pieces * 2.99f), hue, damp);
					}
				}
				foreach (CaveChamber c in system.Chambers)
				{
					float reach = Mathf.Max(c.Radii.x, c.Radii.z);
					float area = Mathf.PI * c.Radii.x * c.Radii.z;
					// One set of joints a room: parallel lines across its roof, a few degrees apart.
					float set = random.Range(0f, 180f);
					int lines = Mathf.Clamp(Mathf.RoundToInt(area / 160f * weight), 0, 6);
					if (c.Kind == CaveChamberKind.Stope || reach < 5f)
					{
						continue;
					}
					for (int k = 0; k < lines; k++)
					{
						float a = random.Range(0f, 2f * Mathf.PI), t = random.Range(0f, 0.7f), turn = set + random.Range(-12f, 12f), roll = random.NextFloat(), pieces = random.NextFloat();
						Vector3 p = InChamber(c, a, t) + Vector3.up * 1.2f;
						if (NearMouth(p) || !SilkRoof(p, out _, out _, out float damp) || roll >= 0.25f + 0.75f * damp)
						{
							continue;
						}
						SilkJoint(random, p, Dir(turn), 2 + Mathf.FloorToInt(pieces * 3.99f), hue, damp);
					}
				}
				// Over the still damp air the cave's life was baked to: a cluster under the drip that keeps it damp, or a fringe.
				foreach (SceneCaveLifeSpot spot in OwnLifeSpots(SceneCaveLifeKind.Glow))
				{
					float roll = random.NextFloat(), pick = random.NextFloat();
					Vector3 p = spot.Position - system.Origin;
					if (roll >= 0.4f * Mathf.Min(1f, weight) || NearMouth(p) || !SilkRoof(p, out float floor, out float roof, out float damp))
					{
						continue;
					}
					if (pick < 0.4f)
					{
						SilkPiece(random, 2, hue, new Vector3(p.x, floor, p.z), roof, Dir(spot.Yaw));
					}
					else
					{
						SilkJoint(random, p, Dir(spot.Yaw), 1 + Mathf.FloorToInt(pick * 2.99f), hue, damp);
					}
				}
			}

			/// <summary>
			/// Whether silk may hang over a point in the void (system frame): a floor under it and a roof over it at a height glow-worms
			/// hang under, in the dark; <paramref name="damp"/> how damp the roof is there or how the glow gathers on it (the habitat's).
			/// </summary>
			private bool SilkRoof(Vector3 p, out float floor, out float roof, out float damp)
			{
				damp = 0f;
				floor = roof = p.y;
				if (system.Void(p) >= 0f || !Ground(p, 4f, out floor) || !Roof(p, CaveHanging.SilkRoomMax + 2f, out roof))
				{
					return false;
				}
				float room = roof - floor;
				var ground = new Vector3(p.x, floor, p.z);
				if (room < CaveHanging.SilkRoomMin || room > CaveHanging.SilkRoomMax || DaylightAt(ground, 1.5f) > 0.15f || Submerged(new Vector3(p.x, roof, p.z)))
				{
					return false;
				}
				CaveHabitatSample h = HabitatAt(new Vector3(p.x, roof, p.z), Vector3.down);
				damp = Mathf.Clamp01(Mathf.Max(h.Wet, h.Glow));
				return true;
			}

			/// <summary>A run of fringes end to end along a roof joint through a point, each hung from the roof over its own middle.</summary>
			private void SilkJoint(DeterministicRNG random, Vector3 p, Vector3 dir, int pieces, int hue, float damp)
			{
				int variant = damp > 0.55f && random.NextFloat() < 0.5f ? 1 : 0;
				float span = CaveGlowMeshes.GlowSilkSpans[variant];
				float start = -0.5f * (pieces - 1) * span;
				for (int i = 0; i < pieces; i++)
				{
					float gap = random.Range(0.85f, 1.25f), side = random.Range(-0.15f, 0.15f);
					Vector3 at = p + dir * ((start + i * span) * gap) + new Vector3(dir.z, 0f, -dir.x) * side;
					if (system.Void(at) >= 0f || !Ground(at, 4f, out float floor) || !Roof(at, CaveHanging.SilkRoomMax + 2f, out float roof)
						|| roof - floor < CaveHanging.SilkRoomMin || roof - floor > CaveHanging.SilkRoomMax)
					{
						continue;
					}
					SilkPiece(random, variant, hue, new Vector3(at.x, floor, at.z), roof, dir);
				}
			}

			/// <summary>One silk piece hung from the roof over a spot of floor, its x along the joint, kept clear of heads over the ways.</summary>
			private void SilkPiece(DeterministicRNG random, int variant, int hue, Vector3 ground, float roof, Vector3 along)
			{
				float scale = random.Range(0.75f, 1.15f);
				if (silkHung >= MaxSilk)
				{
					return;
				}
				if (!CaveArt.TryGlow(CaveGlowKind.GlowSilk, hue, variant, out CaveGlowArt art))
				{
					Missing.Add("cave glow art");
					return;
				}
				float s = HangScale(ground, roof - ground.y, art.Height, scale);
				if (s < 0.35f)
				{
					return;
				}
				// The art's x along the joint: yaw that turns +x onto it.
				float yaw = Mathf.Atan2(along.x, along.z) * Mathf.Rad2Deg - 90f;
				if (Place(art.Prefab, new Vector3(ground.x, roof + 0.03f, ground.z), Quaternion.Euler(0f, yaw, 0f), Vector3.one * s))
				{
					silkHung++;
				}
			}

			// ── Roots ─────────────────────────────────────────

			private readonly Dictionary<BiomeTemplate, float> woodsOver = new Dictionary<BiomeTemplate, float>();

			/// <summary>How wooded the ground over a point (system frame) is: the biome there (the cave's own where none is known).</summary>
			private float WoodsOver(Vector3 local)
			{
				Vector3 world = system.Origin + local;
				BiomeTemplate over = context.BiomeAt != null ? context.BiomeAt(world.x, world.z) : null;
				over = ReferenceEquals(over, null) ? biome : over;
				if (ReferenceEquals(over, null))
				{
					return 0f;
				}
				if (!woodsOver.TryGetValue(over, out float w))
				{
					w = CaveHanging.Wooded(over);
					woodsOver[over] = w;
				}
				return w;
			}

			/// <summary>
			/// Tree roots through the thin roofs past the mouths' daylight: along every passage and over every room's floor, where
			/// the ground is close over the roof and trees grow on it.
			/// </summary>
			private void RootsThroughRoof(DeterministicRNG random, float weight)
			{
				float noise = random.Range(0f, 1000f);
				float stretch = system.Form == CaveForm.Mine ? 1.1f : 1.8f;
				foreach (CavePassage passage in system.Passages)
				{
					List<CaveNode> nodes = passage.Nodes;
					if (nodes.Count < 2)
					{
						continue;
					}
					float end = nodes[nodes.Count - 1].Along;
					for (float along = nodes[0].Along + random.Range(0.5f, 3f); along < end; along += random.Range(2f, 4.5f))
					{
						float across = random.Range(-0.8f, 0.8f), roll = random.NextFloat(), pick = random.NextFloat();
						CentreAt(nodes, along, out Vector3 c, out float nominal);
						CentreAt(nodes, along + 0.5f, out Vector3 ahead, out _);
						Vector3 p = c + Vector3.Cross(Vector3.up, Flat(ahead - c)) * (across * RadiusAt(nodes, along));
						p.y = nominal + 1.2f;
						RootsOver(random, p, roll, pick, noise, weight, stretch);
					}
				}
				foreach (CaveChamber c in system.Chambers)
				{
					int samples = Mathf.Clamp(Mathf.RoundToInt(Mathf.PI * c.Radii.x * c.Radii.z / 30f), 1, 60);
					for (int k = 0; k < samples; k++)
					{
						float a = random.Range(0f, 2f * Mathf.PI), t = Mathf.Sqrt(random.Range(0f, 0.85f)), roll = random.NextFloat(), pick = random.NextFloat();
						RootsOver(random, InChamber(c, a, t) + Vector3.up * 1.2f, roll, pick, noise, weight, stretch);
					}
				}
			}

			/// <summary>Roots over one point (system frame) where the roof is thin enough and wooded over, in patches.</summary>
			private void RootsOver(DeterministicRNG random, Vector3 p, float roll, float pick, float noise, float weight, float stretch)
			{
				if (system.Void(p) >= 0f || DaylightAt(p, stretch) > 0f)
				{
					return;
				}
				// The ground over it first (cheap): deep in the hill no roof here can be thin, so no march is wasted on it.
				Vector3 world = system.Origin + p;
				float surface = context.GroundAt(world.x, world.z) - system.Origin.y;
				if (float.IsNaN(surface) || surface - p.y > CaveHanging.RootCoverMax + 30f || !Ground(p, 4f, out float floor)
					|| !Roof(p, Mathf.Min(30f, surface - p.y), out float roof))
				{
					return;
				}
				float cover = surface - roof;
				float patch = Mathf.Clamp01(Mathf.PerlinNoise(noise + p.x * 0.18f, noise + p.z * 0.18f) * 1.8f - 0.4f);
				if (cover < CaveHanging.RootCoverMin || cover > CaveHanging.RootCoverMax || roll >= CaveHanging.RootChance(cover, WoodsOver(p), weight) * patch)
				{
					return;
				}
				var ground = new Vector3(p.x, floor, p.z);
				float room = roof - floor;
				int hue = pick < 0.65f ? 0 : 1;
				float yaw = random.Range(0f, 360f), girth = random.Range(0.8f, 1.2f);
				if (tapRoots + shortRoots >= MaxRoots)
				{
					return;
				}
				// To the floor where it is off every way and dry; over a way (or too high to reach) a tangle that clears heads.
				bool clear = room >= CaveHanging.TapRoomMin && room <= CaveHanging.TapRoomMax && !Submerged(ground)
					&& !InLane(ground, 1.1f) && !OnWay(ground, 1.1f);
				if (clear && CaveArt.TryGlow(CaveGlowKind.TapRoots, hue, Mathf.FloorToInt(pick * 997f), out CaveGlowArt tap))
				{
					float sy = CaveHanging.TapScaleY(room, tap.Height);
					if (Place(tap.Prefab, new Vector3(ground.x, roof + 0.03f, ground.z), Quaternion.Euler(0f, yaw, 0f), new Vector3(girth, sy, girth)))
					{
						tapRoots++;
					}
					return;
				}
				if (!CaveArt.TryGlow(CaveGlowKind.HangingRoots, hue, Mathf.FloorToInt(pick * 991f), out CaveGlowArt roots))
				{
					Missing.Add("cave glow art");
					return;
				}
				float s = HangScale(ground, room, roots.Height, girth);
				if (s >= 0.35f && Place(roots.Prefab, new Vector3(ground.x, roof + 0.03f, ground.z), Quaternion.Euler(0f, yaw, 0f), Vector3.one * s))
				{
					shortRoots++;
				}
			}

			// ── Guano ─────────────────────────────────────────

			/// <summary>Heaps under the roosts the cave's life was baked to, by how dark each is and the form's guano.</summary>
			private void GuanoUnderRoosts(DeterministicRNG random, float weight)
			{
				foreach (SceneCaveLifeSpot spot in OwnLifeSpots(SceneCaveLifeKind.Roost))
				{
					float roll = random.NextFloat(), shape = random.NextFloat(), fresh = random.NextFloat(), yaw = random.Range(0f, 360f);
					int satellites = random.Range(0, 4);
					if (roll >= weight * (0.35f + 0.65f * spot.Dark))
					{
						continue;
					}
					Vector3 rock = spot.Position - system.Origin;
					if (!Ground(rock + Vector3.down * 0.6f, spot.Extent.y + 3f, out float floor))
					{
						continue;
					}
					float drop = rock.y - floor;
					(int variant, float scale) = CaveHanging.GuanoFor(spot.Extent.x, drop, shape);
					int hue = fresh < 0.65f ? 0 : 1;
					if (!GuanoPile(variant, hue, new Vector3(rock.x, floor, rock.z), scale, yaw))
					{
						continue;
					}
					// Droppings scatter round the colony's patch: smaller heaps about it, more the higher it hangs.
					float ring = spot.Extent.x + 0.6f + 0.04f * Mathf.Clamp(drop, 0f, 30f);
					for (int k = 0; k < satellites; k++)
					{
						float a = random.Range(0f, 360f), r = ring * random.Range(0.6f, 1.1f), s = random.Range(0.35f, 0.7f), turn = random.Range(0f, 360f);
						Vector3 q = rock + Dir(a) * r + Vector3.down * 0.6f;
						if (system.Void(q) < 0f && Ground(q, spot.Extent.y + 3f, out float under))
						{
							GuanoPile(0, hue, new Vector3(q.x, under, q.z), s, turn);
						}
					}
				}
			}

			/// <summary>
			/// A guano heap on a floor (system frame), its foot 2 cm into it: false on a way, in water, or where the floor under its
			/// edge falls or rises more than its skirt can hide.
			/// </summary>
			private bool GuanoPile(int variant, int hue, Vector3 at, float scale, float yaw)
			{
				if (guanoHeaps >= MaxGuano || InLane(at, 0.3f) || OnWay(at, 0.3f) || Submerged(at))
				{
					return false;
				}
				float r = CaveGlowMeshes.GuanoRadii[variant] * scale, low = at.y, high = at.y;
				for (int k = 0; k < 6; k++)
				{
					Vector3 q = at + Dir(yaw + k * 60f) * r + Vector3.up * 0.8f;
					if (system.Void(q) >= 0f || !Ground(q, 2f, out float y))
					{
						return false;
					}
					low = Mathf.Min(low, y);
					high = Mathf.Max(high, y);
				}
				if (high - low > 0.12f + 0.1f * scale)
				{
					return false;
				}
				if (!CaveArt.TryGlow(CaveGlowKind.GuanoHeap, hue, variant, out CaveGlowArt art))
				{
					Missing.Add("cave glow art");
					return false;
				}
				// Seated on the lowest of its edge's floor, so its skirt reaches down past every point of it.
				var foot = new Vector3(at.x, Mathf.Min(at.y, low + 0.04f) - 0.02f, at.z);
				if (!Place(art.Prefab, foot, Quaternion.Euler(0f, yaw, 0f), new Vector3(scale, Mathf.Lerp(1f, scale, 0.5f), scale)))
				{
					return false;
				}
				guanoHeaps++;
				return true;
			}
		}
	}
}
#endif
