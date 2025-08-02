namespace FishMMO.Shared
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
		/// <param name="amount">The amount of damage to apply.</param>
		/// <param name="damageAttribute">The type of damage being applied (e.g., fire, physical).</param>
		/// <param name="ignoreAchievements">If true, achievement progress is not affected by this damage event.</param>
		void Damage(ICharacter attacker, int amount, DamageAttributeTemplate damageAttribute, bool ignoreAchievements = false);
	}
}