using System.Collections.Generic;
using FishNet.Component.Observing;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Observing;
using FishNet.Transporting;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// One registered character in <see cref="ObserverStreamingRegistry"/>: the per-observer send
	/// intervals the scheduler assigned it, the range it is currently visible from, and the
	/// cached relevance inputs (combat, party, guild) used to rank it for each viewer.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Doubles as the object's <see cref="IObserverSendFilter"/>. <see cref="ShouldSend"/> runs
	/// once per observer per unreliable RPC, so it is a dictionary lookup and a modulo — nothing
	/// that allocates or walks a collection.
	/// </para>
	/// <para>
	/// Two things can slow an observer down: the viewer's full-rate cap (this entry's own
	/// intervals, assigned by <see cref="ObserverStreamingRegistry"/>) and the observer's distance
	/// from the object (<see cref="NetworkTransformDistanceLod"/> on the same GameObject). They
	/// are combined by taking the <b>larger</b> interval, never by gating one behind the other:
	/// two independent modulo gates only coincide once in N×M ticks and would starve the observer.
	/// </para>
	/// </remarks>
	public sealed class ObserverStreamingEntry : IObserverSendFilter
	{
		/// <summary>The character's network object.</summary>
		public NetworkObject NetworkObject { get; }

		/// <summary>The character.</summary>
		public ICharacter Character { get; }

		/// <summary>True for player characters, which are the only viewers the cap is computed for.</summary>
		public bool IsPlayer { get; }

		/// <summary>The configured range from the prefab's distance condition, before density scaling.</summary>
		public float BaseRange { get; }

		/// <summary>The range most recently applied to the distance condition.</summary>
		public float AppliedRange { get; private set; }

		/// <summary>
		/// Longest range among this character's known abilities, cached for the pass. Drives how far
		/// out its own view must be tick-exact; see <c>ObserverStreamingPolicy.ResolveEngagementRange</c>.
		/// </summary>
		public float LongestAbilityRange { get; private set; }

		/// <summary>
		/// Object id of the character this one currently targets, or 0. Pinned into its visibility
		/// budget so a fight cannot despawn its own target.
		/// </summary>
		public long CurrentTargetObjectId { get; private set; }

		/// <summary>Party id cached for the current scheduling pass; 0 when none.</summary>
		public long PartyID { get; private set; }

		/// <summary>Guild id cached for the current scheduling pass; 0 when none.</summary>
		public long GuildID { get; private set; }

		/// <summary>Combat state cached for the current scheduling pass.</summary>
		public bool InCombat { get; private set; }

		/// <summary>World position cached for the current scheduling pass.</summary>
		public Vector3 Position { get; private set; }

		/// <summary>Number of viewers this entry is currently rate limited for.</summary>
		public int LimitedObserverCount => intervalsByClientId.Count;

		private readonly DistanceCondition distanceCondition;
		private readonly NetworkTransformDistanceLod distanceLod;
		private readonly Dictionary<int, byte> intervalsByClientId = new Dictionary<int, byte>();
		private IPartyController partyController;
		private IGuildController guildController;
		private ICharacterDamageController damageController;
		private IAbilityController abilityController;
		private ITargetController targetController;
		private bool behavioursResolved;

		public ObserverStreamingEntry(NetworkObject networkObject, ICharacter character)
		{
			NetworkObject = networkObject;
			Character = character;
			IsPlayer = character is IPlayerCharacter;

			distanceCondition = networkObject.NetworkObserver != null
				? networkObject.NetworkObserver.GetObserverCondition<DistanceCondition>() as DistanceCondition
				: null;
			BaseRange = distanceCondition != null ? distanceCondition.GetMaximumDistance() : 0f;
			AppliedRange = BaseRange;
			Position = networkObject.transform.position;
			distanceLod = networkObject.GetComponent<NetworkTransformDistanceLod>();
		}

		/// <summary>Test seam: builds an entry with an explicit distance LOD (or none).</summary>
		internal ObserverStreamingEntry(NetworkObject networkObject, ICharacter character, NetworkTransformDistanceLod distanceLod)
			: this(networkObject, character)
		{
			this.distanceLod = distanceLod;
		}

		/// <summary>True when a <see cref="NetworkTransformDistanceLod"/> also shapes this object's sends.</summary>
		public bool HasDistanceLod => distanceLod != null;

		/// <summary>True when this character's visibility range can be changed at runtime.</summary>
		public bool HasDistanceCondition => distanceCondition != null;

		/// <summary>
		/// Refreshes the cached relevance inputs. Called once per entry per scheduling pass so
		/// the O(viewers × entries) ranking below reads fields, not behaviours.
		/// </summary>
		public void RefreshForPass()
		{
			if (!behavioursResolved)
			{
				Character.TryGet(out partyController);
				Character.TryGet(out guildController);
				Character.TryGet(out damageController);
				Character.TryGet(out abilityController);
				Character.TryGet(out targetController);
				behavioursResolved = true;
			}

			PartyID = partyController != null ? partyController.ID : 0;
			GuildID = guildController != null ? guildController.ID : 0;
			InCombat = damageController != null && damageController.IsInCombat;
			LongestAbilityRange = abilityController is AbilityController abilities ? abilities.LongestKnownAbilityRange : 0f;

			CurrentTargetObjectId = 0;
			if (targetController != null)
			{
				Transform target = targetController.Current.Target;
				if (target != null && target.TryGetComponent(out NetworkObject targetObject))
				{
					CurrentTargetObjectId = targetObject.ObjectId;
				}
			}

			Position = NetworkObject.transform.position;
		}

		/// <summary>
		/// Applies a density-scaled range to the distance condition when it differs from the
		/// applied one by at least <see cref="ObserverStreamingPolicy.RangeChangeThreshold"/>.
		/// The timed observer rebuild picks the new distance up on its next cycle.
		/// </summary>
		/// <returns>True when the range was changed.</returns>
		public bool ApplyRange(float range)
		{
			if (distanceCondition == null)
			{
				return false;
			}
			if (Mathf.Abs(range - AppliedRange) < ObserverStreamingPolicy.RangeChangeThreshold)
			{
				return false;
			}
			AppliedRange = range;
			distanceCondition.SetMaximumDistance(range);
			return true;
		}

		/// <summary>Forgets every per-observer interval; every observer is back at full rate.</summary>
		public void ClearIntervals()
		{
			intervalsByClientId.Clear();
		}

		/// <summary>Sets the send interval for one observer. An interval of 1 removes the limit.</summary>
		public void SetInterval(NetworkConnection connection, byte interval)
		{
			if (connection == null)
			{
				return;
			}
			if (interval <= 1)
			{
				intervalsByClientId.Remove(connection.ClientId);
			}
			else
			{
				intervalsByClientId[connection.ClientId] = interval;
			}
		}

		/// <summary>Send interval assigned to an observer by the viewer cap; 1 when unlimited.</summary>
		public byte GetInterval(NetworkConnection connection)
		{
			return connection != null && intervalsByClientId.TryGetValue(connection.ClientId, out byte interval) ? interval : (byte)1;
		}

		/// <summary>
		/// Send interval an observer actually receives: the larger of the cap interval and the
		/// distance LOD interval, so the two policies compose instead of multiplying.
		/// </summary>
		public byte GetEffectiveInterval(NetworkConnection connection)
		{
			/* An engaged observer is exempt from BOTH throttles. The cap is scored by relevance and
			 * knows nothing about distance, so taking the max below would have happily throttled a
			 * character standing next to its attacker back to every 2nd tick and left lag
			 * compensation rewinding to a pose that was never rendered. */
			if (distanceLod != null && distanceLod.IsEngaged(connection))
			{
				return 1;
			}

			byte cap = GetInterval(connection);
			byte lod = distanceLod != null ? distanceLod.GetInterval(connection) : (byte)1;
			return cap > lod ? cap : lod;
		}

		/// <inheritdoc/>
		public bool ShouldSend(NetworkObject networkObject, NetworkConnection connection, Channel channel)
		{
			/* Reliable sends are never shaped: the settle after a stop must reach everyone. FishNet
			 * only consults the filter for unreliable RPCs, but the contract is enforced here too
			 * so a caller invoking it directly gets the same answer. */
			if (channel != Channel.Unreliable || connection == null)
			{
				return true;
			}
			/* The owner always hears about its own character; only spectators are shaped. (A
			 * NetworkTransform that its owner would discard is already excluded before the filter
			 * runs — see NetworkBehaviour.ExcludeOwnerFromUnbufferedObserversRpcs.) */
			if (connection == networkObject.Owner)
			{
				return true;
			}
			byte interval = GetEffectiveInterval(connection);
			if (interval <= 1)
			{
				return true;
			}
			uint tick = networkObject.TimeManager != null ? networkObject.TimeManager.LocalTick : 0u;
			return ObserverStreamingPolicy.ShouldSendThisTick(tick, interval, connection.ClientId);
		}
	}
}
