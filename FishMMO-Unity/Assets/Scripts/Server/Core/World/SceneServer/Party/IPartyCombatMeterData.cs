namespace FishMMO.Server.Core.World.SceneServer
{
	/// <summary>
	/// One character's damage and healing rates for the encounter it is currently in.
	/// </summary>
	public readonly struct PartyCombatMeterSample
	{
		/// <summary>Damage dealt this encounter, divided by the encounter's elapsed length.</summary>
		public readonly float DamagePerSecond;

		/// <summary>Healing done this encounter, divided by the encounter's elapsed length.</summary>
		public readonly float HealPerSecond;

		/// <summary>
		/// Initializes a meter sample.
		/// </summary>
		/// <param name="damagePerSecond">Damage dealt per second this encounter.</param>
		/// <param name="healPerSecond">Healing done per second this encounter.</param>
		public PartyCombatMeterSample(float damagePerSecond, float healPerSecond)
		{
			DamagePerSecond = damagePerSecond;
			HealPerSecond = healPerSecond;
		}
	}

	/// <summary>
	/// Per-encounter damage and healing meters for the characters this scene server hosts.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Per encounter, not per session.</b> A meter that never resets stops meaning anything
	/// after the first fight — a player who opened with a burst and then stood still for ten
	/// minutes still reads as the top damage dealer. Every contribution refreshes an idle timer;
	/// once that timer lapses the accumulator is discarded and the next hit starts a fresh
	/// encounter from zero. That is the whole definition of "encounter" here, deliberately: it
	/// needs no notion of which mob was pulled or when a boss reset, and it therefore cannot
	/// disagree with one.
	/// </para>
	/// <para>
	/// <b>Main-thread only.</b> Contributions arrive from the damage controller's static events,
	/// which are raised on the server simulation, and samples are read by the party update pump.
	/// Both run on the main thread, so nothing here synchronises.
	/// </para>
	/// <para>
	/// Kept on the server rather than accumulated per client. The client is shown a rate for
	/// other players, so the number has to be one the server computed; a client-side meter could
	/// only ever measure the fraction of a fight that happened inside its own observer range.
	/// </para>
	/// </remarks>
	public interface IPartyCombatMeterData : IRuntimeDataContainer
	{
		/// <summary>
		/// Credits a character with damage dealt.
		/// </summary>
		/// <param name="characterID">The character that dealt the damage.</param>
		/// <param name="amount">Damage amount; non-positive amounts are ignored.</param>
		/// <param name="now">Current unscaled time, in seconds.</param>
		/// <param name="encounterTimeoutSeconds">Idle time after which an encounter is over.</param>
		void RecordDamage(long characterID, float amount, float now, float encounterTimeoutSeconds);

		/// <summary>
		/// Credits a character with healing done.
		/// </summary>
		/// <param name="characterID">The character that did the healing.</param>
		/// <param name="amount">Healing amount; non-positive amounts are ignored.</param>
		/// <param name="now">Current unscaled time, in seconds.</param>
		/// <param name="encounterTimeoutSeconds">Idle time after which an encounter is over.</param>
		void RecordHealing(long characterID, float amount, float now, float encounterTimeoutSeconds);

		/// <summary>
		/// Reads a character's current rates.
		/// </summary>
		/// <param name="characterID">The character to read.</param>
		/// <param name="now">Current unscaled time, in seconds.</param>
		/// <param name="encounterTimeoutSeconds">Idle time after which an encounter is over.</param>
		/// <param name="minimumWindowSeconds">
		/// Floor on the divisor. Without one, the first hit of a fight divides by very nearly
		/// zero and reports a rate in the millions for one tick.
		/// </param>
		/// <returns>
		/// The character's rates, or a zeroed sample when it has no encounter running — including
		/// when its last encounter has timed out, which is reported as zero rather than as no
		/// reading so a stale number can never survive on a client's meter.
		/// </returns>
		PartyCombatMeterSample GetSample(long characterID, float now, float encounterTimeoutSeconds, float minimumWindowSeconds);

		/// <summary>
		/// Drops a character's meter entirely.
		/// </summary>
		/// <param name="characterID">The character to forget.</param>
		void Forget(long characterID);

		/// <summary>
		/// Removes meters whose encounters have long since ended, up to a bounded count.
		/// </summary>
		/// <param name="now">Current unscaled time, in seconds.</param>
		/// <param name="staleAfterSeconds">Idle time after which an entry is discarded.</param>
		/// <param name="maxScan">Maximum entries to examine.</param>
		/// <param name="maxRemove">Maximum entries to remove.</param>
		/// <returns>The number of entries removed.</returns>
		int Sweep(float now, float staleAfterSeconds, int maxScan, int maxRemove);
	}
}
