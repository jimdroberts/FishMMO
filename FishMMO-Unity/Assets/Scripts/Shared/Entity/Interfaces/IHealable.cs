namespace FishMMO.Shared
{
	/// <summary>
	/// Interface for entities that can be healed by a character or other source.
	/// Implement this to allow objects to receive healing.
	/// </summary>
	public interface IHealable
	{
		/// <summary>
		/// Applies healing to the entity.
		/// </summary>
		/// <param name="healer">The character performing the healing.</param>
		/// <param name="amount">The amount of healing to apply.</param>
		/// <param name="ignoreAchievements">If true, achievement progress is not affected by this healing event.</param>
		void Heal(ICharacter healer, int amount, bool ignoreAchievements = false);
	}
}