namespace FishMMO.Shared
{
	public abstract class MoveEvent : AbilityEvent
	{
		public abstract void Invoke(AbilityObject abilityObject, float deltaTime);
	}
}