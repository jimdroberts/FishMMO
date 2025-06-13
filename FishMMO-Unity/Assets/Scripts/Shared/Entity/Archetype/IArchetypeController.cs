namespace FishMMO.Shared
{
	public interface IArchetypeController : ICharacterBehaviour
	{
		ArchetypeTemplate Template { get; }
	}
}