namespace FishMMO.Shared
{
	public interface ICharacterBehaviour
	{
		Character Character { get; }
		bool Initialized { get; }
	}
}