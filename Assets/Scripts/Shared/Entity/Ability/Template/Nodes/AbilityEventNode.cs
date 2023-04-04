using UnityEngine;

public abstract class AbilityEventNode : AbilityNode
{
	//public AudioEvent EventSounds;

	public abstract void Invoke(Ability ability, Character self, TargetInfo targetInfo, GameObject abilityObject, Vector3 startPosition);
}