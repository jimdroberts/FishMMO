using System.Reflection;
using FishMMO.Shared;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The recorded grid behind <see cref="LagCompensationRegistry.AnyWithin"/> and
	/// <see cref="LagCompensationRegistry.AnyPlayerWithin"/>: what ships and regions ask instead of reading every character's
	/// transform each tick.
	/// </summary>
	public class LagCompensationGridTests
	{
		private GameObject npcGo;
		private GameObject playerGo;
		private CharacterPositionHistory npc;
		private CharacterPositionHistory player;
		private Scene scene;

		[SetUp]
		public void SetUp()
		{
			LagCompensationRegistry.Clear();
			npcGo = new GameObject("GridNpc");
			playerGo = new GameObject("GridPlayer");
			npc = npcGo.AddComponent<CharacterPositionHistory>();
			player = playerGo.AddComponent<CharacterPositionHistory>();
			typeof(CharacterPositionHistory).GetProperty("IsPlayer", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
				.SetValue(player, true);
			scene = npcGo.scene;
		}

		[TearDown]
		public void TearDown()
		{
			LagCompensationRegistry.Clear();
			Object.DestroyImmediate(npcGo);
			Object.DestroyImmediate(playerGo);
		}

		[Test]
		public void AnyWithin_FindsARecordedCharacterInReach_AndNothingBeyond()
		{
			LagCompensationRegistry.NoteRecorded(npc, scene.handle, new Vector3(100f, 5f, 100f));

			Assert.IsTrue(LagCompensationRegistry.AnyWithin(scene, new Vector3(130f, 0f, 100f), 31f), "30 m away, 31 m reach.");
			Assert.IsFalse(LagCompensationRegistry.AnyWithin(scene, new Vector3(130f, 0f, 100f), 29f), "30 m away, 29 m reach.");
			// Across the ground only: height never counts, as before.
			Assert.IsTrue(LagCompensationRegistry.AnyWithin(scene, new Vector3(100f, 500f, 100f), 1f));
			// The corner of the reach's square is outside its circle.
			Assert.IsFalse(LagCompensationRegistry.AnyWithin(scene, new Vector3(125f, 0f, 125f), 30f), "35 m away diagonally.");
		}

		[Test]
		public void AnyWithin_FindsNeighboursAcrossCellBorders()
		{
			float cell = LagCompensationRegistry.RecordedGrid.CellSize;
			LagCompensationRegistry.NoteRecorded(npc, scene.handle, new Vector3(cell - 0.5f, 0f, -0.5f));

			Assert.IsTrue(LagCompensationRegistry.AnyWithin(scene, new Vector3(cell + 0.5f, 0f, 0.5f), 2f));
			Assert.IsTrue(LagCompensationRegistry.AnyWithin(scene, new Vector3(-cell * 4f, 0f, 0f), cell * 5f), "A query wider than the crowd scans the list.");
		}

		[Test]
		public void BeginRecordingPass_EmptiesTheGrid()
		{
			LagCompensationRegistry.NoteRecorded(npc, scene.handle, Vector3.zero);
			LagCompensationRegistry.BeginRecordingPass();

			Assert.IsFalse(LagCompensationRegistry.AnyWithin(scene, Vector3.zero, 10f), "Each pass starts from nothing.");
		}

		[Test]
		public void AnyPlayerWithin_CountsOnlyPlayers_InsideTheGrownBounds()
		{
			Bounds region = new Bounds(new Vector3(0f, 0f, 0f), new Vector3(20f, 10f, 20f));
			LagCompensationRegistry.NoteRecorded(npc, scene.handle, new Vector3(0f, 0f, 0f));

			Assert.IsFalse(LagCompensationRegistry.AnyPlayerWithin(scene, region, 4f), "An NPC inside never wakes a region.");

			LagCompensationRegistry.NoteRecorded(player, scene.handle, new Vector3(13f, 0f, 0f));
			Assert.IsTrue(LagCompensationRegistry.AnyPlayerWithin(scene, region, 4f), "3 m outside, 4 m margin.");
			Assert.IsFalse(LagCompensationRegistry.AnyPlayerWithin(scene, region, 2f), "3 m outside, 2 m margin.");

			LagCompensationRegistry.BeginRecordingPass();
			LagCompensationRegistry.NoteRecorded(player, scene.handle, new Vector3(0f, 30f, 0f));
			Assert.IsFalse(LagCompensationRegistry.AnyPlayerWithin(scene, region, 4f), "Height counts for a region: 25 m above.");
		}

		[Test]
		public void TheGridGrows_PastItsFirstCapacity()
		{
			for (int i = 0; i < 200; i++)
			{
				LagCompensationRegistry.NoteRecorded(npc, scene.handle, new Vector3(i * 10f, 0f, 0f));
			}

			Assert.IsTrue(LagCompensationRegistry.AnyWithin(scene, new Vector3(1990f, 0f, 0f), 1f), "The 200th entry.");
			Assert.IsTrue(LagCompensationRegistry.AnyWithin(scene, new Vector3(0f, 0f, 0f), 1f), "The first entry.");
		}
	}
}
