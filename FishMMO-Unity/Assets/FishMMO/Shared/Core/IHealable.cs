namespace FishMMO.Shared.Core
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
		/// <param name="periodic">
		/// True for a heal-over-time tick. Reported as <c>CombatEventKind.PeriodicHeal</c> so it
		/// pairs separately from direct heals — see <c>IDamageable.Damage</c>'s periodic remarks.
		/// </param>
		/// <returns>
		/// The amount the server would report for this heal — the requested amount when it had any
		/// effect, zero when it had none (dead target, empty amount, or the resource was already
		/// full). Matches the combat-event amount so a predicted label paired against the report is
		/// the same number.
		/// </returns>
		int Heal(ICharacter healer, int amount, bool ignoreAchievements = false, bool periodic = false);
	}
}