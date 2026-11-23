using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Client;
using FishMMO.Shared;
using FishMMO.Database.Data;
using FishMMO.Server.Implementation.World.SceneServer;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Pins account-wide fog on the server (issue #241): the world-anchored chunk set, its encoding, the merge a
	/// save makes, the client grid lining up with the server's chunks, and the map's scene dropdown rules.
	/// </summary>
	[TestFixture]
	public class ServerFogTests
	{
		[Test]
		public void ChunkSet_IsWorldAnchored_AndGrowsInEveryDirection()
		{
			FogChunkSet set = new FogChunkSet(40.0f);
			LogAssert.IsTrue(set.AddAt(new Vector3(-1.0f, 0.0f, -1.0f)), "a chunk left and below the origin");
			LogAssert.IsTrue(set.Contains(-1, -1), "is chunk (-1, -1): floor, not truncation");
			LogAssert.IsFalse(set.AddAt(new Vector3(-39.0f, 0.0f, -39.0f)), "the same chunk again explores nothing");
			LogAssert.IsTrue(set.Add(500, -300) && set.Add(-200, 900), "far chunks grow the box");
			LogAssert.IsTrue(set.Contains(-1, -1) && set.Contains(500, -300) && set.Contains(-200, 900), "and every bit survives the growth");
			LogAssert.AreEqual(3, set.Count);
			LogAssert.IsFalse(set.Add(-200 + FogChunkSet.MaxDimension, 0), "nothing past the cap");
		}

		[Test]
		public void AddLine_ExploresEveryChunkBetweenTwoSamples()
		{
			FogChunkSet set = new FogChunkSet(40.0f);
			set.AddLine(new Vector3(5.0f, 0.0f, 5.0f), new Vector3(395.0f, 0.0f, 5.0f));
			for (int x = 0; x <= 9; ++x)
			{
				LogAssert.IsTrue(set.Contains(x, 0), $"chunk {x} along the way is explored");
			}
			LogAssert.AreEqual(10, set.Count, "and nothing off the line");
		}

		[Test]
		public void AddAround_UsesTheClientsNearestPointRule()
		{
			FogChunkSet set = new FogChunkSet(40.0f);
			set.AddAround(new Vector3(20.0f, 0.0f, 20.0f), 25.0f);
			LogAssert.IsTrue(set.Contains(0, 0) && set.Contains(1, 0) && set.Contains(-1, 0) && set.Contains(0, 1) && set.Contains(0, -1), "the chunk and its four neighbours");
			LogAssert.IsFalse(set.Contains(1, 1), "a diagonal neighbour whose nearest corner is ~28 m away is not");
		}

		[Test]
		public void Codec_RoundTripsExactly_AndIsSmall()
		{
			FogChunkSet set = new FogChunkSet(40.0f);
			for (int x = -20; x < 60; ++x)
			{
				for (int z = -10; z < 30; ++z)
				{
					if ((x * 7 + z * 3) % 11 != 0)
					{
						set.Add(x, z);
					}
				}
			}
			byte[] data = FogCodec.Encode(set);
			LogAssert.IsTrue(FogCodec.TryDecode(data, out FogChunkSet back), "decodes");
			LogAssert.AreEqual(set.Count, back.Count);
			LogAssert.AreEqual(40.0f, back.ChunkSize);
			set.ForEach((x, z) => LogAssert.IsTrue(back.Contains(x, z), $"({x}, {z}) survives"));

			FogChunkSet patch = new FogChunkSet(40.0f);
			patch.AddArea(new Rect(0.0f, 0.0f, 2000.0f, 2000.0f));
			LogAssert.IsTrue(FogCodec.Encode(patch).Length < 64, "a solid explored patch is a handful of runs, not a bit per chunk");

			LogAssert.IsTrue(FogCodec.TryDecode(FogCodec.Encode(new FogChunkSet()), out FogChunkSet empty) && empty.IsEmpty, "empty round-trips");
		}

		[Test]
		public void Codec_RejectsWhatItDidNotWrite()
		{
			FogChunkSet set = new FogChunkSet();
			set.Add(1, 2);
			byte[] good = FogCodec.Encode(set);
			LogAssert.IsFalse(FogCodec.TryDecode(null, out _), "null");
			LogAssert.IsFalse(FogCodec.TryDecode(new byte[] { 1, 2, 3 }, out _), "too short");
			byte[] badMagic = (byte[])good.Clone();
			badMagic[0] = (byte)'X';
			LogAssert.IsFalse(FogCodec.TryDecode(badMagic, out _), "wrong magic");
			byte[] truncated = new byte[good.Length - 1];
			System.Array.Copy(good, truncated, truncated.Length);
			LogAssert.IsFalse(FogCodec.TryDecode(truncated, out _), "truncated");
			byte[] trailing = new byte[good.Length + 1];
			System.Array.Copy(good, trailing, good.Length);
			LogAssert.IsFalse(FogCodec.TryDecode(trailing, out _), "trailing bytes");
		}

		[Test]
		public void TheSaveMerge_IsAUnion_AndRepeatable()
		{
			FogChunkSet stored = new FogChunkSet();
			stored.Add(0, 0);
			stored.Add(5, 5);
			FogChunkSet pending = new FogChunkSet();
			pending.Add(5, 5);
			pending.Add(9, -3);

			AccountMapFogMerge first = MapFogSystem.Merge(FogCodec.Encode(stored), pending);
			LogAssert.AreEqual(3, first.ExploredCount, "the stored chunks and the new ones, once each");
			LogAssert.IsTrue(FogCodec.TryDecode(first.Data, out FogChunkSet merged) && merged.Contains(0, 0) && merged.Contains(9, -3));

			AccountMapFogMerge again = MapFogSystem.Merge(first.Data, pending);
			LogAssert.AreEqual(3, again.ExploredCount, "a retried merge adds nothing");

			AccountMapFogMerge fresh = MapFogSystem.Merge(null, pending);
			LogAssert.AreEqual(2, fresh.ExploredCount, "no stored row: just what was explored");

			AccountMapFogMerge corrupt = MapFogSystem.Merge(new byte[] { 9, 9, 9 }, pending);
			LogAssert.AreEqual(2, corrupt.ExploredCount, "an unreadable stored row is replaced, never a reason to lose the new chunks");
		}

		[Test]
		public void TheClientGrid_LinesUpWithTheServersChunks()
		{
			// A map rectangle that does not start on a chunk line.
			FogOfWarMap fog = new FogOfWarMap(new Rect(-540.96f, -100.5f, 1105.92f, 900.0f), 40.0f);
			FogChunkSet set = new FogChunkSet(40.0f);
			Vector3 walked = new Vector3(123.4f, 0.0f, 456.7f);
			set.AddAt(walked);
			set.Add(10000, 10000);

			LogAssert.AreEqual(1, fog.ApplyChunkSet(set), "the chunk on the map is applied; the one off it is ignored");
			LogAssert.IsTrue(fog.IsDiscovered(walked), "and it is the very chunk the server recorded");
			LogAssert.IsFalse(fog.IsDiscovered(walked + new Vector3(40.0f, 0.0f, 0.0f)), "not its neighbour");
		}

		[Test]
		public void TheClientGrid_RasterisesASetOfAnotherChunkSize()
		{
			FogOfWarMap fog = new FogOfWarMap(new Rect(0.0f, 0.0f, 400.0f, 400.0f), 40.0f);
			FogChunkSet set = new FogChunkSet(80.0f);
			set.Add(1, 1); // 80..160 on both axes; its centre (120, 120) is in 40 m chunk (3, 3)
			LogAssert.AreEqual(1, fog.ApplyChunkSet(set));
			LogAssert.IsTrue(fog.IsDiscovered(new Vector3(130.0f, 0.0f, 130.0f)));
		}

		[Test]
		public void SceneChoices_MarkTheCurrentScene_AndNeverRepeatALabel()
		{
			LogAssert.AreEqual("Moron" + MapSceneChoices.CurrentSuffix, MapSceneChoices.Label("Moron", isCurrent: true));
			LogAssert.AreEqual("Kelp", MapSceneChoices.Label("Kelp", isCurrent: false));
			LogAssert.IsFalse(MapSceneChoices.IsListable(null, null), "no scene, no choice");
			LogAssert.IsTrue(MapSceneChoices.IsListable("Anything", null), "without a cache every scene is offered");

			var labels = new List<string> { "Shore", "Shore", "Kelp" };
			var scenes = new List<string> { "Moron_1", "Moron_2", "Kelp_1" };
			MapSceneChoices.Disambiguate(labels, scenes);
			LogAssert.IsTrue(labels[0] != labels[1] && labels[0].Contains("Moron_1") && labels[1].Contains("Moron_2"), "two scenes of one name are told apart");
			LogAssert.AreEqual("Kelp", labels[2], "a unique label is left alone");
		}
	}
}
