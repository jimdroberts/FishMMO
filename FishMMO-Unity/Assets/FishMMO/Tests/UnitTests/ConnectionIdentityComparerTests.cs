using System.Collections.Generic;
using FishNet.Connection;
using NUnit.Framework;
using FishMMO.Server.Implementation.World.SceneServer;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// A dictionary keyed by NetworkConnection must still reach a connection after it disconnects. FishNet resets ClientId to
	/// -1 on disconnect, and NetworkConnection's hash and equality follow ClientId (a -1 connection does not even equal
	/// itself), so the scene server's send schedule lost a disconnected player's entry and threw KeyNotFoundException for
	/// 'Id [-1]' on every tick from then on (live, 2026-11-18).
	/// </summary>
	[TestFixture]
	public class ConnectionIdentityComparerTests
	{
		[Test]
		public void ADisconnectedConnection_IsStillFoundAndRemoved()
		{
			var connection = new NetworkConnection { ClientId = 5 };
			var byValue = new Dictionary<NetworkConnection, int> { [connection] = 1 };
			var byIdentity = new Dictionary<NetworkConnection, int>(ConnectionIdentityComparer.Instance) { [connection] = 1 };

			connection.ClientId = -1; // what FishNet's ResetState does on disconnect

			LogAssert.IsFalse(byValue.ContainsKey(connection), "control: a value-keyed dictionary loses it");
			LogAssert.IsTrue(byIdentity.TryGetValue(connection, out int value) && value == 1, "found by identity");
			LogAssert.IsTrue(byIdentity.Remove(connection), "and removed");
			LogAssert.AreEqual(0, byIdentity.Count);
		}

		[Test]
		public void TheSendScheduleKeysByIdentity()
		{
			string code = SourceScanPins.ReadCode("Assets/FishMMO/Server/Implementation/World/SceneServer/SceneServer/SceneServerSystem.NetworkSchedule.cs");
			LogAssert.IsTrue(code.Contains("new Dictionary<NetworkConnection, (IPlayerCharacter, uint)>(ConnectionIdentityComparer.Instance)"),
				"the schedule's dictionary compares connections by reference");
			LogAssert.IsFalse(code.Contains("= scheduledPlayers[connection];"), "the tick reads with TryGetValue, never the throwing indexer");
		}
	}
}
