using System.Collections.Generic;
using System.Threading.Tasks;
using FishNet.Connection;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Logging;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Shared;
using FishMMO.Shared.Core;

[assembly: FishMMO.Server.Implementation.World.SceneServer.CharacterPersistence(typeof(FishMMO.Server.Implementation.World.SceneServer.PartyPersistenceParticipant))]

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// Puts a character's party membership on it as the character loads, after dropping one it cannot keep.
	/// </summary>
	/// <remarks>
	/// Load only: a membership is written by <see cref="PartySystem"/> when it changes, not by the character save. This
	/// was hard-coded in <c>CharacterSystem.Loading</c>; a failed read abandons the load, as it did there.
	/// </remarks>
	public sealed class PartyPersistenceParticipant : CharacterPersistenceParticipant
	{
		/// <inheritdoc />
		public override string Name => "party membership";

		/// <inheritdoc />
		public override CharacterRows CreateRows() => new CharacterRows<CharacterPartyData>(row => row.CharacterID);

		/// <inheritdoc />
		/// <remarks>The load calls the overload with the character's row; this one cannot check the world server.</remarks>
		public override Task<CharacterFetch> FetchAsync(ICharacterPersistenceHost host, long characterID)
		{
			return FetchAsync(host, characterID, 0);
		}

		/// <inheritdoc />
		public override Task<CharacterFetch> FetchAsync(ICharacterPersistenceHost host, CharacterData character)
		{
			return FetchAsync(host, character.ID, character.WorldServerID);
		}

		private static async Task<CharacterFetch> FetchAsync(ICharacterPersistenceHost host, long characterID, long worldServerID)
		{
			if (host.Services == null || !host.Services.TryGet<ICharacterPartyService>(out var partyService))
			{
				return CharacterFetch.Ok(null);
			}

			var result = await partyService.FetchAsync(characterID);
			if (!result.IsSuccess)
			{
				return CharacterFetch.Failed(result.ErrorCode.ToString(), result.ErrorMessage);
			}
			CharacterPartyData? membership = result.Data;

			/* A party belongs to one world server, and this character may have
			 * arrived on a different one.
			 *
			 * Characters are global on purpose — the same character can be played on
			 * any world server so that friends can play together wherever they are —
			 * but a party is replicated between scene servers through the party update
			 * pump, and that pump is scoped to a world server. Carrying a membership
			 * across would produce a party half of whose members are updated by a pump
			 * the other half cannot see: rosters that never converge, invitations to
			 * instances that do not exist on the other side, and a leader nobody can
			 * reach. Every one of those is a bug that only appears once somebody
			 * changes shard, which is the worst kind to diagnose.
			 *
			 * So the membership is dropped here, at the moment of arrival, before any
			 * of it is applied to the character. Dropped rather than migrated: the
			 * party is still live on the world server it belongs to, and pulling it
			 * across would take its other members with it.
			 */
			if (worldServerID != 0 &&
				membership.HasValue &&
				membership.Value.PartyID > 0 &&
				host.Services.TryGet<IPartyService>(out var partyLookupService))
			{
				var partyResult = await partyLookupService.FetchAsync(membership.Value.PartyID);

				if (!partyResult.IsSuccess)
				{
					/* Could not read the party. The membership is left alone: a
					 * transient database fault is not evidence that the character has
					 * changed shard, and evicting on one would break up parties for
					 * no reason. If it really is a cross-server membership it will be
					 * caught on the next load. */
					await Log.Warning("PartySystem",
						$"Could not read party {membership.Value.PartyID} to check its world server for character {characterID}; leaving the membership in place.");
				}
				else if (partyResult.Data == null)
				{
					// The party is gone. Nothing to belong to.
					await DropAsync(host, membership.Value, "the party no longer exists");
					membership = null;
				}
				else if (partyResult.Data.Value.WorldServerID != 0 &&
						 partyResult.Data.Value.WorldServerID != worldServerID)
				{
					await DropAsync(host, membership.Value,
						$"it belongs to world server {partyResult.Data.Value.WorldServerID}, not {worldServerID}");
					membership = null;
				}
			}

			return CharacterFetch.Ok(membership);
		}

		/// <summary>
		/// Drops a party membership this character cannot keep, during load.
		/// </summary>
		/// <remarks>
		/// Delegated to the party system rather than deleting the row here, because removing a
		/// member is not a single delete: leadership has to move first if the leaver led, and the
		/// party itself has to be retired if nobody is left. Doing it inline would be a second,
		/// divergent copy of rules that already exist.
		/// <para>
		/// A missing party system is not fatal to the load. The membership stays in the database
		/// and the character simply loads without it applied, which is the same outcome the caller
		/// wanted; the next load will try again.
		/// </para>
		/// <para>
		/// The result is deliberately not acted on. This runs while the character is still loading
		/// and the membership is dropped from the load either way; a refusal leaves a row that this
		/// same check finds and deletes on the character's next load, and the party system has
		/// already logged it.
		/// </para>
		/// </remarks>
		private static async Task DropAsync(ICharacterPersistenceHost host, CharacterPartyData membership, string reason)
		{
			if (!host.TryGetSystem(out IPartySystem<NetworkConnection> partySystem))
			{
				await Log.Warning("PartySystem",
					$"Character {membership.CharacterID} should have been removed from party {membership.PartyID} ({reason}) but the party system is unavailable.");
				return;
			}

			await partySystem.RemoveCharacterFromPartyAsync(membership.CharacterID, membership.PartyID, reason);
		}

		/// <inheritdoc />
		public override void Apply(ICharacter character, object fetched)
		{
			if (!(fetched is CharacterPartyData membership) || !character.TryGet(out IPartyMembership controller))
			{
				return;
			}
			controller.ID = membership.PartyID;
			controller.Rank = (PartyRank)membership.Rank;
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
