using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces.Actions;

namespace FishMMO.Database.Npgsql.Services.Interfaces
{
	/// <summary>
	/// Service interface for managing character pet attributes.
	/// </summary>
	/// <remarks>
	/// Pet attribute persistence and deletion should be version-gated via the logical <c>Version</c>
	/// so stale updates are rejected and newer authoritative updates win.
	/// </remarks>
	public interface ICharacterPetAttributeService :
		ICountByKeyAction<long>,
		IPersistAction<CharacterPetAttributeData, long>,
		IPersistManyAction<CharacterPetAttributeData>,
		IDeleteByKeyVersionedAction<long>,
		IFetchCollectionByKeyAction<long, CharacterPetAttributeData>
	{
	}
}
