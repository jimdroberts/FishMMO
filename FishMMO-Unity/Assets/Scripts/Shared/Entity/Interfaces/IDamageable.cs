namespace FishMMO.Shared
{
	public interface IDamageable
	{
		public void Damage(ICharacter attacker, int amount, DamageAttributeTemplate damageAttribute, bool ignoreAchievements = false);
	}
}