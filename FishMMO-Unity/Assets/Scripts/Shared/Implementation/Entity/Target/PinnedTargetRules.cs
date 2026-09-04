namespace FishMMO.Shared
{
	/// <summary>
	/// The rule that decides when a pinned target is released, as arithmetic on facts about it.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A pin is a promise the frame makes to the player: "this card stays up until you let go".
	/// The only things that break the promise are the target ceasing to be something a card can
	/// describe — destroyed or despawned on this client, or dead — and the target leaving the
	/// distance inside which targeting means anything at all. Nothing else does: not the pointer
	/// moving, not a panel opening, not the pinned character walking behind a wall.
	/// </para>
	/// <para>
	/// Static and free of Unity types so the truth table can be pinned by a plain unit test.
	/// <c>TargetController</c> gathers the facts and asks; nothing else decides.
	/// </para>
	/// </remarks>
	public static class PinnedTargetRules
	{
		/// <summary>
		/// Distance beyond which a pin is released, in metres.
		/// </summary>
		/// <remarks>
		/// Deliberately wider than <c>TargetController.MAX_TARGET_DISTANCE</c>, the acquisition
		/// range. A character pinned at the edge of acquisition range and drifting a step further
		/// away would otherwise lose its card at once, and a target that is being chased is, by
		/// definition, one that is trying to get away. The gap is the hysteresis.
		/// </remarks>
		public const float RELEASE_DISTANCE = 75.0f;

		/// <summary>
		/// Decides whether a pinned target must be released.
		/// </summary>
		/// <param name="isDestroyed">True when the target's transform no longer exists on this client.</param>
		/// <param name="isSpawned">True when the target's network object is still spawned on this client.</param>
		/// <param name="isAlive">True when the target is alive, or has no health to lose.</param>
		/// <param name="sqrDistance">Squared distance from the local player to the target, in metres squared.</param>
		/// <param name="releaseDistance">The release distance, in metres; zero or less means no distance limit.</param>
		/// <returns>True when the pin must be released.</returns>
		public static bool ShouldRelease(bool isDestroyed, bool isSpawned, bool isAlive, float sqrDistance, float releaseDistance)
		{
			if (isDestroyed || !isSpawned)
			{
				return true;
			}

			/* Death releases the pin rather than leaving a card at zero. The card exists to
			 * follow a fight; once the fight is over the corpse is a loot interaction, which is
			 * the hover frame's job, and a pinned corpse would sit at the top of the screen until
			 * the player remembered to clear it. */
			if (!isAlive)
			{
				return true;
			}

			if (releaseDistance <= 0.0f || float.IsNaN(releaseDistance))
			{
				return false;
			}

			return sqrDistance > releaseDistance * releaseDistance;
		}
	}
}
