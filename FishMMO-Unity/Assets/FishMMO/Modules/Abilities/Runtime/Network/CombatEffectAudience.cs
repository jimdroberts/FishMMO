using System.Collections.Generic;
using FishNet.Broadcast;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Object;
using FishNet.Transporting;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Who is told about a cast or an ability object's flight: the caster's observers within
	/// <see cref="ObserverStreamingPolicy.CombatEffectRange"/>, and for everything that follows a start, every player the
	/// start reached as well.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why the follow-ups remember the start's audience.</b> An observer that was told an object was activated and is
	/// never told it ended flies it for its whole lifetime and sets it off in empty air (see the activation broadcast in
	/// AbilityController.Activation). Filtering each message by distance on its own would do exactly that to a viewer that
	/// walked out of range mid-flight. So the start's recipients are kept, per activation (caster, ability, spawn tick), and
	/// every hit, redirect and destruction of that activation goes to them plus whoever is now within range. A follow-up
	/// whose start is no longer remembered goes to every observer, as before: an observer that never saw the start drops it.
	/// </para>
	/// <para>
	/// Range 0 turns the filter off: every message goes to every observer, exactly as before.
	/// </para>
	/// </remarks>
	public static class CombatEffectAudience
	{
		private readonly struct ActivationKey : System.IEquatable<ActivationKey>
		{
			public readonly int Caster;
			public readonly long Ability;
			public readonly uint SpawnTick;

			public ActivationKey(int caster, long ability, uint spawnTick)
			{
				Caster = caster;
				Ability = ability;
				SpawnTick = spawnTick;
			}

			public bool Equals(ActivationKey other) => Caster == other.Caster && Ability == other.Ability && SpawnTick == other.SpawnTick;
			public override bool Equals(object obj) => obj is ActivationKey other && Equals(other);
			public override int GetHashCode() => (Caster * 397 ^ Ability.GetHashCode()) * 397 ^ (int)SpawnTick;
		}

		private sealed class Audience
		{
			public readonly HashSet<NetworkConnection> Connections = new HashSet<NetworkConnection>();
			public uint RememberedAt;
		}

		/// <summary>How long a start's audience is remembered, in ticks: 30 s at 30 Hz, longer than any ability object flies.</summary>
		public const uint RememberTicks = 900;

		private static readonly Dictionary<ActivationKey, Audience> audiences = new Dictionary<ActivationKey, Audience>();
		private static readonly Stack<Audience> pool = new Stack<Audience>();
		private static readonly List<ActivationKey> expired = new List<ActivationKey>();
		private static readonly HashSet<NetworkConnection> recipients = new HashSet<NetworkConnection>();
		private static uint nextSweepTick;

		/// <summary>Messages a filter kept from observers beyond the range (a diagnostic for the simulate report).</summary>
		public static long FilteredOut;

		/// <summary>True when <paramref name="connection"/>'s character is within the range of <paramref name="origin"/>, or unknown.</summary>
		public static bool IsWithinRange(NetworkConnection connection, Vector3 origin)
		{
			float range = ObserverStreamingPolicy.CombatEffectRange;
			if (range <= 0f || connection == null)
			{
				return true;
			}
			NetworkObject viewer = connection.FirstObject;
			if (viewer == null)
			{
				return true;
			}
			return (viewer.transform.position - origin).sqrMagnitude <= range * range;
		}

		/// <summary>
		/// Sends an ability activation to the caster's observers within range, except its owner, and remembers who it reached.
		/// </summary>
		public static void SendActivation<T>(NetworkObject caster, long abilityId, uint spawnTick, T message, Channel channel) where T : struct, IBroadcast
		{
			if (ObserverStreamingPolicy.CombatEffectRange <= 0f)
			{
				ObserverBroadcastScope.BroadcastToObserversExceptOwner(caster, message, channel);
				return;
			}
			if (!TryGetServer(caster, out NetworkManager networkManager))
			{
				return;
			}
			uint now = networkManager.TimeManager != null ? networkManager.TimeManager.LocalTick : 0u;
			Sweep(now);

			ActivationKey key = new ActivationKey(caster.ObjectId, abilityId, spawnTick);
			if (!audiences.TryGetValue(key, out Audience audience))
			{
				audience = pool.Count > 0 ? pool.Pop() : new Audience();
				audiences[key] = audience;
			}
			audience.Connections.Clear();
			audience.RememberedAt = now;

			Vector3 origin = caster.transform.position;
			foreach (NetworkConnection connection in caster.Observers)
			{
				if (connection == null || ReferenceEquals(connection, caster.Owner))
				{
					continue;
				}
				if (IsWithinRange(connection, origin))
				{
					audience.Connections.Add(connection);
				}
				else
				{
					FilteredOut++;
				}
			}
			if (audience.Connections.Count > 0)
			{
				recipients.Clear();
				recipients.UnionWith(audience.Connections);
				networkManager.ServerManager.Broadcast(recipients, message, true, channel);
				recipients.Clear();
			}
		}

		/// <summary>
		/// Sends a hit, redirect or destruction of an activation to everyone its start reached and every observer now within
		/// range; to every observer when the start is not remembered. The owner gets it only when <paramref name="includeOwner"/>.
		/// </summary>
		public static void SendFollowUp<T>(NetworkObject caster, long abilityId, uint spawnTick, T message, Channel channel, bool includeOwner) where T : struct, IBroadcast
		{
			if (!TryGetServer(caster, out NetworkManager networkManager))
			{
				return;
			}
			if (ObserverStreamingPolicy.CombatEffectRange <= 0f ||
				!audiences.TryGetValue(new ActivationKey(caster.ObjectId, abilityId, spawnTick), out Audience audience))
			{
				SendToObservers(caster, networkManager, message, channel, includeOwner, null);
				return;
			}
			SendToObservers(caster, networkManager, message, channel, includeOwner, audience.Connections);
		}

		/// <summary>
		/// Sends the start of a cast to the caster's observers within range, except its owner. Its end is not filtered (send it
		/// with <see cref="ObserverBroadcastScope.BroadcastToObserversExceptOwner"/>): a player that starts observing the caster
		/// mid-cast learns the cast from the spawn payload, wherever it stands, and must hear it end.
		/// </summary>
		public static void SendCastStart<T>(NetworkObject caster, T message, Channel channel) where T : struct, IBroadcast
		{
			if (ObserverStreamingPolicy.CombatEffectRange <= 0f)
			{
				ObserverBroadcastScope.BroadcastToObserversExceptOwner(caster, message, channel);
				return;
			}
			if (!TryGetServer(caster, out NetworkManager networkManager))
			{
				return;
			}
			recipients.Clear();
			Vector3 origin = caster.transform.position;
			foreach (NetworkConnection connection in caster.Observers)
			{
				if (connection == null || ReferenceEquals(connection, caster.Owner))
				{
					continue;
				}
				if (IsWithinRange(connection, origin))
				{
					recipients.Add(connection);
				}
				else
				{
					FilteredOut++;
				}
			}
			if (recipients.Count > 0)
			{
				networkManager.ServerManager.Broadcast(recipients, message, true, channel);
			}
			recipients.Clear();
		}

		/// <summary>
		/// Adds <paramref name="connection"/> to a remembered activation's audience: it is being handed the object in flight by
		/// a spawn payload, wherever it stands, so it must hear how the object ends. Nothing is remembered for an activation
		/// whose start is not: its follow-ups already go to every observer.
		/// </summary>
		public static void Remember(NetworkObject caster, long abilityId, uint spawnTick, NetworkConnection connection)
		{
			if (caster == null || connection == null ||
				!audiences.TryGetValue(new ActivationKey(caster.ObjectId, abilityId, spawnTick), out Audience audience))
			{
				return;
			}
			audience.Connections.Add(connection);
		}

		/// <summary>Drops every remembered audience. For tests and server shutdown.</summary>
		public static void Clear()
		{
			foreach (Audience audience in audiences.Values)
			{
				audience.Connections.Clear();
				pool.Push(audience);
			}
			audiences.Clear();
			recipients.Clear();
			nextSweepTick = 0u;
		}

		/// <summary>The observers within range, plus <paramref name="remembered"/> (still observing), plus the owner if asked.</summary>
		private static void SendToObservers<T>(NetworkObject caster, NetworkManager networkManager, T message, Channel channel, bool includeOwner, HashSet<NetworkConnection> remembered) where T : struct, IBroadcast
		{
			recipients.Clear();
			Vector3 origin = caster.transform.position;
			foreach (NetworkConnection connection in caster.Observers)
			{
				if (connection == null)
				{
					continue;
				}
				bool owner = ReferenceEquals(connection, caster.Owner);
				if (owner)
				{
					if (includeOwner)
					{
						recipients.Add(connection);
					}
					continue;
				}
				if (remembered == null || remembered.Contains(connection) || IsWithinRange(connection, origin))
				{
					recipients.Add(connection);
				}
				else
				{
					FilteredOut++;
				}
			}
			if (recipients.Count > 0)
			{
				networkManager.ServerManager.Broadcast(recipients, message, true, channel);
			}
			recipients.Clear();
		}

		private static bool TryGetServer(NetworkObject caster, out NetworkManager networkManager)
		{
			networkManager = caster != null && caster.IsSpawned ? caster.NetworkManager : null;
			return networkManager != null && networkManager.IsServerStarted;
		}

		/// <summary>Forgets audiences older than <see cref="RememberTicks"/>, at most once a second.</summary>
		private static void Sweep(uint now)
		{
			if ((int)(now - nextSweepTick) < 0)
			{
				return;
			}
			nextSweepTick = now + 30u;
			expired.Clear();
			foreach (KeyValuePair<ActivationKey, Audience> entry in audiences)
			{
				if (now - entry.Value.RememberedAt > RememberTicks)
				{
					expired.Add(entry.Key);
				}
			}
			for (int i = 0; i < expired.Count; ++i)
			{
				Audience audience = audiences[expired[i]];
				audiences.Remove(expired[i]);
				audience.Connections.Clear();
				pool.Push(audience);
			}
			expired.Clear();
		}
	}
}
