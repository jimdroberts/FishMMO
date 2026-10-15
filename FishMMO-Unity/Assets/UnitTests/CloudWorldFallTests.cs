using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Falling into a cloud world's deck: the death it causes, and where a character is put back down.
	/// </summary>
	/// <remarks>
	/// The landing cases are the ways a respawn or a revive could drop a character straight back into the deck: a bind
	/// point left over open air by a re-cut, a nearer respawn that is not on an island, a scene with nothing that holds.
	/// The pins hold the death to the game's one death path, and the dead to zero health.
	/// </remarks>
	[TestFixture]
	public class CloudWorldFallTests
	{
		private const string FallPath =
			"Assets/Scripts/Server/Implementation/World/SceneServer/Character/CharacterSystem.CloudWorld.cs";

		private const string DamageControllerPath =
			"Assets/Scripts/Shared/Implementation/Entity/Prediction/CharacterAttribute/CharacterDamageController.cs";

		private GameObject host;

		/// <summary>One island of reach 50 round the origin, ground at 100, death below 0.</summary>
		private SceneCloudWorld World()
		{
			host = new GameObject("Cloud World Test");
			SceneCloudWorld world = host.AddComponent<SceneCloudWorld>();
			world.DeathAltitude = 0f;
			world.IslandFloor = 100f;
			world.IslandCeiling = 100f;
			world.Islands = new[]
			{
				new SceneCloudWorld.Island
				{
					Kind = SceneCloudWorld.IslandKind.City,
					Centre = new Vector3(0f, 100f, 0f),
					Radius = 50f,
					Keel = 40f,
				},
			};
			return world;
		}

		/// <summary>
		/// The same island as a test, for the choice of landing: no GameObject, so these run without the engine.
		/// </summary>
		private static bool OnTheIsland(Vector3 p) => p.y >= 0f && p.x * p.x + p.z * p.z <= 50f * 50f;

		[TearDown]
		public void TearDown()
		{
			if (host != null)
			{
				Object.DestroyImmediate(host);
				host = null;
			}
		}

		// ── Holds ─────────────────────────────────────────────────────────────

		[Test]
		public void OutsideACloudWorld_EverythingHolds()
		{
			LogAssert.IsTrue(CloudWorldLanding.Holds(null, new Vector3(0f, -1000f, 0f)));
		}

		[Test]
		public void IslandGround_Holds()
		{
			LogAssert.IsTrue(CloudWorldLanding.Holds(World(), new Vector3(10f, 100f, 10f)));
		}

		[Test]
		public void OpenAirBetweenIslands_DoesNotHold()
		{
			LogAssert.IsFalse(CloudWorldLanding.Holds(World(), new Vector3(200f, 100f, 0f)),
				"a bind point over open air drops the character straight back into the deck");
		}

		[Test]
		public void BelowTheDeathAltitude_DoesNotHold()
		{
			LogAssert.IsFalse(CloudWorldLanding.Holds(World(), new Vector3(0f, -1f, 0f)));
		}

		// ── TryFind ───────────────────────────────────────────────────────────

		[Test]
		public void ANearerRespawnOverOpenAir_IsPassedOver()
		{
			var details = new WorldSceneDetails();
			details.RespawnPositions.Add("Adrift", new CharacterRespawnPositionDetails { Position = new Vector3(80f, 100f, 0f), Rotation = Quaternion.identity });
			details.RespawnPositions.Add("Town", new CharacterRespawnPositionDetails { Position = new Vector3(0f, 100f, 0f), Rotation = new Quaternion(0f, 0.70710677f, 0f, 0.70710677f) });

			LogAssert.IsTrue(CloudWorldLanding.TryFind(OnTheIsland, details, new Vector3(90f, -20f, 0f), out RescuePoint point));
			LogAssert.AreEqual("Town", point.Name);
			LogAssert.IsTrue(point.HasRotation);
		}

		[Test]
		public void NoRespawnHolds_ASpawnThatDoesIsUsed()
		{
			var details = new WorldSceneDetails();
			details.RespawnPositions.Add("Adrift", new CharacterRespawnPositionDetails { Position = new Vector3(80f, 100f, 0f), Rotation = Quaternion.identity });
			details.InitialSpawnPositions.Add("gate", new CharacterInitialSpawnPositionDetails { SpawnerName = "Gate", Position = new Vector3(5f, 100f, 5f), Rotation = Quaternion.identity });

			LogAssert.IsTrue(CloudWorldLanding.TryFind(OnTheIsland, details, new Vector3(80f, -20f, 0f), out RescuePoint point));
			LogAssert.AreEqual(RescuePointKind.Spawn, point.Kind);
			LogAssert.AreEqual("Gate", point.Name);
		}

		[Test]
		public void NothingHolds_TheNearestRespawnIsStillAnAnswer()
		{
			var details = new WorldSceneDetails();
			details.RespawnPositions.Add("Far", new CharacterRespawnPositionDetails { Position = new Vector3(400f, 100f, 0f), Rotation = Quaternion.identity });
			details.RespawnPositions.Add("Near", new CharacterRespawnPositionDetails { Position = new Vector3(90f, 100f, 0f), Rotation = Quaternion.identity });

			LogAssert.IsTrue(CloudWorldLanding.TryFind(OnTheIsland, details, new Vector3(100f, -20f, 0f), out RescuePoint point));
			LogAssert.AreEqual("Near", point.Name);
		}

		[Test]
		public void AnEmptyScene_FindsNothing()
		{
			LogAssert.IsFalse(CloudWorldLanding.TryFind(OnTheIsland, new WorldSceneDetails(), Vector3.zero, out _));
			LogAssert.IsFalse(CloudWorldLanding.TryFind(OnTheIsland, null, Vector3.zero, out _));
		}

		// ── Pins ──────────────────────────────────────────────────────────────

		[Test]
		public void TheDeckKillsThroughTheDeathPath()
		{
			SourceScanPins.HoldsAndFires("LoseToTheDeck", SourceScanPins.ReadCode(FallPath),
				c =>
				{
					string body = SourceScanPins.Body(c, "private void LoseToTheDeck(");
					if (body == null)
					{
						return "LoseToTheDeck is gone";
					}
					if (!body.Contains("damageController.Kill(null)"))
					{
						return "a fall must die through ICharacterDamageController.Kill, with no killer to credit";
					}
					if (body.Contains("EnableFlags(CharacterFlags.IsDead)"))
					{
						return "setting the dead flag by hand skips OnKilled: no death dialog, no pet, arena or threat cleanup";
					}
					return null;
				},
				SourceScanPins.Replace("damageController.Kill(null);", "character.EnableFlags(CharacterFlags.IsDead);"),
				"the fall flags the character dead instead of killing it");
		}

		[Test]
		public void KillEmptiesHealthAfterTheFlag()
		{
			SourceScanPins.HoldsAndFires("CharacterDamageController.Kill", SourceScanPins.ReadCode(DamageControllerPath),
				c => SourceScanPins.InOrder(SourceScanPins.Body(c, "public void Kill(ICharacter killer)"),
					"Character.EnableFlags(CharacterFlags.IsDead);",
					"ResourceInstance.SetCurrentValue(0.0f);",
					"InvokeKilledIsolated("),
				SourceScanPins.Replace("ResourceInstance.SetCurrentValue(0.0f);", string.Empty),
				"a kill that is not damage leaves the corpse its health");
		}
	}
}
