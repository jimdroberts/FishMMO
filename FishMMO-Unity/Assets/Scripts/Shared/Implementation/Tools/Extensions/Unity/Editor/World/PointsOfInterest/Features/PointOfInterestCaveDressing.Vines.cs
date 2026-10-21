#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Vines and roots inside the caves (2026-10-20: "More vines and stuff inside caves would be great too"): tree roots hanging
	/// through the thin roof near a mouth, ivy and creeper matting the damp walls as far in as the daylight reaches (and the
	/// floor green under an opening in the roof), and in the deep dark the fantasy glowroots hung from the halls' roofs and
	/// glow-vines creeping over their wet walls. A mine has a few roots by its portal and nothing else.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>As the mouths are draped.</b> The ivy is the biome's own vines (its template's, <see cref="BiomeTemplateArt.Vines"/>)
	/// laid by the vine rules' face probe (<see cref="VineRules.TryFace(Func{Vector3, bool}, Vector3, Vector3, float, float, out Vector3, out float, out float, out Vector3)"/>)
	/// against the cave's field, so a mat leans as its wall leans and never stands off it; visual only, no colliders.
	/// </para>
	/// <para>
	/// <b>Never in the way.</b> Whatever hangs over a lane or a way keeps a tall person's head clear of it
	/// (<see cref="Headroom"/>); elsewhere it stops short of the floor.
	/// </para>
	/// </remarks>
	public sealed partial class PointOfInterestCaveDressing
	{
		private sealed partial class Dresser
		{
			/// <summary>Vines and roots laid inside the cave.</summary>
			public int Vined;

			/// <summary>The vines' and roots' passes, each on its own stream.</summary>
			private void CaveVines()
			{
				CaveFungusPlan p = Plan;
				TreeRoots(new DeterministicRNG(unchecked(system.Seed * 0x2B3D + 0x71C9)), p);
				DaylightGreen(new DeterministicRNG(unchecked(system.Seed * 0x4D87 + 0x1E33)), p);
				if (p.Fungi && Colours().Fungi && system.Form != CaveForm.Mine)
				{
					DeepGlow(new DeterministicRNG(unchecked(system.Seed * 0x6A2F + 0x3B91)), p);
				}
			}

			/// <summary>
			/// The daylight at a point (system frame), 0..1: from each mouth that is open to the air (not one under water), falling
			/// off to nothing <paramref name="stretch"/> times as far in as plants grow at that mouth (<see cref="LightRadii"/> bore
			/// radii, within <see cref="LightMin"/>–<see cref="LightMax"/>), and never less than the habitat's daylight there.
			/// </summary>
			private float DaylightAt(Vector3 local, float stretch, Vector3 normal = default)
			{
				float lit = 0f;
				foreach (CaveMouth mouth in system.Mouths)
				{
					if (mouth.Kind == CaveMouthKind.Submerged)
					{
						continue;
					}
					float reach = Mathf.Clamp(LightRadii * mouth.Radius, LightMin, LightMax) * stretch;
					lit = Mathf.Max(lit, 1f - (local - mouth.Floor).magnitude / Mathf.Max(1f, reach));
				}
				if (normal != default)
				{
					lit = Mathf.Max(lit, HabitatAt(local, normal).Daylight);
				}
				return Mathf.Clamp01(lit);
			}

			/// <summary>
			/// How far a piece hung from a roof over a spot may drop: to a tall person's head over a lane or a way, else to a little
			/// over the floor; and the scale that lets one <paramref name="drop"/> metres long hang so (at most <paramref name="scale"/>).
			/// </summary>
			private float HangScale(Vector3 ground, float room, float drop, float scale)
			{
				bool overWay = InLane(ground, 0.3f) || OnWay(ground, 0.3f);
				float limit = overWay ? room - Headroom : room - 0.5f;
				return drop * scale <= limit ? scale : limit / Mathf.Max(0.01f, drop);
			}

			// ── Roots ─────────────────────────────────────────────

			/// <summary>
			/// Tree roots come down through the roof where it is thin under the ground (0.3–5 m of rock and soil over it) near a
			/// mouth, thicker the thinner it is, in patches; in a mine only by its portal.
			/// </summary>
			private void TreeRoots(DeterministicRNG random, in CaveFungusPlan p)
			{
				if (p.RootsNearMouth <= 0f || context.GroundAt == null)
				{
					return;
				}
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
					for (float along = nodes[0].Along + random.Range(0.5f, 2f); along < end; along += random.Range(1.2f, 2.8f))
					{
						float across = random.Range(-0.75f, 0.75f), roll = random.NextFloat(), scale = random.Range(0.7f, 1.25f), yaw = random.Range(0f, 360f);
						int variant = random.Range(0, CaveArt.VariantCount(CaveGlowKind.HangingRoots)), hue = random.NextFloat() < 0.7f ? 0 : 1;
						CentreAt(nodes, along, out Vector3 c, out float nominal);
						float near = DaylightAt(c, stretch);
						if (near <= 0f)
						{
							continue;
						}
						CentreAt(nodes, along + 0.5f, out Vector3 ahead, out _);
						Vector3 side = Vector3.Cross(Vector3.up, Flat(ahead - c));
						Vector3 p0 = c + side * (across * RadiusAt(nodes, along));
						p0.y = nominal + 1.2f;
						if (system.Void(p0) >= 0f || !Ground(p0, 3f, out float floor) || !Roof(p0, 30f, out float roof))
						{
							continue;
						}
						Vector3 world = system.Origin + p0;
						float cover = context.GroundAt(world.x, world.z) - (system.Origin.y + roof);
						if (cover < 0.3f || cover > 5f)
						{
							continue;
						}
						float patch = Mathf.Clamp01(Mathf.PerlinNoise(noise + p0.x * 0.25f, noise + p0.z * 0.25f) * 1.7f - 0.35f);
						if (roll >= p.RootsNearMouth * (1f - cover / 5f) * Mathf.Sqrt(near) * patch)
						{
							continue;
						}
						Hang(CaveGlowKind.HangingRoots, hue, variant, new Vector3(p0.x, floor, p0.z), roof, scale, yaw, light: false);
					}
				}
			}

			/// <summary>A piece of roof-mounted art hung over a spot of floor, from the roof over it, kept clear of heads over the ways.</summary>
			private bool Hang(CaveGlowKind kind, int hue, int variant, Vector3 ground, float roof, float scale, float yaw, bool light)
			{
				if (!CaveArt.TryGlow(kind, hue, variant, out CaveGlowArt art))
				{
					Missing.Add("cave glow art");
					return false;
				}
				float s = HangScale(ground, roof - ground.y, art.Height, scale);
				if (s < 0.35f)
				{
					return false;
				}
				if (!Place(art.Prefab, new Vector3(ground.x, roof + 0.03f, ground.z), Quaternion.Euler(0f, yaw, 0f), Vector3.one * s))
				{
					return false;
				}
				Vined++;
				if (light)
				{
					Glowing++;
					Light(new Vector3(ground.x, roof - 0.6f * art.Height * s, ground.z), art, LightSpacing);
				}
				return true;
			}

			// ── Daylight ──────────────────────────────────────────

			/// <summary>
			/// The biome's ivy and creeper on the walls as far in as the daylight goes: mats up the damp walls from the floor, now
			/// and then a fringe trailing from a low roof by the wall; and under an opening in the roof (any mouth not come to over
			/// the ground before it, under water or along a fall's shelf) the floor green with the mouth's ferns and creeper.
			/// </summary>
			private void DaylightGreen(DeterministicRNG random, in CaveFungusPlan p)
			{
				if (p.DaylightVines <= 0f || vines == null || vines.Species == null || vines.Species.Length == 0)
				{
					return;
				}
				float noise = random.Range(0f, 1000f);
				Func<Vector3, bool> solid = q => system.Void(q) >= 0f;
				foreach (CavePassage passage in system.Passages)
				{
					List<CaveNode> nodes = passage.Nodes;
					if (nodes.Count < 2)
					{
						continue;
					}
					float end = nodes[nodes.Count - 1].Along;
					for (float along = nodes[0].Along + random.Range(0.3f, 1.5f); along < end; along += random.Range(1.1f, 2.4f))
					{
						float hand = random.NextFloat() < 0.5f ? -1f : 1f, roll = random.NextFloat(), pick = random.NextFloat(), size = random.NextFloat();
						float fringe = random.NextFloat(), hang = random.Range(0.6f, 2.6f), width = random.Range(1.2f, 2.4f);
						CentreAt(nodes, along, out Vector3 c, out float nominal);
						if (DaylightAt(c, 1.2f) <= 0f)
						{
							continue;
						}
						CentreAt(nodes, along + 0.5f, out Vector3 ahead, out _);
						Vector3 side = Vector3.Cross(Vector3.up, Flat(ahead - c)) * hand;
						var from = new Vector3(c.x, nominal + 0.4f, c.z);
						if (!Wall(from, side, 2f * RadiusAt(nodes, along) + 1f, out Vector3 surface, out Vector3 into) || !Ground(surface - Flat(into) * 0.5f + Vector3.up * 0.6f, 2f, out float floor))
						{
							continue;
						}
						Vector3 o = Flat(-into);
						float lit = DaylightAt(surface, 1.2f, o);
						CaveHabitatSample h = HabitatAt(surface, o);
						float damp = Mathf.Max(h.Wet, Mathf.PerlinNoise(noise + surface.x * 0.2f, noise * 0.6f + surface.z * 0.2f));
						if (roll >= p.DaylightVines * Mathf.Pow(lit, 1.3f) * (0.35f + 0.65f * damp))
						{
							continue;
						}
						string species = vines.Pick(pick);
						var at = new Vector3(surface.x, floor + 0.25f, surface.z) + o * 0.05f;
						if (VineRules.TryFace(solid, at, o, 3.6f, 1.6f, out Vector3 foot, out float tilt, out float height, out _) && height >= 0.9f)
						{
							bool big = height > 2.2f && size < 0.6f;
							var mat = new VineSpot
							{
								Species = species, Form = VineForm.Mat, Size = big ? "Mat" : "Patch", Variant = size < 0.5f ? 0 : 1,
								Position = foot + o * 0.03f + Vector3.down * 0.15f, Yaw = VineRules.YawOf(o), Tilt = tilt,
								Scale = new Vector3(Mathf.Lerp(0.8f, 1.15f, size), Mathf.Clamp(height / (big ? 2.4f : 1.2f), 0.6f, 1.15f), 1f),
							};
							LayVine(in mat);
						}
						// A fringe trailing from a low roof by the wall, the light on it.
						if (fringe < 0.35f * lit)
						{
							Vector3 under = surface + o * 0.6f;
							under.y = floor + 1.2f;
							if (system.Void(under) < 0f && Roof(under, 6f, out float roof) && roof - floor < 6.5f)
							{
								float s = HangScale(new Vector3(under.x, floor, under.z), roof - floor, hang, 1f);
								if (s * hang >= 0.5f)
								{
									VineSpot curtain = VineRules.Curtain(species, new Vector3(under.x, roof + 0.12f, under.z), VineRules.YawOf(o), s * hang, width, random);
									LayVine(in curtain);
								}
							}
						}
					}
				}
				foreach (CaveMouth mouth in system.Mouths)
				{
					if (mouth.Kind != CaveMouthKind.Ground && mouth.Kind != CaveMouthKind.Submerged && mouth.Kind != CaveMouthKind.Shelf)
					{
						SunlitFloor(random, mouth);
					}
				}
			}

			/// <summary>A vine piece laid (system frame); its art noted missing until generated.</summary>
			private void LayVine(in VineSpot spot)
			{
				GameObject prefab = VinePlacement.PrefabFor(in spot);
				if (prefab == null)
				{
					Missing.Add("vine art");
					return;
				}
				if (Place(prefab, spot.Position, VinePlacement.RotationOf(in spot), spot.Scale))
				{
					Vined++;
				}
			}

			private PointOfInterestCaveMouths.Growth growth;

			/// <summary>
			/// The floor under an opening in the roof, green where the sun falls through it: the mouth's growth (the biome's ferns,
			/// bushes and creeper) thick under the opening and thinning out to half again its radius, off the trodden lines.
			/// </summary>
			private void SunlitFloor(DeterministicRNG random, in CaveMouth mouth)
			{
				growth ??= PointOfInterestCaveMouths.GrowthFor(PointOfInterestCaveMouths.BiomeOf(context, system));
				if (growth.Species.Count == 0)
				{
					return;
				}
				float reach = Mathf.Max(3f, 1.5f * mouth.Radius);
				int count = Mathf.RoundToInt(Mathf.PI * reach * reach * 0.6f);
				for (int k = 0; k < count; k++)
				{
					float a = random.Range(0f, 360f), r = reach * Mathf.Sqrt(random.NextFloat()), pick = random.NextFloat(), size = random.NextFloat(), yaw = random.Range(0f, 360f);
					float lit = 1f - r / reach;
					Vector3 q = mouth.Floor + Dir(a) * r + Vector3.up * 1.5f;
					if (random.NextFloat() >= 0.35f + 0.65f * lit || system.Void(q) >= 0f || !Ground(q, 6f, out float floor))
					{
						continue;
					}
					var at = new Vector3(q.x, floor - 0.03f, q.z);
					if (InLane(at, Trodden - Lane) || Submerged(at))
					{
						continue;
					}
					int species;
					float scale;
					if (growth.Bushes.Count > 0 && pick < 0.2f && lit > 0.3f && !InLane(at, 0f))
					{
						species = growth.Bushes[Mathf.FloorToInt(size * 997f) % growth.Bushes.Count];
						scale = 0.6f + 0.45f * lit;
					}
					else if (growth.Creeper >= 0 && pick < 0.45f)
					{
						species = growth.Creeper;
						scale = 0.8f + 0.5f * size;
					}
					else if (growth.Ferns.Count > 0)
					{
						species = growth.Ferns[Mathf.FloorToInt(size * 991f) % growth.Ferns.Count];
						scale = (0.6f + 0.4f * Mathf.Sqrt(lit)) * (0.8f + 0.4f * size);
					}
					else
					{
						continue;
					}
					if (Place(growth.Species[species], at, Quaternion.Euler(0f, yaw, 0f), Vector3.one * scale))
					{
						Grown++;
					}
				}
			}

			// ── The deep dark ─────────────────────────────────────

			/// <summary>
			/// Glowroot curtains hung from the roofs of the rooms and halls in the dark, where the rock is wet, and glow-vines
			/// creeping over their walls; down the dark passages now and then a glowroot fringe or a glow-vine. A light with some.
			/// </summary>
			private void DeepGlow(DeterministicRNG random, in CaveFungusPlan p)
			{
				float weight = p.GlowRoots * CaveFungi.CharacterWeight(system.Character);
				if (weight <= 0f)
				{
					return;
				}
				float noise = random.Range(0f, 1000f);
				foreach (CaveChamber c in system.Chambers)
				{
					float area = Mathf.PI * c.Radii.x * c.Radii.z;
					int curtains = Mathf.Clamp(Mathf.RoundToInt(area / 240f * weight), 0, 8);
					int mats = Mathf.Clamp(Mathf.RoundToInt(area / 200f * weight), 0, 8);
					if (c.Kind == CaveChamberKind.Stope || Mathf.Max(c.Radii.x, c.Radii.z) < 5f)
					{
						continue;
					}
					for (int k = 0; k < curtains; k++)
					{
						float a = random.Range(0f, 2f * Mathf.PI), t = random.Range(0f, 0.85f), scale = random.Range(0.7f, 1.2f), yaw = random.Range(0f, 360f), roll = random.NextFloat();
						int variant = random.Range(0, CaveArt.VariantCount(CaveGlowKind.GlowRoots));
						int hue = random.NextFloat() < 0.3f ? Colours().FungusAccent : Colours().Fungus;
						Vector3 near = InChamber(c, a, t) + Vector3.up * 1.2f;
						if (system.Void(near) >= 0f || !Ground(near, 4f, out float floor) || !Roof(near, 45f, out float roof))
						{
							continue;
						}
						var ground = new Vector3(near.x, floor, near.z);
						if (roof - floor < 3.5f || roof - floor > 45f || DaylightAt(ground, 1.5f) > 0.35f || roll >= Rich(ground, noise) * 1.25f - 0.1f)
						{
							continue;
						}
						Hang(CaveGlowKind.GlowRoots, hue, variant, ground, roof, scale, yaw, light: random.NextFloat() < 0.5f);
					}
					float reach = Mathf.Max(c.Radii.x, c.Radii.z) + 3f;
					for (int k = 0; k < mats; k++)
					{
						Vector3 dir = Dir(random.Range(0f, 360f));
						int variant = random.Range(0, CaveArt.VariantCount(CaveGlowKind.GlowVine));
						// A cascade hangs from its top: set it high on the wall; a patch or a sheet climbs from near the floor.
						float h = variant == 2 ? random.Range(2.6f, 3.6f) : random.Range(0.2f, 1f), scale = random.Range(0.8f, 1.2f), roll = random.NextFloat();
						int hue = random.NextFloat() < 0.3f ? Colours().FungusAccent : Colours().Fungus;
						Vector3 start = InChamber(c, Mathf.Atan2(Vector3.Dot(dir, Across(c)), Vector3.Dot(dir, Along(c))), 0.55f);
						start.y += h;
						if (system.Void(start) >= 0f || DaylightAt(start, 1.5f) > 0.35f || roll >= Rich(start, noise + 17f) * 1.25f - 0.1f)
						{
							continue;
						}
						WallPiece(random, CaveGlowKind.GlowVine, hue, variant, start, dir, reach, scale, 10f, light: true);
					}
				}
				foreach (CavePassage passage in system.Passages)
				{
					List<CaveNode> nodes = passage.Nodes;
					if (nodes.Count < 2)
					{
						continue;
					}
					float end = nodes[nodes.Count - 1].Along;
					for (float along = nodes[0].Along + random.Range(6f, 16f); along < end; along += random.Range(12f, 30f) / weight)
					{
						float hand = random.NextFloat() < 0.5f ? -1f : 1f, roll = random.NextFloat(), pick = random.NextFloat(), scale = random.Range(0.7f, 1.1f), yaw = random.Range(0f, 360f);
						int hue = random.NextFloat() < 0.3f ? Colours().FungusAccent : Colours().Fungus;
						CentreAt(nodes, along, out Vector3 c, out float nominal);
						CentreAt(nodes, along + 0.5f, out Vector3 ahead, out _);
						var at = new Vector3(c.x, nominal, c.z);
						if (NearMouth(c) || DaylightAt(at, 1.5f) > 0.2f || roll >= Rich(at, noise + 41f, 0.07f) * 1.2f - 0.15f)
						{
							continue;
						}
						Vector3 side = Vector3.Cross(Vector3.up, Flat(ahead - c)) * hand;
						float radius = RadiusAt(nodes, along);
						if (pick < 0.5f)
						{
							WallPiece(random, CaveGlowKind.GlowVine, hue, 0, new Vector3(c.x, nominal + 0.4f, c.z), side, 2f * radius + 1f, scale, 10f, light: true);
							continue;
						}
						// A fringe of glowroots from the roof off the passage's middle.
						Vector3 under = c + side * (0.5f * radius);
						under.y = nominal + 1.2f;
						if (system.Void(under) < 0f && Ground(under, 3f, out float floor) && Roof(under, 20f, out float roof) && roof - floor >= 3f)
						{
							Hang(CaveGlowKind.GlowRoots, hue, 2, new Vector3(under.x, floor, under.z), roof, scale, yaw, light: true);
						}
					}
				}
			}
		}
	}
}
#endif
