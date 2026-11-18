using System.Collections.Generic;
using FishNet.Connection;
using FishMMO.Database;
using FishMMO.Database.Data;
using FishMMO.Server.Core.World.SceneServer;

namespace FishMMO.Server.Implementation
{
	public abstract partial class ServerBehaviour
	{
		/// <summary>
		/// Captures the session claim this server holds for a character, for a write made on that
		/// character's behalf. Main thread only.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>Every per-character write quotes the claim it was captured under</b> — the services'
		/// owned writes (<c>PersistOwnedAsync</c>, <c>DeleteAbilityOwnedAsync</c>,
		/// <c>DeleteQuestOwnedAsync</c>) admit a row only while that claim is still held, under the
		/// character's share lock (<c>CharacterWriteGate</c>). A write captured by a session that is
		/// released a moment later therefore cannot land over the next owner's state, an offline
		/// debit, or a trade's last settlement.
		/// </para>
		/// <para>
		/// <b>Captured here, at the request, and carried with the write.</b> Looked up later, on the
		/// worker, it could be a later session's claim — the character left and came back — and the
		/// write would land under a claim it was never made under. The departure paths take the
		/// claim out of <c>SessionTokens</c> before anything else runs, so a character on its way
		/// out has none: its own rows travel inside the save-and-release, which carries the claim
		/// explicitly (<c>CharacterSystem.SaveAndDespawnCharacter</c>).
		/// </para>
		/// <para>
		/// A resident character — connected, or a combat-logout body — always holds one, so false
		/// means the character is not ours to write: it has left, is leaving, or was evicted. The
		/// caller refuses the request rather than change memory it could not persist.
		/// </para>
		/// </remarks>
		/// <param name="characterID">The character the write is for.</param>
		/// <param name="claim">The claim, when this server holds one.</param>
		/// <returns>True when a usable claim was captured.</returns>
		protected bool TryCaptureSessionClaim(long characterID, out CharacterSessionLeaseData claim)
		{
			claim = default;
			if (characterID <= 0 ||
				Server?.DataContainerRegistry == null ||
				!Server.DataContainerRegistry.TryGet(out ICharacterMappingData<NetworkConnection> mappingData) ||
				mappingData.SessionTokens == null ||
				!mappingData.SessionTokens.TryGetValue(characterID, out CharacterSessionInfo held))
			{
				return false;
			}

			claim = new CharacterSessionLeaseData(characterID, held.ServerID, held.Token);
			return claim.IsValid;
		}

		/// <summary>
		/// One claim as the collection a batched owned write takes. Pure.
		/// </summary>
		/// <param name="claim">The claim.</param>
		/// <returns>A single-element collection.</returns>
		protected static IReadOnlyCollection<CharacterSessionLeaseData> ClaimsOf(CharacterSessionLeaseData claim)
		{
			return new[] { claim };
		}

		/// <summary>
		/// Whether an owned write failed because the claim it quoted is no longer this server's.
		/// Pure.
		/// </summary>
		/// <remarks>
		/// Final: the character belongs to another session now — or to none, after a release — and
		/// retrying would only be refused again. It is also rare, since only a lost claim produces
		/// it, and the row save and the lease refresh evict such a character within one interval.
		/// </remarks>
		/// <param name="result">The failed write.</param>
		/// <returns>True for a refusal by the ownership gate.</returns>
		public static bool IsClaimRefusal(DatabaseResult result)
		{
			return !result.IsSuccess && result.ErrorCode == DatabaseErrorCodes.Forbidden;
		}

		/// <inheritdoc cref="IsClaimRefusal(DatabaseResult)"/>
		public static bool IsClaimRefusal<T>(DatabaseResult<T> result)
		{
			return !result.IsSuccess && result.ErrorCode == DatabaseErrorCodes.Forbidden;
		}
	}
}
