using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Where a rescue sends a character: <c>/gm rescue</c>, <c>/gm unstuck</c> and a player's own
	/// <c>/unstuck</c> all resolve through <see cref="RescuePoints"/>.
	/// </summary>
	/// <remarks>
	/// Every case here is a way a rescue could put a player somewhere nobody chose: a prefix match
	/// landing on a different respawn than the one typed, a waypoint snapping the character to face
	/// north, or an empty scene answering with a default position at the world origin.
	/// </remarks>
	[TestFixture]
	public class RescuePointsTests
	{
		private static WorldSceneDetails Scene()
		{
			var details = new WorldSceneDetails();

			details.RespawnPositions.Add("Camp", new CharacterRespawnPositionDetails
			{
				Position = new Vector3(0f, 0f, 0f),
				Rotation = Quaternion.Euler(0f, 90f, 0f),
			});
			details.RespawnPositions.Add("Camp Far", new CharacterRespawnPositionDetails
			{
				Position = new Vector3(100f, 0f, 0f),
				Rotation = Quaternion.identity,
			});

			details.InitialSpawnPositions.Add("spawner-key", new CharacterInitialSpawnPositionDetails
			{
				SpawnerName = "Village Gate",
				Position = new Vector3(10f, 0f, 10f),
				Rotation = Quaternion.identity,
			});

			details.Waypoints.Add(0, new SceneWaypointDetails { Index = 0, Name = "Camp Waypoint", Position = new Vector3(5f, 1f, 5f) });
			details.Waypoints.Add(3, new SceneWaypointDetails { Index = 3, Name = "Far Field Waypoint", Position = new Vector3(90f, 1f, 0f) });

			return details;
		}

		// ── Kinds ─────────────────────────────────────────────────────────────

		[Test]
		public void KindWordsParseIgnoringCase()
		{
			LogAssert.IsTrue(RescuePoints.TryParseKind("Respawn", out RescuePointKind respawn));
			LogAssert.AreEqual(RescuePointKind.Respawn, respawn);
			LogAssert.IsTrue(RescuePoints.TryParseKind(" SPAWN ", out RescuePointKind spawn));
			LogAssert.AreEqual(RescuePointKind.Spawn, spawn);
			LogAssert.IsTrue(RescuePoints.TryParseKind("waypoint", out RescuePointKind waypoint));
			LogAssert.AreEqual(RescuePointKind.Waypoint, waypoint);
		}

		[Test]
		public void AnythingElseIsNotAKind()
		{
			LogAssert.IsFalse(RescuePoints.TryParseKind("bind", out _));
			LogAssert.IsFalse(RescuePoints.TryParseKind("way", out _));
			LogAssert.IsFalse(RescuePoints.TryParseKind("", out _));
			LogAssert.IsFalse(RescuePoints.TryParseKind(null, out _));
		}

		// ── Respawns ──────────────────────────────────────────────────────────

		[Test]
		public void NoNameMeansTheNearestRespawn()
		{
			LogAssert.IsTrue(RescuePoints.TryFind(Scene(), RescuePointKind.Respawn, null, new Vector3(80f, 0f, 0f), out RescuePoint point));
			LogAssert.AreEqual("Camp Far", point.Name);

			LogAssert.IsTrue(RescuePoints.TryFind(Scene(), RescuePointKind.Respawn, "  ", new Vector3(3f, 0f, 0f), out point));
			LogAssert.AreEqual("Camp", point.Name);
		}

		[Test]
		public void ANamedRespawnMatchesExactlyIgnoringCaseAndKeepsItsFacing()
		{
			LogAssert.IsTrue(RescuePoints.TryFind(Scene(), RescuePointKind.Respawn, "camp", new Vector3(100f, 0f, 0f), out RescuePoint point));
			LogAssert.AreEqual("Camp", point.Name);
			LogAssert.IsTrue(point.HasRotation);
			LogAssert.IsTrue(Quaternion.Angle(Quaternion.Euler(0f, 90f, 0f), point.Rotation) < 0.01f);
		}

		[Test]
		public void APrefixIsNotAName()
		{
			// "Cam" must not quietly pick "Camp" or "Camp Far": the operator cannot see where it went.
			LogAssert.IsFalse(RescuePoints.TryFind(Scene(), RescuePointKind.Respawn, "Cam", Vector3.zero, out _));
		}

		// ── Spawns ────────────────────────────────────────────────────────────

		[Test]
		public void ASpawnIsNamedByItsSpawner()
		{
			LogAssert.IsTrue(RescuePoints.TryFind(Scene(), RescuePointKind.Spawn, "village gate", Vector3.zero, out RescuePoint point));
			LogAssert.AreEqual("Village Gate", point.Name);
			LogAssert.AreEqual(RescuePointKind.Spawn, point.Kind);
		}

		// ── Waypoints ─────────────────────────────────────────────────────────

		[Test]
		public void AWaypointIsFoundByNameOrByIndexAndCarriesNoFacing()
		{
			LogAssert.IsTrue(RescuePoints.TryFind(Scene(), RescuePointKind.Waypoint, "far field waypoint", Vector3.zero, out RescuePoint byName));
			LogAssert.AreEqual("Far Field Waypoint", byName.Name);
			LogAssert.AreEqual(new Vector3(90f, 1f, 0f), byName.Position);
			LogAssert.IsFalse(byName.HasRotation);

			LogAssert.IsTrue(RescuePoints.TryFind(Scene(), RescuePointKind.Waypoint, "0", new Vector3(90f, 0f, 0f), out RescuePoint byIndex));
			LogAssert.AreEqual("Camp Waypoint", byIndex.Name);
			LogAssert.IsFalse(byIndex.HasRotation);
		}

		[Test]
		public void AnUnknownWaypointIndexIsRefused()
		{
			LogAssert.IsFalse(RescuePoints.TryFind(Scene(), RescuePointKind.Waypoint, "7", Vector3.zero, out _));
		}

		// ── Nothing to go to ──────────────────────────────────────────────────

		[Test]
		public void AnEmptySceneOrNoDetailsFindsNothing()
		{
			// Never a default point at the origin: "no rescue point" must be a refusal the caller answers.
			LogAssert.IsFalse(RescuePoints.TryFind(new WorldSceneDetails(), RescuePointKind.Respawn, null, Vector3.zero, out _));
			LogAssert.IsFalse(RescuePoints.TryFind(null, RescuePointKind.Waypoint, null, Vector3.zero, out _));
			LogAssert.AreEqual(0, RescuePoints.List(null, RescuePointKind.Spawn).Count);
		}
	}
}
