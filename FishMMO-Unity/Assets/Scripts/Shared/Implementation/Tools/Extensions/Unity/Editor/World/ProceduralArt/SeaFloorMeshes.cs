#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// The sea floor's detail meshes: kelp and seaweed, brain and table corals, sea fans,
	/// sponges, anemones, urchins, starfish, shells and a vent's tube worms; and since the vegetation expansion
	/// (2026-10-10) mussel, oyster and vent-clam beds, barnacled stones, sea lettuce, sand dollars, sea cucumbers,
	/// brittle stars, sea pens, crinoids, glass sponges, cold-water and soft corals, giant clams, xenophyophores and
	/// microbial mats (with the mat's land cousin, a cave's slime mould). Built like every other detail
	/// (<see cref="VegetationMeshes"/>): one sub-mesh, vertex colours for the hue, the foliage atlas's
	/// cells for the outline (<see cref="FoliageCell.Solid"/> for anything solid), and the sway and
	/// flutter weights the vegetation shader moves by — under the sea by the swell's surge (its
	/// <c>_Aquatic</c> mode, FishSea.hlsl), not the wind.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Animals and corals are not tinted.</b> Their vertex alpha is 0, so the healthy/dry and season
	/// tints (a drought browning a reef) and the ground's colour leave them alone; plants keep 1.
	/// </para>
	/// <para>
	/// <b>Solid shapes are lathes.</b> A dome, a barrel or a cup is a profile turned about the up axis
	/// (<see cref="PlantParts.Lathe"/>), its faces turned outward by the profile's own direction, so a sponge's inner
	/// wall faces in and a table coral's underside faces down without special cases.
	/// </para>
	/// </remarks>
	public static class SeaFloorMeshes
	{
		/// <summary>Builds a sea-floor kind into <paramref name="mesh"/>; false for a kind that is not one.</summary>
		public static bool Build(MeshBuilder mesh, in DetailPlant p, DeterministicRNG rng)
		{
			switch (p.Kind)
			{
				case DetailKind.Kelp: Kelp(mesh, in p, rng); return true;
				case DetailKind.Seaweed: Wrack(mesh, in p, rng); return true;
				case DetailKind.BrainCoral: BrainCoral(mesh, in p, rng); return true;
				case DetailKind.TableCoral: TableCoral(mesh, in p, rng); return true;
				case DetailKind.SeaFan: SeaFan(mesh, in p, rng); return true;
				case DetailKind.Sponge: Sponges(mesh, in p, rng); return true;
				case DetailKind.Anemone: Anemones(mesh, in p, rng); return true;
				case DetailKind.Urchin: Urchins(mesh, in p, rng); return true;
				case DetailKind.Starfish: Starfish(mesh, in p, rng); return true;
				case DetailKind.Shells: Shells(mesh, in p, rng); return true;
				case DetailKind.TubeWorms: TubeWorms(mesh, in p, rng); return true;
				case DetailKind.MusselBed: MusselBed(mesh, in p, rng); return true;
				case DetailKind.Barnacles: Barnacles(mesh, in p, rng); return true;
				case DetailKind.SeaLettuce: SeaLettuce(mesh, in p, rng); return true;
				case DetailKind.SandDollar: SandDollars(mesh, in p, rng); return true;
				case DetailKind.SeaCucumber: SeaCucumbers(mesh, in p, rng); return true;
				case DetailKind.BrittleStar: BrittleStars(mesh, in p, rng); return true;
				case DetailKind.SeaPen: SeaPens(mesh, in p, rng); return true;
				case DetailKind.Crinoid: Crinoids(mesh, in p, rng); return true;
				case DetailKind.GlassSponge: GlassSponges(mesh, in p, rng); return true;
				case DetailKind.ColdCoral: ColdCoral(mesh, in p, rng); return true;
				case DetailKind.SoftCoral: SoftCorals(mesh, in p, rng); return true;
				case DetailKind.GiantClam: GiantClams(mesh, in p, rng); return true;
				case DetailKind.Xenophyophore: Xenophyophores(mesh, in p, rng); return true;
				case DetailKind.BacterialMat: Mats(mesh, in p, rng, false); return true;
				case DetailKind.SlimeMat: Mats(mesh, in p, rng, true); return true;
				default: return false;
			}
		}

		/// <summary>True for a kind that grows under the sea (its material sways with the surge and is kept under the surface).</summary>
		public static bool IsAquatic(DetailKind kind)
		{
			switch (kind)
			{
				case DetailKind.Kelp:
				case DetailKind.Coral:
				case DetailKind.Seaweed:
				case DetailKind.BrainCoral:
				case DetailKind.TableCoral:
				case DetailKind.SeaFan:
				case DetailKind.Sponge:
				case DetailKind.Anemone:
				case DetailKind.Urchin:
				case DetailKind.Starfish:
				case DetailKind.Shells:
				case DetailKind.TubeWorms:
				case DetailKind.MusselBed:
				case DetailKind.Barnacles:
				case DetailKind.SeaLettuce:
				case DetailKind.SandDollar:
				case DetailKind.SeaCucumber:
				case DetailKind.BrittleStar:
				case DetailKind.SeaPen:
				case DetailKind.Crinoid:
				case DetailKind.GlassSponge:
				case DetailKind.ColdCoral:
				case DetailKind.SoftCoral:
				case DetailKind.GiantClam:
				case DetailKind.Xenophyophore:
				case DetailKind.BacterialMat:
					return true;
				// SlimeMat is the bacterial mat's land cousin (a cave's slime mould): built here, never under the sea.
				default:
					return false;
			}
		}

		/// <summary>
		/// How far a sea-floor kind moves with the surge (the material's <c>_WindSway</c>, metres per metre of
		/// sway weight at full surge) and how much its fronds shiver (<c>_WindFlutter</c>): kelp and weed go
		/// with the water, fans and anemones' tentacles give a little, stone-hard corals and shells not at all.
		/// </summary>
		public static Vector2 Sway(DetailKind kind)
		{
			switch (kind)
			{
				case DetailKind.Kelp: return new Vector2(0.3f, 0.04f);
				case DetailKind.Seaweed: return new Vector2(0.45f, 0.03f);
				case DetailKind.SeaFan: return new Vector2(0.08f, 0.005f);
				case DetailKind.Anemone: return new Vector2(0.25f, 0.012f);
				case DetailKind.TubeWorms: return new Vector2(0.03f, 0.01f);
				// Ulva's sheets are a cell thick and go wholly with the water; a sea pen's and a crinoid's feathers bend
				// on their stalks; a soft coral's fleshy fingers and lobes give where a stony coral holds.
				case DetailKind.SeaLettuce: return new Vector2(0.4f, 0.03f);
				case DetailKind.SeaPen: return new Vector2(0.06f, 0.008f);
				case DetailKind.Crinoid: return new Vector2(0.1f, 0.015f);
				case DetailKind.SoftCoral: return new Vector2(0.1f, 0.01f);
				default: return Vector2.zero;
			}
		}

		// ── Helpers ───────────────────────────────────────────────────

		private static Color Pick(in DetailPlant p, DeterministicRNG rng) => Color.Lerp(p.ColourA, p.ColourB, rng.NextFloat());

		private static Color Accent(in DetailPlant p, DeterministicRNG rng) =>
			p.Accents != null && p.Accents.Length > 0 ? p.Accents[rng.Next(p.Accents.Length)] : Pick(in p, rng);

		/// <summary>A random unit vector.</summary>
		private static Vector3 Direction(DeterministicRNG rng)
		{
			float y = rng.Range(-1f, 1f);
			float phi = rng.NextFloat() * Mathf.PI * 2f;
			float r = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y));
			return new Vector3(Mathf.Cos(phi) * r, y, Mathf.Sin(phi) * r);
		}

		/// <summary>A dome's lathe share (run from the foot up) as the share from its crown down to its rim, 0..1.</summary>
		private static float DomeShare(float s, float foot) => Mathf.Clamp01((1f - s) / Mathf.Max(1e-3f, 1f - foot));

		private static Vector2 SolidUV(float u, float v) => FoliageAtlas.CellUV(FoliageCell.Solid, Mathf.Clamp01(u), Mathf.Repeat(v, 1f));

		/// <summary>A point within a disc of the plant's radius: where one of several animals or corals of an instance sits.</summary>
		private static Vector3 Scatter(in DetailPlant p, DeterministicRNG rng, int index, int count)
		{
			if (count <= 1)
			{
				return Vector3.zero;
			}
			float angle = (index + rng.Range(-0.35f, 0.35f)) * Mathf.PI * 2f / count;
			float r = p.Radius * Mathf.Sqrt(rng.Range(0.25f, 1f));
			return new Vector3(Mathf.Cos(angle) * r, 0f, Mathf.Sin(angle) * r);
		}

		/// <summary>The part the next geometry belongs to, always drawn: the shader turns it about its root by its hash (VegVary).</summary>
		private static void Part(MeshBuilder mesh, Vector3 root, int index, int salt, PlantPart kind)
		{
			float hash = (ProceduralNoise.Mix(((uint)index * 0x9e3779b1u) ^ ((uint)salt * 0x85ebca6bu)) >> 8) * (1f / 16777216f);
			mesh.SetPart(root, hash, 0f, kind);
			mesh.SetCard(hash, 0f);
		}

		/// <summary>A thin round stalk, spine or tentacle: a three- or four-sided tube tapering to a point.</summary>
		private static void Spine(MeshBuilder mesh, IList<Vector3> path, float rootRadius, int sides, Color tip, Color root, float tintable,
			Func<float, Vector2> wind)
		{
			var radii = new List<float>(path.Count);
			var colours = new List<Color32>(path.Count);
			var weights = new List<Vector2>(path.Count);
			for (int i = 0; i < path.Count; i++)
			{
				float t = (float)i / (path.Count - 1);
				radii.Add(i == path.Count - 1 ? 0f : rootRadius * (1f - 0.7f * t));
				colours.Add(PlantParts.C32(Color.Lerp(root, tip, t), tintable));
				weights.Add(wind != null ? wind(t) : Vector2.zero);
			}
			PlantParts.Tube(mesh, 0, path, radii, sides, 1f, colours, weights, uvMap: SolidUV);
		}

		// ── Plants ────────────────────────────────────────────────────

		/// <summary>
		/// Giant kelp: a few stipes rising from one holdfast, each hung with strap blades every few hand-spans,
		/// alternating round it, the longest at mid height, a long trailing blade at the top. The stipes are what
		/// the surge sways (the sway weight grows up them); the blades shiver on their own.
		/// </summary>
		public static void Kelp(MeshBuilder mesh, in DetailPlant p, DeterministicRNG rng)
		{
			int segments = Mathf.Max(4, p.Segments);
			var spine = new List<Vector3>();
			var widths = new List<float>();
			var colours = new List<Color32>();
			var wind = new List<Vector2>();
			int stipes = Mathf.Max(1, p.Count);
			for (int k = 0; k < stipes; k++)
			{
				float angle = rng.NextFloat() * Mathf.PI * 2f;
				Vector3 root = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * (p.Radius * 0.4f * Mathf.Sqrt(rng.NextFloat()));
				Part(mesh, root, k, 1100, PlantPart.Frond);
				float height = p.Height * rng.Range(0.65f, 1.15f);
				float phase = rng.NextFloat() * Mathf.PI * 2f;
				var lean = new Vector3(Mathf.Cos(angle + 0.4f), 0f, Mathf.Sin(angle + 0.4f));
				var wave = new Vector3(-lean.z, 0f, lean.x);
				Color stipeColour = Pick(in p, rng) * 0.7f;
				Vector3 At(float t) => root + Vector3.up * (height * t) + lean * (height * 0.12f * t * t) + wave * (Mathf.Sin(t * 4f + phase) * 0.1f * t);
				Vector2 WindAt(float t, float flutter) => new Vector2(Mathf.Pow(t, 1.3f) * height * 0.5f, flutter);

				spine.Clear(); widths.Clear(); colours.Clear(); wind.Clear();
				for (int s = 0; s <= segments; s++)
				{
					float t = (float)s / segments;
					spine.Add(At(t));
					widths.Add(0.03f * (1f - 0.4f * t));
					colours.Add(PlantParts.C32(stipeColour * Mathf.Lerp(0.7f, 1f, t), 1f));
					wind.Add(WindAt(t, 0.1f * t));
				}
				PlantParts.Strip(mesh, 0, spine, widths, Vector3.Cross(Vector3.up, wave), FoliageCell.Strap, colours, wind, Vector3.zero);

				// Blades from a quarter of the way up, every 0.35 m or so, turning a golden angle each time.
				int blades = Mathf.Clamp(Mathf.RoundToInt(height * 0.75f / 0.35f), 3, 11);
				float yaw = rng.NextFloat() * 360f;
				for (int b = 0; b < blades; b++)
				{
					float t = 0.25f + 0.75f * (b + rng.Range(0.1f, 0.9f)) / blades;
					Vector3 foot = At(t);
					yaw += 137.5f;
					Vector3 outward = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
					float length = p.Height * 0.14f * (0.7f + 0.6f * Mathf.Sin(Mathf.PI * t)) * rng.Range(0.8f, 1.2f);
					Vector3 dir = (outward + Vector3.up * rng.Range(0.9f, 1.6f)).normalized;
					Color colour = Pick(in p, rng);
					spine.Clear(); widths.Clear(); colours.Clear(); wind.Clear();
					for (int s = 0; s <= 3; s++)
					{
						float v = s / 3f;
						// Out and up, the far end drooping a little: a blade floats, but not stiffly.
						spine.Add(foot + dir * (length * v) + Vector3.down * (length * 0.15f * v * v));
						widths.Add(p.Width * (s == 0 ? 0.35f : s == 3 ? 0.4f : 1f));
						colours.Add(PlantParts.C32(colour * Mathf.Lerp(0.8f, 1.1f, v), 1f));
						wind.Add(WindAt(t, 0.3f + 0.7f * v));
					}
					PlantParts.Strip(mesh, 0, spine, widths, Vector3.Cross(dir, outward).sqrMagnitude > 1e-6f ? Vector3.Cross(dir, outward) : wave,
						FoliageCell.Strap, colours, wind, Vector3.zero);
				}

				// The trailing blade at the top.
				Vector3 top = At(1f);
				spine.Clear(); widths.Clear(); colours.Clear(); wind.Clear();
				Color tipColour = Pick(in p, rng);
				for (int s = 0; s <= 3; s++)
				{
					float v = s / 3f;
					spine.Add(top + (lean * 0.8f + Vector3.up * 0.6f).normalized * (p.Height * 0.15f * v));
					widths.Add(p.Width * (s == 3 ? 0.3f : 0.8f));
					colours.Add(PlantParts.C32(tipColour, 1f));
					wind.Add(WindAt(1f, 0.5f + 0.5f * v));
				}
				PlantParts.Strip(mesh, 0, spine, widths, Vector3.Cross(Vector3.up, lean), FoliageCell.Strap, colours, wind, Vector3.zero);
			}
		}

		/// <summary>
		/// Seaweed as a wrack: flat olive-brown fronds rising from one holdfast and forking twice, each fork
		/// narrower and a little paler, with paired air bladders along them that hold them up in the water. The
		/// surge sways them from the holdfast; the tips shiver.
		/// </summary>
		private static void Wrack(MeshBuilder mesh, in DetailPlant p, DeterministicRNG rng)
		{
			var spine = new List<Vector3>(3);
			var widths = new List<float>(3);
			var colours = new List<Color32>(3);
			var wind = new List<Vector2>(3);
			float plantHeight = p.Height;
			float plantWidth = p.Width;
			Color baseColour = p.ColourA, tipColour = p.ColourB;
			void Frond(Vector3 start, Vector3 dir, float length, float width, int depth, float along)
			{
				Vector3 side = Vector3.Cross(dir, Vector3.up);
				if (side.sqrMagnitude < 1e-6f)
				{
					side = Vector3.right;
				}
				side.Normalize();
				// Flat fronds twist a little along their length, so a clump is not all one plane.
				side = Quaternion.AngleAxis(rng.Range(-35f, 35f), dir) * side;
				spine.Clear(); widths.Clear(); colours.Clear(); wind.Clear();
				for (int s = 0; s <= 2; s++)
				{
					float t = s / 2f;
					Vector3 at = start + dir * (length * t) + Vector3.down * (length * 0.08f * t * t);
					spine.Add(at);
					widths.Add(width * (depth == 0 && s == 2 ? 0.55f : 1f));
					float share = Mathf.Clamp01((along + length * t) / plantHeight);
					colours.Add(PlantParts.C32(Color.Lerp(baseColour * 0.7f, tipColour, share), 1f));
					wind.Add(new Vector2(Mathf.Max(0f, at.y) * 0.8f, 0.2f + 0.8f * share));
				}
				PlantParts.Strip(mesh, 0, spine, widths, side, FoliageCell.Strap, colours, wind, Vector3.zero);
				Vector3 end = spine[2];
				// A pair of bladders just below each fork.
				if (depth > 0)
				{
					Color bladder = tipColour * 0.85f;
					Vector3 below = end - dir * (length * 0.15f);
					for (int k = -1; k <= 1; k += 2)
					{
						PlantParts.Card(mesh, 0, below + side * (width * 0.45f * k), side * (width * 0.6f), dir * (width * 0.9f), FoliageCell.Solid,
							PlantParts.C32(bladder, 1f), new Vector2(Mathf.Max(0f, below.y) * 0.8f, 0.4f), new Vector2(Mathf.Max(0f, below.y) * 0.8f, 0.4f), Vector3.zero);
					}
				}
				if (depth <= 0)
				{
					return;
				}
				for (int k = -1; k <= 1; k += 2)
				{
					Vector3 fork = Quaternion.AngleAxis(k * rng.Range(18f, 32f), side) * dir;
					Frond(end, fork.normalized, length * rng.Range(0.65f, 0.8f), width * 0.85f, depth - 1, along + length);
				}
			}
			int fronds = Mathf.Max(1, p.Count);
			for (int f = 0; f < fronds; f++)
			{
				float angle = (f + rng.Range(-0.3f, 0.3f)) * Mathf.PI * 2f / fronds;
				var outward = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
				Vector3 root = outward * (p.Radius * 0.3f * rng.NextFloat());
				Part(mesh, root, f, 1300, PlantPart.Frond);
				Vector3 dir = (Vector3.up + outward * (p.Lean + rng.Range(0f, 0.3f))).normalized;
				Frond(root, dir, plantHeight * 0.42f * rng.Range(0.8f, 1.15f), plantWidth, 2, 0f);
			}
		}

		// ── Corals and sponges ────────────────────────────────────────

		/// <summary>
		/// Brain coral: a dome or two, a little undercut at the foot, the surface worn into meandering ridges
		/// and valleys — the valleys darker — from three crossed waves warping one another.
		/// </summary>
		private static void BrainCoral(MeshBuilder mesh, in DetailPlant p, DeterministicRNG rng)
		{
			for (int d = 0; d < Mathf.Max(1, p.Count); d++)
			{
				float radius = p.Radius * (d == 0 ? 1f : rng.Range(0.4f, 0.65f));
				Vector3 centre = d == 0 ? Vector3.zero : Scatter(in p, rng, d, p.Count + 1) * 1.1f;
				float height = radius * (p.Height / Mathf.Max(0.01f, p.Radius)) * rng.Range(0.85f, 1.1f);
				Color colour = Accent(in p, rng);
				Vector3 a1 = Direction(rng), a2 = Direction(rng), a3 = Direction(rng);
				float k = rng.Range(8f, 10f);
				float Ridge(float s, float u)
				{
					// The direction this vertex looks out along on the unit dome.
					float theta = s * Mathf.PI * 0.55f;
					float phi = u * Mathf.PI * 2f;
					var dir = new Vector3(Mathf.Sin(theta) * Mathf.Cos(phi), Mathf.Cos(theta), Mathf.Sin(theta) * Mathf.Sin(phi));
					float g = Mathf.Sin(k * (Vector3.Dot(dir, a1) + 0.35f * Mathf.Sin(k * 0.7f * Vector3.Dot(dir, a2)) + 0.25f * Mathf.Sin(k * 1.3f * Vector3.Dot(dir, a3))));
					return 1f - Mathf.Abs(g); // 1 on a ridge, 0 in a valley
				}
				var profile = new List<Vector2>();
				const int rings = 10;
				for (int i = 0; i <= rings; i++)
				{
					float theta = (float)i / rings * Mathf.PI * 0.55f;
					profile.Add(new Vector2(radius * Mathf.Sin(theta), height * Mathf.Cos(theta) - height * 0.08f));
				}
				// Tucked in under the rim into the sand; then run from the foot up, so the faces turn out.
				// (Below the rim, which the dome already curls under to: a tuck above it folded back inside out.)
				profile.Add(new Vector2(radius * 0.85f, profile[profile.Count - 1].y - height * 0.07f));
				profile.Reverse();
				const float foot = 1f / (rings + 1);
				PlantParts.Lathe(mesh, centre, profile, 28,
					// Ridged everywhere but the very crown's point and the tuck under the rim.
					(s, u) => s <= foot ? 1f : 1f + 0.045f * (Ridge(DomeShare(s, foot), u) - 0.5f) * Mathf.Min(1f, DomeShare(s, foot) * 6f),
					// Valleys dark, ridges pale, and the whole shaded toward the foot.
					(s, u) => PlantParts.C32(colour * Mathf.Lerp(0.6f, 1.08f, Ridge(DomeShare(s, foot), u)) * Mathf.Lerp(1f, 0.75f, Mathf.Clamp01((DomeShare(s, foot) - 0.8f) * 5f)), 0f),
					yaw: rng.NextFloat() * Mathf.PI * 2f);
			}
		}

		/// <summary>
		/// Table coral: a short stalk carrying a broad flat plate, its rim higher than its middle and its edge
		/// scalloped, the top pale where it faces the light and the underside dark.
		/// </summary>
		private static void TableCoral(MeshBuilder mesh, in DetailPlant p, DeterministicRNG rng)
		{
			for (int t = 0; t < Mathf.Max(1, p.Count); t++)
			{
				float radius = p.Radius * (t == 0 ? 1f : rng.Range(0.45f, 0.7f));
				float stalk = p.Height * (t == 0 ? 1f : rng.Range(0.5f, 0.8f));
				Vector3 centre = t == 0 ? Vector3.zero : Scatter(in p, rng, t, p.Count + 1) * 1.3f;
				Color colour = Accent(in p, rng);
				float a = rng.Range(0f, 6.28f), b = rng.Range(0f, 6.28f);
				var profile = new List<Vector2>
				{
					new Vector2(radius * 0.09f, -0.03f),
					new Vector2(radius * 0.07f, stalk * 0.6f),
					new Vector2(radius * 0.18f, stalk * 0.9f),
					new Vector2(radius * 0.6f, stalk),          // underside, out to the rim
					new Vector2(radius, stalk + radius * 0.06f),
					new Vector2(radius * 1.02f, stalk + radius * 0.1f),  // the rim
					new Vector2(radius * 0.9f, stalk + radius * 0.12f),  // the top, back in, dipping to the middle
					new Vector2(radius * 0.45f, stalk + radius * 0.06f),
					new Vector2(1e-3f, stalk + radius * 0.04f),
				};
				int last = profile.Count - 1;
				PlantParts.Lathe(mesh, centre, profile, 32,
					(s, u) => s < 0.3f ? 1f : 1f + Mathf.Clamp01((s - 0.3f) * 3f) * (0.07f * Mathf.Sin(u * Mathf.PI * 14f + a) + 0.04f * Mathf.Sin(u * Mathf.PI * 26f + b)),
					(s, u) =>
					{
						float i = s * last;
						Color c = i <= 3.5f ? colour * 0.45f : i <= 5.5f ? colour * 0.9f : colour * 1.1f;
						return PlantParts.C32(c, 0f);
					});
			}
		}

		/// <summary>
		/// A sea fan (gorgonian): a short trunk branching again and again in one plane, slightly cupped, into a
		/// lattice of fine twigs. It turns its face to the current and gives a little with the surge.
		/// </summary>
		private static void SeaFan(MeshBuilder mesh, in DetailPlant p, DeterministicRNG rng)
		{
			Color colour = Accent(in p, rng);
			float height = p.Height;
			float reach = Mathf.Max(0.1f, p.Radius);
			var path = new List<Vector3>(4);
			int branches = 0;
			void Branch(Vector3 start, Vector2 dir, float length, float radius, int depth)
			{
				branches++;
				path.Clear();
				for (int s = 0; s <= 2; s++)
				{
					float t = s / 2f;
					Vector2 at = new Vector2(start.x, start.y) + dir * (length * t);
					// Cupped: the fan bows back the further it reaches from its middle.
					path.Add(new Vector3(at.x, at.y, at.x * at.x * 0.35f / reach));
				}
				Spine(mesh, path, radius, 3, colour * 1.1f, colour * 0.85f, 0f, t => new Vector2(Mathf.Max(0f, path[0].y + t * length * dir.y) * 0.5f, 0.2f + 0.3f * depth));
				// Sixty twigs at most: a fan is drawn by the hundred on a reef.
				if (depth <= 0 || branches >= 60)
				{
					return;
				}
				Vector3 end = path[path.Count - 1];
				int children = rng.Range(2, 4);
				for (int c = 0; c < children; c++)
				{
					float spread = (c - (children - 1) * 0.5f) * rng.Range(0.35f, 0.6f);
					float turn = Mathf.Atan2(dir.y, dir.x) + spread;
					var child = new Vector2(Mathf.Cos(turn), Mathf.Sin(turn));
					// Keep growing upward and outward: a fan, never a ball.
					if (child.y < 0.2f)
					{
						child = new Vector2(child.x, 0.2f).normalized;
					}
					Branch(new Vector3(end.x, end.y, 0f), child, length * rng.Range(0.62f, 0.78f), radius * 0.7f, depth - 1);
				}
			}
			Branch(Vector3.zero, Vector2.up, height * 0.22f, Mathf.Max(0.006f, p.Width), 4);
		}

		/// <summary>
		/// A cluster of tube and barrel sponges: open-topped, ribbed walls a hand thick, the inside wall facing
		/// in and darker, each its own colour.
		/// </summary>
		private static void Sponges(MeshBuilder mesh, in DetailPlant p, DeterministicRNG rng)
		{
			int count = Mathf.Max(1, p.Count);
			for (int k = 0; k < count; k++)
			{
				Vector3 centre = Scatter(in p, rng, k, count) * 0.6f;
				float height = p.Height * rng.Range(0.45f, 1.1f);
				float radius = p.Width * rng.Range(0.6f, 1.3f);
				Color colour = Accent(in p, rng);
				float wall = radius * 0.22f;
				var profile = new List<Vector2>
				{
					new Vector2(radius * 0.8f, -0.04f),
					new Vector2(radius, height * 0.3f),
					new Vector2(radius * 1.12f, height * 0.8f),
					new Vector2(radius * 1.15f, height),            // the lip
					new Vector2(radius * 1.15f - wall, height),
					new Vector2(radius - wall, height * 0.7f),      // down the inside
					new Vector2(radius * 0.4f, height * 0.32f),
					new Vector2(1e-3f, height * 0.3f),
				};
				int ribs = rng.Range(7, 12);
				float twist = rng.NextFloat() * 6.28f;
				PlantParts.Lathe(mesh, centre, profile, 16,
					(s, u) => 1f + 0.06f * Mathf.Cos(u * Mathf.PI * 2f * ribs + twist),
					(s, u) => PlantParts.C32(colour * (s < 0.45f ? Mathf.Lerp(0.75f, 1.05f, s / 0.45f) : 0.45f) * (0.92f + 0.08f * Mathf.Cos(u * Mathf.PI * 2f * ribs + twist)), 0f));
			}
		}

		// ── Animals ───────────────────────────────────────────────────

		/// <summary>
		/// Anemones: a short column flaring into an oral disc ringed with two rows of tapering tentacles that
		/// curl out and shiver in the water, tipped paler.
		/// </summary>
		private static void Anemones(MeshBuilder mesh, in DetailPlant p, DeterministicRNG rng)
		{
			int count = Mathf.Max(1, p.Count);
			var path = new List<Vector3>(5);
			for (int k = 0; k < count; k++)
			{
				Vector3 centre = Scatter(in p, rng, k, count);
				float size = rng.Range(0.7f, 1.25f);
				float column = p.Height * 0.6f * size;
				float disc = p.Height * 0.55f * size;
				Color body = Pick(in p, rng);
				Color tentacle = Accent(in p, rng);
				var profile = new List<Vector2>
				{
					new Vector2(disc * 0.75f, -0.02f),
					new Vector2(disc * 0.55f, column * 0.5f),
					new Vector2(disc * 0.7f, column * 0.9f),
					new Vector2(disc, column),
					new Vector2(1e-3f, column * 0.95f),
				};
				PlantParts.Lathe(mesh, centre, profile, 12, null, (s, u) => PlantParts.C32(s > 0.7f ? tentacle * 0.6f : body * Mathf.Lerp(0.7f, 1f, s), 0f));
				for (int ring = 0; ring < 2; ring++)
				{
					int tentacles = ring == 0 ? 18 : 8;
					float at = ring == 0 ? disc * 0.95f : disc * 0.55f;
					for (int t = 0; t < tentacles; t++)
					{
						float angle = (t + rng.Range(-0.3f, 0.3f) + ring * 0.5f) * Mathf.PI * 2f / tentacles;
						var outward = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
						Vector3 root = centre + outward * at + Vector3.up * column;
						float length = p.Height * size * rng.Range(0.7f, 1.1f) * (ring == 0 ? 1f : 0.8f);
						float rise = ring == 0 ? rng.Range(0.3f, 0.7f) : rng.Range(0.8f, 1.2f);
						path.Clear();
						for (int s = 0; s <= 3; s++)
						{
							float v = s / 3f;
							// Up and out, the outer row curling over at the tip.
							path.Add(root + outward * (length * v * (ring == 0 ? 0.9f : 0.4f)) + Vector3.up * (length * (rise * v - (ring == 0 ? 0.5f : 0.1f) * v * v)));
						}
						Spine(mesh, path, Mathf.Max(0.003f, p.Width) * size, 3, Color.Lerp(tentacle, Color.white, 0.45f), tentacle, 0f,
							v => new Vector2(length * v, v));
					}
				}
			}
		}

		/// <summary>Sea urchins: dark flattened balls bristling with long spines, a few to an instance.</summary>
		private static void Urchins(MeshBuilder mesh, in DetailPlant p, DeterministicRNG rng)
		{
			int count = Mathf.Max(1, p.Count);
			var path = new List<Vector3>(3);
			for (int k = 0; k < count; k++)
			{
				Vector3 centre = Scatter(in p, rng, k, count);
				float radius = p.Height * 0.5f * rng.Range(0.75f, 1.2f);
				Color colour = Pick(in p, rng);
				var profile = new List<Vector2>();
				for (int i = 0; i <= 6; i++)
				{
					float theta = (float)i / 6 * Mathf.PI;
					profile.Add(new Vector2(radius * Mathf.Sin(theta), radius * 0.75f * Mathf.Cos(theta) + radius * 0.45f));
				}
				profile.Reverse(); // bottom to top: faces out
				PlantParts.Lathe(mesh, centre, profile, 8, null, (s, u) => PlantParts.C32(colour * Mathf.Lerp(0.6f, 1f, s), 0f));
				// Spines spread over the upper body by the golden angle.
				const int spines = 24;
				for (int i = 0; i < spines; i++)
				{
					float y = 1f - 1.2f * (i + 0.5f) / spines;
					float r = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y));
					float phi = i * 2.39996f;
					var dir = new Vector3(Mathf.Cos(phi) * r, y, Mathf.Sin(phi) * r);
					Vector3 root = centre + new Vector3(dir.x * radius, radius * 0.45f + dir.y * radius * 0.75f, dir.z * radius);
					float length = radius * rng.Range(1.1f, 1.7f);
					path.Clear();
					path.Add(root);
					path.Add(root + dir * (length * 0.5f));
					path.Add(root + dir * length);
					Spine(mesh, path, Mathf.Max(0.002f, p.Width), 3, colour * 1.2f, colour * 0.8f, 0f, null);
				}
			}
		}

		/// <summary>Starfish: five tapering arms, ridged along the top and flat beneath, each arm a little curled, lying on the bed.</summary>
		private static void Starfish(MeshBuilder mesh, in DetailPlant p, DeterministicRNG rng)
		{
			int count = Mathf.Max(1, p.Count);
			for (int k = 0; k < count; k++)
			{
				Vector3 centre = Scatter(in p, rng, k, count);
				float arm = p.Radius * 0.35f * rng.Range(0.7f, 1.2f);
				float thick = Mathf.Max(0.008f, p.Height) * rng.Range(0.8f, 1.2f);
				Color colour = Accent(in p, rng);
				float yaw = rng.NextFloat() * Mathf.PI * 2f;
				const int stations = 5;
				for (int a = 0; a < 5; a++)
				{
					float angle = yaw + a * Mathf.PI * 2f / 5f;
					float curl = rng.Range(-0.35f, 0.35f);
					int first = mesh.VertexCount;
					for (int s = 0; s <= stations; s++)
					{
						float t = (float)s / stations;
						float heading = angle + curl * t * t;
						var along = new Vector3(Mathf.Cos(heading), 0f, Mathf.Sin(heading));
						var side = new Vector3(-along.z, 0f, along.x);
						Vector3 c = centre + along * (arm * t);
						float half = Mathf.Lerp(arm * 0.32f, arm * 0.05f, t);
						float h = thick * (1f - 0.65f * t);
						Color ridge = colour * Mathf.Lerp(1.05f, 0.9f, t);
						Color edge = colour * 0.75f;
						Vector2 uv = SolidUV(0.5f, t);
						mesh.AddVertex(c - side * half + Vector3.up * (h * 0.3f), Vector3.zero, uv, PlantParts.C32(edge, 0f));
						mesh.AddVertex(c + Vector3.up * h, Vector3.zero, uv, PlantParts.C32(ridge, 0f));
						mesh.AddVertex(c + side * half + Vector3.up * (h * 0.3f), Vector3.zero, uv, PlantParts.C32(edge, 0f));
						mesh.AddVertex(c + Vector3.up * 0.002f, Vector3.zero, uv, PlantParts.C32(colour * 0.5f, 0f));
					}
					for (int s = 0; s < stations; s++)
					{
						int i0 = first + s * 4, i1 = i0 + 4;
						mesh.AddQuad(0, i0, i0 + 1, i1 + 1, i1, Vector3.up);
						mesh.AddQuad(0, i0 + 1, i0 + 2, i1 + 2, i1 + 1, Vector3.up);
						// The flat underside, from both edges to the middle line.
						mesh.AddQuad(0, i0, i0 + 3, i1 + 3, i1, Vector3.down);
						mesh.AddQuad(0, i0 + 3, i0 + 2, i1 + 2, i1 + 3, Vector3.down);
					}
				}
			}
		}

		/// <summary>Shells: cockles lying on the bed, ribbed fans of cream, sand and rose.</summary>
		private static void Shells(MeshBuilder mesh, in DetailPlant p, DeterministicRNG rng)
		{
			int count = Mathf.Max(1, p.Count);
			for (int k = 0; k < count; k++)
			{
				Vector3 centre = Scatter(in p, rng, k, count);
				float size = Mathf.Max(0.015f, p.Height) * rng.Range(0.7f, 1.4f);
				Color colour = Accent(in p, rng);
				var profile = new List<Vector2>
				{
					new Vector2(size, 0f),
					new Vector2(size * 0.95f, size * 0.3f),
					new Vector2(size * 0.7f, size * 0.55f),
					new Vector2(size * 0.3f, size * 0.68f),
					new Vector2(1e-3f, size * 0.7f),
				};
				int ribs = rng.Range(9, 15);
				float yaw = rng.NextFloat() * Mathf.PI * 2f;
				PlantParts.Lathe(mesh, centre, profile, 12,
					(s, u) => (1f + 0.07f * Mathf.Abs(Mathf.Cos(u * Mathf.PI * ribs))) * (1f + 0.25f * Mathf.Cos(u * Mathf.PI * 2f)),
					(s, u) => PlantParts.C32(colour * (0.85f + 0.15f * Mathf.Abs(Mathf.Cos(u * Mathf.PI * ribs))) * Mathf.Lerp(1f, 0.8f, s), 0f),
					yaw: yaw);
			}
		}

		/// <summary>
		/// A vent's tube worms: a stand of white chitin tubes, the tallest in the middle, each crowned with a
		/// blood-red plume that shivers in the vent's flow.
		/// </summary>
		private static void TubeWorms(MeshBuilder mesh, in DetailPlant p, DeterministicRNG rng)
		{
			var path = new List<Vector3>(4);
			Color plume = p.Accents != null && p.Accents.Length > 0 ? p.Accents[0] : new Color(0.75f, 0.12f, 0.12f);
			for (int k = 0; k < Mathf.Max(1, p.Count); k++)
			{
				float angle = rng.NextFloat() * Mathf.PI * 2f;
				float r = Mathf.Sqrt(rng.NextFloat());
				var root = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * (p.Radius * r);
				float height = p.Height * Mathf.Lerp(1f, 0.45f, r) * rng.Range(0.75f, 1.15f);
				var lean = new Vector3(rng.Range(-0.08f, 0.08f), 0f, rng.Range(-0.08f, 0.08f)) + root * 0.15f;
				float radius = p.Width * rng.Range(0.8f, 1.2f);
				Color tube = Pick(in p, rng);
				path.Clear();
				for (int s = 0; s <= 3; s++)
				{
					float t = s / 3f;
					path.Add(root + Vector3.up * (height * t) + lean * (height * t * t));
				}
				var radii = new List<float> { radius * 1.1f, radius, radius, radius * 0.95f };
				var colours = new List<Color32>();
				var wind = new List<Vector2>();
				for (int s = 0; s <= 3; s++)
				{
					colours.Add(PlantParts.C32(tube * Mathf.Lerp(0.7f, 1f, s / 3f), 0f));
					wind.Add(new Vector2(height * s / 3f * 0.3f, 0f));
				}
				PlantParts.Tube(mesh, 0, path, radii, 4, 1f, colours, wind, uvMap: SolidUV);
				// The plume: a red tuft opening out of the mouth.
				Vector3 top = path[3];
				var profile = new List<Vector2>
				{
					new Vector2(radius * 0.9f, 0f),
					new Vector2(radius * 2.4f, radius * 2f),
					new Vector2(radius * 1.8f, radius * 4.5f),
					new Vector2(1e-3f, radius * 5f),
				};
				PlantParts.Lathe(mesh, top, profile, 6,
					(s, u) => 1f + 0.25f * Mathf.Sin(u * Mathf.PI * 12f) * s,
					(s, u) => PlantParts.C32(plume * Mathf.Lerp(0.6f, 1.1f, s), 0f),
					s => new Vector2(height * 0.3f + s * radius * 5f, 0.5f + 0.5f * s));
			}
		}

		// ── Beds, crusts and sheets (2026-10-10) ──────────────────────

		/// <summary>A profile turned about the up axis at the origin (<see cref="PlantParts.Lathe"/>), then set in place by <paramref name="place"/>.</summary>
		/// <remarks>Every placement here is a rotation and a positive scale, so the faces still turn the way the lathe turned them.</remarks>
		private static void LatheAt(MeshBuilder mesh, Matrix4x4 place, IList<Vector2> profile, int sides, Func<float, float, float> radiusScale,
			Func<float, float, Color32> colour, Func<float, Vector2> wind = null)
		{
			int first = mesh.VertexCount;
			PlantParts.Lathe(mesh, Vector3.zero, profile, sides, radiusScale, colour, wind);
			mesh.Transform(place, first);
		}

		/// <summary>
		/// A bed of bivalves, by <see cref="DetailPlant.Variant"/>:
		/// <list type="bullet">
		/// <item>0, <b>blue mussels</b>: wedge-shaped blue-black shells packed on end in their byssus threads, the pointed umbo
		/// down in the bed and the rounded end up, worn purple at the umbo and edged brown where the skin survives.</item>
		/// <item>1, <b>oysters</b>: rough, irregular, flattened grey-khaki shells with frilled edges, cemented in a heap, some
		/// flat and some on edge, the heap highest in the middle.</item>
		/// <item>2, a seep's or a vent's <b>white clams</b> (Calyptogena): long, smooth, chalk-white shells edged with brown
		/// skin, half sunk in the sediment on their edges.</item>
		/// </list>
		/// Each shell is a lathed lens (a mussel a spindle) squashed and stretched into shape; the commissure, where the
		/// valves meet, is drawn darker.
		/// </summary>
		private static void MusselBed(MeshBuilder mesh, in DetailPlant p, DeterministicRNG rng)
		{
			int count = Mathf.Max(1, p.Count);
			int variant = p.Variant;
			float typical = p.Height, bed = Mathf.Max(0.01f, p.Radius);
			var worn = new Color(0.42f, 0.36f, 0.42f);
			var skin = new Color(0.36f, 0.3f, 0.16f);
			for (int k = 0; k < count; k++)
			{
				// Packed toward the middle: a bed is shell against shell, the largest in its heart.
				float angle = rng.NextFloat() * Mathf.PI * 2f;
				float r = bed * rng.NextFloat();
				var at = new Vector3(Mathf.Cos(angle) * r, 0f, Mathf.Sin(angle) * r);
				float heart = 1f - r / bed;
				float length = typical * rng.Range(0.75f, 1.2f) * Mathf.Lerp(0.85f, 1.1f, heart);
				Color colour = Accent(in p, rng);
				float yaw = rng.NextFloat() * 360f;
				float a = rng.Range(0f, 6.28f), b = rng.Range(0f, 6.28f);
				if (variant == 1)
				{
					// An oyster: a cupped lower valve, a flatter upper, the outline lobed and frilled.
					var profile = new List<Vector2>
					{
						new Vector2(1e-3f, -length * 0.11f),
						new Vector2(length * 0.24f, -length * 0.08f),
						new Vector2(length * 0.36f, 0f),
						new Vector2(length * 0.25f, length * 0.05f),
						new Vector2(1e-3f, length * 0.065f),
					};
					float tilt = rng.NextFloat() < 0.35f ? rng.Range(45f, 80f) : rng.Range(0f, 25f);
					float lift = length * 0.3f * heart * rng.NextFloat();
					Matrix4x4 place = Matrix4x4.TRS(at + Vector3.up * (lift - length * 0.03f),
						Quaternion.Euler(0f, yaw, 0f) * Quaternion.AngleAxis(tilt, Vector3.forward), new Vector3(1.35f, 1f, 1f));
					LatheAt(mesh, place, profile, 8,
						(s, u) => 1f + 0.16f * Mathf.Sin(u * Mathf.PI * 4f + a) + 0.09f * Mathf.Sin(u * Mathf.PI * 10f + b) + 0.04f * Mathf.Sin(u * Mathf.PI * 22f + a * 3f),
						// The frilled rim pale and purple-edged, the upper valve's growth layers lighter than the cup.
						(s, u) =>
						{
							float rim = 1f - Mathf.Abs(s - 0.5f) * 2f;
							Color c = Color.Lerp(colour * (s < 0.5f ? 0.78f : 0.95f), new Color(0.6f, 0.58f, 0.52f), rim * 0.35f);
							return PlantParts.C32(c * (0.92f + 0.08f * Mathf.Sin(u * Mathf.PI * 18f + b)), 0f);
						});
				}
				else if (variant == 2)
				{
					// A vent clam: a long smooth lens standing half buried on its edge.
					var profile = new List<Vector2>
					{
						new Vector2(1e-3f, -length * 0.15f),
						new Vector2(length * 0.17f, -length * 0.11f),
						new Vector2(length * 0.25f, 0f),
						new Vector2(length * 0.17f, length * 0.11f),
						new Vector2(1e-3f, length * 0.15f),
					};
					float stand = rng.Range(50f, 85f) * (rng.NextFloat() < 0.5f ? -1f : 1f);
					Matrix4x4 place = Matrix4x4.TRS(at + Vector3.down * (length * 0.1f),
						Quaternion.Euler(0f, yaw, 0f) * Quaternion.AngleAxis(stand, Vector3.right), new Vector3(2f, 1f, 0.9f));
					LatheAt(mesh, place, profile, 8, (s, u) => 1f + 0.04f * Mathf.Sin(u * Mathf.PI * 2f + a),
						// Chalk white, the edge and the umbo end still in their brown skin.
						(s, u) =>
						{
							float rim = Mathf.Clamp01(1f - Mathf.Abs(s - 0.5f) * 5f);
							float umbo = Mathf.Clamp01((Mathf.Cos(u * Mathf.PI * 2f) - 0.6f) * 2.5f);
							return PlantParts.C32(Color.Lerp(colour, skin * 1.6f, Mathf.Max(rim * 0.5f, umbo * 0.35f)), 0f);
						});
				}
				else
				{
					// A mussel: a spindle from the pointed umbo (down in the bed) to the broad rounded end, squashed to a wedge.
					var profile = new List<Vector2>
					{
						new Vector2(1e-3f, 0f),
						new Vector2(length * 0.15f, length * 0.14f),
						new Vector2(length * 0.27f, length * 0.6f),
						new Vector2(length * 0.2f, length * 0.88f),
						new Vector2(1e-3f, length),
					};
					float lean = rng.Range(30f, 80f);
					float roll = rng.Range(-35f, 35f);
					Matrix4x4 place = Matrix4x4.TRS(at + Vector3.down * (length * 0.22f),
						Quaternion.Euler(0f, yaw, 0f) * Quaternion.AngleAxis(lean, Vector3.forward) * Quaternion.AngleAxis(roll, Vector3.up), new Vector3(1f, 1f, 0.7f));
					LatheAt(mesh, place, profile, 6, null,
						(s, u) =>
						{
							// The valves are the flattened sides, so they meet along the narrow edges (round 0 and 0.5).
							float seam = Mathf.Clamp01((Mathf.Abs(Mathf.Cos(u * Mathf.PI * 2f)) - 0.8f) * 5f);
							Color c = Color.Lerp(colour, worn, Mathf.Clamp01((0.3f - s) / 0.3f) * 0.8f);
							c = Color.Lerp(c, skin, Mathf.Clamp01((s - 0.8f) * 5f) * 0.5f);
							return PlantParts.C32(c * (1f - 0.4f * seam), 0f);
						});
				}
			}
		}

		/// <summary>
		/// A stone crusted with acorn barnacles: a lumpy, dark, wet dome, and on its upper faces a crowd of little white
		/// volcanoes of shell plates, each standing square to the stone with the dark slit of its closed lid in the crater.
		/// </summary>
		private static void Barnacles(MeshBuilder mesh, in DetailPlant p, DeterministicRNG rng)
		{
			float radius = Mathf.Max(0.02f, p.Radius), height = Mathf.Max(0.02f, p.Height);
			Color stone = Pick(in p, rng);
			float a = rng.Range(0f, 6.28f), b = rng.Range(0f, 6.28f);
			float Lump(float u) => 1f + 0.12f * Mathf.Sin(u * Mathf.PI * 4f + a) + 0.07f * Mathf.Sin(u * Mathf.PI * 6f + b);
			// The stone: a quarter ellipse sampled by angle, tucked under at the foot, so a barnacle's place on it is
			// known exactly from the same two angles.
			const int rings = 5;
			var profile = new List<Vector2> { new Vector2(radius * 0.85f, -0.03f) };
			for (int i = 0; i <= rings; i++)
			{
				float theta = Mathf.PI * 0.5f * (1f - (float)i / rings);
				profile.Add(new Vector2(Mathf.Max(1e-3f, radius * Mathf.Sin(theta)), height * Mathf.Cos(theta)));
			}
			// The upper stone pale with the crust of barnacles too small and too many to model one by one, patchy round
			// the stone; the modelled ones stand out of it.
			Color crust = p.Accents != null && p.Accents.Length > 0 ? p.Accents[0] * 0.85f : new Color(0.85f, 0.83f, 0.78f);
			PlantParts.Lathe(mesh, Vector3.zero, profile, 9, (s, u) => Lump(u),
				(s, u) => PlantParts.C32(Color.Lerp(stone * Mathf.Lerp(0.65f, 1.05f, s), crust,
					Mathf.Clamp01((s - 0.35f) * 2f) * (0.45f + 0.25f * Mathf.Sin(u * Mathf.PI * 6f + a + s * 5f))), 0f));

			int count = Mathf.Max(1, p.Count);
			float plate = Mathf.Max(0.004f, p.Width);
			for (int k = 0; k < count; k++)
			{
				// Up the stone's shoulders and over its top, where the water reaches them; none on the buried foot.
				float cosTheta = rng.Range(0.2f, 0.97f);
				float sinTheta = Mathf.Sqrt(1f - cosTheta * cosTheta);
				float u = rng.NextFloat();
				float phi = u * Mathf.PI * 2f;
				float reach = radius * Lump(u);
				var on = new Vector3(Mathf.Cos(phi) * reach * sinTheta, height * cosTheta, Mathf.Sin(phi) * reach * sinTheta);
				Vector3 normal = new Vector3(Mathf.Cos(phi) * sinTheta / reach, cosTheta / height, Mathf.Sin(phi) * sinTheta / reach).normalized;
				float size = rng.Range(0.75f, 1.4f);
				float rb = plate * size, hb = plate * size * 1.05f;
				Color shell = Accent(in p, rng);
				var cone = new List<Vector2>
				{
					// Set a little into the stone, which the lathe's flat rings cut inside its true curve.
					new Vector2(rb, -0.003f),
					new Vector2(rb * 0.45f, hb),
					new Vector2(1e-3f, hb * 0.6f),
				};
				int first = mesh.VertexCount;
				LatheAt(mesh, Matrix4x4.TRS(on, Quaternion.FromToRotation(Vector3.up, normal) * Quaternion.Euler(0f, rng.NextFloat() * 360f, 0f), Vector3.one),
					cone, 4, null, (s, uu) => PlantParts.C32(s > 0.75f ? new Color(0.22f, 0.2f, 0.19f) : shell * Mathf.Lerp(0.78f, 1.05f, s / 0.5f), 0f));
				// Normals given, not summed from the faces: a barnacle's faces are square millimetres, and their summed
				// normal is too short for Unity to normalise (it gives zero under 1e-5). Shaded as a little dome, out from
				// a point below its middle, which is how a centimetre cone reads anyway.
				Vector3 heart = on - normal * (rb * 1.5f);
				for (int i = first; i < mesh.VertexCount; i++)
				{
					mesh.Normals[i] = (mesh.Positions[i] - heart).normalized;
				}
			}
		}

		/// <summary>
		/// Sea lettuce (Ulva): a handful of bright green sheets a single cell thick, rising from one tiny holdfast and
		/// flopping over as they broaden, cupped and ruffled at their edges, paler and yellower toward the rim. The
		/// surge takes the whole sheet; its edges shiver.
		/// </summary>
		private static void SeaLettuce(MeshBuilder mesh, in DetailPlant p, DeterministicRNG rng)
		{
			const int across = 4, along = 4;
			int sheets = Mathf.Max(1, p.Count);
			var row = new int[across + 1];
			var previous = new int[across + 1];
			for (int k = 0; k < sheets; k++)
			{
				float angle = (k + rng.Range(-0.35f, 0.35f)) * Mathf.PI * 2f / sheets;
				var outward = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
				Vector3 root = outward * (p.Radius * 0.25f * rng.NextFloat());
				Part(mesh, root, k, 1500, PlantPart.Frond);
				float length = p.Height * rng.Range(0.7f, 1.2f);
				float width = p.Width * rng.Range(0.75f, 1.25f);
				Vector3 dir = (Vector3.up * rng.Range(0.8f, 1.5f) + outward).normalized;
				Vector3 side = Quaternion.AngleAxis(rng.Range(-30f, 30f), dir) * Vector3.Cross(dir, outward).normalized;
				Vector3 face = Vector3.Cross(side, dir).normalized;
				float flop = rng.Range(0.25f, 0.5f), phase = rng.NextFloat() * 6.28f, cup = rng.Range(0.05f, 0.15f);
				Color colour = Pick(in p, rng);
				for (int j = 0; j <= along; j++)
				{
					float v = (float)j / along;
					Vector3 spine = root + dir * (length * v) + outward * (length * 0.3f * v * v) + Vector3.down * (length * flop * v * v);
					// Narrow at the holdfast, broad through the middle, a broad rounded end.
					float half = width * 0.5f * Mathf.Lerp(0.15f, 1f, Mathf.Sqrt(Mathf.Sin(Mathf.PI * (0.08f + 0.8f * v))));
					for (int i = 0; i <= across; i++)
					{
						float x = -1f + 2f * i / across;
						// The margins ruffled, most along the sides and less at the end, whose corners are drawn back
						// so the sheet ends round rather than square.
						float ruffle = width * 0.22f * x * x * v * (1f - 0.5f * v) * Mathf.Sin(v * 11f + x * 5f + phase);
						float round = x * x * v * v * v;
						Vector3 pos = spine + side * (half * x * (1f - 0.3f * round)) - dir * (length * 0.12f * round) + face * (ruffle + width * cup * x * x);
						Color c = colour * Mathf.Lerp(0.85f, 1.15f, Mathf.Abs(x)) * Mathf.Lerp(0.8f, 1f, v);
						row[i] = mesh.AddVertex(pos, Vector3.zero, SolidUV(0.5f + 0.5f * x, v), PlantParts.C32(c, 1f),
							new Vector2(v * length * 0.6f, 0.3f + 0.7f * Mathf.Abs(x)));
					}
					if (j > 0)
					{
						for (int i = 0; i < across; i++)
						{
							mesh.AddQuad(0, previous[i], previous[i + 1], row[i + 1], row[i], face);
						}
					}
					(previous, row) = (row, previous);
				}
			}
		}

		/// <summary>
		/// Sand dollars: flat discs a hand-span across lying on the sand, the rim thin, the top domed a little and marked
		/// with the five-petalled flower of their breathing feet. Most are alive, velvety purple-brown; the odd one a
		/// bleached white test.
		/// </summary>
		private static void SandDollars(MeshBuilder mesh, in DetailPlant p, DeterministicRNG rng)
		{
			int count = Mathf.Max(1, p.Count);
			for (int k = 0; k < count; k++)
			{
				Vector3 centre = Scatter(in p, rng, k, count);
				float size = rng.Range(0.75f, 1.2f);
				float r = Mathf.Max(0.01f, p.Width) * size, h = Mathf.Max(0.002f, p.Height) * size;
				Color colour = Accent(in p, rng);
				var profile = new List<Vector2>
				{
					new Vector2(1e-3f, 0.0005f),
					new Vector2(r * 0.96f, 0.001f),
					new Vector2(r, h * 0.3f),
					new Vector2(r * 0.82f, h * 0.72f),
					new Vector2(r * 0.42f, h * 0.96f),
					new Vector2(1e-3f, h),
				};
				float tilt = rng.Range(0f, 10f);
				Matrix4x4 place = Matrix4x4.TRS(centre + Vector3.down * (r * Mathf.Sin(tilt * Mathf.Deg2Rad) * 0.5f),
					Quaternion.Euler(0f, rng.NextFloat() * 360f, 0f) * Quaternion.AngleAxis(tilt, Vector3.right), Vector3.one);
				LatheAt(mesh, place, profile, 10, null, (s, u) =>
				{
					// The petals: five pale bands over the dome, from near the middle most of the way to the rim.
					float petal = s >= 0.55f && s <= 0.92f ? Mathf.Max(0f, Mathf.Cos(u * Mathf.PI * 10f)) : 0f;
					float underside = s < 0.25f ? 1.1f : 1f;
					return PlantParts.C32(colour * underside * (0.9f + 0.4f * petal), 0f);
				});
			}
		}

		/// <summary>
		/// Sea cucumbers: lying warty sausages, blunt at both ends and a little curved, the sole flat and paler and the
		/// back darker and set with soft conical papillae. Shallow ones brown, black or ochre; the deep sea's pale pink.
		/// </summary>
		/// <remarks>
		/// Turned on a lathe along the up axis and then laid down. The lathe's axes are re-labelled by a proper rotation
		/// (along ← y, up ← x, side ← −z: a determinant of +1), so its faces still face out; a plain swap of x and y would
		/// have mirrored it inside out.
		/// </remarks>
		private static void SeaCucumbers(MeshBuilder mesh, in DetailPlant p, DeterministicRNG rng)
		{
			int count = Mathf.Max(1, p.Count);
			// Body radius along the length, as shares of the widest.
			float[] stations = { 0f, 0.06f, 0.2f, 0.45f, 0.72f, 0.92f, 1f };
			float[] widths = { 0f, 0.5f, 0.88f, 1f, 0.93f, 0.6f, 0f };
			float WidthAt(float t)
			{
				for (int i = 1; i < stations.Length; i++)
				{
					if (t <= stations[i])
					{
						return Mathf.Lerp(widths[i - 1], widths[i], (t - stations[i - 1]) / (stations[i] - stations[i - 1]));
					}
				}
				return 0f;
			}
			const float flatten = 0.75f;
			var path = new List<Vector3>(2);
			for (int k = 0; k < count; k++)
			{
				Vector3 centre = Scatter(in p, rng, k, count);
				float radius = Mathf.Max(0.01f, p.Width) * rng.Range(0.8f, 1.2f);
				float length = Mathf.Max(0.05f, p.Radius) * 2f * rng.Range(0.7f, 1.15f);
				float bend = rng.Range(-0.35f, 0.35f);
				float ph = rng.Range(0f, 6.28f);
				Quaternion yaw = Quaternion.Euler(0f, rng.NextFloat() * 360f, 0f);
				Color colour = Accent(in p, rng);
				// Laid down: along the length, up from the lathe's x, flattened, the sole just into the sediment.
				Vector3 Lay(Vector3 q)
				{
					float along = q.y - length * 0.5f;
					var laid = new Vector3(along, (q.x + radius) * flatten - 0.008f, -q.z);
					laid.z += bend * along * along / length;
					return centre + yaw * laid;
				}
				var profile = new List<Vector2>(stations.Length);
				for (int i = 0; i < stations.Length; i++)
				{
					profile.Add(new Vector2(Mathf.Max(1e-3f, radius * widths[i]), length * stations[i]));
				}
				int first = mesh.VertexCount;
				PlantParts.Lathe(mesh, Vector3.zero, profile, 8,
					(s, u) => 1f + 0.06f * Mathf.Sin(u * Mathf.PI * 6f + ph) * Mathf.Sin(s * Mathf.PI * 5f),
					// The lathe's x becomes up: the back (round 0) darker, the sole (round 0.5) paler.
					(s, u) => PlantParts.C32(colour * Mathf.Lerp(1.25f, 0.85f, 0.5f + 0.5f * Mathf.Cos(u * Mathf.PI * 2f)), 0f));
				for (int i = first; i < mesh.VertexCount; i++)
				{
					mesh.Positions[i] = Lay(mesh.Positions[i]);
				}
				// Papillae along the back and flanks.
				int papillae = rng.Range(10, 15);
				for (int j = 0; j < papillae; j++)
				{
					float t = rng.Range(0.12f, 0.88f);
					float around = rng.Range(-1.1f, 1.1f);
					float body = radius * WidthAt(t);
					var surface = new Vector3(Mathf.Cos(around) * body, length * t, Mathf.Sin(around) * body);
					var outward = new Vector3(Mathf.Cos(around), 0f, Mathf.Sin(around));
					float tall = radius * rng.Range(0.15f, 0.32f);
					path.Clear();
					path.Add(Lay(surface - outward * (radius * 0.08f)));
					path.Add(Lay(surface + outward * tall));
					Spine(mesh, path, tall * 0.45f, 3, colour * 1.3f, colour * 0.9f, 0f, null);
				}
			}
		}

		/// <summary>
		/// Brittle stars: a small pentagonal disc and five long, thin, snaking arms, banded light and dark, lying flat on
		/// the bed, a few to an instance (beds of them carpet a slope by the thousand).
		/// </summary>
		private static void BrittleStars(MeshBuilder mesh, in DetailPlant p, DeterministicRNG rng)
		{
			int count = Mathf.Max(1, p.Count);
			const int points = 5;
			var path = new List<Vector3>(points);
			var radii = new List<float>(points);
			var colours = new List<Color32>(points);
			var wind = new List<Vector2>(points);
			for (int k = 0; k < count; k++)
			{
				Vector3 centre = Scatter(in p, rng, k, count);
				float disc = Mathf.Max(0.005f, p.Width) * rng.Range(0.8f, 1.2f);
				float arm = disc * rng.Range(4.5f, 6.5f);
				float thick = Mathf.Max(0.003f, p.Height) * rng.Range(0.8f, 1.2f);
				Color colour = Accent(in p, rng);
				Color band = colour * 0.6f;
				float yaw = rng.NextFloat() * Mathf.PI * 2f;
				// The disc, its five corners where the arms leave it (a lathe of five sides set on the arms).
				var profile = new List<Vector2>
				{
					new Vector2(disc, 0.001f),
					new Vector2(disc * 0.9f, thick * 0.7f),
					new Vector2(disc * 0.5f, thick),
					new Vector2(1e-3f, thick * 0.95f),
				};
				PlantParts.Lathe(mesh, centre, profile, 5, null, (s, u) => PlantParts.C32(colour * Mathf.Lerp(0.8f, 1.05f, s), 0f), yaw: yaw);
				for (int a = 0; a < 5; a++)
				{
					float heading = yaw + a * Mathf.PI * 2f / 5f;
					float sweep = rng.Range(0.3f, 0.9f) * (rng.NextFloat() < 0.5f ? -1f : 1f), phase = rng.Range(0f, 3f);
					Vector3 at = centre + new Vector3(Mathf.Cos(heading), 0f, Mathf.Sin(heading)) * (disc * 0.8f);
					path.Clear(); radii.Clear(); colours.Clear(); wind.Clear();
					for (int s = 0; s < points; s++)
					{
						float t = (float)s / (points - 1);
						if (s > 0)
						{
							// Snaking: the heading swings along the arm, more toward its tip.
							float turn = heading + sweep * Mathf.Sin(t * Mathf.PI * 1.5f + phase) * t;
							at += new Vector3(Mathf.Cos(turn), 0f, Mathf.Sin(turn)) * (arm / (points - 1));
						}
						path.Add(new Vector3(at.x, thick * 0.5f * (1f - 0.5f * t) + 0.001f, at.z));
						radii.Add(s == points - 1 ? 0f : thick * 0.45f * (1f - 0.65f * t));
						colours.Add(PlantParts.C32((s & 1) == 0 ? colour : band, 0f));
						wind.Add(Vector2.zero);
					}
					PlantParts.Tube(mesh, 0, path, radii, 3, 1f, colours, wind, uvMap: SolidUV);
				}
			}
		}

		// ── Filter feeders ────────────────────────────────────────────

		/// <summary>
		/// Sea pens: a bulb anchored in the mud, a bare stalk, and above it a feather of polyp leaves in two rows — drawn
		/// with the fern frond's cell, whose leaflets rise from the rachis just as a sea pen's leaves do, in a crossing
		/// pair so it reads from every side. Red, pink, cream or orange; the stalk paler. They bend in the current.
		/// </summary>
		private static void SeaPens(MeshBuilder mesh, in DetailPlant p, DeterministicRNG rng)
		{
			int count = Mathf.Max(1, p.Count);
			var path = new List<Vector3>(5);
			var widths = new List<float>(5);
			var colours = new List<Color32>(5);
			var wind = new List<Vector2>(5);
			float[] stalkAt = { 0f, 0.12f, 0.55f, 1f };
			for (int k = 0; k < count; k++)
			{
				Vector3 root = Scatter(in p, rng, k, count) * 0.8f;
				Part(mesh, root, k, 1600, PlantPart.Frond);
				float height = p.Height * rng.Range(0.55f, 1.1f);
				float stalk = Mathf.Max(0.003f, p.Width) * rng.Range(0.8f, 1.2f);
				float leanYaw = rng.NextFloat() * Mathf.PI * 2f;
				var lean = new Vector3(Mathf.Cos(leanYaw), 0f, Mathf.Sin(leanYaw)) * rng.Range(0.03f, 0.12f);
				Color colour = Accent(in p, rng);
				Color pale = Color.Lerp(colour, Color.white, 0.35f);
				Vector3 At(float t) => root + Vector3.up * (height * t) + lean * (height * t * t);
				path.Clear(); widths.Clear(); colours.Clear(); wind.Clear();
				for (int s = 0; s < stalkAt.Length; s++)
				{
					float t = stalkAt[s];
					// The first point is the bulb, below the mud.
					path.Add(s == 0 ? root + Vector3.down * 0.04f : At(t));
					widths.Add(stalk * (s == 0 ? 1.8f : s == 1 ? 1.1f : s == 2 ? 1f : 0.5f));
					colours.Add(PlantParts.C32(pale * Mathf.Lerp(0.8f, 1f, t), 0f));
					wind.Add(new Vector2(t * height * 0.5f, 0.05f));
				}
				PlantParts.Tube(mesh, 0, path, widths, 4, 1f, colours, wind, uvMap: SolidUV);
				float vane = height * 0.32f;
				float yaw = rng.NextFloat() * 360f;
				for (int v = 0; v < 2; v++)
				{
					Vector3 side = Quaternion.Euler(0f, yaw + v * 80f, 0f) * Vector3.right;
					path.Clear(); widths.Clear(); colours.Clear(); wind.Clear();
					for (int s = 0; s <= 4; s++)
					{
						float t = 0.3f + 0.7f * s / 4f;
						path.Add(At(t));
						widths.Add(vane * (v == 0 ? 1f : 0.75f) * (s == 0 ? 0.6f : s == 4 ? 0.4f : s == 3 ? 0.85f : 1f));
						colours.Add(PlantParts.C32(colour * Mathf.Lerp(0.8f, 1.1f, t), 0f));
						wind.Add(new Vector2(t * height * 0.5f, 0.15f));
					}
					PlantParts.Strip(mesh, 0, path, widths, side, FoliageCell.Frond, colours, wind, Vector3.zero);
				}
			}
		}

		/// <summary>
		/// Stalked crinoids (sea lilies): a long jointed stalk, banded where its discs meet, bending downstream; a small cup
		/// at its head; and ten feathery arms (the fern frond's cell again: pinnules rising from each arm) that spread up and
		/// out and curl back, a living fan held to the current.
		/// </summary>
		private static void Crinoids(MeshBuilder mesh, in DetailPlant p, DeterministicRNG rng)
		{
			int count = Mathf.Max(1, p.Count);
			const int arms = 10, joints = 6;
			var path = new List<Vector3>(joints);
			var widths = new List<float>(joints);
			var colours = new List<Color32>(joints);
			var wind = new List<Vector2>(joints);
			for (int k = 0; k < count; k++)
			{
				Vector3 root = Scatter(in p, rng, k, count);
				Part(mesh, root, k, 1700, PlantPart.Frond);
				float height = p.Height * rng.Range(0.7f, 1.1f);
				float stalk = Mathf.Max(0.003f, p.Width) * rng.Range(0.85f, 1.15f);
				float leanYaw = rng.NextFloat() * Mathf.PI * 2f;
				var lean = new Vector3(Mathf.Cos(leanYaw), 0f, Mathf.Sin(leanYaw)) * rng.Range(0.08f, 0.2f);
				Color colour = Accent(in p, rng);
				Color stem = Color.Lerp(Pick(in p, rng), colour, 0.3f);
				path.Clear(); widths.Clear(); colours.Clear(); wind.Clear();
				for (int s = 0; s < joints; s++)
				{
					float t = (float)s / (joints - 1);
					path.Add(root + Vector3.up * (height * t - (s == 0 ? 0.03f : 0f)) + lean * (height * t * t));
					widths.Add(stalk * Mathf.Lerp(1.25f, 0.9f, t));
					colours.Add(PlantParts.C32(stem * ((s & 1) == 0 ? 0.88f : 1.05f), 0f));
					wind.Add(new Vector2(t * height * 0.4f, 0.02f));
				}
				PlantParts.Tube(mesh, 0, path, widths, 4, 1f, colours, wind, uvMap: SolidUV);
				Vector3 top = path[joints - 1];
				Vector3 axis = (path[joints - 1] - path[joints - 2]).normalized;
				Quaternion crown = Quaternion.FromToRotation(Vector3.up, axis);
				var calyx = new List<Vector2>
				{
					new Vector2(stalk * 1.1f, -0.002f),
					new Vector2(stalk * 2.4f, 0.016f),
					new Vector2(stalk * 3.2f, 0.028f),
				};
				Vector2 crownWind = new Vector2(height * 0.4f, 0.05f);
				LatheAt(mesh, Matrix4x4.TRS(top, crown, Vector3.one), calyx, 6, null, (s, u) => PlantParts.C32(Color.Lerp(stem, colour, s), 0f), s => crownWind);
				float reach = height * rng.Range(0.22f, 0.3f), armWidth = reach * 0.34f;
				float yaw = rng.NextFloat() * Mathf.PI * 2f;
				Vector3 foot = top + axis * 0.026f;
				for (int a = 0; a < arms; a++)
				{
					float phi = yaw + (a + rng.Range(-0.2f, 0.2f)) * Mathf.PI * 2f / arms;
					Vector3 outward = crown * new Vector3(Mathf.Cos(phi), 0f, Mathf.Sin(phi));
					Vector3 side = Vector3.Cross(axis, outward).normalized;
					float length = reach * rng.Range(0.85f, 1.15f);
					float rise = rng.Range(0.75f, 1f);
					Vector3 start = foot + outward * (stalk * 3f);
					path.Clear(); widths.Clear(); colours.Clear(); wind.Clear();
					for (int s = 0; s <= 4; s++)
					{
						float t = s / 4f;
						// Up and opening outward like a lily (a palm's fronds droop; a crinoid's arms are held up to the water).
						path.Add(start + outward * (length * (0.45f * t + 0.3f * t * t)) + axis * (length * (rise * t - 0.3f * t * t)));
						widths.Add(armWidth * (s == 0 ? 0.5f : s == 4 ? 0.35f : s == 3 ? 0.8f : 1f));
						colours.Add(PlantParts.C32(colour * Mathf.Lerp(0.85f, 1.1f, t), 0f));
						wind.Add(new Vector2(height * 0.4f + t * length, 0.3f + 0.5f * t));
					}
					// Lit as the crown's open bowl, up and out, whichever way a strip's own face happens to turn.
					PlantParts.Strip(mesh, 0, path, widths, side, FoliageCell.Frond, colours, wind, (axis + outward * 0.6f).normalized);
				}
			}
		}

		/// <summary>
		/// Glass sponges (hexactinellids), pale and lattice-walled, by <see cref="DetailPlant.Variant"/>:
		/// <list type="bullet">
		/// <item>0, on rock: a <b>Venus' flower basket</b> (Euplectella) — a tall, gently curved tube widening upward,
		/// criss-crossed by diagonal ridges and closed by a sieve plate, a tuft of glassy rooting spicules at its foot —
		/// and beside it an open <b>vase</b> (Aphrocallistes) with a thin flared lip.</item>
		/// <item>1, on mud: <b>stalked</b> sponges (Hyalonema), an egg-shaped body held up on a long twisted rope of glass
		/// fibres rooted in the sediment.</item>
		/// </list>
		/// The lattice is drawn in the vertex colours: every other ring and every other side pale, the windows between dark.
		/// </summary>
		private static void GlassSponges(MeshBuilder mesh, in DetailPlant p, DeterministicRNG rng)
		{
			int count = Mathf.Max(1, p.Count);
			const int sides = 16;
			Color32 Lattice(Color colour, float s, float u, int rings, int round)
			{
				int k = Mathf.RoundToInt(u * round), i = Mathf.RoundToInt(s * rings);
				bool window = (k & 1) == 1 && (i & 1) == 1;
				return PlantParts.C32(colour * (window ? 0.74f : 1f), 0f);
			}
			if (p.Variant == 1)
			{
				var path = new List<Vector3>(5);
				var radii = new List<float>(5);
				var colours = new List<Color32>(5);
				var wind = new List<Vector2>(5);
				for (int k = 0; k < count; k++)
				{
					Vector3 root = Scatter(in p, rng, k, count);
					float height = p.Height * rng.Range(0.7f, 1.15f);
					float body = Mathf.Max(0.01f, p.Width) * rng.Range(0.8f, 1.2f);
					float leanYaw = rng.NextFloat() * Mathf.PI * 2f;
					var lean = new Vector3(Mathf.Cos(leanYaw), 0f, Mathf.Sin(leanYaw)) * rng.Range(0.05f, 0.15f);
					Color colour = Accent(in p, rng);
					Color glass = p.ColourB;
					path.Clear(); radii.Clear(); colours.Clear(); wind.Clear();
					for (int s = 0; s <= 4; s++)
					{
						float t = s / 4f;
						path.Add(root + Vector3.up * (height * t - (s == 0 ? 0.03f : 0f)) + lean * (height * t * t));
						radii.Add(body * 0.08f * (1f - 0.3f * t));
						// A twisted rope: its fibres catch the light in turn.
						colours.Add(PlantParts.C32(glass * ((s & 1) == 0 ? 0.9f : 1.08f), 0f));
						wind.Add(Vector2.zero);
					}
					PlantParts.Tube(mesh, 0, path, radii, 3, 1f, colours, wind, uvMap: SolidUV);
					Vector3 axis = (path[4] - path[3]).normalized;
					// The body: a tall cup narrowing to its oscule, the stalk running up into it.
					float tall = body * 3f;
					var egg = new List<Vector2>
					{
						new Vector2(1e-3f, -tall * 0.05f),
						new Vector2(body * 0.5f, tall * 0.08f),
						new Vector2(body, tall * 0.4f),
						new Vector2(body * 0.85f, tall * 0.78f),
						new Vector2(body * 0.5f, tall),
						new Vector2(1e-3f, tall * 0.96f),
					};
					LatheAt(mesh, Matrix4x4.TRS(path[4] - axis * (tall * 0.15f), Quaternion.FromToRotation(Vector3.up, axis), Vector3.one), egg, 10,
						(s, u) => 1f + 0.04f * Mathf.Cos(u * Mathf.PI * 20f), (s, u) => Lattice(colour, s, u, 5, 10));
				}
				return;
			}
			for (int k = 0; k < count; k++)
			{
				Vector3 centre = k == 0 ? Vector3.zero : Scatter(in p, rng, k, count + 1) * 0.8f;
				Color colour = Accent(in p, rng);
				float radius = Mathf.Max(0.01f, p.Width) * rng.Range(0.85f, 1.15f);
				int first = mesh.VertexCount;
				if (k == 0)
				{
					// The flower basket: tall and narrow, curving as it rises, closed by its sieve plate.
					float height = p.Height * rng.Range(0.8f, 1.1f);
					var profile = new List<Vector2>
					{
						new Vector2(radius * 0.55f, -0.03f),
						new Vector2(radius * 0.7f, height * 0.12f),
						new Vector2(radius * 0.85f, height * 0.4f),
						new Vector2(radius, height * 0.72f),
						new Vector2(radius * 1.1f, height * 0.93f),
						new Vector2(radius * 1.04f, height * 0.99f),
						new Vector2(radius * 0.55f, height),
						new Vector2(1e-3f, height * 0.99f),
					};
					float spiral = rng.Range(1.2f, 1.8f);
					PlantParts.Lathe(mesh, Vector3.zero, profile, sides,
						// The diagonal ridges that wind both ways round the basket.
						(s, u) => s > 0.85f ? 1f : 1f + 0.045f * Mathf.Max(Mathf.Cos(Mathf.PI * 2f * (6f * u + spiral * s)), Mathf.Cos(Mathf.PI * 2f * (6f * u - spiral * s))),
						(s, u) => Lattice(colour, s, u, 7, sides));
					float bend = rng.Range(0.08f, 0.2f) * height;
					float yaw = rng.NextFloat() * 360f;
					Quaternion turn = Quaternion.Euler(0f, yaw, 0f);
					for (int i = first; i < mesh.VertexCount; i++)
					{
						Vector3 q = mesh.Positions[i];
						float t = Mathf.Clamp01(q.y / height);
						mesh.Positions[i] = centre + turn * new Vector3(q.x + bend * t * t, q.y, q.z);
					}
					// The rooting tuft: fine glassy spicules fanning out at the foot into the sediment.
					var tuft = new List<Vector3>(3);
					for (int f = 0; f < 6; f++)
					{
						float a = (f + rng.Range(-0.3f, 0.3f)) * Mathf.PI * 2f / 6f;
						var outward = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
						float reach = radius * rng.Range(1.2f, 2f);
						tuft.Clear();
						tuft.Add(centre + outward * (radius * 0.4f) + Vector3.up * 0.03f);
						tuft.Add(centre + outward * (radius * 0.4f + reach * 0.6f) + Vector3.up * 0.01f);
						tuft.Add(centre + outward * (radius * 0.4f + reach) + Vector3.down * 0.01f);
						Spine(mesh, tuft, radius * 0.06f, 3, colour, colour * 0.85f, 0f, null);
					}
				}
				else
				{
					// A vase: a thin wall flaring to an open lip, its inside facing in and darker.
					float height = p.Height * rng.Range(0.35f, 0.55f);
					float wall = radius * 0.12f;
					var profile = new List<Vector2>
					{
						new Vector2(radius * 0.5f, -0.02f),
						new Vector2(radius * 0.7f, height * 0.25f),
						new Vector2(radius * 1.1f, height * 0.7f),
						new Vector2(radius * 1.35f, height),
						new Vector2(radius * 1.35f - wall, height),
						new Vector2(radius * 1.1f - wall, height * 0.7f),
						new Vector2(radius * 0.6f - wall, height * 0.3f),
						new Vector2(1e-3f, height * 0.28f),
					};
					PlantParts.Lathe(mesh, centre, profile, sides, (s, u) => 1f + 0.05f * Mathf.Sin(u * Mathf.PI * 6f + s * 4f),
						(s, u) => s > 0.5f ? PlantParts.C32(colour * 0.55f, 0f) : Lattice(colour, s, u, 7, sides), yaw: rng.NextFloat() * Mathf.PI * 2f);
				}
			}
		}

		/// <summary>
		/// Cold-water coral (Lophelia): a bushy thicket of zig-zag branches, each polyp's cup budding from the side of the
		/// last and the branch turning at every one. Living tips white, pink or orange; the older wood inside the thicket a
		/// dead grey-brown, as a reef mound is a living skin over its own dead skeleton.
		/// </summary>
		/// <remarks>
		/// A polyp is a three-sided tube flaring open at its mouth (the cup is the tube's open end, which the two-sided
		/// material shows from inside), 12 triangles; a thicket stops at 60 of them so it stays inside the sea floor's budget.
		/// Short chains that branch often, each turning in a plane of its own, so the thicket is a bush and not a fan.
		/// </remarks>
		private static void ColdCoral(MeshBuilder mesh, in DetailPlant p, DeterministicRNG rng)
		{
			var path = new List<Vector3>(3);
			var radii = new List<float>(3);
			var colours = new List<Color32>(3);
			var wind = new List<Vector2>(3);
			float cup = Mathf.Max(0.003f, p.Width);
			float step = Mathf.Max(0.02f, p.Height * 0.085f);
			Color live = Accent(in p, rng);
			var dead = new Color(0.52f, 0.48f, 0.42f);
			int trunks = Mathf.Max(1, p.Count);
			const int cap = 60;
			int budget = 0;
			void Polyp(Vector3 start, Vector3 dir, float life)
			{
				Color colour = Color.Lerp(dead, live, life);
				path.Clear(); radii.Clear(); colours.Clear(); wind.Clear();
				path.Add(start);
				path.Add(start + dir * (step * 0.55f));
				path.Add(start + dir * step);
				radii.Add(cup);
				radii.Add(cup * 0.92f);
				radii.Add(cup * 1.45f);
				colours.Add(PlantParts.C32(colour * 0.85f, 0f));
				colours.Add(PlantParts.C32(colour, 0f));
				colours.Add(PlantParts.C32(colour * 1.05f, 0f));
				for (int i = 0; i < 3; i++)
				{
					wind.Add(Vector2.zero);
				}
				PlantParts.Tube(mesh, 0, path, radii, 3, 1f, colours, wind, uvMap: SolidUV);
				budget--;
			}
			void Branch(Vector3 start, Vector3 dir, int depth, int chain)
			{
				Vector3 normal = Vector3.Cross(dir, Vector3.up);
				normal = normal.sqrMagnitude < 1e-6f ? Vector3.right : normal.normalized;
				normal = Quaternion.AngleAxis(rng.Range(-40f, 40f), dir) * normal;
				float zig = rng.NextFloat() < 0.5f ? -1f : 1f;
				Vector3 at = start, heading = dir;
				for (int c = 0; c < chain && budget > 0; c++)
				{
					// Sympodial: each polyp buds from just below the last one's lip, turning the other way.
					heading = (Quaternion.AngleAxis(zig * rng.Range(18f, 32f), normal) * heading + Vector3.up * 0.2f).normalized;
					normal = Quaternion.AngleAxis(rng.Range(-35f, 35f), heading) * normal;
					zig = -zig;
					Polyp(at, heading, Mathf.Clamp01(0.15f + 0.3f * (3 - depth) + 0.15f * c));
					at += heading * (step * 0.85f);
					if (depth > 0 && budget > 0 && rng.NextFloat() < 0.8f)
					{
						Vector3 off = Quaternion.AngleAxis(rng.Range(35f, 60f) * (rng.NextFloat() < 0.5f ? -1f : 1f), normal) * heading;
						off = Quaternion.AngleAxis(rng.Range(0f, 360f), heading) * off;
						Branch(at, (off + Vector3.up * 0.3f).normalized, depth - 1, Mathf.Max(2, chain - 1));
					}
				}
			}
			for (int t = 0; t < trunks; t++)
			{
				budget = cap / trunks;
				float a = (t + rng.Range(-0.3f, 0.3f)) * Mathf.PI * 2f / trunks;
				var outward = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
				Vector3 dir = (outward + Vector3.up * rng.Range(1.2f, 2.2f)).normalized;
				Branch(outward * (p.Radius * 0.2f * rng.NextFloat()) + Vector3.down * 0.02f, dir, 3, 3);
			}
		}

		/// <summary>
		/// Soft corals, two kinds to an instance: a <b>leather coral</b> (Sarcophyton) — a smooth fleshy stalk under a broad
		/// mushroom cap whose rim is folded into waves — and a <b>finger coral</b> (Sinularia), a squat base putting up a
		/// crowd of blunt fleshy fingers. Pastel tan, pink, olive or lilac, the polyp-covered tops paler. Unlike a stony coral
		/// they give with the surge.
		/// </summary>
		private static void SoftCorals(MeshBuilder mesh, in DetailPlant p, DeterministicRNG rng)
		{
			int count = Mathf.Max(1, p.Count);
			var path = new List<Vector3>(5);
			var radii = new List<float>(5);
			var colours = new List<Color32>(5);
			var wind = new List<Vector2>(5);
			float[] fingerAt = { 0f, 0.4f, 0.8f, 0.95f, 1f };
			float[] fingerWidth = { 1f, 1f, 0.92f, 0.7f, 0f };
			for (int k = 0; k < count; k++)
			{
				Vector3 centre = Scatter(in p, rng, k, count) * 0.9f;
				Part(mesh, centre, k, 1800, PlantPart.Frond);
				Color colour = Accent(in p, rng);
				Color stalk = Color.Lerp(colour, new Color(0.85f, 0.8f, 0.68f), 0.5f);
				float cap = Mathf.Max(0.04f, p.Radius * 0.5f) * rng.Range(0.8f, 1.2f);
				float height = p.Height * rng.Range(0.7f, 1.1f);
				float a = rng.Range(0f, 6.28f), b = rng.Range(0f, 6.28f);
				if ((k & 1) == 0)
				{
					var profile = new List<Vector2>
					{
						new Vector2(cap * 0.32f, -0.02f),
						new Vector2(cap * 0.28f, height * 0.45f),
						new Vector2(cap * 0.42f, height * 0.75f),
						new Vector2(cap * 0.85f, height * 0.9f),
						new Vector2(cap, height * 0.98f),
						new Vector2(cap * 0.92f, height * 1.04f),
						new Vector2(cap * 0.5f, height),
						new Vector2(1e-3f, height * 0.96f),
					};
					int first = mesh.VertexCount;
					PlantParts.Lathe(mesh, centre, profile, 12,
						(s, u) => 1f + Mathf.Clamp01((s - 0.35f) * 3f) * (0.1f * Mathf.Sin(u * Mathf.PI * 14f + a) + 0.05f * Mathf.Sin(u * Mathf.PI * 22f + b)),
						(s, u) => PlantParts.C32(s < 0.45f ? stalk * Mathf.Lerp(0.8f, 1f, s / 0.45f) : colour * (s > 0.7f ? 1.1f : 0.95f) * (1f + 0.08f * Mathf.Sin(u * 73f + s * 41f)), 0f),
						s => new Vector2(s * height * 0.4f, 0.1f * s));
					// The cap's rim folded up and down as well as in and out.
					for (int i = first; i < mesh.VertexCount; i++)
					{
						Vector3 q = mesh.Positions[i] - centre;
						float out2 = (q.x * q.x + q.z * q.z) / (cap * cap);
						if (out2 > 0.36f && q.y > height * 0.6f)
						{
							float phi = Mathf.Atan2(q.z, q.x);
							mesh.Positions[i] += Vector3.up * (height * 0.1f * Mathf.Sin(phi * 7f + a) * Mathf.Clamp01((out2 - 0.36f) * 2f));
						}
					}
				}
				else
				{
					// A broad, low, lobed base, the fingers crowding its top.
					var trunk = new List<Vector2>
					{
						new Vector2(cap * 0.75f, -0.02f),
						new Vector2(cap * 0.7f, height * 0.18f),
						new Vector2(cap * 0.8f, height * 0.3f),
						new Vector2(1e-3f, height * 0.36f),
					};
					PlantParts.Lathe(mesh, centre, trunk, 8, (s, u) => 1f + 0.1f * Mathf.Sin(u * Mathf.PI * 6f + a),
						(s, u) => PlantParts.C32(stalk * Mathf.Lerp(0.8f, 1f, s), 0f), s => new Vector2(s * height * 0.2f, 0f));
					int fingers = rng.Range(11, 15);
					for (int f = 0; f < fingers; f++)
					{
						float angle = (f + rng.Range(-0.3f, 0.3f)) * Mathf.PI * 2f / fingers;
						var outward = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
						float spread = Mathf.Sqrt(rng.NextFloat());
						Vector3 foot = centre + outward * (cap * 0.7f * spread) + Vector3.up * (height * (0.34f - 0.06f * spread));
						Vector3 dir = (Vector3.up + outward * (0.1f + 0.45f * spread)).normalized;
						float length = height * rng.Range(0.25f, 0.45f);
						float thick = Mathf.Max(0.004f, p.Width) * rng.Range(1.2f, 1.8f);
						path.Clear(); radii.Clear(); colours.Clear(); wind.Clear();
						for (int s = 0; s < fingerAt.Length; s++)
						{
							float t = fingerAt[s];
							path.Add(foot + dir * (length * t) + outward * (length * 0.15f * t * t));
							radii.Add(thick * fingerWidth[s]);
							colours.Add(PlantParts.C32(Color.Lerp(stalk, colour, 0.5f + 0.5f * t) * Mathf.Lerp(0.9f, 1.15f, t), 0f));
							wind.Add(new Vector2(height * 0.2f + length * t * 0.6f, 0.15f * t));
						}
						PlantParts.Tube(mesh, 0, path, radii, 4, 1f, colours, wind, uvMap: SolidUV);
					}
				}
			}
		}

		/// <summary>
		/// A giant clam (Tridacna) bedded hinge-down in the reef: two heavy grey-white valves cut by four or five great
		/// radial folds, so their meeting rims run in a zig-zag, and between them the fleshy mantle bulging over the gape in
		/// electric blue, turquoise, purple or green-gold, spotted paler. Algae dull the shell toward the reef.
		/// </summary>
		/// <remarks>
		/// Not a lathe: a grid of stations along the clam's length, each a U-shaped section from one rim through the hinge
		/// to the other (an ellipse's lower half), the folds pushing the section out and lifting the rims. The narrow ends
		/// are closed with a fan, and the mantle is a strip of five across laid over the gape.
		/// </remarks>
		private static void GiantClams(MeshBuilder mesh, in DetailPlant p, DeterministicRNG rng)
		{
			int count = Mathf.Max(1, p.Count);
			const int stations = 9, around = 8, mantleAcross = 4;
			var shellRow = new int[around + 1];
			var shellPrevious = new int[around + 1];
			var mantleRow = new int[mantleAcross + 1];
			var mantlePrevious = new int[mantleAcross + 1];
			for (int k = 0; k < count; k++)
			{
				Vector3 centre = Scatter(in p, rng, k, count);
				float length = Mathf.Max(0.1f, p.Radius) * 2f * rng.Range(0.75f, 1.15f);
				float width = length * 0.55f, depth = Mathf.Max(0.05f, p.Height) * (length / Mathf.Max(0.2f, p.Radius * 2f));
				Color shell = Pick(in p, rng);
				Color mantle = Accent(in p, rng);
				int folds = rng.Range(4, 6);
				float phase = rng.Range(0f, 6.28f);
				int first = mesh.VertexCount;
				float RimAt(float xn) => depth * Mathf.Sqrt(Mathf.Max(0f, 1f - xn * xn)) * 0.6f + depth * 0.4f;
				float FoldAt(float xn) => Mathf.Sin((xn * 0.5f + 0.5f) * Mathf.PI * folds + phase);
				for (int i = 0; i < stations; i++)
				{
					float xn = Mathf.Lerp(-0.96f, 0.96f, (float)i / (stations - 1));
					float envelope = Mathf.Sqrt(1f - xn * xn);
					float x = xn * length * 0.5f;
					float fold = FoldAt(xn);
					float rim = RimAt(xn) + depth * 0.2f * fold * envelope;
					float half = width * 0.5f * envelope;
					for (int j = 0; j <= around; j++)
					{
						float theta = Mathf.PI * j / around;
						float sin = Mathf.Sin(theta), cos = Mathf.Cos(theta);
						float bulge = 1f + 0.18f * fold * sin;
						var pos = new Vector3(x, rim - rim * sin * bulge, half * cos * bulge);
						// The folds lighter on their crests; the shell dulled by algae toward the reef.
						Color c = shell * Mathf.Lerp(1.05f, 0.7f, sin) * (1f + 0.12f * fold);
						shellRow[j] = mesh.AddVertex(pos, Vector3.zero, SolidUV((float)j / around, (float)i / (stations - 1)), PlantParts.C32(c, 0f));
					}
					// The mantle, lipping over both rims and bulging a little over the gape, its edge iridescent.
					for (int j = 0; j <= mantleAcross; j++)
					{
						float across = 1f - 2f * j / mantleAcross;
						float mid = 1f - across * across;
						var pos = new Vector3(x, rim + depth * 0.05f * mid, half * 1.06f * across);
						bool spot = ((i * 2 + j) % 5) == 0;
						Color c = Mathf.Abs(across) > 0.9f ? Color.Lerp(mantle, new Color(0.3f, 0.8f, 0.75f), 0.4f) : mantle * (spot ? 1.2f : 0.95f);
						mantleRow[j] = mesh.AddVertex(pos, Vector3.zero, SolidUV(0.5f, 0.5f), PlantParts.C32(c, 0f));
					}
					if (i > 0)
					{
						for (int j = 0; j < around; j++)
						{
							Vector3 mid = (mesh.Positions[shellPrevious[j]] + mesh.Positions[shellRow[j + 1]]) * 0.5f;
							mesh.AddQuad(0, shellPrevious[j], shellPrevious[j + 1], shellRow[j + 1], shellRow[j], new Vector3(0f, mid.y - rim, mid.z));
						}
						for (int j = 0; j < mantleAcross; j++)
						{
							mesh.AddQuad(0, mantlePrevious[j], mantlePrevious[j + 1], mantleRow[j + 1], mantleRow[j], Vector3.up);
						}
					}
					else
					{
						CapClamEnd(mesh, shellRow, new Vector3(-length * 0.5f, RimAt(-1f) * 0.55f, 0f), Vector3.left, shell);
					}
					if (i == stations - 1)
					{
						CapClamEnd(mesh, shellRow, new Vector3(length * 0.5f, RimAt(1f) * 0.55f, 0f), Vector3.right, shell);
					}
					(shellPrevious, shellRow) = (shellRow, shellPrevious);
					(mantlePrevious, mantleRow) = (mantleRow, mantlePrevious);
				}
				mesh.Transform(Matrix4x4.TRS(centre + Vector3.down * (depth * 0.15f),
					Quaternion.Euler(0f, rng.NextFloat() * 360f, 0f) * Quaternion.AngleAxis(rng.Range(-12f, 12f), Vector3.right), Vector3.one), first);
			}
		}

		/// <summary>
		/// Closes a giant clam's narrow end: a fan from the end's point to the last section's ring, and the section's open
		/// top (rim to rim, under the mantle) so the end shows no hollow.
		/// </summary>
		private static void CapClamEnd(MeshBuilder mesh, int[] ring, Vector3 point, Vector3 facing, Color shell)
		{
			int tip = mesh.AddVertex(point, Vector3.zero, SolidUV(0.5f, 0.5f), PlantParts.C32(shell * 0.9f, 0f));
			for (int j = 0; j + 1 < ring.Length; j++)
			{
				mesh.AddTriangle(0, ring[j], ring[j + 1], tip, facing);
			}
			mesh.AddTriangle(0, ring[ring.Length - 1], ring[0], tip, facing);
		}

		/// <summary>
		/// Xenophyophores: giant single cells of the abyss, fist-sized and fragile, a lumpy ball of their own cemented
		/// sediment folded into meandering ridges with dark grooves between, sitting on the ooze in the sediment's colour.
		/// </summary>
		private static void Xenophyophores(MeshBuilder mesh, in DetailPlant p, DeterministicRNG rng)
		{
			int count = Mathf.Max(1, p.Count);
			const int rings = 7;
			for (int k = 0; k < count; k++)
			{
				Vector3 centre = Scatter(in p, rng, k, count);
				float radius = Mathf.Max(0.02f, p.Height * 0.5f) * rng.Range(0.7f, 1.25f);
				Color colour = Accent(in p, rng);
				Vector3 a1 = Direction(rng), a2 = Direction(rng);
				float frequency = rng.Range(7f, 10f), lumps = rng.Range(2f, 4f), phase = rng.Range(0f, 6.28f);
				float Groove(float s, float u)
				{
					float theta = s * Mathf.PI, phi = u * Mathf.PI * 2f;
					var dir = new Vector3(Mathf.Sin(theta) * Mathf.Cos(phi), -Mathf.Cos(theta), Mathf.Sin(theta) * Mathf.Sin(phi));
					return Mathf.Abs(Mathf.Sin(frequency * (Vector3.Dot(dir, a1) + 0.4f * Mathf.Sin(frequency * 0.8f * Vector3.Dot(dir, a2)))));
				}
				var profile = new List<Vector2>(rings + 1);
				for (int i = 0; i <= rings; i++)
				{
					float theta = Mathf.PI * i / rings;
					// Settled: the underside flattened into the ooze.
					profile.Add(new Vector2(Mathf.Max(1e-3f, radius * Mathf.Sin(theta)), radius * (1f - Mathf.Cos(theta)) * 0.85f - 0.01f));
				}
				PlantParts.Lathe(mesh, centre, profile, 10,
					(s, u) => 1f + 0.14f * Mathf.Sin(u * Mathf.PI * 2f * lumps + phase) * Mathf.Sin(s * Mathf.PI * 2.5f + phase) + 0.07f * (Groove(s, u) - 0.5f),
					(s, u) => PlantParts.C32(colour * Mathf.Lerp(0.55f, 1.05f, Groove(s, u)), 0f),
					yaw: rng.NextFloat() * Mathf.PI * 2f);
			}
		}

		/// <summary>
		/// Microbial mats: flat, lobed, slightly raised patches, overlapping. Under the sea (BacterialMat) the white,
		/// yellow and orange Beggiatoa carpets of a cold seep or a vent's rim, white at the heart and yellowing to the edge;
		/// on land (SlimeMat) a cave's orange-yellow slime mould, its plasmodium fanning out in branching veins across the mud.
		/// </summary>
		private static void Mats(MeshBuilder mesh, in DetailPlant p, DeterministicRNG rng, bool veins)
		{
			int patches = Mathf.Max(1, p.Count);
			const int around = 12;
			var inner = new int[around];
			var outer = new int[around];
			var spine = new List<Vector3>(4);
			var widths = new List<float>(4);
			var colours = new List<Color32>(4);
			var wind = new List<Vector2>(4);
			float thick = Mathf.Max(0.002f, p.Height);
			Vector2 uv = SolidUV(0.5f, 0.5f);
			for (int k = 0; k < patches; k++)
			{
				Vector3 centre = Scatter(in p, rng, k, patches) * 0.7f;
				float radius = Mathf.Max(0.05f, p.Radius) * rng.Range(0.45f, 0.8f);
				Color colour = Accent(in p, rng);
				Color edge = Pick(in p, rng);
				// Each patch a hair above the last, so overlapping patches never fight for the same depth.
				float lift = 0.0015f + 0.0012f * k;
				float a = rng.Range(0f, 6.28f), b = rng.Range(0f, 6.28f);
				float Outline(float u) => 1f + 0.25f * Mathf.Sin(u * Mathf.PI * 4f + a) + 0.12f * Mathf.Sin(u * Mathf.PI * 10f + b);
				int heart = mesh.AddVertex(centre + Vector3.up * (lift + thick), Vector3.zero, uv, PlantParts.C32(colour * 1.05f, 0f));
				for (int j = 0; j < around; j++)
				{
					float u = (float)j / around, phi = u * Mathf.PI * 2f;
					var dir = new Vector3(Mathf.Cos(phi), 0f, Mathf.Sin(phi));
					float reach = radius * Outline(u);
					inner[j] = mesh.AddVertex(centre + dir * (reach * 0.55f) + Vector3.up * (lift + thick * 0.8f), Vector3.zero, uv, PlantParts.C32(colour, 0f));
					outer[j] = mesh.AddVertex(centre + dir * reach + Vector3.up * lift, Vector3.zero, uv, PlantParts.C32(Color.Lerp(colour, edge, 0.7f), 0f));
				}
				for (int j = 0; j < around; j++)
				{
					int n = (j + 1) % around;
					mesh.AddTriangle(0, heart, inner[j], inner[n], Vector3.up);
					mesh.AddQuad(0, inner[j], inner[n], outer[n], outer[j], Vector3.up);
				}
				if (!veins)
				{
					continue;
				}
				// The plasmodium's veins, fanning from the patch out over the bare ground, thinning to their tips.
				int count = rng.Range(5, 8);
				for (int v = 0; v < count; v++)
				{
					float heading = (v + rng.Range(-0.35f, 0.35f)) * Mathf.PI * 2f / count;
					float length = radius * rng.Range(1.1f, 1.8f);
					float wander = rng.Range(-0.5f, 0.5f);
					spine.Clear(); widths.Clear(); colours.Clear(); wind.Clear();
					Vector3 at = centre + Vector3.up * (lift + thick * 0.6f);
					for (int s = 0; s < 4; s++)
					{
						float t = s / 3f;
						if (s > 0)
						{
							float turn = heading + wander * t;
							at += new Vector3(Mathf.Cos(turn), 0f, Mathf.Sin(turn)) * (length / 3f);
							at.y = lift + 0.001f + thick * 0.6f * (1f - t);
						}
						spine.Add(at);
						widths.Add(s == 3 ? 0f : thick * (2.2f - 0.6f * s));
						colours.Add(PlantParts.C32(Color.Lerp(colour * 1.15f, edge, t * 0.5f), 0f));
						wind.Add(Vector2.zero);
					}
					var along = new Vector3(Mathf.Cos(heading), 0f, Mathf.Sin(heading));
					PlantParts.Strip(mesh, 0, spine, widths, Vector3.Cross(along, Vector3.up), FoliageCell.Solid, colours, wind, Vector3.up);
				}
			}
		}
	}
}
#endif
