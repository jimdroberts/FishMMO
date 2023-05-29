using UnityEngine;

public abstract class AbilityEvent
{
	/// <summary>
	/// Invokes the functionality of the event.
	/// </summary>
	/// <param name="ability">A copy of the ability. (Todo)</param>
	/// <param name="self">The character that activated the ability.</param>
	/// <param name="other">The target or attacking characters information.</param>
	/// <param name="abilityObject">Used to reference a previous ability object. Used for chaining abilities or doing continuous effects with a single ability.</param>
	public abstract void Invoke(Ability ability, Character self, TargetInfo other, GameObject abilityObject);
}