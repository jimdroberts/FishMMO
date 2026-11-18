namespace FishMMO.Shared.Core
{
	/// <summary>
	/// Server-side interface for shrine interactables.
	/// Exposes the shrine template needed for healing and buff application.
	/// </summary>
	public interface IShrine : IInteractable
	{
		/// <summary>
		/// The shrine template defining heal percentages, buff grants, and other shrine effects.
		/// </summary>
		ShrineTemplate Template { get; }

		/// <summary>
		/// Achievement template to increment when a player uses this shrine.
		/// </summary>
		AchievementTemplate AchievementTemplate { get; }

		/// <summary>
		/// Seconds remaining before the given character may use this shrine again.
		/// </summary>
		/// <remarks>
		/// A pure query, and the value the refusal reply carries so the client can say how long is
		/// left rather than merely that nothing happened. Answers 0 on a client, which holds no
		/// cooldown table — the server is the only peer that knows.
		/// </remarks>
		/// <param name="characterID">The character to test.</param>
		/// <returns>Seconds remaining, or 0 when the shrine is ready for that character.</returns>
		float GetRemainingCooldown(long characterID);

		/// <summary>
		/// Spends the character's shrine cooldown, returning false when it is still running.
		/// </summary>
		/// <remarks>
		/// Separate from <see cref="IInteractable.CanInteract"/>, which tests the same limiter
		/// without spending it — the same split, and for the same reason, as
		/// <see cref="IInteractable.TryConsumeInteractRateLimit"/>: several callers ask the
		/// question and only one of them is entitled to consume the answer.
		/// </remarks>
		/// <param name="characterID">The character using the shrine.</param>
		/// <returns>True when the shrine was ready and the cooldown has now started.</returns>
		bool TryConsumeCooldown(long characterID);
	}
}