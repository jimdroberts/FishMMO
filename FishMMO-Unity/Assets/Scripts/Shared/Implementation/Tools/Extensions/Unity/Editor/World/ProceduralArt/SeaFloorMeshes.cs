#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// The sea floor's detail meshes: kelp and seaweed, brain and table corals, sea fans,
	/// sponges, anemones, urchins, starfish, shells and a vent's tube worms. Built like every other detail
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
	/// (<see cref="Lathe"/>), its faces turned outward by the profile's own direction, so a sponge's inner
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
					return true;
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

		/// <summary>
		/// A surface of revolution: <paramref name="profile"/> (x the radius, y the height) turned about the up
		/// axis through <paramref name="centre"/>. Faces turn outward by the profile's direction — up the outside
		/// faces out, across the top faces up, down an inside wall faces in. Normals are left to
		/// <see cref="MeshBuilder.RecalculateNormals"/>.
		/// </summary>
		/// <param name="radiusScale">Scales the radius at (profile share 0..1, round 0..1); null for round.</param>
		/// <param name="colour">The colour at (profile share, round).</param>
		/// <param name="wind">Sway and flutter weights at a profile share; null for none.</param>
		private static void Lathe(MeshBuilder mesh, Vector3 centre, IList<Vector2> profile, int sides,
			Func<float, float, float> radiusScale, Func<float, float, Color32> colour, Func<float, Vector2> wind = null, float yaw = 0f)
		{
			int count = profile.Count;
			if (count < 2)
			{
				return;
			}
			sides = Mathf.Max(3, sides);
			int first = mesh.VertexCount;
			for (int i = 0; i < count; i++)
			{
				float s = (float)i / (count - 1);
				for (int k = 0; k <= sides; k++)
				{
					float u = (float)k / sides;
					float angle = yaw + u * Mathf.PI * 2f;
					var radial = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
					float r = Mathf.Max(1e-4f, profile[i].x) * (radiusScale != null ? radiusScale(s, u) : 1f);
					mesh.AddVertex(centre + radial * r + Vector3.up * profile[i].y, Vector3.zero, SolidUV(u, s), colour(s, u),
						wind != null ? wind(s) : Vector2.zero);
				}
			}
			int row = sides + 1;
			for (int i = 0; i + 1 < count; i++)
			{
				Vector2 along = profile[i + 1] - profile[i];
				if (along.sqrMagnitude < 1e-12f)
				{
					continue;
				}
				// Outward in the profile's plane: right of the direction the profile runs (x radius, y up).
				var outward = new Vector2(along.y, -along.x);
				for (int k = 0; k < sides; k++)
				{
					float angle = yaw + (k + 0.5f) / sides * Mathf.PI * 2f;
					var radial = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
					Vector3 facing = radial * outward.x + Vector3.up * outward.y;
					int a = first + i * row + k, b = a + 1, c = a + row + 1, d = a + row;
					mesh.AddQuad(0, a, b, c, d, facing);
				}
			}
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
				Lathe(mesh, centre, profile, 28,
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
				Lathe(mesh, centre, profile, 32,
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
				Lathe(mesh, centre, profile, 16,
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
				Lathe(mesh, centre, profile, 12, null, (s, u) => PlantParts.C32(s > 0.7f ? tentacle * 0.6f : body * Mathf.Lerp(0.7f, 1f, s), 0f));
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
				Lathe(mesh, centre, profile, 8, null, (s, u) => PlantParts.C32(colour * Mathf.Lerp(0.6f, 1f, s), 0f));
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
				Lathe(mesh, centre, profile, 12,
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
				Lathe(mesh, top, profile, 6,
					(s, u) => 1f + 0.25f * Mathf.Sin(u * Mathf.PI * 12f) * s,
					(s, u) => PlantParts.C32(plume * Mathf.Lerp(0.6f, 1.1f, s), 0f),
					s => new Vector2(height * 0.3f + s * radius * 5f, 0.5f + 0.5f * s));
			}
		}
	}
}
#endif
