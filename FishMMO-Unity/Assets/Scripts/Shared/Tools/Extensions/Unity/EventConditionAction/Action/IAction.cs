namespace FishMMO.Shared
{
	public interface IAction
	{
		void Execute(ICharacter initiator, EventData eventData);
	}
}