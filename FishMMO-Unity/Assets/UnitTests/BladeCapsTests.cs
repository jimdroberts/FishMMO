using System;
using System.Collections.Generic;
using System.IO;
using FishMMO.Client;
using NUnit.Framework;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The blade grass's caps after the detail layers stopped wrapping at 256 (2026-10-27): 128 blade types a scene
	/// (GrassMath.MaxTypes, the record's seventh type bit in a sixth word) and 64 channels a terrain (GrassTerrain.MaxChannels,
	/// the generate kernel's GRASS_WIDEST variant, asked for only by a terrain past 32). A record of type 0..63 is bit for bit
	/// what it was, its sixth word 0; the terrain row keeps every older field where it was.
	/// </summary>
	public class BladeCapsTests
	{
		private const string ComputePath = "Assets/Prefabs/Client/Weather/Shaders/FishGrassBlades.compute";
		private const string BladesPath = "Assets/Prefabs/Client/Weather/Shaders/FishGrassBlades.hlsl";

		/// <summary>
		/// Measured 2026-10-27 over the saved tiles after the 256-layer fix's re-cut (painted blade prototypes keyed as
		/// GrassBladeSystem.SelectBlades keys them: prefab, the biome's look, the rule's tints): scene types and the busiest
		/// tile's channels.
		/// </summary>
		private static readonly (string Scene, int Types, int BusiestTile)[] Measured =
		{
			("Moron 1", 27, 20),
			("Moron 2", 80, 46),
			("Moron 3", 79, 48),
		};

		[Test]
		public void Caps_HoldEveryScenesMeasuredBladeDemand_WithHeadroom()
		{
			foreach ((string scene, int types, int busiest) in Measured)
			{
				LogAssert.IsTrue(GrassMath.MaxTypes * 2 >= types * 3, $"{scene}: {types} blade types against {GrassMath.MaxTypes} (half again of headroom)");
				LogAssert.IsTrue(GrassTerrain.MaxChannels * 4 >= busiest * 5, $"{scene}: {busiest} channels on its busiest tile against {GrassTerrain.MaxChannels} (a quarter of headroom)");
			}
			LogAssert.AreEqual(128, GrassMath.MaxTypes, "seven type bits");
			LogAssert.AreEqual(GrassMath.MaxTypes, GrassBladeRenderer.MaxTypes, "the renderer's table is the record's");
			LogAssert.AreEqual(64, GrassTerrain.MaxChannels, "sixteen density maps of four");
			LogAssert.AreEqual(16, GrassTerrain.MaxDensityMaps, "four channels a map");
			string compute = File.ReadAllText(ComputePath);
			LogAssert.IsTrue(compute.Contains("#define GRASS_CHANNELS 64 "), "the widest kernel walks every channel a terrain can have");
			LogAssert.IsTrue(compute.Contains("#define GRASS_DENSITY_MAPS 16"), "and every map");
			LogAssert.IsTrue(compute.Contains("#define GRASS_CHANNELS 32 ") && compute.Contains("#define GRASS_CHANNELS 16 "), "the narrow and wide kernels keep their counts");
		}

		[Test]
		public void ChannelTier_IsOnlyAsWideAsATerrainAsks()
		{
			LogAssert.AreEqual(0, GrassTerrain.TierOf(0), "no channels: narrow");
			LogAssert.AreEqual(0, GrassTerrain.TierOf(GrassTerrain.NarrowChannels), "sixteen: the narrow kernel, as always");
			LogAssert.AreEqual(1, GrassTerrain.TierOf(GrassTerrain.NarrowChannels + 1), "seventeen: GRASS_WIDE");
			LogAssert.AreEqual(1, GrassTerrain.TierOf(GrassTerrain.WideChannels), "thirty-two: still GRASS_WIDE");
			LogAssert.AreEqual(2, GrassTerrain.TierOf(GrassTerrain.WideChannels + 1), "thirty-three: GRASS_WIDEST");
			LogAssert.AreEqual(2, GrassTerrain.TierOf(GrassTerrain.MaxChannels), "the cap: GRASS_WIDEST");
			LogAssert.AreEqual(1, GrassTerrain.TierOf(Measured[0].BusiestTile), "Moron 1 keeps the wide kernel it ran");
			LogAssert.AreEqual(2, GrassTerrain.TierOf(Measured[1].BusiestTile), "Moron 2 needs the widest");
			string renderer = File.ReadAllText("Assets/Scripts/Client/World/Terrain/Grass/GrassBladeRenderer.cs");
			LogAssert.IsTrue(renderer.Contains("compute.SetKeyword(wideChannels, atlas.Tier == 1);"), "GRASS_WIDE for tier 1 only");
			LogAssert.IsTrue(renderer.Contains("compute.SetKeyword(widestChannels, atlas.Tier >= 2);"), "GRASS_WIDEST for tier 2 only");
		}

		[Test]
		public void TerrainRow_KeepsEveryOlderField_AndGivesEachChannelItsOwnUint()
		{
			var fixedFields = new HashSet<int> { 0, 1, 2, 3, 4, 5, 6, 7, 16, 17, 18, 19, 20, 21, 22, 23, 24, 33 };
			var seen = new HashSet<int>();
			for (int c = 0; c < GrassTerrain.MaxChannels; c++)
			{
				int at = GrassTerrainAtlas.ChannelUint(c);
				LogAssert.IsTrue(at < GrassTerrainAtlas.TerrainUints, $"channel {c} at {at}: inside the row");
				LogAssert.IsFalse(fixedFields.Contains(at), $"channel {c} at {at}: not on a fixed field");
				LogAssert.IsTrue(seen.Add(at), $"channel {c} at {at}: its own uint");
				if (c < 32)
				{
					int old = c < 8 ? 8 + c : c < 16 ? 25 + c - 8 : 34 + c - 16;
					LogAssert.AreEqual(old, at, $"channel {c}: where the 32-channel row had it");
				}
				// FishGrassBlades.compute GrassChannelTypeAt's offset, the same arithmetic.
				uint u = (uint)c;
				uint gpu = u < 8u ? 8u + u : u < 16u ? 17u + u : 18u + u;
				LogAssert.AreEqual((uint)at, gpu, $"channel {c}: the compute reads it where the atlas writes it");
			}
			LogAssert.AreEqual(GrassTerrainAtlas.TerrainUints, fixedFields.Count + GrassTerrain.MaxChannels, "no uint of the row is unused");
			string compute = File.ReadAllText(ComputePath);
			LogAssert.IsTrue(compute.Contains("uint offset = c < 8u ? 8u + c : c < 16u ? 17u + c : 18u + c;"), "the compute's offsets are the ones checked here");
		}

		// ── The record ───────────────────────────────────────────────

		[Test]
		public void BladeRecord_Carries128Types_InEveryRecordOfABlade()
		{
			for (int type = 0; type < GrassMath.MaxTypes; type++)
			{
				foreach (float height in new[] { 0.3f, 2.4f, 3.6f })
				{
					uint packed = GrassMath.Pack(type, 1.1f, height, 0.4f, 0.2f, 0.7f);
					uint more = GrassMath.RecordMore(type);
					uint stem = GrassMath.WithBurnTone(GrassMath.PackColour(0.2f, 0.5f, 0.1f, true) | GrassMath.RecordBits(type, height), 0.6f, 0.3f);
					for (int part = 0; part < 3; part++)
					{
						uint colour = part == 0 ? stem : GrassMath.WithHeadPart(stem, part);
						GrassMath.Unpack(packed, colour, more, out int got, out _, out float h, out _, out _, out _);
						LogAssert.AreEqual(type, got, $"type {type} at {height} m, head part {part}");
						LogAssert.IsTrue(Math.Abs(h - height) < 0.08f, $"type {type}: its height {h} for {height}");
					}
					// A shadow caster's record: the shape bits alone, still the whole type.
					uint cast = GrassMath.RecordBits(type, height);
					GrassMath.Unpack(packed, cast, more, out int castType, out _, out _, out _, out _, out _);
					LogAssert.AreEqual(type, castType, $"type {type}: its shadow record");
					GrassMath.UnpackColour(cast, out _, out _, out _, out bool shadowValid);
					LogAssert.IsFalse(shadowValid, $"type {type}: a shadow record's colour is invalid");
					LogAssert.AreEqual(0u, more & ~1u, $"type {type}: the sixth word's other bits are free");
				}
			}
			LogAssert.AreEqual(GrassMath.MaxTypes - 1, Decoded(GrassMath.MaxTypes + 9), "a type past the cap clamps to the last");
		}

		private static int Decoded(int type)
		{
			GrassMath.Unpack(GrassMath.Pack(type, 0f, 1f, 0f, 1f, 0f), GrassMath.RecordBits(type, 1f), GrassMath.RecordMore(type), out int got, out _, out _, out _, out _, out _);
			return got;
		}

		/// <summary>The packing before the seventh type bit (copied as it stood): types 0..63 must pack to exactly these words.</summary>
		private static uint OldPack(int type, float facing, float height, float lean, float fade, float clumpColour)
		{
			uint t = (uint)Math.Max(0, Math.Min(63, type)) & 15u;
			float packedHeight = height > GrassMath.MaxPackedHeight ? height / GrassMath.TallHeightScale : height;
			uint f = (uint)((int)Math.Round(facing / 6.2831853f * 128f) & 127);
			uint h = (uint)Math.Max(0, Math.Min(127, (int)Math.Round(Math.Sqrt(Math.Max(0f, packedHeight) / GrassMath.MaxPackedHeight) * 127f)));
			uint l = (uint)Math.Max(0, Math.Min(15, (int)Math.Round(lean * 15f)));
			uint d = (uint)Math.Max(0, Math.Min(31, (int)Math.Round(fade * 31f)));
			uint c = (uint)Math.Max(0, Math.Min(31, (int)Math.Round(clumpColour * 31f)));
			return t | f << 4 | h << 11 | l << 18 | d << 22 | c << 27;
		}

		private static uint OldRecordBits(int type, float height)
		{
			uint t = (uint)Math.Max(0, Math.Min(63, type));
			uint tall = height > GrassMath.MaxPackedHeight ? 1u : 0u;
			return (t >> 4 & 1u) << 28 | (t >> 5 & 1u) << 31 | tall << 27;
		}

		[Test]
		public void BladeRecord_TypesUnder64_AreBitForBitWhatTheyWere()
		{
			var random = new Random(20261027);
			for (int type = 0; type < 64; type++)
			{
				for (int n = 0; n < 40; n++)
				{
					float facing = (float)random.NextDouble() * 6.2831853f, height = (float)random.NextDouble() * 5f;
					float lean = (float)random.NextDouble(), fade = (float)random.NextDouble(), clump = (float)random.NextDouble();
					uint packed = GrassMath.Pack(type, facing, height, lean, fade, clump);
					LogAssert.AreEqual(OldPack(type, facing, height, lean, fade, clump), packed, $"type {type}: the packed word");
					LogAssert.AreEqual(OldRecordBits(type, height), GrassMath.RecordBits(type, height), $"type {type}: the colour word's shape bits");
					LogAssert.AreEqual(0u, GrassMath.RecordMore(type), $"type {type}: the sixth word is 0");
					uint colour = GrassMath.PackColour(0.3f, 0.2f, 0.1f, true) | GrassMath.RecordBits(type, height);
					GrassMath.Unpack(packed, colour, out int a, out float fa, out float ha, out float la, out float da, out float ca);
					GrassMath.Unpack(packed, colour, 0u, out int b, out float fb, out float hb, out float lb, out float db, out float cb);
					LogAssert.IsTrue(a == b && fa == fb && ha == hb && la == lb && da == db && ca == cb, $"type {type}: the two-word decode is the whole record's with its sixth word 0");
					LogAssert.AreEqual(type, a, $"type {type}: decoded");
				}
			}
			for (int type = 64; type < GrassMath.MaxTypes; type++)
			{
				LogAssert.AreEqual(1u, GrassMath.RecordMore(type), $"type {type}: its seventh bit");
				LogAssert.AreEqual(OldRecordBits(type - 64, 1f), GrassMath.RecordBits(type, 1f), $"type {type}: its low six bits where type {type - 64}'s are");
			}
		}

		[Test]
		public void BladeRecord_Is24Bytes_AndBothShadersCarryTheSixthWord()
		{
			LogAssert.AreEqual(24, GrassMath.BladeStride, "float3 root + packed + colour + more");
			string compute = File.ReadAllText(ComputePath);
			string blades = File.ReadAllText(BladesPath);
			LogAssert.IsTrue(compute.Contains("struct GrassBlade { float3 root; uint packed; uint colour; uint more; };"), "the compute writes the sixth word");
			LogAssert.IsTrue(compute.Contains("blade.more = GrassRecordMore(type);"), "every record of a blade takes it (lit and cast are copies of blade)");
			LogAssert.IsTrue(compute.Contains("lit.more = 0u;"), "an empty record's is set too");
			LogAssert.IsTrue(compute.Contains("return (t >> 6) & 1u;"), "GrassRecordMore is GrassMath.RecordMore");
			LogAssert.IsTrue(blades.Contains("    uint more;"), "the blades' vertex shader reads it");
			LogAssert.IsTrue(blades.Contains("| (int)((more & 1u) << 6);"), "GrassUnpack takes the seventh bit");
			LogAssert.IsTrue(blades.Contains("GrassUnpack(blade.packed, blade.colour, blade.more,"), "from the record it fetched");
		}
	}
}
