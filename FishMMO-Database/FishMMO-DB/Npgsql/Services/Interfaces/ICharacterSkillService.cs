using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces.Actions;

namespace FishMMO.Database.Npgsql.Services.Interfaces
{
	/// <summary>
	/// Service interface for managing character skills.
	/// </summary>
	/// <remarks>
	/// Skill persistence and deletion should be version-gated via the logical <c>Version</c>
	/// so stale updates are rejected and newer authoritative updates win.
	/// </remarks>
	public interface ICharacterSkillService :
		ICountByKeyAction<long>,
		IPersistAction<CharacterSkillData, long>,
		IPersistManyAction<CharacterSkillData>,
		IDeleteByKeyVersionedAction<long>,
		IFetchCollectionByKeyAction<long, CharacterSkillData>
	{
	}
}
