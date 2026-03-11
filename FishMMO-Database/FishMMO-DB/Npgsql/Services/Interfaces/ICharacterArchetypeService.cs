using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces.Actions;

namespace FishMMO.Database.Npgsql.Services.Interfaces
{
	/// <summary>
	/// Service interface for managing character archetypes.
	/// </summary>
	/// <remarks>
	/// Archetype persistence and deletion are version-gated via the logical <c>Version</c>
	/// so stale updates are rejected and newer authoritative updates win.
	/// </remarks>
	public interface ICharacterArchetypeService :
		IPersistManyAction<CharacterArchetypeData>,
		IDeleteByKeyVersionedAction<long>,
		IFetchCollectionByKeyAction<long, CharacterArchetypeData>
	{
	}
}