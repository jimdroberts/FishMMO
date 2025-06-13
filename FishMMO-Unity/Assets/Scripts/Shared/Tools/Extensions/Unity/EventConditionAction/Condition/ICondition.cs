namespace FishMMO.Shared
{
    public interface ICondition
    {
        bool Evaluate(ICharacter initiator, EventData eventData);
    }
}