namespace FishMMO.Shared.Core
{
	/// <summary>
	/// Interface for entities that can receive damage from attacks.
	/// Implement this to allow objects to be damaged by characters or other sources.
	/// </summary>
	public interface IDamageable
	{
		/// <summary>
		/// Applies damage to the entity.
		/// </summary>
		/// <param name="attacker">The character dealing the damage.</param>
		/// <param name="amount">The amount of damage to apply, before resistances and mitigation.</param>
		/// <param name="damageAttribute">The type of damage being applied (e.g., fire, physical).</param>
		/// <param name="ignoreAchievements">If true, achievement progress is not affected by this damage event.</param>
		/// <param name="periodic">
		/// True for a damage-over-time tick rather than a direct hit. Reported as
		/// <c>CombatEventKind.PeriodicDamage</c> so it coalesces and pairs separately from direct
		/// hits — the caster's client predicts direct hits but never DoT ticks, and a periodic
		/// report of the same type must not consume a direct hit's pending prediction.
		/// </param>
		/// <returns>
		/// The amount that actually landed, after resistances and mitigation — the same number the
		/// server reports in its combat event. Zero when nothing landed (immortal, already dead,
		/// fully resisted or fully blocked). A caller that displays a predicted number must display
		/// THIS value, not the raw amount it passed in, or the caster's own screen shows a
		/// pre-mitigation number the server's report never corrects.
		/// </returns>
		int Damage(ICharacter attacker, int amount, DamageAttributeTemplate damageAttribute, bool ignoreAchievements = false, bool periodic = false);
	}
}