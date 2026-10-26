#if UNITY_EDITOR
using System.Collections.Generic;
using FishMMO.Shared.Biomes;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// What lights a natural cave (2026-10-13: "enormous caves with lots of glowy rocks lining the walkable pathways …
	/// Crystals, Luminous fungi, Glow-worm ceilings"): glowing crystal clusters and luminous fungus lining the ways a
	/// player walks — across each great hall from every way into it to its middle, over the bridges and ramps, round a pit's
	/// ledge, to the crossings — then what the system's character adds: crystal galleries, glowcap groves, glow down the
	/// pits and the canyon walls, glow-worms hung from high roofs.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Lining, not a fence.</b> Each side of a way is walked on its own, a pace off its edge, every couple of metres,
	/// and kept or skipped by a noise along it: the glow comes in clumps of one to three with gaps between, as it grows
	/// where water seeps, and nothing stands on a way itself.
	/// </para>
	/// <para>
	/// <b>A palette a system.</b> One crystal hue and one fungus hue lead, an accent of each a quarter of the time (from
	/// the system's seed); an ice cave has blue ice crystals only, a lava tube amber crystal and emberstone, neither has
	/// fungi or glow-worms (no cave life lives in either).
	/// </para>
	/// <para>
	/// <b>Lights.</b> Every piece glows by its own material; a light goes only with a cluster group, nine metres or more
	/// from any other of the system's glow lights, as SiteLightPool lights only the nearest sixteen emitters.
	/// </para>
	/// </remarks>
	public sealed partial class PointOfInterestCaveDressing
	{
		private sealed partial class Dresser
		{
			/// <summary>The least distance between two of the glow's lights, metres.</summary>
			private const float LightSpacing = 9f;

			private CaveGlowPalette palette;
			private bool paletteMade;

			/// <summary>The ways across the halls (system frame, at walking height): from each way into a great hall to its middle.</summary>
			private readonly List<(Vector3 a, Vector3 b)> ways = new List<(Vector3, Vector3)>();

			/// <summary>The system's palette, drawn from its seed by form (<see cref="CaveGlowPalettes.For"/>, shared with the cave cover).</summary>
			private CaveGlowPalette Colours()
			{
				if (!paletteMade)
				{
					palette = CaveGlowPalettes.For(system.Form, system.Seed, in caves);
					paletteMade = true;
				}
				return palette;
			}

			/// <summary>
			/// A piece of crystal glow art for a hue of the palette: the glow art's, or where the biome's own glowing crystals line the
			/// ways, the lead's (the accent's for the accent's hue). False, as <see cref="CaveArt.TryGlow"/>, while the art is missing.
			/// </summary>
			private bool TryCrystal(CaveGlowKind kind, int hue, int variant, out CaveGlowArt art)
			{
				CaveGlowPalette p = Colours();
				if (kind == CaveGlowKind.Crystal && p.Minerals)
				{
					bool accent = hue == p.CrystalAccent && hue != p.Crystal;
					return CaveArt.TryMineral(accent ? p.MineralAccent : p.Mineral, variant, out art, accent ? p.MineralAccentLook : p.MineralLook);
				}
				return CaveArt.TryGlow(kind, hue, variant, out art);
			}

			/// <summary>How thickly the system's character lines its ways (1 a hall cave's).</summary>
			private float Density
			{
				get
				{
					float d;
					switch (system.Character)
					{
						case CaveCharacter.Crystal: d = 1.3f; break;
						case CaveCharacter.Halls: d = 1f; break;
						case CaveCharacter.Abyss:
						case CaveCharacter.Rift: d = 0.85f; break;
						case CaveCharacter.Warren: d = 0.6f; break;
						default: d = 0.5f; break;
					}
					return system.Form == CaveForm.SeaCave || system.Form == CaveForm.Grotto ? d * 0.4f : d;
				}
			}

			/// <summary>The share of the floor's glow that is crystal (the rest luminous fungus).</summary>
			private float CrystalShare
			{
				get
				{
					if (!Colours().Fungi)
					{
						return 1f;
					}
					float share;
					switch (system.Character)
					{
						case CaveCharacter.Crystal: share = 0.8f; break;
						case CaveCharacter.Halls: share = 0.25f; break;
						case CaveCharacter.Abyss:
						case CaveCharacter.Rift: share = 0.45f; break;
						default: share = 0.4f; break;
					}
					// The cave biome's fungus weighs against the crystal (its Cave Glows, 2026-10-25): 1 leaves the share as it was.
					float fungus = Colours().FungusWeight;
					return fungus == 1f ? share : BiomeCaveInteriors.CrystalShare(share, fungus);
				}
			}

			/// <summary>Within a lane's width (and <paramref name="margin"/>) of a way across a hall, in plan and near its height.</summary>
			private bool OnWay(Vector3 p, float margin)
			{
				foreach ((Vector3 a, Vector3 b) in ways)
				{
					if (NearLine(p, a, b, Lane + margin, 3f))
					{
						return true;
					}
				}
				return false;
			}

			/// <summary>A hall lobe or a great cavern: what has ways across it lined.</summary>
			private bool IsHall(in CaveChamber c) => system.Form != CaveForm.Mine
				&& (c.Kind == CaveChamberKind.Hall || c.Kind == CaveChamberKind.Cavern) && Mathf.Max(c.Radii.x, c.Radii.z) >= 12f;

			/// <summary>The ways across every hall: from each passage end that opens into it, and each bridge's and ramp's end on its floor, to its middle.</summary>
			private void FindWays()
			{
				ways.Clear();
				foreach (CaveChamber c in system.Chambers)
				{
					if (!IsHall(c))
					{
						continue;
					}
					var middle = new Vector3(c.Centre.x, c.Floor, c.Centre.z);
					float reach = Mathf.Max(c.Radii.x, c.Radii.z);
					foreach (CavePassage passage in system.Passages)
					{
						if (passage.Nodes.Count < 2)
						{
							continue;
						}
						foreach (CaveNode n in new[] { passage.Nodes[0], passage.Nodes[passage.Nodes.Count - 1] })
						{
							var d = new Vector2(n.Centre.x - c.Centre.x, n.Centre.z - c.Centre.z);
							if (d.magnitude < reach + n.Radius + 2f && Mathf.Abs(n.Floor - c.Floor) < 8f)
							{
								ways.Add((new Vector3(n.Centre.x, n.Floor, n.Centre.z), middle));
							}
						}
					}
					foreach (CaveSolid s in system.Solids)
					{
						if (s.Kind != CaveSolidKind.Bridge && s.Kind != CaveSolidKind.Ramp)
						{
							continue;
						}
						foreach (Vector3 end in new[] { s.A, s.B })
						{
							if (InFootprint(c, end, 0f) && Mathf.Abs(end.y - CaveSystem.FloorOf(c, end)) < 3f)
							{
								ways.Add((end, middle));
							}
						}
					}
				}
			}

			// ── The pass ──────────────────────────────────────────

			/// <summary>All the natural glow, each part on its own stream so one's count does not move another's.</summary>
			private void GlowAll()
			{
				FindWays();
				Colours();
				LineHalls(new DeterministicRNG(unchecked(system.Seed * 0x61C9 + 0x0B57)));
				LineFeatures(new DeterministicRNG(unchecked(system.Seed * 0x7E25 + 0x33A1)));
				Pits(new DeterministicRNG(unchecked(system.Seed * 0x1B87 + 0x5F0D)));
				Centrepieces(new DeterministicRNG(unchecked(system.Seed * 0x2D4F + 0x6C13)));
				PassageGlow(new DeterministicRNG(unchecked(system.Seed * 0x4A93 + 0x18E7)));
				HallWalls(new DeterministicRNG(unchecked(system.Seed * 0x3F61 + 0x2A5B)));
				GlowWorms(new DeterministicRNG(unchecked(system.Seed * 0x58B1 + 0x4D29)));
			}

			// ── Lining ────────────────────────────────────────────

			/// <summary>Every way across every hall, lined both sides.</summary>
			private void LineHalls(DeterministicRNG random)
			{
				foreach ((Vector3 a, Vector3 b) in ways)
				{
					Line(random, a, b, Lane, 0f, 0f, drop: false);
				}
			}

			/// <summary>
			/// The layout's own ways: each bridge (and down into the chasm under it), each ramp (and at its flanks' feet), each
			/// crossing and its approaches.
			/// </summary>
			private void LineFeatures(DeterministicRNG random)
			{
				for (int i = 0; i < system.Solids.Count; i++)
				{
					CaveSolid s = system.Solids[i];
					if (s.Kind == CaveSolidKind.Bridge)
					{
						Line(random, s.A, s.B, s.Width + 0.6f, 5f, 0.45f, drop: true);
					}
					else if (s.Kind == CaveSolidKind.Ramp)
					{
						Line(random, s.A, s.B, s.Width + 0.6f, 2f, 1f, drop: true);
					}
				}
				foreach (CaveCrossing crossing in system.Crossings)
				{
					Line(random, crossing.A, crossing.B, crossing.HalfWidth + 1f, 5f, 0.45f, drop: true);
				}
			}

			/// <summary>
			/// One way lined on both sides: a pace (0.5–1.8 m) off its edge at <paramref name="half"/> from its line, from
			/// <paramref name="extend"/> before its start to as far past its end, every 1.6–3.4 m (closer the denser the
			/// character), kept where a noise along the side is high, so the glow comes in clumps with gaps. Where the floor
			/// beside it falls away (a bridge's chasm, a ramp's flank) and <paramref name="drop"/>, a share
			/// <paramref name="depthShare"/> of the spots are taken on the floor below instead.
			/// </summary>
			private void Line(DeterministicRNG random, Vector3 a, Vector3 b, float half, float extend, float depthShare, bool drop)
			{
				Vector3 run = b - a;
				var dir = new Vector3(run.x, 0f, run.z);
				float length = dir.magnitude;
				if (length < 1f && extend <= 0f)
				{
					return;
				}
				dir = length > 1e-3f ? dir / length : Vector3.forward;
				var across = new Vector3(dir.z, 0f, -dir.x);
				float density = Density;
				for (int side = -1; side <= 1; side += 2)
				{
					float noise = random.Range(0f, 1000f);
					for (float along = -extend + random.Range(0f, 2f); along < length + extend; along += random.Range(1.6f, 3.4f) / Mathf.Sqrt(density))
					{
						float gate = Mathf.PerlinNoise(noise + along * 0.16f, noise * 0.37f + side * 5.1f);
						float roll = random.NextFloat(), off = half + random.Range(0.5f, 1.8f), deep = random.NextFloat();
						int clump = 1 + (random.NextFloat() < 0.5f ? 1 : 0) + (random.NextFloat() < 0.2f ? 1 : 0);
						if (roll >= Mathf.Clamp01(gate * 1.7f - 0.45f) * Mathf.Min(1f, density))
						{
							continue;
						}
						float t = Mathf.Clamp01(along / Mathf.Max(1e-3f, length));
						float height = Mathf.Lerp(a.y, b.y, t);
						Vector3 p = a + dir * along + across * (side * off);
						p.y = height + 0.8f;
						if (!Ground(p, drop ? 45f : 2.2f, out float floor))
						{
							continue;
						}
						// Off the way's level: a chasm's floor or a flank's foot, for a share of the spots; else none.
						if (height - floor > 1.4f && (!drop || deep >= depthShare))
						{
							continue;
						}
						var at = new Vector3(p.x, floor, p.z);
						if (!Clear(at, 0.25f))
						{
							continue;
						}
						Clump(random, at, clump, small: false);
					}
				}
			}

			/// <summary>One to three pieces of floor glow round a spot, each set on the floor where it stands; the first may carry a light.</summary>
			private void Clump(DeterministicRNG random, Vector3 at, int count, bool small, int exceptSolid = -1)
			{
				for (int k = 0; k < count; k++)
				{
					Vector3 q = at;
					float a = random.Range(0f, 2f * Mathf.PI), r = random.Range(0.5f, 1.1f);
					if (k > 0)
					{
						q += new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
						if (!Ground(new Vector3(q.x, at.y + 0.8f, q.z), 1.6f, out float floor) || Mathf.Abs(floor - at.y) > 0.6f)
						{
							continue;
						}
						q.y = floor;
						if (!Clear(q, 0.1f, exceptSolid))
						{
							continue;
						}
					}
					FloorGlowPick(random, q, small, light: k == 0);
				}
			}

			/// <summary>A crystal cluster or a fungus tuft, by the palette and the character's share.</summary>
			private bool FloorGlowPick(DeterministicRNG random, Vector3 at, bool small, bool light)
			{
				CaveGlowPalette p = Colours();
				bool crystal = random.NextFloat() < CrystalShare;
				bool accent = random.NextFloat() < 0.25f;
				float size = random.NextFloat();
				if (crystal)
				{
					float scale = small ? Mathf.Lerp(0.4f, 0.6f, size) : Mathf.Lerp(0.6f, 1.3f, size);
					return FloorGlow(random, CaveGlowKind.Crystal, accent ? p.CrystalAccent : p.Crystal, at, scale, 12f, light);
				}
				return FloorGlow(random, CaveGlowKind.Fungus, accent ? p.FungusAccent : p.Fungus, at, Mathf.Lerp(0.8f, small ? 1.1f : 1.5f, size), 5f, light);
			}

			/// <summary>
			/// A piece of floor-mounted glow art stood at a spot on the floor (a little sunk, leaning a little, any way round);
			/// false where its art is missing or the roof is too low for it. Its draws come first, so a missing piece leaves
			/// the stream where a placed one would.
			/// </summary>
			private bool FloorGlow(DeterministicRNG random, CaveGlowKind kind, int hue, Vector3 at, float scale, float lean, bool light, float lightChance = 0.65f)
			{
				int variant = random.Range(0, CaveArt.VariantCount(kind));
				float yaw = random.Range(0f, 360f), tx = random.Range(-lean, lean), tz = random.Range(-lean, lean), chance = random.NextFloat();
				if (!TryCrystal(kind, hue, variant, out CaveGlowArt art))
				{
					Missing.Add("cave glow art");
					return false;
				}
				if (system.Void(at + Vector3.up * (art.Height * scale + 0.2f)) >= 0f)
				{
					return false;
				}
				if (kind == CaveGlowKind.Crystal || kind == CaveGlowKind.GiantCrystal)
				{
					scale *= Giant(art, at, scale, kind == CaveGlowKind.GiantCrystal ? GiantCrystalMost : CrystalMost);
				}
				if (!Place(art.Prefab, at - Vector3.up * (0.03f * scale), Quaternion.Euler(tx, yaw, tz), Vector3.one * scale))
				{
					return false;
				}
				Glowing++;
				if (light && chance < lightChance)
				{
					Light(at + Vector3.up * (0.6f * art.Height * scale), art, LightSpacing);
				}
				return true;
			}

			/// <summary>The most a floor crystal's size is multiplied by for the odd giant among them.</summary>
			private const float CrystalMost = 4f;
			/// <summary>The same for the giant crystals (already the size of a man and more).</summary>
			private const float GiantCrystalMost = 3.5f;
			/// <summary>The lean of that multiplier: most crystals keep their size, the odd one grows toward the most.</summary>
			private const float GiantSkew = 2.5f;

			/// <summary>
			/// The odd giant among a cave's crystals (2026-10-20: "we want massive crystals"): a multiplier on a floor crystal's
			/// size, 1 + (most − 1)·u^<see cref="GiantSkew"/> from a hash of where it stands (no draw is taken, so the dressing's
			/// stream and every other piece stay where they were), cut back by 30% at a time to where it still clears the roof and,
			/// for a crystal that collides, stays out of the walked lanes. 1 where none fits larger.
			/// </summary>
			private float Giant(in CaveGlowArt art, Vector3 at, float scale, float most)
			{
				float u = PointOfInterestPlanner.Unit(PointOfInterestPlanner.Hash(system.Seed, Mathf.RoundToInt(at.x * 10f), Mathf.RoundToInt(at.z * 10f), 0x61A7));
				float giant = 1f + (most - 1f) * Mathf.Pow(u, GiantSkew);
				while (giant > 1.05f)
				{
					float size = scale * giant;
					if (system.Void(at + Vector3.up * (art.Height * size + 0.2f)) < 0f && (!art.Collides || !InLane(at, art.Radius * size)))
					{
						return giant;
					}
					giant = Mathf.Max(1f, giant * 0.7f);
				}
				return 1f;
			}

			/// <summary>
			/// A piece of glow art on a wall met by a ray: a wall piece flat on the rock (+z into it, rolled so a seam dips), a
			/// crystal standing out of it (+y out of the rock, as a geode's grow); false where no wall is met.
			/// </summary>
			private bool WallGlow(DeterministicRNG random, CaveGlowKind kind, int hue, Vector3 from, Vector3 dir, float reach, float scale, bool light)
			{
				int variant = random.Range(0, CaveArt.VariantCount(kind));
				float roll = random.Range(-35f, 35f), spin = random.Range(0f, 360f), chance = random.NextFloat();
				if (!Wall(from, dir, reach, out Vector3 surface, out Vector3 into))
				{
					return false;
				}
				if (!TryCrystal(kind, hue, variant, out CaveGlowArt art))
				{
					Missing.Add("cave glow art");
					return false;
				}
				// A floor-mounted piece that collides stands out of the wall into the way: not within a lane.
				if (art.Collides && InLane(new Vector3(surface.x, from.y - 1f, surface.z), 0.2f))
				{
					return false;
				}
				Quaternion turn = art.Mount == CaveMount.Floor
					? Quaternion.FromToRotation(Vector3.up, -into) * Quaternion.Euler(0f, spin, 0f)
					: Quaternion.LookRotation(into, Vector3.up) * Quaternion.Euler(0f, 0f, roll);
				if (!Place(art.Prefab, surface + into * 0.02f, turn, Vector3.one * scale))
				{
					return false;
				}
				Glowing++;
				if (light && chance < 0.6f)
				{
					Light(surface - into * 0.4f, art, LightSpacing);
				}
				return true;
			}

			/// <summary>The kind a wall glows with here: shelf fungus, emberstone in a lava tube, crystal in an ice cave or a crystal gallery.</summary>
			private (CaveGlowKind kind, int hue) WallKind(DeterministicRNG random)
			{
				CaveGlowPalette p = Colours();
				bool accent = random.NextFloat() < 0.25f, crystal = random.NextFloat() < 0.5f;
				if (system.Form == CaveForm.LavaTube)
				{
					return (CaveGlowKind.GlowOreVein, p.GlowOre);
				}
				if (!p.Fungi || system.Character == CaveCharacter.Crystal && crystal)
				{
					return (CaveGlowKind.Crystal, accent ? p.CrystalAccent : p.Crystal);
				}
				return (CaveGlowKind.ShelfFungus, accent ? p.FungusAccent : p.Fungus);
			}

			/// <summary>A crystal-kind light by a piece of glow art, where none of the system's is within <paramref name="spacing"/>.</summary>
			private void Light(Vector3 local, in CaveGlowArt art, float spacing)
			{
				if (art.SuggestedReach <= 0f || art.EmitterHue < 0)
				{
					return;
				}
				foreach (Vector3 lit in glowLights)
				{
					if ((lit - local).sqrMagnitude < spacing * spacing)
					{
						return;
					}
				}
				uint seed = PointOfInterestPlanner.Hash(system.Seed, Mathf.RoundToInt(local.x * 10f), Mathf.RoundToInt(local.z * 10f), 0xC7);
				if (art.Flickers)
				{
					// A crystal that flickers (stormglass, 2026-10-18): a cold glow with the flicker, in its hue.
					Emit(local, SceneEmitterKind.Glow, SceneGlow.WithFlicker(SceneGlow.WithCrystalHue(seed, art.EmitterHue), true), art.SuggestedReach);
					return;
				}
				Emit(local, SceneEmitterKind.Crystal, CaveArt.EmitterSeed(seed, art.EmitterHue), art.SuggestedReach);
			}

			// ── Pits ──────────────────────────────────────────────

			/// <summary>
			/// Each pit: small glow along its ledge's inner lip (out of the walk, which keeps the ledge's middle), shelf fungus
			/// (or crystal, or emberstone) on its wall over the ledge, crystals on its floor — more of all in an abyss.
			/// </summary>
			private void Pits(DeterministicRNG random)
			{
				bool abyss = system.Character == CaveCharacter.Abyss;
				float density = Density * (abyss ? 1.3f : 1f);
				CaveGlowPalette colours = Colours();
				for (int i = 0; i < system.Solids.Count; i++)
				{
					CaveSolid s = system.Solids[i];
					if (s.Kind != CaveSolidKind.Spiral)
					{
						continue;
					}
					float pitch = Mathf.Max(0.1f, Mathf.Abs(s.Arch)), hand = s.Arch < 0f ? -1f : 1f;
					float last = (s.B.y - s.A.y) / pitch, inner = s.Depth - s.Width, lip = inner + 0.35f;
					float noise = random.Range(0f, 1000f);
					for (float phi = random.Range(0.2f, 0.6f); phi < last; phi += random.Range(2.2f, 5.5f) / (lip * density))
					{
						float roll = random.NextFloat(), wallRoll = random.NextFloat(), h = random.Range(1f, 2.5f);
						float ang = s.Angle * Mathf.Deg2Rad + hand * phi;
						var outward = new Vector3(Mathf.Sin(ang), 0f, Mathf.Cos(ang));
						float ledge = s.B.y - phi * pitch;
						float gate = Mathf.Clamp01(Mathf.PerlinNoise(noise + phi * 1.3f, noise * 0.5f) * 1.7f - 0.4f);
						if (roll < gate)
						{
							Vector3 p = s.A + outward * lip;
							p.y = ledge + 0.6f;
							if (Ground(p, 1.2f, out float floor) && Mathf.Abs(floor - ledge) < 0.5f)
							{
								var at = new Vector3(p.x, floor, p.z);
								if (Clear(at, 0f, i))
								{
									// Small: a tuft or a low crystal at the lip leaves the ledge's walk clear.
									if (colours.Fungi)
									{
										FloorGlow(random, CaveGlowKind.Fungus, random.NextFloat() < 0.25f ? colours.FungusAccent : colours.Fungus, at, random.Range(0.8f, 1.2f), 4f, light: true);
									}
									else
									{
										FloorGlow(random, CaveGlowKind.Crystal, colours.Crystal, at, random.Range(0.4f, 0.55f), 8f, light: true);
									}
								}
							}
						}
						if (wallRoll < 0.55f * density)
						{
							Vector3 from = s.A + outward * (s.Depth - 0.6f);
							from.y = ledge + h;
							(CaveGlowKind kind, int hue) = WallKind(random);
							WallGlow(random, kind, hue, from, outward, 2.5f, random.Range(0.8f, 1.3f), light: true);
						}
					}
					// The pit's floor: crystals about it, and in an abyss now and then a giant.
					int clusters = abyss ? random.Range(3, 8) : random.Range(0, 3);
					for (int k = 0; k < clusters; k++)
					{
						float a = random.Range(0f, 2f * Mathf.PI), t = Mathf.Sqrt(random.NextFloat()) * Mathf.Max(0f, inner - 1.2f);
						bool giant = abyss && k == 0 && random.NextFloat() < 0.5f;
						var p = new Vector3(s.A.x + Mathf.Sin(a) * t, s.A.y + 1.2f, s.A.z + Mathf.Cos(a) * t);
						if (!Ground(p, 3f, out float floor))
						{
							continue;
						}
						var at = new Vector3(p.x, floor, p.z);
						if (!Clear(at, giant ? 1.5f : 0.3f, i))
						{
							continue;
						}
						if (giant)
						{
							FloorGlow(random, CaveGlowKind.GiantCrystal, colours.Crystal, at, random.Range(0.8f, 1.15f), 5f, light: true, lightChance: 1f);
						}
						else
						{
							Clump(random, at, 1 + random.Range(0, 3), small: false, exceptSolid: i);
						}
					}
				}
			}

			// ── Centrepieces ──────────────────────────────────────

			/// <summary>
			/// A crystal gallery's giant crystals (two to five, in its halls, else its greatest rooms), and glowcap groves in
			/// the halls (most of a hall cave's, a few of others'): each well off the ways, under a roof high enough.
			/// </summary>
			private void Centrepieces(DeterministicRNG random)
			{
				CaveGlowPalette colours = Colours();
				var halls = new List<CaveChamber>();
				var rooms = new List<CaveChamber>();
				foreach (CaveChamber c in system.Chambers)
				{
					if (IsHall(c))
					{
						halls.Add(c);
					}
					else if (c.Kind != CaveChamberKind.Alcove && Mathf.Max(c.Radii.x, c.Radii.z) >= 7f)
					{
						rooms.Add(c);
					}
				}
				// The dressing's great pieces (empty until now), so the later passes keep their own clear of these.
				List<Vector3> giants = bigs;
				if (system.Character == CaveCharacter.Crystal)
				{
					List<CaveChamber> where = halls.Count > 0 ? halls : rooms;
					int want = random.Range(2, 6);
					for (int tries = 0; giants.Count < want && where.Count > 0 && tries < want * 10; tries++)
					{
						CaveChamber c = where[random.Range(0, where.Count)];
						Vector3 p = InChamber(c, random.Range(0f, 2f * Mathf.PI), random.Range(0.3f, 0.75f));
						float scale = random.Range(0.8f, 1.25f);
						bool accent = random.NextFloat() < 0.3f;
						if (!Spot(p, 3.5f, giants, 7f, out Vector3 at))
						{
							continue;
						}
						if (FloorGlow(random, CaveGlowKind.GiantCrystal, accent ? colours.CrystalAccent : colours.Crystal, at, scale, 6f, light: true, lightChance: 1f))
						{
							giants.Add(at);
							// Lesser points about its foot.
							Vector3 by = at + Dir(random.Range(0f, 360f)) * random.Range(1.6f, 2.4f);
							int lesser = random.Range(1, 3);
							if (Ground(by + Vector3.up * 1.2f, 2.5f, out float byFloor) && Clear(new Vector3(by.x, byFloor, by.z), 0.2f))
							{
								Clump(random, new Vector3(by.x, byFloor, by.z), lesser, small: false);
							}
						}
					}
				}
				if (!colours.Fungi)
				{
					return;
				}
				float groveChance = system.Character == CaveCharacter.Halls ? 0.6f : 0.15f;
				foreach (CaveChamber c in halls)
				{
					if (random.NextFloat() >= groveChance)
					{
						continue;
					}
					Vector3 p = InChamber(c, random.Range(0f, 2f * Mathf.PI), random.Range(0.4f, 0.8f));
					if (!Spot(p, 3f, giants, 8f, out Vector3 middle))
					{
						continue;
					}
					giants.Add(middle);
					int hue = random.NextFloat() < 0.3f ? colours.FungusAccent : colours.Fungus;
					int caps = random.Range(3, 8);
					bool lit = false;
					for (int k = 0; k < caps; k++)
					{
						float a = random.Range(0f, 2f * Mathf.PI), r = k == 0 ? 0f : random.Range(1.6f, 4.5f);
						var q = new Vector3(middle.x + Mathf.Cos(a) * r, middle.y + 1f, middle.z + Mathf.Sin(a) * r);
						if (!Ground(q, 2.5f, out float floor) || !Clear(new Vector3(q.x, floor, q.z), 1.5f))
						{
							continue;
						}
						lit |= FloorGlow(random, CaveGlowKind.GiantFungus, hue, new Vector3(q.x, floor, q.z), random.Range(0.7f, 1.25f), 6f, light: !lit, lightChance: 1f);
					}
					// Small caps under and about the giants.
					for (int k = 0; k < random.Range(3, 7); k++)
					{
						float a = random.Range(0f, 2f * Mathf.PI), r = random.Range(1f, 5.5f);
						var q = new Vector3(middle.x + Mathf.Cos(a) * r, middle.y + 1f, middle.z + Mathf.Sin(a) * r);
						if (Ground(q, 2.5f, out float floor) && Clear(new Vector3(q.x, floor, q.z), 0.3f))
						{
							FloorGlow(random, CaveGlowKind.Fungus, hue, new Vector3(q.x, floor, q.z), random.Range(0.8f, 1.4f), 5f, light: false);
						}
					}
				}
			}

			/// <summary>A spot on a chamber's floor near a point, clear by <paramref name="margin"/> and away from others laid; false where none.</summary>
			private bool Spot(Vector3 near, float margin, List<Vector3> others, float apart, out Vector3 at)
			{
				at = near;
				if (!Ground(near + Vector3.up * 1.5f, 4f, out float floor))
				{
					return false;
				}
				at = new Vector3(near.x, floor, near.z);
				foreach (Vector3 o in others)
				{
					if ((o - at).sqrMagnitude < apart * apart)
					{
						return false;
					}
				}
				return Clear(at, margin);
			}

			// ── Passages ──────────────────────────────────────────

			/// <summary>
			/// Along the passages, by character: a crystal gallery's crystals at the walls' feet every 5–9 m; a rift's shelf
			/// fungus up its tall canyon walls; a warren's fungus tufts where its ways meet; elsewhere now and then a wall's glow.
			/// </summary>
			private void PassageGlow(DeterministicRNG random)
			{
				CaveGlowPalette colours = Colours();
				float density = Density;
				foreach (CavePassage passage in system.Passages)
				{
					List<CaveNode> nodes = passage.Nodes;
					if (nodes.Count < 2)
					{
						continue;
					}
					float start = nodes[0].Along, end = nodes[nodes.Count - 1].Along;
					float hand = random.NextFloat() < 0.5f ? -1f : 1f;
					if (system.Character == CaveCharacter.Crystal)
					{
						for (float along = start + random.Range(3f, 7f); along < end; along += random.Range(5f, 9f))
						{
							if (random.NextFloat() < 0.7f)
							{
								hand = -hand;
							}
							bool second = random.NextFloat() < 0.3f;
							CentreAt(nodes, along, out Vector3 c, out float nominal);
							CentreAt(nodes, along + 0.5f, out Vector3 ahead, out _);
							Vector3 side = Vector3.Cross(Vector3.up, Flat(ahead - c)) * hand;
							float radius = RadiusAt(nodes, along);
							var from = new Vector3(c.x, nominal + 0.5f, c.z);
							if (NearMouth(c) || !WallFoot(from, side, 2f * radius + 1f, out Vector3 foot) || !Ground(foot - side * 0.45f + Vector3.up * 0.4f, 2f, out float floor))
							{
								continue;
							}
							var at = new Vector3(foot.x - side.x * 0.45f, floor, foot.z - side.z * 0.45f);
							if (Clear(at, 0.3f))
							{
								Clump(random, at, second ? 2 : 1, small: radius < 3f);
							}
						}
					}
					// The walls' glow: up a rift's tall canyon walls closely, elsewhere far between.
					bool rift = system.Character == CaveCharacter.Rift;
					float wallChance = rift ? 0.85f : system.Character == CaveCharacter.Crystal ? 0.3f : 0.5f;
					for (float along = start + random.Range(4f, 12f); along < end; along += rift ? random.Range(6f, 14f) : random.Range(18f, 40f) / density)
					{
						hand = -hand;
						float roll = random.NextFloat(), up = random.NextFloat();
						CentreAt(nodes, along, out Vector3 c, out float nominal);
						CentreAt(nodes, along + 0.5f, out Vector3 ahead, out _);
						if (roll >= wallChance || NearMouth(c))
						{
							continue;
						}
						float radius = RadiusAt(nodes, along), tall = TallAt(nodes, along);
						float top = rift && tall >= 1.4f ? Mathf.Min(6f, radius * tall) : 2.5f;
						var from = new Vector3(c.x, nominal + Mathf.Lerp(1f, Mathf.Max(1.2f, top), up), c.z);
						(CaveGlowKind kind, int hue) = WallKind(random);
						WallGlow(random, kind, hue, from, Vector3.Cross(Vector3.up, Flat(ahead - c)) * hand, 2f * radius * Mathf.Max(1f, tall) + 1f, random.Range(0.8f, 1.3f), light: true);
					}
					// Where a warren's ways meet: a few tufts by the walls round the junction.
					if (system.Character == CaveCharacter.Warren && colours.Fungi && passage.Kind != CavePassageKind.Trunk)
					{
						bool here = random.NextFloat() < 0.6f;
						int tufts = random.Range(1, 4);
						CaveNode n = nodes[0];
						for (int k = 0; k < tufts && here; k++)
						{
							Vector3 dir = Dir(random.Range(0f, 360f));
							if (WallFoot(new Vector3(n.Centre.x, n.Floor + 0.5f, n.Centre.z), dir, 2f * n.Radius + 2f, out Vector3 foot)
								&& Ground(foot - dir * 0.3f + Vector3.up * 0.4f, 2f, out float floor))
							{
								var at = new Vector3(foot.x - dir.x * 0.3f, floor, foot.z - dir.z * 0.3f);
								if (Clear(at, 0f))
								{
									FloorGlow(random, CaveGlowKind.Fungus, colours.Fungus, at, random.Range(0.8f, 1.3f), 4f, light: true);
								}
							}
						}
					}
				}
			}

			/// <summary>The passage's height against its section (its <see cref="CaveNode.Height"/>) at an arc length along it.</summary>
			private static float TallAt(List<CaveNode> nodes, float along)
			{
				int i = 1;
				while (i < nodes.Count - 1 && nodes[i].Along < along)
				{
					i++;
				}
				CaveNode a = nodes[i - 1], b = nodes[i];
				return Mathf.Lerp(a.Height, b.Height, Mathf.Clamp01((along - a.Along) / Mathf.Max(1e-4f, b.Along - a.Along)));
			}

			// ── Hall walls ────────────────────────────────────────

			/// <summary>Round each hall's walls at a reach's height: shelf fungus, or a crystal gallery's crystals, or emberstone.</summary>
			private void HallWalls(DeterministicRNG random)
			{
				float density = Density;
				foreach (CaveChamber c in system.Chambers)
				{
					if (!IsHall(c))
					{
						continue;
					}
					float round = Mathf.PI * (c.Radii.x + c.Radii.z);
					int count = Mathf.Clamp(Mathf.RoundToInt(round / 16f * density), 2, 12);
					float reach = Mathf.Max(c.Radii.x, c.Radii.z) + 3f;
					for (int k = 0; k < count; k++)
					{
						Vector3 dir = Dir(random.Range(0f, 360f));
						float h = random.Range(1f, 2.5f);
						var from = new Vector3(c.Centre.x, CaveSystem.FloorOf(c, c.Centre) + h, c.Centre.z);
						// From well out toward that wall, so a column or a ramp near the middle is not what the ray meets.
						Vector3 start = InChamber(c, Mathf.Atan2(Vector3.Dot(dir, Across(c)), Vector3.Dot(dir, Along(c))), 0.6f);
						start.y += h;
						(CaveGlowKind kind, int hue) = WallKind(random);
						if (system.Void(start) < 0f)
						{
							from = start;
						}
						WallGlow(random, kind, hue, from, dir, reach, random.Range(0.9f, 1.4f), light: true);
					}
				}
			}

			private static Vector3 Along(in CaveChamber c) => Dir(c.Yaw);

			private static Vector3 Across(in CaveChamber c)
			{
				Vector3 a = Dir(c.Yaw);
				return new Vector3(a.z, 0f, -a.x);
			}

			// ── Glow-worms ────────────────────────────────────────

			/// <summary>
			/// Glow-worm colonies hung from the roofs of large rooms and halls, in clusters, and in threads over the chasms
			/// (not in ice or lava, where nothing lives): only where the roof is 4–30 m over the floor, as the larvae hang
			/// their snares in still, damp air, and never lower than a tall person's head over a way.
			/// </summary>
			private void GlowWorms(DeterministicRNG random)
			{
				if (!Colours().Worms)
				{
					return;
				}
				float chance;
				switch (system.Character)
				{
					case CaveCharacter.Halls: chance = 0.8f; break;
					case CaveCharacter.Rift: chance = 0.55f; break;
					case CaveCharacter.Abyss: chance = 0.5f; break;
					case CaveCharacter.Classic: chance = 0.35f; break;
					default: chance = 0.3f; break;
				}
				if (system.Form == CaveForm.SeaCave)
				{
					chance = 0.1f;
				}
				// The cave biome's glow-worms (its Cave Glows, 2026-10-25): 1 the chance as it was.
				chance *= Colours().WormChance;
				foreach (CaveChamber c in system.Chambers)
				{
					float reach = Mathf.Max(c.Radii.x, c.Radii.z);
					bool roll = random.NextFloat() < chance;
					if (c.Kind == CaveChamberKind.Alcove || c.Kind == CaveChamberKind.Stope || reach < 9f || !roll)
					{
						continue;
					}
					int clusters = Mathf.Min(4, 1 + random.Range(0, 1 + Mathf.FloorToInt(reach / 15f)));
					for (int k = 0; k < clusters; k++)
					{
						Vector3 middle = InChamber(c, random.Range(0f, 2f * Mathf.PI), random.Range(0f, 0.65f));
						int patches = random.Range(3, 9);
						for (int j = 0; j < patches; j++)
						{
							float a = random.Range(0f, 2f * Mathf.PI), r = random.Range(0f, 4.5f);
							var p = new Vector3(middle.x + Mathf.Cos(a) * r, middle.y + 1.2f, middle.z + Mathf.Sin(a) * r);
							if (Ground(p, 3f, out float floor))
							{
								WormPatch(random, new Vector3(p.x, floor, p.z), floor);
							}
						}
					}
				}
				// Over the chasms: threads along the slot, under the roof over the floor it splits.
				float over = system.Character == CaveCharacter.Rift || system.Character == CaveCharacter.Abyss ? 1f : 0.4f;
				foreach (CaveRift rift in system.Rifts)
				{
					var line = new Vector3(rift.B.x - rift.A.x, 0f, rift.B.z - rift.A.z);
					float length = line.magnitude;
					if (length < 2f || random.NextFloat() >= over)
					{
						continue;
					}
					Vector3 dir = line / length;
					var across = new Vector3(dir.z, 0f, -dir.x);
					float noise = random.Range(0f, 1000f);
					for (float s = random.Range(0f, 3f); s < length; s += random.Range(2.5f, 5f))
					{
						float jitter = random.Range(-0.8f, 0.8f) * rift.Width, keep = random.NextFloat();
						if (keep >= Mathf.Clamp01(Mathf.PerlinNoise(noise + s * 0.12f, noise) * 1.8f - 0.35f))
						{
							continue;
						}
						Vector3 p = new Vector3(rift.A.x, 0f, rift.A.z) + dir * s + across * jitter;
						p.y = rift.Top - 0.5f;
						if (system.Void(p) < 0f)
						{
							WormPatch(random, p, rift.Top - 1.5f);
						}
					}
				}
			}

			/// <summary>One colony hung from the roof over a point (system frame), measured against the floor it is over.</summary>
			private void WormPatch(DeterministicRNG random, Vector3 p, float floor)
			{
				int variant = random.Range(0, CaveArt.VariantCount(CaveGlowKind.GlowWorms));
				float scale = random.Range(0.6f, 1f), yaw = random.Range(0f, 360f);
				var under = new Vector3(p.x, Mathf.Max(p.y, floor) + 0.5f, p.z);
				if (!Roof(under, 32f, out float roof) || roof - floor < 4f || roof - floor > 30f)
				{
					return;
				}
				if (!CaveArt.TryGlow(CaveGlowKind.GlowWorms, 0, variant, out CaveGlowArt art))
				{
					Missing.Add("cave glow art");
					return;
				}
				var ground = new Vector3(p.x, floor, p.z);
				if ((InLane(ground, 0f) || OnWay(ground, 0f)) && roof - floor - art.Height * scale < Headroom)
				{
					return;
				}
				if (Place(art.Prefab, new Vector3(p.x, roof + 0.03f, p.z), Quaternion.Euler(0f, yaw, 0f), Vector3.one * scale))
				{
					Glowing++;
				}
			}
		}
	}
}
#endif
