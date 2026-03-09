using System.Runtime.CompilerServices;

namespace FishMMO.Shared
{
	/// <summary>
	/// Immutable cooldown instance using <see cref="StartTick"/> + <see cref="DurationTicks"/>.
	/// Remaining time is computed as <c>DurationTicks - (currentTick - StartTick)</c>,
	/// which is perfectly deterministic across client and server with zero float drift.
	/// No per-tick mutation is needed — cooldowns auto-expire via integer comparison.
	/// </summary>
	/// <remarks>
	/// This replaces the previous SubtractTick() model. Because the struct is immutable
	/// after construction, it is safe during reconcile replay — the Replicate loop does
	/// not need to tick cooldowns at all, eliminating the double-subtract risk entirely.
	/// Reconcile only needs to transmit (StartTick, DurationTicks) which never change.
	/// </remarks>
	public readonly struct CooldownInstance
	{
		/// <summary>
		/// The network tick at which this cooldown was started.
		/// </summary>
		public uint StartTick { get; }

		/// <summary>
		/// The cooldown duration in ticks. Immutable after construction.
		/// </summary>
		public uint DurationTicks { get; }

		/// <summary>
		/// The fixed time step per tick, cached for UI-facing time conversions.
		/// </summary>
		private readonly float tickDelta;

		/// <summary>
		/// The total cooldown duration in seconds, for UI display.
		/// </summary>
		public float TotalTime => DurationTicks * tickDelta;

		/// <summary>
		/// Computes remaining time in seconds for the given current tick.
		/// Returns 0 if the cooldown has expired.
		/// </summary>
		/// <param name="currentTick">The current network tick.</param>
		/// <returns>Remaining cooldown time in seconds, clamped to zero.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public float RemainingTime(uint currentTick)
		{
			uint remaining = RemainingTicks(currentTick);
			return remaining * tickDelta;
		}

		/// <summary>
		/// Computes remaining ticks for the given current tick.
		/// Returns 0 if the cooldown has expired.
		/// </summary>
		/// <param name="currentTick">The current network tick.</param>
		/// <returns>Remaining cooldown in ticks, clamped to zero.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public uint RemainingTicks(uint currentTick)
		{
			uint elapsed = currentTick - StartTick;
			return elapsed >= DurationTicks ? 0u : DurationTicks - elapsed;
		}

		/// <summary>
		/// Whether the cooldown is still active at the given tick.
		/// </summary>
		/// <param name="currentTick">The current network tick.</param>
		/// <returns>True if the cooldown has not yet expired.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool IsOnCooldown(uint currentTick)
		{
			return (currentTick - StartTick) < DurationTicks;
		}

		/// <summary>
		/// Initializes a cooldown starting at the given tick with a duration in seconds.
		/// Duration is converted to ticks via <c>ceil(duration / tickDelta)</c>.
		/// </summary>
		/// <param name="startTick">The network tick at which the cooldown begins.</param>
		/// <param name="durationSeconds">Cooldown duration in seconds.</param>
		/// <param name="tickDelta">Fixed time step per tick.</param>
		public CooldownInstance(uint startTick, float durationSeconds, float tickDelta)
		{
			StartTick = startTick;
			this.tickDelta = tickDelta;
			DurationTicks = tickDelta > 0f ? (uint)System.Math.Ceiling(durationSeconds / tickDelta) : 0u;
		}

		/// <summary>
		/// Initializes a cooldown from pre-computed tick values (deserialization / reconcile).
		/// </summary>
		/// <param name="startTick">The tick at which the cooldown began.</param>
		/// <param name="durationTicks">Total duration in ticks.</param>
		/// <param name="tickDelta">Fixed time step per tick (for UI conversion).</param>
		public CooldownInstance(uint startTick, uint durationTicks, float tickDelta)
		{
			StartTick = startTick;
			DurationTicks = durationTicks;
			this.tickDelta = tickDelta;
		}

		/// <summary>
		/// Creates a cooldown from total and remaining durations in seconds.
		/// Computes <see cref="StartTick"/> backwards from <paramref name="currentTick"/>
		/// so that <c>RemainingTicks(currentTick)</c> equals the expected remaining value.
		/// Used when restoring cooldown state from database or network payloads that store
		/// seconds rather than ticks.
		/// </summary>
		/// <param name="currentTick">The current network tick.</param>
		/// <param name="totalDurationSeconds">Total cooldown duration in seconds.</param>
		/// <param name="remainingDurationSeconds">Remaining cooldown duration in seconds.</param>
		/// <param name="tickDelta">Fixed time step per tick.</param>
		/// <returns>A cooldown instance with <see cref="StartTick"/> derived so remaining time is correct.</returns>
		public static CooldownInstance FromRemainingSeconds(uint currentTick, float totalDurationSeconds, float remainingDurationSeconds, float tickDelta)
		{
			uint durationTicks = tickDelta > 0f ? (uint)System.Math.Ceiling(totalDurationSeconds / tickDelta) : 0u;
			uint remainingTicks = tickDelta > 0f ? (uint)System.Math.Ceiling(remainingDurationSeconds / tickDelta) : 0u;
			uint elapsed = durationTicks > remainingTicks ? durationTicks - remainingTicks : 0u;
			uint startTick = currentTick - elapsed;
			return new CooldownInstance(startTick, durationTicks, tickDelta);
		}
	}
}