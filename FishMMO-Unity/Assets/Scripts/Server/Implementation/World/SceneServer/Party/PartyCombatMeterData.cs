using System.Collections.Generic;
using FishMMO.Server.Core;
using FishMMO.Server.Core.World.SceneServer;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// Runtime data container holding per-encounter damage and healing meters.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Deliberately a plain dictionary rather than the shared
	/// <c>LastSeenCacheTracker</c> the invitation caches use. That tracker allocates a linked-list
	/// node on every touch, and this is touched once per landed hit for every character on the
	/// scene server — a per-hit allocation in the combat path is a far worse trade than the small
	/// amount of bookkeeping below.
	/// </para>
	/// <para>
	/// Bounded cleanup is done with a rotating cursor over a key ring instead of a time-ordered
	/// queue, because entries are re-touched constantly and constantly re-ordering a queue is the
	/// very cost being avoided. The ring never has to be exact: a key it still holds for an entry
	/// that has already gone is simply dropped when the cursor next reaches it.
	/// </para>
	/// </remarks>
	public class PartyCombatMeterData : RuntimeDataContainer, IPartyCombatMeterData
	{
		/// <summary>
		/// One character's accumulation for the encounter it is currently in.
		/// </summary>
		private struct Meter
		{
			/// <summary>Unscaled time the current encounter began.</summary>
			public float EncounterStart;
			/// <summary>Unscaled time of the most recent contribution.</summary>
			public float LastContribution;
			/// <summary>Damage dealt since <see cref="EncounterStart"/>.</summary>
			public float Damage;
			/// <summary>Healing done since <see cref="EncounterStart"/>.</summary>
			public float Healing;
		}

		/// <summary>Meters keyed by character ID.</summary>
		private Dictionary<long, Meter> meters;

		/// <summary>Keys in insertion order, for the bounded sweep. May lag <see cref="meters"/>.</summary>
		private List<long> keyRing;

		/// <summary>
		/// Which keys <see cref="keyRing"/> currently holds.
		/// </summary>
		/// <remarks>
		/// The ring is allowed to outlive the meter it names — <see cref="Forget"/> drops a meter
		/// without paying for a scan, and the sweep tidies the orphan up when it next comes round.
		/// That is only safe with a membership set: without one, a character who disconnects and
		/// then lands a hit after reconnecting is not found in <see cref="meters"/>, is treated as
		/// new, and is appended to the ring a SECOND time. The duplicate is never collected,
		/// because both copies name a key whose meter is live, so the ring grows by one entry per
		/// reconnect for the lifetime of the process.
		/// </remarks>
		private HashSet<long> ringKeys;

		/// <summary>Rotating position in <see cref="keyRing"/> where the next sweep resumes.</summary>
		private int sweepCursor;

		/// <inheritdoc />
		public override ServerComponentInitializationStatus InitializeOnce()
		{
			meters = new Dictionary<long, Meter>();
			keyRing = new List<long>();
			ringKeys = new HashSet<long>();
			sweepCursor = 0;
			return ServerComponentInitializationStatus.Initialized;
		}

		/// <inheritdoc />
		public override void Clear()
		{
			meters?.Clear();
			keyRing?.Clear();
			ringKeys?.Clear();
			sweepCursor = 0;
		}

		/// <inheritdoc />
		protected override void OnDeinitialize()
		{
			Clear();
		}

		/// <inheritdoc />
		public void RecordDamage(long characterID, float amount, float now, float encounterTimeoutSeconds)
		{
			Record(characterID, amount, 0.0f, now, encounterTimeoutSeconds);
		}

		/// <inheritdoc />
		public void RecordHealing(long characterID, float amount, float now, float encounterTimeoutSeconds)
		{
			Record(characterID, 0.0f, amount, now, encounterTimeoutSeconds);
		}

		/// <summary>
		/// Adds a contribution, starting a new encounter when the previous one has lapsed.
		/// </summary>
		/// <param name="characterID">The contributing character.</param>
		/// <param name="damage">Damage to add.</param>
		/// <param name="healing">Healing to add.</param>
		/// <param name="now">Current unscaled time, in seconds.</param>
		/// <param name="encounterTimeoutSeconds">Idle time after which an encounter is over.</param>
		private void Record(long characterID, float damage, float healing, float now, float encounterTimeoutSeconds)
		{
			if (meters == null || characterID <= 0)
			{
				return;
			}

			/* Zero and negative contributions are dropped rather than accumulated. A fully
			 * resisted hit or a heal on a character already at full health is not participation in
			 * the fight, and letting either through would refresh the idle timer — keeping an
			 * encounter alive on activity that had no effect. */
			if (damage <= 0.0f && healing <= 0.0f)
			{
				return;
			}

			if (!meters.TryGetValue(characterID, out Meter meter))
			{
				meter = new Meter() { EncounterStart = now };

				// Only if the ring is not already carrying this key; see ringKeys.
				if (ringKeys.Add(characterID))
				{
					keyRing.Add(characterID);
				}
			}
			else if (HasLapsed(meter, now, encounterTimeoutSeconds))
			{
				// Previous encounter is over. This contribution opens a new one from zero.
				meter = new Meter() { EncounterStart = now };
			}

			meter.LastContribution = now;
			meter.Damage += damage;
			meter.Healing += healing;

			meters[characterID] = meter;
		}

		/// <inheritdoc />
		public PartyCombatMeterSample GetSample(long characterID, float now, float encounterTimeoutSeconds, float minimumWindowSeconds)
		{
			if (meters == null ||
				!meters.TryGetValue(characterID, out Meter meter) ||
				HasLapsed(meter, now, encounterTimeoutSeconds))
			{
				return default;
			}

			/* Divided by the time since the encounter STARTED rather than by the span between the
			 * first and last contribution, so a rate visibly decays through a lull instead of
			 * freezing at whatever the last burst averaged. That is what a player reading the
			 * meter expects it to mean, and it is what makes the reading fall to zero on its own
			 * when the encounter finally times out rather than snapping down from a high number. */
			float elapsed = now - meter.EncounterStart;
			if (elapsed < minimumWindowSeconds)
			{
				elapsed = minimumWindowSeconds;
			}
			if (elapsed <= 0.0f)
			{
				return default;
			}

			return new PartyCombatMeterSample(meter.Damage / elapsed, meter.Healing / elapsed);
		}

		/// <inheritdoc />
		public void Forget(long characterID)
		{
			/* The ring entry is deliberately left behind rather than scanned for. It names a key
			 * with no meter, which the sweep recognises and collects on its next pass, and
			 * <see cref="ringKeys"/> is what stops the same key being appended again in the
			 * meantime. Scanning here would make every disconnect walk the ring. */
			meters?.Remove(characterID);
		}

		/// <inheritdoc />
		public int Sweep(float now, float staleAfterSeconds, int maxScan, int maxRemove)
		{
			if (meters == null || keyRing == null || ringKeys == null || keyRing.Count < 1 || maxScan <= 0 || maxRemove <= 0)
			{
				return 0;
			}

			int scanned = 0;
			int removed = 0;

			while (scanned < maxScan && removed < maxRemove && keyRing.Count > 0)
			{
				if (sweepCursor >= keyRing.Count)
				{
					sweepCursor = 0;
				}

				long key = keyRing[sweepCursor];
				++scanned;

				bool drop = !meters.TryGetValue(key, out Meter meter) ||
							now - meter.LastContribution > staleAfterSeconds;

				if (!drop)
				{
					++sweepCursor;
					continue;
				}

				meters.Remove(key);
				ringKeys.Remove(key);

				// Swap-remove; the ring has no ordering to preserve.
				int last = keyRing.Count - 1;
				keyRing[sweepCursor] = keyRing[last];
				keyRing.RemoveAt(last);
				++removed;
			}

			return removed;
		}

		/// <summary>
		/// Reports whether a meter's encounter has gone idle for longer than the timeout.
		/// </summary>
		/// <param name="meter">The meter to test.</param>
		/// <param name="now">Current unscaled time, in seconds.</param>
		/// <param name="encounterTimeoutSeconds">Idle time after which an encounter is over.</param>
		private static bool HasLapsed(Meter meter, float now, float encounterTimeoutSeconds)
		{
			return now - meter.LastContribution > encounterTimeoutSeconds;
		}
	}
}
