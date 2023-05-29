using UnityEngine;

public class AbilityProjectileEvent : AbilityEvent
{
	public GameObject ProjectilePrefab;
	public float Spread = 60;
	public float Distance = 60;
	public AbilityNode LesserMultipleProjectileNode;
	public AbilityNode MultipleProjectileNode;
	public AbilityNode GreaterMultipleProjectileNode;

	public override void Invoke(Ability ability, Character self, TargetInfo other, GameObject abilityObject)
	{
		if (ability == null) return;
		if (self == null) return;

		int spawnCount = 1;

		// multiple projectile nodes increase spawn count
		if (ability.HasAbilityNode(LesserMultipleProjectileNode))
		{
			spawnCount += 1;
		}
		if (ability.HasAbilityNode(MultipleProjectileNode))
		{
			spawnCount += 2;
		}
		if (ability.HasAbilityNode(GreaterMultipleProjectileNode))
		{
			spawnCount += 3;
		}

		for (int i = 0; i < spawnCount; ++i)
		{
			// apply spread to projectile rotation if there are more than one projectiles
			Quaternion spread = self.transform.rotation;
			if (spawnCount > 1)
			{
				spread = self.transform.forward.GetRandomConicalDirection(self.AbilitySpawnPoint.position, Spread, Distance);
			}

			Projectile projectile = SpawnProjectile(self.AbilitySpawnPoint.position, spread);
			if (projectile != null)
			{
				projectile.Ability = ability;
				projectile.Caster = self;
				projectile.gameObject.SetActive(true);
			}
		}
	}

	private Projectile SpawnProjectile(Vector3 position, Quaternion rotation)
	{
		GameObject gob = GameObject.Instantiate(ProjectilePrefab, position, rotation);
		if (gob != null)
		{
			return gob.GetComponent<Projectile>();
		}
		return null;
	}
}