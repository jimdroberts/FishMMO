using System.Collections.Generic;
using System.Threading.Tasks;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Shared;
using FishMMO.Shared.Core;

[assembly: FishMMO.Server.Implementation.World.SceneServer.CharacterPersistence(typeof(FishMMO.Server.Implementation.World.SceneServer.GuildPersistenceParticipant))]

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// Puts a character's guild membership on it as the character loads.
	/// </summary>
	/// <remarks>
	/// Load only: a membership is written by <see cref="GuildSystem"/> when it changes, not by the character save. This
	/// was hard-coded in <c>CharacterSystem.Loading</c>; a failed read abandons the load, as it did there.
	/// </remarks>
	public sealed class GuildPersistenceParticipant : CharacterPersistenceParticipant
	{
		/// <inheritdoc />
		public override string Name => "guild membership";

		/// <inheritdoc />
		public override CharacterRows CreateRows() => new CharacterRows<CharacterGuildData>(row => row.CharacterID);

		/// <inheritdoc />
		public override async Task<CharacterFetch> FetchAsync(ICharacterPersistenceHost host, long characterID)
		{
			if (host.Services == null || !host.Services.TryGet<ICharacterGuildService>(out var guildService))
			{
				return CharacterFetch.Ok(null);
			}
			var result = await guildService.FetchAsync(characterID);
			return result.IsSuccess
				? CharacterFetch.Ok(result.Data)
				: CharacterFetch.Failed(result.ErrorCode.ToString(), result.ErrorMessage);
		}

		/// <inheritdoc />
		public override void Apply(ICharacter character, object fetched)
		{
			if (!(fetched is CharacterGuildData membership) || !character.TryGet(out IGuildMembership controller))
			{
				return;
			}
			controller.ID = membership.GuildID;
			/* The ladder POSITION, as stored. What that position is allowed to do is not
			 * knowable from the membership row alone — it depends on the guild's own rank
			 * rows — so the permission mask stays empty until GuildSystem resolves and
			 * publishes it. Empty is the safe direction: the pre-filters that read this mask
			 * refuse, and every guild operation re-resolves before acting anyway. */
			controller.RankOrder = membership.Rank;
			controller.Permissions = GuildPermissions.None;
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
