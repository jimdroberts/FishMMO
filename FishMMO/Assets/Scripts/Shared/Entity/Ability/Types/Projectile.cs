using UnityEngine;

public class Projectile : MonoBehaviour
{
	public AbilityNode ForkAbilityNode;
	public Ability Ability;
	public Character Caster;

	public void OnCollisionEnter(Collision other)
	{
		Character hitCharacter = other.gameObject.GetComponent<Character>();
		if (hitCharacter != null)
		{
			if (Ability != null)
			{
				Ability.Hit(Caster, new TargetInfo()
				{
					target = other.transform,
					hitPosition = other.GetContact(0).point,
				}, gameObject);

				// fork - randomly redirects the projectile towards a random direction
				if (Ability.HasAbilityNode(ForkAbilityNode))
				{
					transform.rotation = transform.forward.GetRandomConicalDirection(transform.position, 180.0f, 60.0f);
				}
			}
			else
			{
				Destroy(gameObject);
			}
		}
	}
}