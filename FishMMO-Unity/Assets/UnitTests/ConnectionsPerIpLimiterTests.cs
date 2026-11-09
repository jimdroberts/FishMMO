using System.IO;
using FishMMO.Server.Implementation;
using NUnit.Framework;
using UnityEngine;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Connections are limited per real player address, never per proxy address.
	/// </summary>
	/// <remarks>
	/// Every player reaches the game servers through the UDP proxy, so the native transport sees them
	/// all as 127.0.0.1. Its per-IP limits therefore applied to all players of a server at once. The
	/// real address is known once the connection token has been verified, and that is where this
	/// limit is applied.
	/// </remarks>
	[TestFixture]
	public class ConnectionsPerIpLimiterTests
	{
		private const string PlayerA = "203.0.113.10";
		private const string PlayerB = "203.0.113.20";

		[Test]
		public void AnAddressIsRefusedAtTheLimit_AndOtherAddressesAreNot()
		{
			var limiter = new ConnectionsPerIpLimiter();

			LogAssert.IsTrue(limiter.TryAcquire(1, PlayerA, 2), "the first connection is within the limit");
			LogAssert.IsTrue(limiter.TryAcquire(2, PlayerA, 2), "the second connection reaches the limit");
			LogAssert.IsFalse(limiter.TryAcquire(3, PlayerA, 2), "the third connection from that address is over it");
			LogAssert.IsTrue(limiter.TryAcquire(4, PlayerB, 2), "another player is unaffected");
		}

		[Test]
		public void ReleasingFreesTheSlot_AndOnlyOnce()
		{
			var limiter = new ConnectionsPerIpLimiter();
			limiter.TryAcquire(1, PlayerA, 1);

			limiter.Release(1);
			limiter.Release(1);

			LogAssert.AreEqual(0, limiter.CountFor(PlayerA), "a released connection holds nothing, however often it is released");
			LogAssert.IsTrue(limiter.TryAcquire(2, PlayerA, 1), "the freed slot can be taken");
		}

		[Test]
		public void AConnectionHoldsOneSlot()
		{
			var limiter = new ConnectionsPerIpLimiter();

			limiter.TryAcquire(1, PlayerA, 1);

			LogAssert.IsTrue(limiter.TryAcquire(1, PlayerA, 1), "the same connection presenting its address again is not a new connection");
			LogAssert.AreEqual(1, limiter.CountFor(PlayerA), "and it still holds one slot");
		}

		[Test]
		public void ChangingAddressMovesTheSlot()
		{
			var limiter = new ConnectionsPerIpLimiter();

			limiter.TryAcquire(1, PlayerA, 1);
			limiter.TryAcquire(1, PlayerB, 1);

			LogAssert.AreEqual(0, limiter.CountFor(PlayerA), "the old address gets its slot back");
			LogAssert.AreEqual(1, limiter.CountFor(PlayerB), "the new address holds it");
		}

		[Test]
		public void ARefusedMoveKeepsTheSlotItHad()
		{
			var limiter = new ConnectionsPerIpLimiter();
			limiter.TryAcquire(1, PlayerA, 1);
			limiter.TryAcquire(2, PlayerB, 1);

			LogAssert.IsFalse(limiter.TryAcquire(1, PlayerB, 1), "the new address is full");
			LogAssert.AreEqual(1, limiter.CountFor(PlayerA), "so the connection keeps what it held");
		}

		[Test]
		public void ZeroMeansNoLimit_AndAnUnknownAddressIsNotCounted()
		{
			var limiter = new ConnectionsPerIpLimiter();

			for (int connection = 1; connection <= 50; ++connection)
			{
				LogAssert.IsTrue(limiter.TryAcquire(connection, PlayerA, 0), "zero disables the limit");
			}

			LogAssert.IsTrue(limiter.TryAcquire(99, null, 1), "an unknown address is never refused here");
		}

		private static string Source(string relativePath)
		{
			return File.ReadAllText(Path.Combine(Application.dataPath, "Scripts/Server/Implementation", relativePath));
		}

		[Test]
		public void TheTransportDoesNotApplyPerIpLimitsToTheProxyAddressByDefault()
		{
			string source = Source("FishNetNetworkWrapper.cs");

			LogAssert.IsTrue(source.Contains("config.GetInt(\"TransportConnectIntervalMs\", 0)"),
				"the native connect interval must default to off: behind the proxy it is one limit shared by every player");
			LogAssert.IsTrue(source.Contains("config.GetInt(\"TransportMaxConnectionsPerIP\", 0)"),
				"the native per-IP cap must default to off for the same reason");
		}

		[Test]
		public void TheLimitIsTakenFromTheVerifiedTokenAndGivenBackOnDisconnect()
		{
			string source = Source("Authentication/BaseServerAuthenticator.cs");

			LogAssert.IsTrue(source.Contains("connectionsPerRealIp.TryAcquire(clientId, HandshakeService.NormalizeIp(realIp)"),
				"the slot must be taken against the address the connection token verified");
			LogAssert.IsTrue(source.Contains("connectionsPerRealIp.Release(conn.ClientId);"),
				"a stopped connection must give its slot back, or an address runs out of them");
		}
	}
}
