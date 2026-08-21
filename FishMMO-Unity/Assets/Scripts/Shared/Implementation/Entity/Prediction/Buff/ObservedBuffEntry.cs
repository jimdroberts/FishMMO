namespace FishMMO.Shared
{
	/// <summary>
	/// One buff on another character, as the SERVER has chosen to show it to observers.
	/// Display-only: nothing on the client applies an effect from this.
	/// </summary>
	/// <remarks>
	/// <para>
	/// This is deliberately not <see cref="Buff"/>. A <see cref="Buff"/> is simulation state — it
	/// carries expiry in the owner's replicate-tick domain, a stack count that drives attribute
	/// modifiers, and a template whose <c>OnApply</c>/<c>OnRemove</c> mutate the character. Handing
	/// that to an observer would either desynchronise the observer's own prediction domain (ticks
	/// mean different things on different clients) or, worse, let a client run buff effects on a
	/// character it does not own.
	/// </para>
	/// <para>
	/// Duration travels as SECONDS remaining at the moment the server sent it, not as an absolute
	/// tick, precisely because the receiving client's tick domain is its own. The observer counts
	/// down locally from receipt. That drifts by the one-way latency, which for a bar a few pixels
	/// tall on someone else's nameplate is not worth a tick-domain translation.
	/// </para>
	/// </remarks>
	public struct ObservedBuffEntry
	{
		/// <summary>The buff template's cached ID.</summary>
		public int TemplateID;

		/// <summary>Stack count above the base application (0 = one application).</summary>
		public int Stacks;

		/// <summary>Seconds remaining when the server sent this, or 0 for a permanent buff.</summary>
		public float RemainingSeconds;

		/// <summary>The buff's full duration in seconds, or 0 for a permanent buff.</summary>
		public float TotalSeconds;
	}
}
