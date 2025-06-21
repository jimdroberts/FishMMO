namespace FishMMO.Shared
{
	public abstract class BaseAction : CachedScriptableObject<BaseAction>, ICachedObject, IAction
	{
		public abstract void Execute(ICharacter initiator, EventData eventData);
	}
}