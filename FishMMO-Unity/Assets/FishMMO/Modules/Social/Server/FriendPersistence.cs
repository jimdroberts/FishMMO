using System.Collections.Generic;
using System.Threading.Tasks;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Shared.Core;

[assembly: FishMMO.Server.Implementation.World.SceneServer.CharacterPersistence(typeof(FishMMO.Server.Implementation.World.SceneServer.FriendPersistenceParticipant))]

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// Loads a character's friend list into its <see cref="IFriendController"/> as the character loads.
	/// </summary>
	/// <remarks>
	/// Load only. A friendship is written the moment it is made or ended (<see cref="FriendSystem"/>), not by the
	/// character save, so there is nothing to capture. The load used to be hard-coded in <c>CharacterSystem.Loading</c>;
	/// a failed read abandons the load, as it did there.
	/// </remarks>
	public sealed class FriendPersistenceParticipant : CharacterPersistenceParticipant
	{
		/// <inheritdoc />
		public override string Name => "friends";

		/// <inheritdoc />
		public override CharacterRows CreateRows() => new CharacterRows<CharacterFriendData>(row => row.CharacterID);

		/// <inheritdoc />
		public override async Task<CharacterFetch> FetchAsync(ICharacterPersistenceHost host, long characterID)
		{
			if (host.Services == null || !host.Services.TryGet<ICharacterFriendService>(out var friendService))
			{
				return CharacterFetch.Ok(null);
			}
			var result = await friendService.FetchAsync(characterID);
			return result.IsSuccess
				? CharacterFetch.Ok(result.Data)
				: CharacterFetch.Failed(result.ErrorCode.ToString(), result.ErrorMessage);
		}

		/// <inheritdoc />
		public override void Apply(ICharacter character, object fetched)
		{
			if (!(fetched is IReadOnlyList<CharacterFriendData> friends) || friends.Count == 0 ||
				!character.TryGet(out IFriendController controller))
			{
				return;
			}
			foreach (CharacterFriendData friend in friends)
			{
				controller.AddFriend(friend.FriendCharacterID);
			}
		}

		/// <inheritdoc />
		public override void Capture(ICharacter character, CharacterRows rows)
		{
		}

		/// <inheritdoc />
		public override Task<bool> SaveAsync(ICharacterPersistenceHost host, CharacterRows rows, IReadOnlyCollection<CharacterSessionLeaseData> claims)
		{
			return Task.FromResult(true);
		}
	}
}
