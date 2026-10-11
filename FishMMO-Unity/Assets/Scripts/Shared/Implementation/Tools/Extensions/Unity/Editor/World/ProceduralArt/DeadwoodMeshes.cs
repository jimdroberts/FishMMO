#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>What shape a deadwood prop takes.</summary>
	public enum DeadwoodForm
	{
		/// <summary>A trunk lying on the ground, snapped at both ends, with branch stubs.</summary>
		FallenLog,
		/// <summary>A stump on its root flare, snapped (splintered) or sawn flat.</summary>
		Stump,
		/// <summary>Barkless, bleached, smooth and twisted, with a fork and root knobs.</summary>
		Driftwood,
		/// <summary>A silicified trunk broken into cordwood segments.</summary>
		PetrifiedLog,
		/// <summary>A windthrown tree's root plate stood on edge, the snapped trunk lying from it.</summary>
		RootPlate,
	}

	/// <summary>One deadwood prop's recipe, at its real size: one unit is one metre.</summary>
	public struct DeadwoodSpecies
	{
		public string Name;
		public DeadwoodForm Form;
		/// <summary>A log's length; a stump's height; a root plate's diameter.</summary>
		public float Length;
		/// <summary>The trunk's radius at its thick end.</summary>
		public float Radius;
		/// <summary>The <see cref="Bark"/> family on its bark (sub-mesh 0).</summary>
		public string BarkFamily;
		/// <summary>A tint on the bark (white: the bark as drawn; bleached for driftwood, stone for a petrified log).</summary>
		public Color BarkTint;
		/// <summary>The exposed wood's two colours (sapwood and heartwood; soil and clay on a root plate).</summary>
		public Color WoodA, WoodB;
		/// <summary>How much of its upper surface is mossed over, 0..1.</summary>
		public float Moss;
		public Color MossColour;
		/// <summary>A stump sawn flat (rings on its top) instead of snapped.</summary>
		public bool Sawn;
		/// <summary>Bracket fungi shelved on its side.</summary>
		public int Brackets;
		/// <summary>Branch stubs on a log, roots on a root plate, knobs at a driftwood's foot.</summary>
		public int Stubs;
		/// <summary>Extra colours: a petrified log's agate bands (rind first, pith last); a bracket's zones; a root plate's stones.</summary>
		public Color[] Bands;
	}

	/// <summary>
	/// Deadwood props added by the vegetation expansion (2026-10-10): fallen logs, stumps, driftwood, a petrified log and a
	/// windthrow root plate, at three levels of detail each, for the tree channel (bedded baked props with streamed mesh
	/// colliders). Written by BiomeArtGenerator.Deadwood.cs, named and listed in ProceduralArtCatalogue.Flora.cs.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Two sub-meshes.</b> Sub-mesh <see cref="BarkSubmesh"/> is the bark, on its family's bark texture (UVs in bark
	/// metres, the vertex colour a tint: moss on a log's top, bleach on driftwood, stone on a petrified log). Sub-mesh
	/// <see cref="WoodSubmesh"/> is everything that is not bark — splintered and sawn ends, a petrified log's agate faces,
	/// bracket fungi, a root plate's soil — in vertex colours on the atlas's solid cell, because a bark texture under a
	/// cut face would show the bark's grain where the rings should be.
	/// </para>
	/// <para>
	/// <b>Grown once, drawn three times.</b> Each prop's pieces (tubes along wandering centre lines from
	/// <see cref="PlantParts.CrookedPath"/>, lathed flares, caps, shelves and plates from <see cref="PlantParts.Lathe"/>,
	/// splintered ends of its own) are decided once from the stream, then emitted at each level with fewer sides and
	/// rings, the smallest pieces (stubs, roots, brackets) dropped at the last level. So the levels share one skeleton
	/// and the collider (the last level's mesh, as a rock's) matches what is drawn.
	/// </para>
	/// <para>
	/// <b>Lying on the ground.</b> A log's centre line runs at about three quarters of its radius above the pivot, so a
	/// quarter of it is bedded below the ground and it sits in the litter on any gentle slope; a stump's flare and a
	/// root plate's lower edge go below the pivot the same way. Nothing sways (wind weights zero).
	/// </para>
	/// </remarks>
	public static class DeadwoodMeshes
	{
		/// <summary>The levels of detail every prop is built at.</summary>
		public const int Levels = 3;

		/// <summary>The bark sub-mesh and the exposed-wood sub-mesh.</summary>
		public const int BarkSubmesh = 0, WoodSubmesh = 1;

		/// <summary>Triangle budgets per level: near, middle distance, far.</summary>
		public static readonly int[] Budget = { 3000, 1200, 400 };

		/// <summary>The prop at a level of detail (0 full, 1 reduced, 2 far), two sub-meshes.</summary>
		public static MeshBuilder Build(in DeadwoodSpecies species, int lod, int seed)
		{
			var grower = new Grower(in species, ProceduralNoise.SeedFor(species.Name, seed));
			grower.Grow();
			var mesh = new MeshBuilder(2);
			grower.Emit(mesh, Mathf.Clamp(lod, 0, Levels - 1));
			mesh.RecalculateNormals(true, onlyMissing: true);
			mesh.RecalculateTangents();
			return mesh;
		}

		/// <summary>Sides a ring of <paramref name="full"/> sides keeps at a level.</summary>
		private static int SidesAt(int full, int lod) => lod == 0 ? full : lod == 1 ? Mathf.Max(5, Mathf.RoundToInt(full * 0.55f)) : Mathf.Max(4, Mathf.RoundToInt(full * 0.35f));

		private static Vector2 SolidUV(float u, float v) => FoliageAtlas.CellUV(FoliageCell.Solid, Mathf.Clamp01(u), Mathf.Repeat(v, 1f));

		/// <summary>A smooth value noise in 3D, 0..1, for moss patches and mottling.</summary>
		private static float Noise(Vector3 p, int salt)
		{
			int x0 = Mathf.FloorToInt(p.x), y0 = Mathf.FloorToInt(p.y), z0 = Mathf.FloorToInt(p.z);
			float fx = p.x - x0, fy = p.y - y0, fz = p.z - z0;
			fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy); fz = fz * fz * (3f - 2f * fz);
			float H(int x, int y, int z) => ProceduralNoise.ToUnit(ProceduralNoise.Hash(x, y, z, salt));
			float a = Mathf.Lerp(Mathf.Lerp(H(x0, y0, z0), H(x0 + 1, y0, z0), fx), Mathf.Lerp(H(x0, y0 + 1, z0), H(x0 + 1, y0 + 1, z0), fx), fy);
			float b = Mathf.Lerp(Mathf.Lerp(H(x0, y0, z0 + 1), H(x0 + 1, y0, z0 + 1), fx), Mathf.Lerp(H(x0, y0 + 1, z0 + 1), H(x0 + 1, y0 + 1, z0 + 1), fx), fy);
			return Mathf.Lerp(a, b, fz);
		}

		// ── Pieces ────────────────────────────────────────────────────

		/// <summary>A piece of the prop, decided once, emitted at each level it appears at.</summary>
		private abstract class Piece
		{
			/// <summary>The last level that draws it.</summary>
			public int MaxLod = Levels - 1;
			public abstract void Emit(MeshBuilder mesh, int lod);
		}

		/// <summary>A tube along a centre line (a trunk, a stub, a root), on the bark or the wood.</summary>
		private sealed class TubePiece : Piece
		{
			public readonly List<Vector3> Path = new List<Vector3>();
			public readonly List<float> Radii = new List<float>();
			public readonly List<Color> Colours = new List<Color>();
			public int Submesh = BarkSubmesh;
			public int Sides = 10;
			public float UvMetres = 1f;
			/// <summary>Moss on its upward faces: the share (0 none), its colour and its noise.</summary>
			public float Moss;
			public Color MossColour;
			public int MossSalt;

			public override void Emit(MeshBuilder mesh, int lod)
			{
				var path = new List<Vector3>();
				var radii = new List<float>();
				var colours = new List<Color32>();
				var wind = new List<Vector2>();
				int step = lod == 0 ? 1 : lod == 1 ? 2 : 4;
				for (int i = 0; i < Path.Count; i += step)
				{
					path.Add(Path[i]); radii.Add(Radii[i]); colours.Add(PlantParts.C32(Colours[i], 0f)); wind.Add(Vector2.zero);
				}
				if ((Path.Count - 1) % step != 0)
				{
					int last = Path.Count - 1;
					path.Add(Path[last]); radii.Add(Radii[last]); colours.Add(PlantParts.C32(Colours[last], 0f)); wind.Add(Vector2.zero);
				}
				int first = mesh.VertexCount;
				Func<float, float, Vector2> uv = Submesh == WoodSubmesh ? SolidUV : (Func<float, float, Vector2>)null;
				PlantParts.Tube(mesh, Submesh, path, radii, SidesAt(Sides, lod), UvMetres, colours, wind, uvMap: uv);
				if (Moss <= 0f)
				{
					return;
				}
				// Moss on the upper faces, in patches: the tube's normals are radial already.
				for (int i = first; i < mesh.VertexCount; i++)
				{
					float up = mesh.Normals[i].y;
					float patch = Noise(mesh.Positions[i] * 1.7f, MossSalt) * 0.65f + Noise(mesh.Positions[i] * 5.3f, MossSalt + 1) * 0.35f;
					float weight = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.15f, 0.75f, up)) * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1f - Moss, 1.05f - Moss * 0.5f, patch));
					if (weight <= 0f)
					{
						continue;
					}
					Color32 c = mesh.Colors[i];
					Color moss = PlantParts.C32(MossColour, 0f);
					mesh.Colors[i] = new Color32((byte)Mathf.Lerp(c.r, moss.r * 255f, weight), (byte)Mathf.Lerp(c.g, moss.g * 255f, weight), (byte)Mathf.Lerp(c.b, moss.b * 255f, weight), c.a);
				}
			}
		}

		/// <summary>A lathed solid placed in the prop (a flare, a disc, a cap, a shelf, a plate).</summary>
		private sealed class LathePiece : Piece
		{
			public Vector2[] Profile;
			public int Sides = 12;
			public Matrix4x4 Place = Matrix4x4.identity;
			public int Submesh = WoodSubmesh;
			public Func<float, float, Color32> Colour;
			public Func<float, float, float> Lump;
			public Func<float, float, Vector2> Uv;

			public override void Emit(MeshBuilder mesh, int lod)
			{
				int first = mesh.VertexCount;
				PlantParts.Lathe(mesh, Vector3.zero, Profile, SidesAt(Sides, lod), Lump, Colour, null, 0f, Submesh, Uv ?? SolidUV);
				mesh.Transform(Place, first);
			}
		}

		/// <summary>
		/// A snapped end: from a rim matching the trunk's end ring, splinters standing out along the axis by their own
		/// lengths round a lower, darker heart — pale torn wood on the wood sub-mesh.
		/// </summary>
		private sealed class JaggedEnd : Piece
		{
			public Vector3 Centre, Axis;
			public float Radius;
			public int Sides = 12;
			/// <summary>Splinter lengths round the end, as a share of the radius (sampled by angle).</summary>
			public float[] Jag;
			public float Heart;
			public Color Pale, Dark;

			private float JagAt(float u)
			{
				float f = Mathf.Repeat(u, 1f) * Jag.Length;
				int i = (int)f % Jag.Length;
				return Mathf.Lerp(Jag[i], Jag[(i + 1) % Jag.Length], f - (int)f);
			}

			public override void Emit(MeshBuilder mesh, int lod)
			{
				int sides = SidesAt(Sides, lod);
				Vector3 axis = Axis.normalized;
				Vector3 e1 = PlantParts.Perpendicular(axis);
				Vector3 e2 = Vector3.Cross(axis, e1).normalized;
				// Set a little inside the trunk's end, so no crack shows between the two rings.
				Vector3 c = Centre - axis * (Radius * 0.04f);
				int rim = mesh.VertexCount;
				for (int k = 0; k <= sides; k++)
				{
					float u = (float)k / sides, a = u * Mathf.PI * 2f;
					Vector3 radial = e1 * Mathf.Cos(a) + e2 * Mathf.Sin(a);
					mesh.AddVertex(c + radial * Radius, Vector3.zero, SolidUV(u, 0f), PlantParts.C32(Dark * 0.85f, 0f));
				}
				int mid = mesh.VertexCount;
				for (int k = 0; k <= sides; k++)
				{
					float u = (float)k / sides, a = u * Mathf.PI * 2f;
					Vector3 radial = e1 * Mathf.Cos(a) + e2 * Mathf.Sin(a);
					float jag = JagAt(u);
					mesh.AddVertex(c + radial * (Radius * 0.78f) + axis * (Radius * jag), Vector3.zero, SolidUV(u, 0.5f),
						PlantParts.C32(Color.Lerp(Pale, Color.white, 0.2f * jag), 0f));
				}
				int heart = mesh.AddVertex(c + axis * (Radius * Heart), Vector3.zero, SolidUV(0.5f, 1f), PlantParts.C32(Dark, 0f));
				for (int k = 0; k < sides; k++)
				{
					float a = (k + 0.5f) / sides * Mathf.PI * 2f;
					Vector3 radial = e1 * Mathf.Cos(a) + e2 * Mathf.Sin(a);
					mesh.AddQuad(WoodSubmesh, rim + k, rim + k + 1, mid + k + 1, mid + k, axis + radial * 0.6f);
					mesh.AddTriangle(WoodSubmesh, mid + k, mid + k + 1, heart, axis);
				}
			}
		}

		// ── The grower ────────────────────────────────────────────────

		private sealed class Grower
		{
			private readonly DeadwoodSpecies sp;
			private readonly int seed;
			private DeterministicRNG rng;
			private readonly List<Piece> pieces = new List<Piece>();

			public Grower(in DeadwoodSpecies species, int seed)
			{
				sp = species;
				this.seed = seed;
				rng = new DeterministicRNG(seed);
			}

			/// <summary>A fresh stream for one part, so one part's draws never shift the next.</summary>
			private void Reseed(int part) => rng = new DeterministicRNG(seed ^ (int)ProceduralNoise.Mix((uint)part * 0x9e3779b1u + 7u));

			public void Grow()
			{
				Reseed(0);
				switch (sp.Form)
				{
					case DeadwoodForm.Stump: Stump(); break;
					case DeadwoodForm.Driftwood: Driftwood(); break;
					case DeadwoodForm.PetrifiedLog: Petrified(); break;
					case DeadwoodForm.RootPlate: RootPlate(); break;
					default: Log(); break;
				}
			}

			public void Emit(MeshBuilder mesh, int lod)
			{
				foreach (Piece piece in pieces)
				{
					if (lod <= piece.MaxLod)
					{
						piece.Emit(mesh, lod);
					}
				}
			}

			private static Vector3 OnPath(List<Vector3> path, float t)
			{
				float f = Mathf.Clamp01(t) * (path.Count - 1);
				int i = Mathf.Min((int)f, path.Count - 2);
				return Vector3.Lerp(path[i], path[i + 1], f - i);
			}

			private static float OnRadii(List<float> radii, float t)
			{
				float f = Mathf.Clamp01(t) * (radii.Count - 1);
				int i = Mathf.Min((int)f, radii.Count - 2);
				return Mathf.Lerp(radii[i], radii[i + 1], f - i);
			}

			private float[] Splinters(int count, float min, float max)
			{
				var jag = new float[count];
				for (int i = 0; i < count; i++)
				{
					// Long splinters come in runs: a torn trunk tears along its grain in strips.
					jag[i] = rng.NextFloat() < 0.35f ? rng.Range(max * 0.6f, max) : rng.Range(min, max * 0.45f);
				}
				return jag;
			}

			private JaggedEnd End(Vector3 centre, Vector3 outward, float radius, float min, float max, int maxLod = Levels - 1)
			{
				return new JaggedEnd
				{
					Centre = centre, Axis = outward, Radius = radius, Sides = Mathf.Clamp(Mathf.RoundToInt(radius * 30f), 6, 12),
					// The heart sits low: a trunk tears in its outer wood, and an old one's heart has rotted.
					Jag = Splinters(16, min, max), Heart = rng.Range(0f, min * 0.6f + 0.05f), Pale = sp.WoodA, Dark = sp.WoodB, MaxLod = maxLod,
				};
			}

			/// <summary>A round flat disc closing a tube's end, its rings by <paramref name="colour"/> over the share out from its middle (1 the rim).</summary>
			private LathePiece Disc(Vector3 centre, Vector3 outward, float radius, int sides, Func<float, Color32> colour, int rings = 5, int maxLod = Levels - 1)
			{
				var profile = new Vector2[rings + 1];
				for (int i = 0; i < profile.Length; i++)
				{
					float share = 1f - (float)i / (profile.Length - 1);
					profile[i] = new Vector2(radius * Mathf.Max(0.0005f, share), i == profile.Length - 1 ? radius * 0.01f : 0f);
				}
				return new LathePiece
				{
					Profile = profile, Sides = sides, Place = Matrix4x4.TRS(centre, Quaternion.FromToRotation(Vector3.up, outward), Vector3.one),
					Colour = (s, u) => colour(1f - s), MaxLod = maxLod,
				};
			}

			// ── Logs ──────────────────────────────────────────────────

			/// <summary>
			/// A fallen trunk: a gently wandering centre line along x, tapering from the butt to three quarters of it, its
			/// girth swelling and pinching a little; lying with a quarter of its radius bedded; snapped at both ends; mossed
			/// along its top; a few branch stubs standing out of its sides and top.
			/// </summary>
			private void Log()
			{
				float length = sp.Length, radius = sp.Radius;
				int segments = Mathf.Clamp(Mathf.RoundToInt(length / 0.45f), 8, 28);
				var trunk = new TubePiece { Sides = radius >= 0.3f ? 12 : 10, Moss = sp.Moss, MossColour = sp.MossColour, MossSalt = rng.Next(1 << 20) };
				PlantParts.CrookedPath(new Vector3(-length * 0.5f, 0f, 0f), new Vector3(length * 0.5f, 0f, 0f), segments, 0.012f, 0f, rng, trunk.Path);
				float phase = rng.NextFloat() * Mathf.PI * 2f;
				for (int i = 0; i < trunk.Path.Count; i++)
				{
					float t = (float)i / (trunk.Path.Count - 1);
					float r = radius * Mathf.Lerp(1f, 0.72f, t) * (1f + 0.05f * Mathf.Sin(t * 9f + phase) + 0.03f * Mathf.Sin(t * 23f + phase * 2f));
					trunk.Radii.Add(r);
					trunk.Path[i] = new Vector3(trunk.Path[i].x, r * 0.74f, trunk.Path[i].z);
					// The butt a little darker (wetter, earthier) than the rest.
					trunk.Colours.Add(sp.BarkTint * Mathf.Lerp(0.85f, 1f, Mathf.SmoothStep(0f, 1f, t * 4f)));
				}
				pieces.Add(trunk);
				int last = trunk.Path.Count - 1;
				Reseed(1);
				pieces.Add(End(trunk.Path[0], (trunk.Path[0] - trunk.Path[1]).normalized, trunk.Radii[0], 0.1f, 0.9f));
				Reseed(2);
				pieces.Add(End(trunk.Path[last], (trunk.Path[last] - trunk.Path[last - 1]).normalized, trunk.Radii[last], 0.15f, 1.3f));
				for (int k = 0; k < sp.Stubs; k++)
				{
					Reseed(10 + k);
					float t = rng.Range(0.2f, 0.88f);
					Vector3 at = OnPath(trunk.Path, t);
					float r = OnRadii(trunk.Radii, t);
					// Out of a side or the top, never down into the ground.
					Vector3 dir = new Vector3(rng.Range(-0.5f, 0.5f), rng.Range(0.1f, 1f), rng.NextFloat() < 0.5f ? -1f : 1f).normalized;
					Stub(at + dir * (r * 0.6f), dir, r * rng.Range(0.18f, 0.3f), r * rng.Range(0.8f, 1.8f) + 0.15f, trunk.Colours[0]);
				}
			}

			/// <summary>A snapped branch stub: a short tapering tube with a splintered end; gone at the last level.</summary>
			private void Stub(Vector3 from, Vector3 dir, float radius, float length, Color colour)
			{
				var stub = new TubePiece { Sides = 6, MaxLod = 1, Moss = sp.Moss * 0.5f, MossColour = sp.MossColour, MossSalt = rng.Next(1 << 20) };
				stub.Path.Add(from - dir * (radius * 1.5f));
				stub.Path.Add(from + dir * (length * 0.5f));
				stub.Path.Add(from + dir * length);
				stub.Radii.Add(radius * 1.15f); stub.Radii.Add(radius * 0.92f); stub.Radii.Add(radius * 0.8f);
				stub.Colours.Add(colour); stub.Colours.Add(colour); stub.Colours.Add(colour);
				pieces.Add(stub);
				pieces.Add(End(from + dir * length, dir, radius * 0.8f, 0.2f, 1.2f, 1));
			}

			// ── Stumps ────────────────────────────────────────────────

			/// <summary>
			/// A stump: a lathed bole flaring into four to six root buttresses that run out under the ground, mossed low on
			/// its flare; snapped into splinters (or sawn flat and ringed); an old one shelved up one side with brackets.
			/// </summary>
			private void Stump()
			{
				float height = sp.Length, radius = sp.Radius;
				int lobes = 4 + rng.Next(3);
				float phase = rng.NextFloat() * Mathf.PI * 2f;
				float flare = rng.Range(0.25f, 0.4f);
				int mossSalt = rng.Next(1 << 20);
				Vector2[] profile =
				{
					new Vector2(radius * 1.9f, -0.12f), new Vector2(radius * 1.45f, 0.03f), new Vector2(radius * 1.15f, height * 0.22f),
					new Vector2(radius * 1.03f, height * 0.55f), new Vector2(radius, height),
				};
				float round = Mathf.Max(1f, Mathf.Round(2f * Mathf.PI * radius * 1.3f));
				Color bark = sp.BarkTint, moss = sp.MossColour;
				float mossShare = sp.Moss;
				pieces.Add(new LathePiece
				{
					Profile = profile, Sides = 16, Submesh = BarkSubmesh,
					Lump = (s, u) =>
					{
						float low = (1f - s) * (1f - s);
						return 1f + flare * low * (Mathf.Pow(0.5f + 0.5f * Mathf.Cos(lobes * u * Mathf.PI * 2f + phase), 3f) - 0.3f);
					},
					Colour = (s, u) =>
					{
						float patch = Noise(new Vector3(u * 7f, s * 3f, 0.5f), mossSalt);
						float weight = mossShare * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.4f, 0.8f, patch)) * (s < 0.35f ? 1f : 0.35f);
						return PlantParts.C32(Color.Lerp(bark * Mathf.Lerp(0.82f, 1f, s), moss, weight), 0f);
					},
					Uv = (u, s) => new Vector2(u * round, s * (height + 0.12f) / 1f),
				});
				var top = new Vector3(0f, height, 0f);
				if (sp.Sawn)
				{
					// Growth rings: pale sapwood at the rim, alternating early- and latewood, a darker heart.
					int rings = 5 + rng.Next(4);
					Color sap = sp.WoodA, heart = sp.WoodB;
					pieces.Add(Disc(top, Vector3.up, radius * 1.005f, 16, share =>
					{
						if (share > 0.92f) return PlantParts.C32(Color.Lerp(sap, Color.white, 0.15f), 0f);
						float band = Mathf.PingPong(share * rings, 1f);
						return PlantParts.C32(Color.Lerp(Color.Lerp(heart, sap, share), heart * 0.9f, band * 0.45f), 0f);
					}));
				}
				else
				{
					Reseed(3);
					pieces.Add(End(top, Vector3.up, radius, 0.08f, 1f));
				}
				Color[] zones = sp.Bands != null && sp.Bands.Length >= 3 ? sp.Bands : new[] { new Color(0.9f, 0.86f, 0.75f), new Color(0.62f, 0.48f, 0.32f), new Color(0.4f, 0.3f, 0.22f) };
				float side = rng.NextFloat() * Mathf.PI * 2f;
				for (int k = 0; k < sp.Brackets; k++)
				{
					Reseed(20 + k);
					float y = height * Mathf.Lerp(0.25f, 0.85f, (k + rng.Range(0.1f, 0.9f)) / Mathf.Max(1, sp.Brackets));
					float yaw = side + rng.Range(-0.6f, 0.6f);
					var outward = new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw));
					float surface = radius * Mathf.Lerp(1.15f, 1f, y / height);
					float r = radius * rng.Range(0.25f, 0.42f);
					Vector3 at = outward * (surface + r * 0.15f) + Vector3.up * y;
					Bracket(at, outward, r, zones);
				}
			}

			/// <summary>
			/// A bracket fungus: a flattened lathed shelf, its inner half sunk in the wood, pale pores underneath and
			/// zoned brown and cream above; gone at the last level.
			/// </summary>
			private void Bracket(Vector3 at, Vector3 outward, float radius, Color[] zones)
			{
				Vector2[] shelf = { new Vector2(radius * 0.15f, -radius * 0.05f), new Vector2(radius, 0f), new Vector2(radius * 0.92f, radius * 0.12f), new Vector2(radius * 0.5f, radius * 0.24f), new Vector2(0f, radius * 0.28f) };
				Quaternion turn = Quaternion.FromToRotation(Vector3.forward, outward);
				pieces.Add(new LathePiece
				{
					Profile = shelf, Sides = 10, MaxLod = 1, Place = Matrix4x4.TRS(at, turn, new Vector3(1f, 1f, 0.7f)),
					Colour = (s, u) => PlantParts.C32(s < 0.25f ? zones[0] : Color.Lerp(zones[1], zones[2], Mathf.PingPong(s * 5f, 1f)) * (s < 0.4f ? 1.15f : 1f), 0f),
				});
			}

			// ── Driftwood ─────────────────────────────────────────────

			/// <summary>
			/// Driftwood: the bark long gone, a smooth, twisted, bleached trunk tapering to half its girth, its ends worn
			/// round; a forked branch from its upper part; a few root knobs splaying from its foot.
			/// </summary>
			private void Driftwood()
			{
				float length = sp.Length, radius = sp.Radius;
				var trunk = new TubePiece { Sides = 9 };
				PlantParts.CrookedPath(new Vector3(-length * 0.5f, 0f, 0f), new Vector3(length * 0.5f, 0f, 0f), Mathf.Clamp(Mathf.RoundToInt(length / 0.3f), 8, 20), 0.06f, 0f, rng, trunk.Path);
				float phase = rng.NextFloat() * Mathf.PI * 2f;
				for (int i = 0; i < trunk.Path.Count; i++)
				{
					float t = (float)i / (trunk.Path.Count - 1);
					float r = radius * Mathf.Lerp(1f, 0.48f, t) * (1f + 0.12f * Mathf.Sin(t * 11f + phase));
					trunk.Radii.Add(r);
					trunk.Path[i] = new Vector3(trunk.Path[i].x, Mathf.Max(r * 0.7f, trunk.Path[i].y * 0.3f + r * 0.7f), trunk.Path[i].z);
					trunk.Colours.Add(sp.BarkTint * Mathf.Lerp(0.92f, 1.02f, Noise(new Vector3(t * 6f, 0f, 0f), 77)));
				}
				pieces.Add(trunk);
				int last = trunk.Path.Count - 1;
				pieces.Add(RoundEnd(trunk.Path[0], (trunk.Path[0] - trunk.Path[1]).normalized, trunk.Radii[0]));
				pieces.Add(RoundEnd(trunk.Path[last], (trunk.Path[last] - trunk.Path[last - 1]).normalized, trunk.Radii[last]));

				// The fork.
				Reseed(4);
				float at = rng.Range(0.55f, 0.75f);
				Vector3 from = OnPath(trunk.Path, at);
				float r0 = OnRadii(trunk.Radii, at) * 0.6f;
				Vector3 along = (trunk.Path[last] - trunk.Path[0]).normalized;
				Vector3 dir = Quaternion.AngleAxis(rng.Range(25f, 40f) * (rng.NextFloat() < 0.5f ? -1f : 1f), Vector3.up) * along;
				var branch = new TubePiece { Sides = 7, MaxLod = 1 };
				float branchLength = length * rng.Range(0.3f, 0.42f);
				PlantParts.CrookedPath(from - dir * r0, from + dir * branchLength, 5, 0.05f, 0f, rng, branch.Path);
				for (int i = 0; i < branch.Path.Count; i++)
				{
					float t = (float)i / (branch.Path.Count - 1);
					float r = i == branch.Path.Count - 1 ? 0f : r0 * Mathf.Lerp(1f, 0.35f, t);
					branch.Radii.Add(r);
					branch.Path[i] = new Vector3(branch.Path[i].x, Mathf.Max(branch.Path[i].y, r * 0.8f + 0.02f), branch.Path[i].z);
					branch.Colours.Add(trunk.Colours[0]);
				}
				pieces.Add(branch);

				// Root knobs at the foot, splaying back from it.
				for (int k = 0; k < sp.Stubs; k++)
				{
					Reseed(30 + k);
					Vector3 foot = trunk.Path[0];
					Vector3 back = (Quaternion.AngleAxis(rng.Range(-70f, 70f), Vector3.up) * -along + Vector3.up * rng.Range(-0.1f, 0.5f)).normalized;
					float r = trunk.Radii[0] * rng.Range(0.3f, 0.45f);
					var root = new TubePiece { Sides = 6, MaxLod = 1 };
					float rootLength = rng.Range(0.3f, 0.7f);
					root.Path.Add(foot + back * (trunk.Radii[0] * 0.3f));
					root.Path.Add(foot + back * (rootLength * 0.6f));
					root.Path.Add(foot + back * rootLength);
					root.Radii.Add(r); root.Radii.Add(r * 0.6f); root.Radii.Add(0f);
					for (int i = 0; i < 3; i++)
					{
						root.Path[i] = new Vector3(root.Path[i].x, Mathf.Max(root.Path[i].y, root.Radii[i] * 0.7f + 0.01f), root.Path[i].z);
						root.Colours.Add(trunk.Colours[0]);
					}
					pieces.Add(root);
				}
			}

			/// <summary>A worn, rounded end (driftwood): a low dome over the end ring, pale grey wood.</summary>
			private LathePiece RoundEnd(Vector3 centre, Vector3 outward, float radius)
			{
				Color a = sp.WoodA, b = sp.WoodB;
				return new LathePiece
				{
					Profile = new[] { new Vector2(radius * 1.01f, -radius * 0.05f), new Vector2(radius * 0.85f, radius * 0.3f), new Vector2(radius * 0.45f, radius * 0.52f), new Vector2(radius * 0.02f, radius * 0.6f) },
					Sides = 9, Place = Matrix4x4.TRS(centre, Quaternion.FromToRotation(Vector3.up, outward), Vector3.one),
					Colour = (s, u) => PlantParts.C32(Color.Lerp(a, b, s * 0.6f), 0f),
				};
			}

			// ── Petrified log ─────────────────────────────────────────

			/// <summary>
			/// A petrified log: silica keeps a trunk's shape but not its strength, so it lies broken into cordwood — four or
			/// five straight segments with clean, near-square breaks, each turned a few degrees and nudged off the line, half
			/// bedded in the ground. The outside is the bark's texture in stone colours, mottled; every broken face is banded
			/// with agate rings (a dark rind, then reds, ambers, creams and purples to a grey pith).
			/// </summary>
			private void Petrified()
			{
				float length = sp.Length, radius = sp.Radius;
				int count = 4 + rng.Next(2);
				var lengths = new float[count];
				float total = 0f;
				for (int i = 0; i < count; i++)
				{
					lengths[i] = rng.Range(0.6f, 1.4f);
					total += lengths[i];
				}
				float gaps = 0f;
				var gap = new float[count];
				for (int i = 0; i < count; i++)
				{
					gap[i] = i == 0 ? 0f : rng.Range(0.06f, 0.25f);
					gaps += gap[i];
				}
				Color[] bands = sp.Bands != null && sp.Bands.Length >= 2 ? sp.Bands : new[] { sp.WoodA, sp.WoodB };
				float x = -length * 0.5f;
				for (int i = 0; i < count; i++)
				{
					Reseed(40 + i);
					x += gap[i];
					float piece = lengths[i] / total * (length - gaps);
					float r = radius * rng.Range(0.92f, 1.05f) * Mathf.Lerp(1f, 0.85f, (float)i / count);
					Quaternion turn = Quaternion.AngleAxis(rng.Range(-6f, 6f), Vector3.up) * Quaternion.AngleAxis(rng.Range(-3f, 3f), Vector3.forward);
					var centre = new Vector3(x + piece * 0.5f, r * 0.55f, rng.Range(-0.06f, 0.06f));
					Vector3 axis = turn * Vector3.right;
					Vector3 a = centre - axis * (piece * 0.5f), c = centre + axis * (piece * 0.5f);
					var segment = new TubePiece { Sides = 12, UvMetres = 1f };
					segment.Path.Add(a); segment.Path.Add(Vector3.Lerp(a, c, 0.5f)); segment.Path.Add(c);
					segment.Radii.Add(r); segment.Radii.Add(r * rng.Range(0.97f, 1.04f)); segment.Radii.Add(r * rng.Range(0.95f, 1f));
					for (int k = 0; k < 3; k++)
					{
						segment.Colours.Add(Color.Lerp(sp.BarkTint, sp.WoodB, rng.Range(0f, 0.35f)) * rng.Range(0.9f, 1.05f));
					}
					pieces.Add(segment);
					// The agate faces: each its own sequence of the bands, the rind always first.
					int rings = 4 + rng.Next(4);
					var order = new Color[rings];
					order[0] = bands[0];
					for (int k = 1; k < rings; k++)
					{
						order[k] = bands[1 + rng.Next(bands.Length - 1)];
					}
					Color pith = bands[bands.Length - 1];
					Func<float, Color32> face = share =>
					{
						if (share > 0.9f) return PlantParts.C32(order[0], 0f);
						if (share < 0.08f) return PlantParts.C32(pith, 0f);
						int k = Mathf.Clamp(Mathf.FloorToInt((0.9f - share) / 0.82f * rings), 0, rings - 1);
						return PlantParts.C32(order[k], 0f);
					};
					pieces.Add(Disc(a, -axis, r * 1.005f, 10, face, 4));
					pieces.Add(Disc(c, axis, segment.Radii[2] * 1.005f, 10, face, 4));
					x += piece;
				}
			}

			// ── Root plate ────────────────────────────────────────────

			/// <summary>
			/// A windthrown tree's root plate: the disc of soil and roots the tree tore up as it fell, stood on edge with its
			/// lower part still in its pit; ragged at its rim, dark soil with clay and stones on its face; roots standing out
			/// of the face and trailing past the rim; and the snapped trunk lying away from it, sloping down to the ground.
			/// </summary>
			private void RootPlate()
			{
				float plate = sp.Length * 0.5f, radius = sp.Radius;
				var centre = new Vector3(0f, plate * 0.72f, 0f);
				float phaseA = rng.NextFloat() * Mathf.PI * 2f, phaseB = rng.NextFloat() * Mathf.PI * 2f;
				Color soil = sp.WoodA, clay = sp.WoodB;
				Color stone = sp.Bands != null && sp.Bands.Length > 0 ? sp.Bands[0] : new Color(0.55f, 0.55f, 0.52f);
				int salt = rng.Next(1 << 20);
				// The plate's axis is +x→−x: its root face (once the underside) looks away from the trunk, along −x.
				Vector2[] profile =
				{
					new Vector2(0.02f, -0.18f), new Vector2(plate * 0.9f, -0.15f), new Vector2(plate, 0.03f),
					new Vector2(plate * 0.86f, 0.22f), new Vector2(plate * 0.45f, 0.32f), new Vector2(0.02f, 0.36f),
				};
				pieces.Add(new LathePiece
				{
					Profile = profile, Sides = 18, Submesh = WoodSubmesh, Place = Matrix4x4.TRS(centre, Quaternion.FromToRotation(Vector3.up, Vector3.left), Vector3.one),
					Lump = (s, u) => 0.82f + 0.12f * Mathf.Sin(u * Mathf.PI * 4f + phaseA) + 0.08f * Mathf.Sin(u * Mathf.PI * 10f + phaseB),
					Colour = (s, u) =>
					{
						float n = Noise(new Vector3(u * 9f, s * 6f, 0.3f), salt);
						Color c = Color.Lerp(soil, clay, Mathf.SmoothStep(0.45f, 0.75f, n));
						if (Noise(new Vector3(u * 23f, s * 17f, 0.7f), salt + 1) > 0.78f)
						{
							c = stone;
						}
						return PlantParts.C32(c, 0f);
					},
				});

				// The trunk: from inside the plate, down to rest on the ground, snapped at its far end.
				var trunk = new TubePiece { Sides = 12, Moss = sp.Moss, MossColour = sp.MossColour, MossSalt = rng.Next(1 << 20) };
				float trunkLength = plate * rng.Range(1.5f, 1.8f);
				var from = new Vector3(0.1f, centre.y * 0.8f, 0f);
				var to = new Vector3(trunkLength, radius * 0.75f, rng.Range(-0.2f, 0.2f));
				PlantParts.CrookedPath(from, to, 7, 0.015f, 0f, rng, trunk.Path);
				for (int i = 0; i < trunk.Path.Count; i++)
				{
					float t = (float)i / (trunk.Path.Count - 1);
					// The butt flares into the plate.
					trunk.Radii.Add(radius * (1f + 0.6f * (1f - Mathf.SmoothStep(0f, 1f, t * 3f))) * Mathf.Lerp(1f, 0.9f, t));
					trunk.Colours.Add(sp.BarkTint * 1.4f);
				}
				pieces.Add(trunk);
				int last = trunk.Path.Count - 1;
				Reseed(5);
				pieces.Add(End(trunk.Path[last], (trunk.Path[last] - trunk.Path[last - 1]).normalized, trunk.Radii[last], 0.2f, 1.4f));

				// Roots out of the face, radiating in its plane and standing out of it; some trailing down past the rim.
				for (int k = 0; k < sp.Stubs; k++)
				{
					Reseed(60 + k);
					float yaw = (k + rng.Range(-0.3f, 0.3f)) * Mathf.PI * 2f / Mathf.Max(1, sp.Stubs);
					var inPlane = new Vector3(0f, Mathf.Sin(yaw), Mathf.Cos(yaw));
					float startShare = rng.Range(0.1f, 0.5f);
					Vector3 start = centre + Vector3.left * 0.3f + inPlane * (plate * startShare);
					// The roots above the plate's middle were torn shorter as it lifted.
					float reach = plate * rng.Range(0.3f, 0.75f) * (inPlane.y > 0.3f ? 0.5f : 1f);
					Vector3 outFace = Vector3.left * rng.Range(0.3f, 0.9f);
					Vector3 end = start + (inPlane + outFace).normalized * reach;
					if (end.y < 0.05f)
					{
						end.y = 0.05f;
					}
					// Roots hang: the far part sags.
					Vector3 mid = Vector3.Lerp(start, end, 0.5f) + Vector3.down * (reach * rng.Range(0.12f, 0.3f));
					float r = rng.Range(0.04f, 0.11f);
					var root = new TubePiece { Sides = 5, MaxLod = 1 };
					root.Path.Add(start); root.Path.Add(mid); root.Path.Add(end); root.Path.Add(end + (end - mid).normalized * (reach * 0.15f));
					root.Radii.Add(r); root.Radii.Add(r * 0.7f); root.Radii.Add(r * 0.4f); root.Radii.Add(0f);
					for (int i = 0; i < 4; i++)
					{
						root.Colours.Add(sp.BarkTint * rng.Range(0.85f, 1.05f));
					}
					pieces.Add(root);
				}
			}
		}
	}
}
#endif
