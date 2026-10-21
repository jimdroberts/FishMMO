using System;
using System.Collections.Generic;
using System.Linq;
using FishMMO.Shared.WorldDesign;
using NUnit.Framework;
using UnityEngine;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// An ice cave's ice (2026-10-20, caves livened: <see cref="CaveIce"/>): nothing in any other form of cave, the same every time,
	/// some of every kind over a few caves; its falls against rock, its curtains hung from the roof clear of the floor and of a
	/// person's head over a way, its pools' ice on the floor or in their basins, nothing on a built way; the new glow art appended
	/// after every older kind and as large as the placer is told.
	/// </summary>
	[TestFixture]
	public class CaveIceTests
	{
		private const int ArtSeed = ProceduralArtCatalogue.DefaultSeed;

		/// <summary>A foot at 70 m, a face rising 60 m over 30 m, a rolling plateau behind (CaveLifeBakeTests' escarpment).</summary>
		private static Func<float, float, float> Escarpment(float tiltDegrees, int variant)
		{
			float a = tiltDegrees * Mathf.Deg2Rad, c = Mathf.Cos(a), s = Mathf.Sin(a);
			return (x, z) =>
			{
				float u = c * z - s * x, w = s * z + c * x;
				float t = Mathf.Clamp01(u / 30f);
				float face = 10f + 60f * (t * t * (3f - 2f * t));
				float roll = 3f * Mathf.Sin(w * 0.05f + variant) + 2f * Mathf.Sin(u * 0.031f + 0.7f * variant) * Mathf.Clamp01(u / 40f);
				return 60f + face + roll * Mathf.Clamp01((u + 10f) / 30f) - 0.04f * Mathf.Max(0f, -u);
			};
		}

		/// <summary>A cave of a form, rebuilt from its stored shape as the dressing reads it.</summary>
		private static CaveSystem Plan(CaveForm form, int seed, int size = 1)
		{
			float tilt = seed * 13f % 40f - 20f;
			CaveGround ground = CaveGround.Sample(Escarpment(tilt, seed), -560f, -560f, 560f, 560f, 2f);
			CaveSystem system = CaveSystem.Plan(form, size, new Vector3(0f, 0f, -1f), Mathf.Repeat(180f - tilt, 360f), seed * 7919, seed, ground, 0f, out string problem);
			Assert.That(system, Is.Not.Null, $"{form} seed {seed} was refused: {problem}");
			return CaveSystem.FromValues(system.ToValues(), system.Origin, system.Seed);
		}

		/// <summary>The dressing's lanes (1.4 m either side of each passage's line), as the way the ice is told of.</summary>
		private static Func<Vector3, float, bool> Lanes(CaveSystem system)
		{
			var lanes = new List<(Vector3 a, Vector3 b)>();
			foreach (CavePassage passage in system.Passages)
			{
				for (int i = 0; i + 1 < passage.Nodes.Count; i++)
				{
					CaveNode a = passage.Nodes[i], b = passage.Nodes[i + 1];
					lanes.Add((new Vector3(a.Centre.x, a.Floor, a.Centre.z), new Vector3(b.Centre.x, b.Floor, b.Centre.z)));
				}
			}
			return (p, margin) =>
			{
				float reach = 1.4f + margin;
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
			};
		}

		/// <summary>The climbs' feet and heads and the bridges' decks: what the ice is told is a built way.</summary>
		private static Func<Vector3, float, bool> Features(CaveSystem system)
		{
			return (p, margin) =>
			{
				foreach (CaveClimb climb in system.Climbs)
				{
					if ((new Vector2(p.x - climb.Bottom.x, p.z - climb.Bottom.z).magnitude < 2f + margin && Mathf.Abs(p.y - climb.Bottom.y) < 3f)
						|| (new Vector2(p.x - climb.Top.x, p.z - climb.Top.z).magnitude < 2f + margin && Mathf.Abs(p.y - climb.Top.y) < 3f))
					{
						return true;
					}
				}
				return false;
			};
		}

		private static CaveIceLayout Lay(CaveSystem system)
			=> CaveIce.Lay(system, CaveHabitat.For(system), CaveStreams.Plan(system), Lanes(system), Features(system));

		private static readonly int[] Seeds = { 3, 5, 8, 12 };

		// ── Where it applies ──────────────────────────────────────────

		[Test]
		public void AnyOtherFormOfCave_LaysNoIce()
		{
			foreach (CaveForm form in new[] { CaveForm.Cave, CaveForm.LavaTube, CaveForm.Mine })
			{
				CaveSystem system = Plan(form, 5);
				Assert.That(CaveIce.Applies(system), Is.False, $"{form}");
				CaveIceLayout layout = CaveIce.Lay(system, null, null, null, null);
				Assert.That(layout.Pieces, Is.Empty, $"{form} lays no ice");
				Assert.That(layout.Lights, Is.Empty, $"{form} lights no ice");
			}
			Assert.That(CaveIce.Lay(null, null, null, null, null).Pieces, Is.Empty);
		}

		[Test]
		public void TheColdZone_IsFrozen_AndThawsPastIt()
		{
			CaveSystem system = Plan(CaveForm.IceCave, 5);
			CaveHabitat habitat = CaveHabitat.For(system);
			Assert.That(CaveIce.Cold(habitat, system, system.Mouths[0].Floor + Vector3.up * 0.5f), Is.EqualTo(1f), "frozen at the mouth");
			// Every passage node: frozen inside the cold zone, never colder than 0 or warmer than 1, falling as the way in lengthens.
			foreach (CavePassage passage in system.Passages)
			{
				foreach (CaveNode node in passage.Nodes)
				{
					var p = new Vector3(node.Centre.x, node.Floor + 0.5f, node.Centre.z);
					float cold = CaveIce.Cold(habitat, system, p), walked = habitat.WalkedFromMouth(p);
					Assert.That(cold, Is.InRange(0f, 1f));
					if (walked <= CaveIce.ColdReach)
					{
						Assert.That(cold, Is.EqualTo(1f), $"frozen {walked:0} m in");
					}
					// The cold trap (more than ColdSinkDepth under the sill) is frozen however far in; above it, thawed past the ring.
					bool trap = p.y < CaveIce.SillOf(system, p) - CaveIce.ColdSinkDepth;
					if (trap)
					{
						Assert.That(cold, Is.EqualTo(1f), $"frozen in the cold trap {walked:0} m in");
					}
					else if (walked > CaveIce.ColdReach + CaveIce.ThawRing)
					{
						Assert.That(cold, Is.EqualTo(0f), $"thawed {walked:0} m in");
					}
				}
			}
		}

		// ── Determinism and amount ────────────────────────────────────

		[Test]
		public void TheSameCave_LaysTheSameIce()
		{
			CaveSystem system = Plan(CaveForm.IceCave, 8);
			CaveIceLayout a = Lay(system), b = Lay(Plan(CaveForm.IceCave, 8));
			Assert.That(b.Pieces.Count, Is.EqualTo(a.Pieces.Count));
			for (int i = 0; i < a.Pieces.Count; i++)
			{
				Assert.That(b.Pieces[i].Kind, Is.EqualTo(a.Pieces[i].Kind));
				Assert.That(b.Pieces[i].Variant, Is.EqualTo(a.Pieces[i].Variant));
				Assert.That((b.Pieces[i].Position - a.Pieces[i].Position).sqrMagnitude, Is.LessThan(1e-8f), $"piece {i}");
				Assert.That(Quaternion.Angle(b.Pieces[i].Rotation, a.Pieces[i].Rotation), Is.LessThan(0.01f));
			}
			Assert.That(b.Lights, Is.EqualTo(a.Lights));
		}

		[Test]
		public void IceCaves_GetEveryKindOfIce_WithinTheirBudget()
		{
			var kinds = new HashSet<CaveGlowKind>();
			foreach (int seed in Seeds)
			{
				CaveSystem system = Plan(CaveForm.IceCave, seed, seed % 2 == 0 ? 2 : 1);
				CaveIceLayout layout = Lay(system);
				TestContextWrite($"ice cave {seed}: {layout.Falls} falls, {layout.Curtains} curtains, {layout.Pools} pools, {layout.Sheets} sheets, "
					+ $"{layout.Frost} frost, {layout.StreamPieces} stream pieces, {layout.StreamFalls} stream falls, {layout.Lights.Count} lights, {layout.Pieces.Count} pieces");
				foreach (CaveIcePiece piece in layout.Pieces)
				{
					kinds.Add(piece.Kind);
					Assert.That(piece.Variant, Is.InRange(0, CaveArt.VariantCount(piece.Kind) - 1));
					Assert.That(piece.Hue, Is.InRange(0, CaveArt.HueCount(piece.Kind) - 1));
					Assert.That(piece.Scale.x > 0f && piece.Scale.y > 0f && piece.Scale.z > 0f, Is.True);
				}
				// A modest share of its props: 120 pieces, more in a cave of many chambers (2026-10-21: the ice grows with its chambers).
				Assert.That(layout.Pieces.Count, Is.LessThanOrEqualTo(Mathf.Max(120, 6 * system.Chambers.Count)), "an ice cave's ice stays a modest share of its props");
				Assert.That(layout.Lights.Count, Is.LessThanOrEqualTo(CaveIce.MaxLights));
				for (int i = 0; i < layout.Lights.Count; i++)
				{
					for (int j = i + 1; j < layout.Lights.Count; j++)
					{
						Assert.That(Vector3.Distance(layout.Lights[i], layout.Lights[j]), Is.GreaterThanOrEqualTo(CaveIce.LightSpacing - 1e-3f));
					}
				}
			}
			Assert.That(kinds, Does.Contain(CaveGlowKind.IceFall), "frozen falls");
			Assert.That(kinds, Does.Contain(CaveGlowKind.IcicleCurtain), "icicle curtains");
			Assert.That(kinds, Does.Contain(CaveGlowKind.FrozenPool), "frozen pools");
			Assert.That(kinds, Does.Contain(CaveGlowKind.Hoarfrost), "hoarfrost");
		}

		// ── Where each piece sits ─────────────────────────────────────

		[Test]
		public void EveryPiece_SitsOnItsRock_AndKeepsOffTheWays()
		{
			foreach (int seed in Seeds)
			{
				CaveSystem system = Plan(CaveForm.IceCave, seed, seed % 2 == 0 ? 2 : 1);
				Func<Vector3, float, bool> lanes = Lanes(system), features = Features(system);
				CaveIceLayout layout = CaveIce.Lay(system, CaveHabitat.For(system), CaveStreams.Plan(system), lanes, features);
				foreach (CaveIcePiece piece in layout.Pieces)
				{
					string name = $"seed {seed} {piece.Kind} at {piece.Position}";
					Vector3 up = piece.Rotation * Vector3.up, into = piece.Rotation * Vector3.forward;
					switch (piece.Kind)
					{
						case CaveGlowKind.IceFall:
						{
							// Rock behind it at a third and two thirds of its height, within a metre and a half.
							float height = piece.Scale.y * CaveGlowMeshes.IceFallHeight;
							if (piece.Stream)
							{
								// A stream's frozen fall may hang free (water down from a roof's aven): not measured against a face.
								break;
							}
							foreach (float share in new[] { 0.33f, 0.66f })
							{
								Vector3 front = piece.Position + up * (share * height) - into * 0.6f;
								Assert.That(CaveIce.March(system, front, into, 2.1f, 0.05f, out _), Is.True, $"{name}: rock behind it at {share:0.00} of its height");
							}
							if (!piece.Stream)
							{
								Assert.That(features(piece.Position, 0f), Is.False, $"{name}: not on a built way");
							}
							break;
						}
						case CaveGlowKind.IcicleCurtain:
						{
							// Hung from the roof (rock just over its origin), the longest icicle clear of the floor, and of a head over a lane.
							Assert.That(system.Void(piece.Position + Vector3.up * 0.2f), Is.GreaterThanOrEqualTo(0f), $"{name}: rock over it");
							float drop = piece.Scale.y * CaveGlowMeshes.IcicleDrops[piece.Variant];
							Vector3 under = piece.Position - Vector3.up * 0.3f;
							// (Marched down the whole height a curtain may hang over: a hall's stepped roof stands more than 20 m up.)
							Assert.That(CaveIce.March(system, under, Vector3.down, 80f, 0.25f, out float down), Is.True, $"{name}: air under it");
							float floor = under.y - down;
							float tip = piece.Position.y - drop;
							Assert.That(tip - floor, Is.GreaterThan(0.5f), $"{name}: its icicles clear the floor");
							if (lanes(new Vector3(piece.Position.x, floor, piece.Position.z), 0f))
							{
								Assert.That(tip - floor, Is.GreaterThanOrEqualTo(CaveIce.Headroom - 0.05f), $"{name}: a person's headroom over a way");
							}
							break;
						}
						case CaveGlowKind.FrozenPool:
						{
							// The ice's surface over the floor's highest knobs by a little at most, and the floor under it within reach.
							Vector3 surface = piece.Position + up * CaveGlowMeshes.FrozenPoolRise;
							Assert.That(system.Void(surface + up * 0.25f), Is.LessThan(0f), $"{name}: air over the ice");
							Assert.That(CaveIce.March(system, surface + up * 0.25f, -up, 3f, 0.05f, out float d), Is.True, $"{name}: rock under the ice");
							Assert.That(d - 0.25f, Is.LessThan(2.6f), $"{name}: the ice lies on its floor or in its basin");
							if (!piece.Stream)
							{
								Assert.That(features(surface, 0f), Is.False, $"{name}: not on a built way");
							}
							break;
						}
						case CaveGlowKind.Hoarfrost:
							// On its wall: rock just behind its origin.
							Assert.That(CaveIce.March(system, piece.Position - into * 0.2f, into, 0.4f, 0.02f, out _), Is.True, $"{name}: on the rock");
							break;
					}
				}
			}
		}

		// ── The art ───────────────────────────────────────────────────

		[Test]
		public void TheIceKinds_AreAppended_AfterEveryOlderKind()
		{
			string[] names = Enum.GetNames(typeof(CaveGlowKind));
			int fall = Array.IndexOf(names, nameof(CaveGlowKind.IceFall));
			Assert.That(fall, Is.GreaterThan(Array.IndexOf(names, nameof(CaveGlowKind.LavaCrust))), "after the lava's kinds");
			Assert.That(names.Skip(fall).Take(4), Is.EqualTo(new[] { "IceFall", "IcicleCurtain", "FrozenPool", "Hoarfrost" }), "in their order");
			foreach (CaveGlowKind kind in new[] { CaveGlowKind.IceFall, CaveGlowKind.IcicleCurtain, CaveGlowKind.FrozenPool, CaveGlowKind.Hoarfrost })
			{
				CaveGlowFamily family = ProceduralArtCatalogue.CaveGlow(kind);
				Assert.That(family.Kind, Is.EqualTo(kind));
				Assert.That(family.MaterialSet, Is.EqualTo("Ice"));
			}
			Assert.That(ProceduralArtCatalogue.CaveGlow(CaveGlowKind.FrozenPool).Collides, Is.True, "a frozen pool is walked on");
			Assert.That(ProceduralArtCatalogue.CaveGlow(CaveGlowKind.IceFall).Collides, Is.True, "a frozen fall is solid");
		}

		[Test]
		public void TheIceArt_IsAsLargeAsThePlacerIsTold()
		{
			for (int v = 0; v < 3; v++)
			{
				Bounds fall = CaveGlowMeshes.Build(CaveGlowKind.IceFall, v, 0, ArtSeed).Bounds;
				Assert.That(fall.max.y, Is.InRange(CaveGlowMeshes.IceFallHeight, CaveGlowMeshes.IceFallHeight + 0.4f), $"fall v{v}'s height");
				Assert.That(fall.min.y, Is.InRange(-0.4f, 0f), $"fall v{v} sits on its foot");
				Assert.That(-fall.min.z, Is.InRange(0.5f * CaveGlowMeshes.IceFallAprons[v], CaveGlowMeshes.IceFallAprons[v] + 0.05f), $"fall v{v}'s cone reaches its apron");

				Bounds curtain = CaveGlowMeshes.Build(CaveGlowKind.IcicleCurtain, v, 0, ArtSeed).Bounds;
				Assert.That(-curtain.min.y, Is.InRange(CaveGlowMeshes.IcicleDrops[v], CaveGlowMeshes.IcicleDrops[v] + 0.1f), $"curtain v{v}'s longest icicle and the drop at its tip");
				Assert.That(curtain.size.x, Is.InRange(0.6f * CaveGlowMeshes.IcicleSpans[v], CaveGlowMeshes.IcicleSpans[v] + 0.3f), $"curtain v{v}'s span");

				Bounds pool = CaveGlowMeshes.Build(CaveGlowKind.FrozenPool, v, 0, ArtSeed).Bounds;
				Assert.That(pool.max.y, Is.EqualTo(CaveGlowMeshes.FrozenPoolRise).Within(1e-4f), $"pool v{v}'s surface");
				Assert.That(Mathf.Max(pool.max.x, -pool.min.x, pool.max.z, -pool.min.z), Is.EqualTo(1f).Within(0.02f), $"pool v{v} is widest 1");
				Vector2 extent = CaveIce.PoolExtent(v);
				Assert.That(Mathf.Max(extent.x, extent.y), Is.EqualTo(1f).Within(0.02f));

				Bounds frost = CaveGlowMeshes.Build(CaveGlowKind.Hoarfrost, v, 0, ArtSeed).Bounds;
				Assert.That(frost.size.x, Is.LessThanOrEqualTo(CaveGlowMeshes.HoarfrostPatches[v].x + 0.2f), $"frost v{v} across");
				Assert.That(frost.size.y, Is.LessThanOrEqualTo(CaveGlowMeshes.HoarfrostPatches[v].y + 0.2f), $"frost v{v} up");
				Assert.That(-frost.min.z, Is.LessThan(0.15f), $"frost v{v} stays close to its wall");
			}
		}

		[Test]
		public void TheIceArt_IsSound_AtEveryLevel()
		{
			foreach (CaveGlowKind kind in new[] { CaveGlowKind.IceFall, CaveGlowKind.IcicleCurtain, CaveGlowKind.FrozenPool, CaveGlowKind.Hoarfrost })
			{
				for (int v = 0; v < 3; v++)
				{
					int previous = int.MaxValue;
					for (int lod = 0; lod < CaveGlowMeshes.Levels; lod++)
					{
						MeshBuilder m = CaveGlowMeshes.Build(kind, v, lod, ArtSeed);
						List<string> problems = m.Validate(true);
						Assert.That(problems, Is.Empty, $"{kind} v{v} LOD{lod}: {(problems.Count > 0 ? problems[0] : "")}");
						Assert.That(m.TriangleCount, Is.InRange(1, Math.Min(previous, 8000)), $"{kind} v{v} LOD{lod} triangles");
						previous = m.TriangleCount;
						foreach (Vector2 uv in m.UVs)
						{
							Assert.That(uv.x, Is.InRange(CaveGlowMeshes.Glow(0f) - 1e-4f, CaveGlowMeshes.Glow(1f) + 1e-4f), $"{kind}: inside the glow ramp");
						}
					}
				}
			}
		}

		private static void TestContextWrite(string line) => Console.WriteLine(line);
	}
}
