using System;
using System.Numerics;
using FishMMO.Client;
using NUnit.Framework;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The procedural blade grass's arithmetic (GrassMath, mirrored by FishGrassBlades.compute/.hlsl):
	/// determinism, the nested lattice that keeps the far field a subset of the near one, and the blade
	/// record and curve. Pure math: no GPU, no scene.
	/// </summary>
	public class GrassMathTests
	{
		[Test]
		public void Hashes_AreAFunctionOfCellAndSalt()
		{
			uint a = GrassMath.Hash(1203, -88, GrassMath.SaltPriority);
			LogAssert.AreEqual(a, GrassMath.Hash(1203, -88, GrassMath.SaltPriority), "same cell, same hash");
			LogAssert.AreNotEqual(a, GrassMath.Hash(1204, -88, GrassMath.SaltPriority), "the next cell differs");
			LogAssert.AreNotEqual(a, GrassMath.Hash(1203, -88, GrassMath.SaltSpawn), "another salt differs");
			Vector2 r = GrassMath.CellRoot(1203, -88, 0.062f);
			LogAssert.AreEqual(r, GrassMath.CellRoot(1203, -88, 0.062f), "a cell's root is the same every time");
			LogAssert.IsTrue(r.X >= 1203 * 0.062f && r.X < 1204 * 0.062f && r.Y >= -88 * 0.062f && r.Y < -87 * 0.062f, $"the root stays in its cell: {r}");
		}

		[Test]
		public void Thinning_IsNested_AndKeepsTheShareItPromises()
		{
			// Every cell kept at a share has a level at least LevelForShare(share): a tile on that coarser
			// lattice misses none of them.
			float[] shares = { 1f, 0.6f, 0.25f, 0.1f, 0.03f, 0.004f };
			const int side = 256;
			foreach (float share in shares)
			{
				int level = GrassMath.LevelForShare(share);
				int kept = 0;
				for (int j = -side / 2; j < side / 2; j++)
				{
					for (int i = -side / 2; i < side / 2; i++)
					{
						if (GrassMath.CellPriority(i, j) < share)
						{
							kept++;
							LogAssert.IsTrue(GrassMath.CellLevel(i, j) >= level, $"cell ({i}, {j}) kept at share {share} is below lattice level {level}");
						}
					}
				}
				// Priorities are uniform: the kept fraction is the share (the field's density follows the rings exactly).
				float fraction = kept / (float)(side * side);
				LogAssert.IsTrue(Math.Abs(fraction - share) <= Math.Max(0.004f, share * 0.08f), $"share {share}: kept {fraction}");
			}
			// A smaller share keeps a subset: blades thin out and come back without reshuffling.
			for (int j = 0; j < 64; j++)
			{
				for (int i = 0; i < 64; i++)
				{
					float p = GrassMath.CellPriority(i, j);
					LogAssert.IsTrue(!(p < 0.05f) || p < 0.2f, "kept at 0.05 implies kept at 0.2");
				}
			}
		}

		[Test]
		public void Rings_FallFromOne_AndWidenCompensates()
		{
			float[] d = { 6f, 15f, 40f, 100f, 300f };
			float[] n = { 260f, 80f, 20f, 5.5f, 1.5f };
			LogAssert.AreEqual(1f, GrassMath.Share(3f, d, n, 5), "full density inside the first ring");
			float last = 1f;
			for (float x = 6f; x <= 300f; x += 3f)
			{
				float s = GrassMath.Share(x, d, n, 5);
				LogAssert.IsTrue(s <= last + 1e-6f && s > 0f, $"share falls with distance: {x} m → {s}");
				last = s;
			}
			LogAssert.IsTrue(Math.Abs(GrassMath.Share(40f, d, n, 5) - 20f / 260f) < 1e-4f, "a ring point's own density");
			LogAssert.IsTrue(Math.Abs(GrassMath.Widen(0.25f, 0.5f, 6f) - 2f) < 1e-4f, "a quarter of the blades, twice as wide");
			LogAssert.AreEqual(6f, GrassMath.Widen(1e-4f, 0.5f, 6f), "widening is capped");
		}

		[Test]
		public void BladeRecord_RoundTrips()
		{
			uint packed = GrassMath.Pack(7, 2.5f, 0.45f, 0.6f, 0.5f, 0.3f);
			GrassMath.Unpack(packed, out int type, out float facing, out float height, out float lean, out float fade, out float colour);
			LogAssert.AreEqual(7, type, "type");
			LogAssert.IsTrue(Math.Abs(facing - 2.5f) <= 6.2832f / 256f + 1e-4f, $"facing {facing}");
			LogAssert.IsTrue(Math.Abs(height - 0.45f) <= 0.012f, $"height {height}");
			LogAssert.IsTrue(Math.Abs(lean - 0.6f) <= 1f / 30f + 1e-4f, $"lean {lean}");
			LogAssert.IsTrue(Math.Abs(fade - 0.5f) <= 1f / 62f + 1e-4f, $"fade {fade}");
			LogAssert.IsTrue(Math.Abs(colour - 0.3f) <= 1f / 62f + 1e-4f, $"colour {colour}");
		}

		[Test]
		public void TerrainColour_RoundTrips_KeepingDarkSoilSteps()
		{
			// A dark soil (linear ~0.02) must not collapse to black: square-root coding keeps its steps.
			uint packed = GrassMath.PackColour(0.02f, 0.35f, 0.9f, true);
			GrassMath.UnpackColour(packed, out float r, out float g, out float b, out bool valid);
			LogAssert.IsTrue(valid, "valid");
			LogAssert.IsTrue(Math.Abs(r - 0.02f) <= 0.002f, $"r {r}");
			LogAssert.IsTrue(Math.Abs(g - 0.35f) <= 0.005f, $"g {g}");
			LogAssert.IsTrue(Math.Abs(b - 0.9f) <= 0.008f, $"b {b}");
			GrassMath.UnpackColour(GrassMath.PackColour(0.5f, 0.5f, 0.5f, false), out _, out _, out _, out bool none);
			LogAssert.IsFalse(none, "no colour without arrays");
		}

		[Test]
		public void PackSurfaceLayers_KeepsTheTwoStrongest_LessTheThird()
		{
			// Layers 5 (0.5), 2 (0.3), 7 (0.2): the third's 0.2 comes off both, so 0.3 vs 0.1 = a 0.75 share.
			var weights = new float[10];
			weights[5] = 0.5f;
			weights[2] = 0.3f;
			weights[7] = 0.2f;
			uint packed = GrassMath.PackSurfaceLayers(weights, weights.Length);
			LogAssert.AreEqual(5u, packed & 255u, "first layer");
			LogAssert.AreEqual(2u, packed >> 8 & 255u, "second layer");
			LogAssert.AreEqual((uint)Math.Round(0.75f * 255f), packed >> 16 & 255u, "first layer's share");
			LogAssert.AreEqual(255u, packed >> 24, "valid");

			// A single layer is the whole colour; no weight at all is invalid.
			var one = new float[4];
			one[3] = 1f;
			uint single = GrassMath.PackSurfaceLayers(one, one.Length);
			LogAssert.AreEqual(3u, single & 255u, "the only layer");
			LogAssert.AreEqual(255u, single >> 16 & 255u, "all of it");
			LogAssert.AreEqual(0u, GrassMath.PackSurfaceLayers(new float[4], 4), "no layers, no colour");
		}

		[Test]
		public void Curve_KeepsTheBladesLength_UnderLeanBendAndWind()
		{
			var facing = Vector2.Normalize(new Vector2(0.6f, -0.8f));
			foreach (float lean in new[] { 0f, 0.3f, 0.7f })
			{
				foreach (float bend in new[] { 0f, 0.5f, 1f })
				{
					foreach (float push in new[] { 0f, 0.2f, 0.45f })
					{
						GrassMath.Curve(0.5f, lean, bend, facing, new Vector2(push, 0.1f * push), out Vector3 control, out Vector3 tip);
						float length = GrassMath.MeasuredLength(control, tip);
						LogAssert.IsTrue(Math.Abs(length - 0.5f) <= 0.5f * 0.03f, $"lean {lean} bend {bend} push {push}: length {length}");
						LogAssert.IsTrue(tip.Y > 0f || push > 0.3f, "an unpushed blade's tip stays above its root");
					}
				}
			}
		}

		[Test]
		public void HeadParts_RideTheColourWord_WithoutTouchingTheColourOrItsFlag()
		{
			uint colour = GrassMath.PackColour(0.2f, 0.4f, 0.1f, true);
			for (int part = 0; part < 3; part++)
			{
				uint marked = GrassMath.WithHeadPart(colour, part);
				LogAssert.AreEqual(part, GrassMath.HeadPartOf(marked), $"part {part}");
				GrassMath.UnpackColour(marked, out float r, out float g, out float b, out bool valid);
				GrassMath.UnpackColour(colour, out float r0, out float g0, out float b0, out _);
				LogAssert.IsTrue(valid, $"part {part} keeps the colour valid");
				LogAssert.IsTrue(r == r0 && g == g0 && b == b0, $"part {part} keeps the colour");
			}
			// A shadow caster's record has no colour: its head parts must not read as a valid terrain colour.
			GrassMath.UnpackColour(GrassMath.WithHeadPart(0u, 2), out _, out _, out _, out bool shadowValid);
			LogAssert.IsFalse(shadowValid, "a colourless head record stays colourless");
			LogAssert.AreEqual(0, GrassMath.HeadPartOf(GrassMath.WithHeadPart(GrassMath.WithHeadPart(colour, 2), 0)), "part 0 clears it");
		}

		[Test]
		public void FloraLook_LeavesTheBladeAlone_WhenNothingIsPublished()
		{
			// FishFlora.hlsl's invalid look carries height and bend 1: every blade must be what it was.
			foreach (float h in new[] { 0.05f, 0.4f, 1.3f, 2.5f, 3.9f })
			{
				LogAssert.AreEqual(h, GrassMath.FloraHeight(h, 1f), $"height {h} under no look");
			}
			foreach (float l in new[] { 0f, 0.3f, 0.75f, 1f })
			{
				LogAssert.AreEqual(l, GrassMath.FloraLean(l, 1f), $"lean {l} under no look");
			}
		}

		[Test]
		public void FloraLook_NeverOutgrowsTheRecordOrTheCullVolumes()
		{
			// Every type height a GrassType can have (up to the packed ceiling / 1.6, twice that for a Tall type), every
			// clump and own scale (Shape's HeightScale, at most ~1.43, tested to 1.6), every look a world can give.
			foreach (bool tall in new[] { false, true })
			{
				float maxType = GrassMath.MaxPackedHeight * (tall ? GrassMath.TallHeightScale : 1f) / 1.6f;
				for (float type = 0.05f; type <= maxType + 1e-4f; type += 0.05f)
				{
					float reach = GrassMath.BladeReach(type);
					for (float scale = 0.1f; scale <= 1.6f + 1e-4f; scale += 0.25f)
					{
						foreach (float look in new[] { 0f, 0.3f, 0.45f, 1f, 1.6f, 1.75f, 2.7f, 10f })
						{
							float h = GrassMath.FloraHeight(type * scale, look);
							LogAssert.IsTrue(h <= GrassMath.MaxBladeHeight, $"type {type} scale {scale} look {look}: {h} m fits the record");
							LogAssert.IsTrue(h + 0.5f <= reach + 1e-4f, $"type {type} scale {scale} look {look}: {h} m within the tiles' reach {reach}");
							uint packed = GrassMath.Pack(3, 1f, h, 0.5f, 1f, 0.5f);
							GrassMath.Unpack(packed, GrassMath.RecordBits(3, h), out _, out _, out float unpacked, out _, out _, out _);
							LogAssert.IsTrue(Math.Abs(unpacked - h) <= 0.04f * Math.Max(1f, h), $"{h} m round-trips as {unpacked}");
						}
					}
				}
			}
			foreach (float bend in new[] { 0f, 0.5f, 1.3f, 4f })
			{
				foreach (float l in new[] { 0f, 0.5f, 1f })
				{
					float lean = GrassMath.FloraLean(l, bend);
					LogAssert.IsTrue(lean >= 0f && lean <= 1f, $"lean {l} bend {bend}: {lean} packs");
				}
			}
		}

		[Test]
		public void FloraShares_RoundTripInTenBits_WithoutBleeding()
		{
			// The grass reads a layer's shares from the albedo header's eighth word (FishGrassBlades.compute GrassLayerFlora).
			float step = 1f / 1023f;
			foreach (Vector3 s in new[] { new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f), new Vector3(0f, 1f, 0f), new Vector3(0f, 0f, 1f),
				new Vector3(0.55f, 0f, 0.45f), new Vector3(0f, 0.6f, 0.4f), new Vector3(0.25f, 0f, 0.225f), new Vector3(1f, 1f, 1f) })
			{
				uint packed = GrassMath.PackFloraShares(s.X, s.Y, s.Z);
				Vector3 back = GrassMath.UnpackFloraShares(packed);
				LogAssert.IsTrue(Math.Abs(back.X - s.X) <= step * 0.5f + 1e-6f && Math.Abs(back.Y - s.Y) <= step * 0.5f + 1e-6f
					&& Math.Abs(back.Z - s.Z) <= step * 0.5f + 1e-6f, $"{s} round-trips as {back}");
				LogAssert.AreEqual(0u, packed >> 30, $"{s}: the top two bits stay clear");
			}
			// Out of range clamps rather than spilling into the next share.
			LogAssert.AreEqual(new Vector3(1f, 0f, 0f), GrassMath.UnpackFloraShares(GrassMath.PackFloraShares(7f, -2f, 0f)), "clamped to 0..1");
		}

		[Test]
		public void FloraGround_LeavesTheGroundAlone_WithoutAShareOrAStrength()
		{
			// The terrain's colour readers (GroundColourMap) mirror FishFloraGround: bare ground under an ordinary air, or the looks
			// at no strength, must come back exactly as the terrain drew it before the looks.
			var albedo = new Vector3(0.21f, 0.17f, 0.09f);
			var foliage = new Vector3(0.05f, 0.2f, 0.03f);
			var accent = new Vector3(0.4f, 0.35f, 0.1f);
			var ground = new Vector3(0.3f, 0.08f, 0.04f);
			LogAssert.AreEqual(albedo, GrassMath.FloraGround(albedo, Vector3.Zero, foliage, accent, ground, 0.7f, 0.75f, 0.75f), "no share");
			LogAssert.AreEqual(albedo, GrassMath.FloraGround(albedo, new Vector3(1f, 0f, 1f), foliage, accent, ground, 0f, 0.75f, 0f), "no strength");
			LogAssert.AreEqual(albedo, GrassMath.FloraGround(albedo, new Vector3(0f, 0f, 1f), foliage, accent, ground, 0.7f, 0.75f, 0f),
				"mineral ground under an ordinary air (_FishFloraGround.a = 0)");
		}

		[Test]
		public void FloraGround_ShiftsLivingGround_AndRecoloursOnlyTheMineral()
		{
			// Living ground keeps the colour its climate gave it and is moved by the look's shift (2026-10-15: a look that
			// replaced it turned meadows into chunks of colour that ignored the ground's dry and lush).
			var turf = new Vector3(0.1f, 0.2f, 0.05f);
			var shift = new Vector3(1.2f, 0.9f, 0.7f);
			Vector3 shifted = GrassMath.FloraGround(turf, new Vector3(1f, 0f, 0f), shift, Vector3.Zero, Vector3.Zero, 1f, 0f, 0f);
			LogAssert.IsTrue(Vector3.Distance(shifted, turf * shift) < 1e-6f, $"full strength multiplies: {shifted}");
			Vector3 half = GrassMath.FloraGround(turf, new Vector3(0.5f, 0f, 0f), shift, Vector3.Zero, Vector3.Zero, 1f, 0f, 0f);
			LogAssert.IsTrue(Vector3.Distance(half, turf * Vector3.Lerp(Vector3.One, shift, 0.5f)) < 1e-6f, $"half a living share, half the shift: {half}");
			LogAssert.IsTrue(Vector3.Distance(GrassMath.FloraGround(turf, new Vector3(1f, 0f, 0f), Vector3.One, Vector3.Zero, Vector3.Zero, 1f, 0f, 0f), turf) < 1e-6f,
				"a white shift (the common look on an Earth-like world) leaves the turf as it was");
			// Mineral ground takes the world's ground at its own lightness, nothing of the shift.
			var target = new Vector3(0.08f, 0.22f, 0.04f);
			float lum = GrassMath.FloraLuminance(target);
			var same = new Vector3(lum, lum, lum);
			var ground = new Vector3(0.3f, 0.08f, 0.04f);
			Vector3 rock = GrassMath.FloraGround(same, new Vector3(0f, 0f, 1f), shift, Vector3.Zero, ground, 1f, 0f, 1f);
			LogAssert.IsTrue(Vector3.Distance(rock, ground * (1.5f * (lum / GrassMath.FloraLuminance(ground)) / (lum / GrassMath.FloraLuminance(ground) + 0.5f))) < 1e-5f,
				$"wholly mineral ground at full strength is the world's ground at its own lightness: {rock}");
		}

		[Test]
		public void Sprinkle_HoldsItsOwnChance_WhateverElseGrows()
		{
			// Channel 0 grass (a tussock species: no chance), 1 flowers at 2% a candidate, 2 reeds at 6%, half painted.
			float[] chances = { 0f, 0.02f, 0.06f };
			const float fullness = 0.35f;
			var full = new[] { 1f, 1f, 0.175f };
			int flowers = 0, reeds = 0;
			const int side = 300;
			for (int j = 0; j < side; j++)
			{
				for (int i = 0; i < side; i++)
				{
					float u = GrassMath.Unit(GrassMath.Hash(i, j, GrassMath.SaltSprinkle));
					int c = GrassMath.PickSprinkle(u, full, chances, 3, fullness);
					LogAssert.IsTrue(c != 0, "a tussock species is never sprinkled");
					flowers += c == 1 ? 1 : 0;
					reeds += c == 2 ? 1 : 0;
				}
			}
			float n = side * side;
			LogAssert.IsTrue(Math.Abs(flowers / n - 0.02f) < 0.003f, $"flowers fully painted: {flowers / n}");
			// Reeds painted at half the fullness: half their chance.
			LogAssert.IsTrue(Math.Abs(reeds / n - 0.03f) < 0.004f, $"reeds half painted: {reeds / n}");
			LogAssert.AreEqual(-1, GrassMath.PickSprinkle(0.5f, new[] { 1f, 0f, 0f }, chances, 3, fullness), "no sprinkled layer painted, no sprinkle");
		}

		[Test]
		public void PickChannel_FallsBackToTheLastPaintedChannel()
		{
			// A pick of exactly 1 runs past the end: the last channel with any weight, never an empty one.
			LogAssert.AreEqual(1, GrassMath.PickChannel(1f, new[] { 0.3f, 0.7f, 0f, 0f }, 4), "the last painted channel");
			LogAssert.AreEqual(0, GrassMath.PickChannel(0.1f, new[] { 0.3f, 0.7f, 0f, 0f }, 4), "in order");
			LogAssert.AreEqual(-1, GrassMath.PickChannel(0.5f, new float[4], 4), "nothing painted");
		}
	}
}
