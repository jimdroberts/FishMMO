using FishNet.Connection;
using FishNet.Transporting;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FishMMO.Database;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Server.Core;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Shared.Core;
using FishMMO.Shared;
using FishMMO.Logging;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// Editable guild ranks (E3) and member notes (E6): the request handlers.
	/// </summary>
	/// <remarks>
	/// Every handler here follows the same shape as the ones in <c>GuildSystem.cs</c>: a cheap
	/// main-thread pre-filter against the controller's cached mask, then an async path that
	/// re-resolves the requester's standing from the guild's own rank rows and decides there.
	/// </remarks>
	public partial class GuildSystem
	{
		/// <summary>
		/// Handles a request for the guild's rank ladder.
		/// </summary>
		/// <param name="conn">The requesting connection.</param>
		/// <param name="msg">The request. Carries nothing.</param>
		/// <param name="channel">Network channel used for the broadcast.</param>
		/// <remarks>
		/// Any member may read the ladder — a player cannot be expected to work within rules they
		/// are not allowed to see, and the permission masks in it are already implied by which
		/// buttons every other member visibly has.
		/// </remarks>
		public void OnServerGuildRankListRequestBroadcastReceived(NetworkConnection conn, GuildRankListRequestBroadcast msg, Channel channel)
		{
			if (!TryBeginGuildRequest(conn, IngressOperation.RankList, out long guardKey, out IGuildController guildController))
			{
				return;
			}

			bool deferGuardRelease = false;
			try
			{
				long guildID = guildController.ID;
				long characterID = guildController.Character.ID;

				deferGuardRelease = TryEnqueueIngressWork(async () =>
				{
					GuildAuthority authority = await ResolveGuildAuthorityAsync(guildID, characterID);
					SendGuildRankList(conn, authority);
				}, guardKey, characterID);

				if (!deferGuardRelease) SendServerBusy(conn);
			}
			finally
			{
				if (!deferGuardRelease)
				{
					EndIngressGuard(guardKey);
				}
			}
		}

		/// <summary>
		/// Handles a request to rename a rank or change its permissions.
		/// </summary>
		/// <param name="conn">The requesting connection.</param>
		/// <param name="msg">The requested name and mask.</param>
		/// <param name="channel">Network channel used for the broadcast.</param>
		public void OnServerGuildEditRankBroadcastReceived(NetworkConnection conn, GuildEditRankBroadcast msg, Channel channel)
		{
			if (!TryBeginGuildRequest(conn, IngressOperation.EditRank, out long guardKey, out IGuildController guildController))
			{
				return;
			}

			bool deferGuardRelease = false;
			try
			{
				if (!guildController.HasGuildPermission(GuildPermissions.EditRanks))
				{
					SendGuildResult(conn, GuildResultType.InsufficientRank);
					return;
				}

				long guildID = guildController.ID;
				long characterID = guildController.Character.ID;
				byte rankOrder = msg.RankOrder;
				string requestedName = msg.Name;
				long requestedPermissions = msg.Permissions;

				deferGuardRelease = TryEnqueueIngressWork(
					() => EditGuildRankAsync(conn, guildID, characterID, rankOrder, requestedName, requestedPermissions),
					guardKey,
					guildID);

				if (!deferGuardRelease) SendServerBusy(conn);
			}
			finally
			{
				if (!deferGuardRelease)
				{
					EndIngressGuard(guardKey);
				}
			}
		}

		/// <summary>
		/// Applies an edit to one rank, after re-establishing every rule server-side.
		/// </summary>
		/// <param name="conn">The requesting connection, for feedback.</param>
		/// <param name="guildID">The guild.</param>
		/// <param name="editorCharacterID">The editing character.</param>
		/// <param name="rankOrder">The rank being edited.</param>
		/// <param name="requestedName">The proposed name.</param>
		/// <param name="requestedPermissions">The proposed mask.</param>
		/// <returns>Asynchronous edit task.</returns>
		/// <remarks>
		/// <para>
		/// Five rules, and every one of them is load-bearing:
		/// </para>
		/// <list type="number">
		/// <item>the editor must hold <c>EditRanks</c>;</item>
		/// <item>the rank must exist;</item>
		/// <item>the rank must be STRICTLY BELOW the editor's own — otherwise an officer with
		/// <c>EditRanks</c> could rewrite the leader's rank, or their own, and grant themselves
		/// anything;</item>
		/// <item>the editor may not grant a permission they do not themselves hold — the classic
		/// privilege-escalation move, and the one that makes <c>EditRanks</c> otherwise equivalent
		/// to every permission at once;</item>
		/// <item>the last rank holding <c>EditRanks</c> may not give it up, which would soft-lock
		/// the guild permanently.</item>
		/// </list>
		/// <para>
		/// Rule 4 is enforced by MASKING rather than by refusing, for the bits the editor holds,
		/// and refusing outright for the ones they do not: silently dropping a bit would make the
		/// resulting rank differ from what the editor asked for with no explanation.
		/// </para>
		/// </remarks>
		private async Task EditGuildRankAsync(NetworkConnection conn, long guildID, long editorCharacterID, byte rankOrder, string requestedName, long requestedPermissions)
		{
			try
			{
				if (!TryGetDbService(out IGuildRankService rankService))
				{
					return;
				}

				GuildAuthority editor = await ResolveGuildAuthorityAsync(guildID, editorCharacterID);
				if (!editor.Has(GuildPermissions.EditRanks))
				{
					SendGuildResult(conn, GuildResultType.InsufficientRank);
					return;
				}

				GuildPermissions proposed = (GuildPermissions)requestedPermissions & GuildPermissions.All;

				// THE decision. See GuildRules.CanEditRank for the four rules and why each exists.
				GuildActionResult decision = GuildRules.CanEditRank(editor, rankOrder, proposed);
				if (decision != GuildActionResult.Allowed)
				{
					RefuseRankEdit(conn, ToGuildResultType(decision), editor);
					return;
				}

				editor.TryGetRank(rankOrder, out GuildRankData existing);

				if (!GuildRankDefaults.TrySanitizeRankName(requestedName, out string sanitizedName))
				{
					RefuseRankEdit(conn, GuildResultType.InvalidRankName, editor);
					return;
				}

				DatabaseResult updateResult = await rankService.UpdateAsync(guildID, rankOrder, sanitizedName, (long)proposed, existing.Version + 1);
				if (!updateResult.IsSuccess)
				{
					await Log.Warning("GuildSystem", $"EditGuildRankAsync update failed (GuildID={guildID}, RankOrder={rankOrder}): {updateResult.ErrorCode} - {updateResult.ErrorMessage}");

					/* A stale version here means another editor — usually on another scene
					 * server — got to this row first. The ladder the requester is looking at is
					 * therefore already wrong, so it is re-resolved and re-sent rather than left
					 * for the update pump to correct whenever it next runs. */
					RefuseRankEdit(conn, GuildResultType.Failed, await ResolveGuildAuthorityAsync(guildID, editorCharacterID));
					return;
				}

				AppendGuildLog(guildID, GuildLogEventType.RankEdited, editorCharacterID, 0, sanitizedName);

				await NotifyGuildOfRankChangeAsync(guildID);
			}
			catch (Exception ex)
			{
				await Log.Error("GuildSystem", $"Error editing guild rank (GuildID={guildID}, RankOrder={rankOrder}): {ex}");
			}
		}

		/// <summary>
		/// Handles a request to add a rank to the ladder.
		/// </summary>
		/// <param name="conn">The requesting connection.</param>
		/// <param name="msg">The requested position, name and mask.</param>
		/// <param name="channel">Network channel used for the broadcast.</param>
		public void OnServerGuildCreateRankBroadcastReceived(NetworkConnection conn, GuildCreateRankBroadcast msg, Channel channel)
		{
			if (!TryBeginGuildRequest(conn, IngressOperation.CreateRank, out long guardKey, out IGuildController guildController))
			{
				return;
			}

			bool deferGuardRelease = false;
			try
			{
				if (!guildController.HasGuildPermission(GuildPermissions.EditRanks))
				{
					SendGuildResult(conn, GuildResultType.InsufficientRank);
					return;
				}

				if (msg.RankOrder < GuildRankDefaults.MinRankOrder ||
					msg.RankOrder > GuildRankDefaults.MaxRankOrder)
				{
					return;
				}

				long guildID = guildController.ID;
				long characterID = guildController.Character.ID;
				byte rankOrder = msg.RankOrder;
				string requestedName = msg.Name;
				long requestedPermissions = msg.Permissions;

				deferGuardRelease = TryEnqueueIngressWork(
					() => CreateGuildRankAsync(conn, guildID, characterID, rankOrder, requestedName, requestedPermissions),
					guardKey,
					guildID);

				if (!deferGuardRelease) SendServerBusy(conn);
			}
			finally
			{
				if (!deferGuardRelease)
				{
					EndIngressGuard(guardKey);
				}
			}
		}

		/// <summary>
		/// Inserts a new rank row, after re-establishing every rule server-side.
		/// </summary>
		/// <param name="conn">The requesting connection, for feedback.</param>
		/// <param name="guildID">The guild.</param>
		/// <param name="creatorCharacterID">The creating character.</param>
		/// <param name="rankOrder">The requested position.</param>
		/// <param name="requestedName">The proposed name.</param>
		/// <param name="requestedPermissions">The proposed mask.</param>
		/// <returns>Asynchronous create task.</returns>
		/// <remarks>
		/// The new rank must sit at or below the creator's own seat, and may hold only permissions
		/// the creator holds. The second is for the same reason as the edit path: otherwise "may
		/// edit ranks" is "may become the leader", one step removed. The first is weaker than the
		/// edit path's rule on purpose — inserting AT the creator's order puts the new rank
		/// directly below them and carries their own row up with the rest of the ladder, which
		/// changes nobody's permissions and nobody's relative standing. See
		/// <c>GuildRules.CanCreateRank</c>.
		/// </remarks>
		private async Task CreateGuildRankAsync(NetworkConnection conn, long guildID, long creatorCharacterID, byte rankOrder, string requestedName, long requestedPermissions)
		{
			try
			{
				if (!TryGetDbService(out IGuildRankService rankService))
				{
					return;
				}

				GuildAuthority creator = await ResolveGuildAuthorityAsync(guildID, creatorCharacterID);
				if (!creator.Has(GuildPermissions.EditRanks))
				{
					SendGuildResult(conn, GuildResultType.InsufficientRank);
					return;
				}

				GuildPermissions proposed = (GuildPermissions)requestedPermissions & GuildPermissions.All;

				GuildActionResult decision = GuildRules.CanCreateRank(creator, rankOrder, proposed);
				if (decision != GuildActionResult.Allowed)
				{
					RefuseRankEdit(conn, ToGuildResultType(decision), creator);
					return;
				}

				if (!GuildRankDefaults.TrySanitizeRankName(requestedName, out string sanitizedName))
				{
					RefuseRankEdit(conn, GuildResultType.InvalidRankName, creator);
					return;
				}

				GuildRankData rank = new GuildRankData(0, 1, guildID, rankOrder, sanitizedName, (long)proposed);

				/* An INSERT, not a fill. The ladder is contiguous — a seeded guild is 1, 2, 3 —
				 * and a new rank may only sit at or below the creator's own seat, so there is
				 * never a free position to drop one into. The service makes room: everything at or
				 * above this order, ranks and membership rows alike, moves up one rung inside a
				 * single transaction. */
				DatabaseResult createResult = await rankService.InsertAsync(
					rank,
					GuildRankDefaults.MaxRanksPerGuild,
					GuildRankDefaults.MaxRankOrder);

				if (!createResult.IsSuccess)
				{
					/* The service decided this against the locked ladder, which may differ from
					 * the one the creator resolved a moment ago — so the refusal carries a fresh
					 * ladder, not the one the decision above was made on. */
					RefuseRankEdit(
						conn,
						createResult.ErrorCode == DatabaseErrorCodes.CapacityExceeded
							? GuildResultType.TooManyRanks
							: GuildResultType.RankNotFound,
						await ResolveGuildAuthorityAsync(guildID, creatorCharacterID));
					return;
				}

				AppendGuildLog(guildID, GuildLogEventType.RankCreated, creatorCharacterID, 0, sanitizedName);

				await NotifyGuildOfRankChangeAsync(guildID);
			}
			catch (Exception ex)
			{
				await Log.Error("GuildSystem", $"Error creating guild rank (GuildID={guildID}, RankOrder={rankOrder}): {ex}");
			}
		}

		/// <summary>
		/// Handles a request to remove a rank from the ladder.
		/// </summary>
		/// <param name="conn">The requesting connection.</param>
		/// <param name="msg">The rank to remove.</param>
		/// <param name="channel">Network channel used for the broadcast.</param>
		public void OnServerGuildDeleteRankBroadcastReceived(NetworkConnection conn, GuildDeleteRankBroadcast msg, Channel channel)
		{
			if (!TryBeginGuildRequest(conn, IngressOperation.DeleteRank, out long guardKey, out IGuildController guildController))
			{
				return;
			}

			bool deferGuardRelease = false;
			try
			{
				if (!guildController.HasGuildPermission(GuildPermissions.EditRanks))
				{
					SendGuildResult(conn, GuildResultType.InsufficientRank);
					return;
				}

				long guildID = guildController.ID;
				long characterID = guildController.Character.ID;
				byte rankOrder = msg.RankOrder;

				deferGuardRelease = TryEnqueueIngressWork(
					() => DeleteGuildRankAsync(conn, guildID, characterID, rankOrder),
					guardKey,
					guildID);

				if (!deferGuardRelease) SendServerBusy(conn);
			}
			finally
			{
				if (!deferGuardRelease)
				{
					EndIngressGuard(guardKey);
				}
			}
		}

		/// <summary>
		/// Removes a rank row, after re-establishing every rule server-side.
		/// </summary>
		/// <param name="conn">The requesting connection, for feedback.</param>
		/// <param name="guildID">The guild.</param>
		/// <param name="deleterCharacterID">The deleting character.</param>
		/// <param name="rankOrder">The rank to remove.</param>
		/// <returns>Asynchronous delete task.</returns>
		/// <remarks>
		/// The occupancy refusal lives in the service, in the same statement as the delete, so a
		/// member moved into the rank between check and delete cannot end up holding a rank that
		/// no longer exists. What lives HERE is the soft-lock guard: removing the last rank that
		/// can administer ranks is refused for the same reason editing it away is.
		/// </remarks>
		private async Task DeleteGuildRankAsync(NetworkConnection conn, long guildID, long deleterCharacterID, byte rankOrder)
		{
			try
			{
				if (!TryGetDbService(out IGuildRankService rankService))
				{
					return;
				}

				GuildAuthority deleter = await ResolveGuildAuthorityAsync(guildID, deleterCharacterID);
				if (!deleter.Has(GuildPermissions.EditRanks))
				{
					SendGuildResult(conn, GuildResultType.InsufficientRank);
					return;
				}

				GuildActionResult decision = GuildRules.CanDeleteRank(deleter, rankOrder);
				if (decision != GuildActionResult.Allowed)
				{
					RefuseRankEdit(conn, ToGuildResultType(decision), deleter);
					return;
				}

				deleter.TryGetRank(rankOrder, out GuildRankData existing);

				DatabaseResult deleteResult = await rankService.DeleteAsync(guildID, rankOrder);
				if (!deleteResult.IsSuccess)
				{
					RefuseRankEdit(
						conn,
						deleteResult.ErrorCode == DatabaseErrorCodes.NotFound
							? GuildResultType.RankNotFound
							: GuildResultType.RankInUse,
						await ResolveGuildAuthorityAsync(guildID, deleterCharacterID));
					return;
				}

				AppendGuildLog(guildID, GuildLogEventType.RankDeleted, deleterCharacterID, 0, existing.Name ?? string.Empty);

				await NotifyGuildOfRankChangeAsync(guildID);
			}
			catch (Exception ex)
			{
				await Log.Error("GuildSystem", $"Error deleting guild rank (GuildID={guildID}, RankOrder={rankOrder}): {ex}");
			}
		}

		/// <summary>
		/// Handles a request to set one of a member's two notes.
		/// </summary>
		/// <param name="conn">The requesting connection.</param>
		/// <param name="msg">The member, the note and which note it is.</param>
		/// <param name="channel">Network channel used for the broadcast.</param>
		/// <remarks>
		/// E6. The two notes are separately permissioned, and the officer note is separately
		/// permissioned for READING as well — a rank may be allowed to write public notes without
		/// being allowed to see the officer ones.
		/// </remarks>
		public void OnServerGuildSetMemberNoteBroadcastReceived(NetworkConnection conn, GuildSetMemberNoteBroadcast msg, Channel channel)
		{
			if (!TryBeginGuildRequest(conn, IngressOperation.SetNote, out long guardKey, out IGuildController guildController))
			{
				return;
			}

			bool deferGuardRelease = false;
			try
			{
				GuildPermissions required = msg.IsOfficerNote
					? GuildPermissions.EditOfficerNotes
					: GuildPermissions.EditPublicNotes;

				if (!guildController.HasGuildPermission(required))
				{
					SendGuildResult(conn, GuildResultType.InsufficientRank);
					return;
				}

				if (msg.CharacterID < 1)
				{
					return;
				}

				/* Sanitised on the way IN, not on the way out. The note is written once and read
				 * by every member on every roster refresh, so cleaning it at the boundary means
				 * cleaning it once rather than on every read — and means the stored value is the
				 * value, rather than something that has to be reinterpreted to be safe. */
				string note = ChatSanitizer.SanitizeIncoming(msg.Note, GuildTextLimits.MaxMemberNoteLength);

				long guildID = guildController.ID;
				long editorCharacterID = guildController.Character.ID;
				long targetCharacterID = msg.CharacterID;
				bool isOfficerNote = msg.IsOfficerNote;

				deferGuardRelease = TryEnqueueIngressWork(
					() => SetGuildMemberNoteAsync(guildID, editorCharacterID, targetCharacterID, note, isOfficerNote),
					guardKey,
					guildID);

				if (!deferGuardRelease) SendServerBusy(conn);
			}
			finally
			{
				if (!deferGuardRelease)
				{
					EndIngressGuard(guardKey);
				}
			}
		}

		/// <summary>
		/// Persists one member note.
		/// </summary>
		/// <param name="guildID">The guild.</param>
		/// <param name="editorCharacterID">The editing character.</param>
		/// <param name="targetCharacterID">The member the note is about.</param>
		/// <param name="note">The sanitised note.</param>
		/// <param name="isOfficerNote">True for the officer-only note.</param>
		/// <returns>Asynchronous write task.</returns>
		/// <remarks>
		/// The TARGET is verified to be in the same guild. Without that check a note could be
		/// written onto a stranger's membership row — the update statement is keyed on
		/// <c>(character_id, guild_id)</c> so it would miss, but a permission model that relies on
		/// a WHERE clause it does not own is one refactor away from not having the check at all.
		/// </remarks>
		private async Task SetGuildMemberNoteAsync(long guildID, long editorCharacterID, long targetCharacterID, string note, bool isOfficerNote)
		{
			try
			{
				if (!TryGetDbService(out ICharacterGuildService charGuildService) ||
					!TryGetDbService(out IGuildUpdateService guildUpdateService))
				{
					return;
				}

				GuildAuthority editor = await ResolveGuildAuthorityAsync(guildID, editorCharacterID);
				if (!editor.Has(isOfficerNote ? GuildPermissions.EditOfficerNotes : GuildPermissions.EditPublicNotes))
				{
					return;
				}

				DatabaseResult<CharacterGuildData?> targetResult = await charGuildService.FetchAsync(targetCharacterID);
				if (!targetResult.IsSuccess ||
					!targetResult.Data.HasValue ||
					targetResult.Data.Value.GuildID != guildID)
				{
					return;
				}

				DatabaseResult noteResult = await charGuildService.UpdateNoteAsync(targetCharacterID, guildID, note, isOfficerNote);
				if (!noteResult.IsSuccess)
				{
					await Log.Warning("GuildSystem", $"SetGuildMemberNoteAsync update failed (GuildID={guildID}, Target={targetCharacterID}): {noteResult.ErrorCode} - {noteResult.ErrorMessage}");
					return;
				}

				AppendGuildLog(guildID, GuildLogEventType.NoteChanged, editorCharacterID, targetCharacterID);

				/* The roster carries the notes, so the guild update pump is what redistributes
				 * them — and it applies the officer-note filter per recipient on the way out. */
				DatabaseResult updateResult = await guildUpdateService.PersistAsync(guildID);
				if (!updateResult.IsSuccess)
				{
					await Log.Warning("GuildSystem", $"SetGuildMemberNoteAsync guild update notification failed (GuildID={guildID}): {updateResult.ErrorCode} - {updateResult.ErrorMessage}");
				}
			}
			catch (Exception ex)
			{
				await Log.Error("GuildSystem", $"Error setting guild member note (GuildID={guildID}, Target={targetCharacterID}): {ex}");
			}
		}

		/// <summary>
		/// Refuses a rank edit, create or delete: sends the reason, then the ladder as it stands.
		/// </summary>
		/// <param name="conn">The requesting connection.</param>
		/// <param name="result">Why the request was refused.</param>
		/// <param name="requester">The requester's standing, resolved for this request.</param>
		/// <remarks>
		/// <para>
		/// The ladder goes back WITH the refusal because the client applies a permission toggle
		/// to its own copy of the ladder the moment it is flipped, so that two quick toggles
		/// compose instead of the second resurrecting the first. On acceptance the republished
		/// ladder overwrites that guess; on refusal nothing would, and the panel would keep
		/// drawing a mask the server never accepted until the player happened to reopen the tab.
		/// </para>
		/// <para>
		/// Sent from the standing this request was decided on, which costs no extra read for the
		/// rules-based refusals. The callers that refused on a DATABASE outcome — a stale version,
		/// a full ladder, an occupied rank — re-resolve first, because those refusals are
		/// evidence that the standing they hold is already out of date.
		/// </para>
		/// </remarks>
		private void RefuseRankEdit(NetworkConnection conn, GuildResultType result, GuildAuthority requester)
		{
			SendGuildResult(conn, result);
			SendGuildRankList(conn, requester);
		}

		/// <summary>
		/// Republishes the ladder locally and asks the other scene servers to do the same.
		/// </summary>
		/// <param name="guildID">The guild whose ladder changed.</param>
		/// <returns>Asynchronous notify task.</returns>
		/// <remarks>
		/// Two mechanisms, because they answer two different questions. The local publish gets the
		/// new ladder to the members on THIS server immediately, so the editor sees their own edit
		/// land. The guild-update row is what tells the other scene servers anything happened at
		/// all; their pump then re-reads the ladder and re-sends it to their own members. Without
		/// the second, a rank edited on one server would not reach a member standing in another
		/// zone until something else touched the guild.
		/// </remarks>
		private async Task NotifyGuildOfRankChangeAsync(long guildID)
		{
			await PublishGuildRankLadderAsync(guildID);

			if (TryGetDbService(out IGuildUpdateService guildUpdateService))
			{
				DatabaseResult updateResult = await guildUpdateService.PersistAsync(guildID);
				if (!updateResult.IsSuccess)
				{
					await Log.Warning("GuildSystem", $"NotifyGuildOfRankChangeAsync guild update notification failed (GuildID={guildID}): {updateResult.ErrorCode} - {updateResult.ErrorMessage}");
				}
			}
		}

		/// <summary>
		/// Maps a decision onto the result code sent to the client.
		/// </summary>
		/// <param name="result">The decision.</param>
		/// <returns>The wire result type.</returns>
		/// <remarks>
		/// The mapping lives at the edge so that <c>GuildRules</c> stays free of the shared
		/// assembly's broadcast types, which is what lets the rules be compiled and tested on
		/// their own without standing up a network stack.
		/// </remarks>
		private static GuildResultType ToGuildResultType(GuildActionResult result)
		{
			switch (result)
			{
				case GuildActionResult.RankNotFound:
					return GuildResultType.RankNotFound;
				case GuildActionResult.WouldOrphanGuild:
					return GuildResultType.WouldOrphanGuild;
				case GuildActionResult.Allowed:
					return GuildResultType.Success;
				default:
					return GuildResultType.InsufficientRank;
			}
		}

		/// <summary>
		/// The shared opening every guild request handler performs.
		/// </summary>
		/// <param name="conn">The requesting connection.</param>
		/// <param name="operation">The ingress operation key.</param>
		/// <param name="guardKey">The acquired guard key.</param>
		/// <param name="guildController">The requester's guild controller.</param>
		/// <returns>True when the request may proceed.</returns>
		/// <remarks>
		/// Extracted because the eight handlers added by E3/E6/E10 would otherwise repeat the same
		/// twenty lines — null connection, no spawned object, cannot act, ingress guard, not in a
		/// guild — and a permission model whose handlers differ only in the parts nobody reads
		/// twice is a permission model with a hole in it.
		///
		/// The guard is NOT released here on success. The caller owns it, because whether it is
		/// released now or deferred until an async continuation finishes depends on what the
		/// caller does next.
		/// </remarks>
		private bool TryBeginGuildRequest(NetworkConnection conn, IngressOperation operation, out long guardKey, out IGuildController guildController)
		{
			guardKey = 0;
			guildController = null;

			if (conn == null || conn.FirstObject == null)
			{
				return false;
			}

			IPlayerCharacter player = conn.FirstObject.GetComponent<IPlayerCharacter>();
			if (player == null || !CharacterStateValidation.CanAct(player))
			{
				return false;
			}

			if (Server?.Database?.ServiceRegistry == null)
			{
				return false;
			}

			if (!TryBeginIngressGuard(conn.ClientId, operation, out guardKey))
			{
				return false;
			}

			guildController = conn.FirstObject.GetComponent<IGuildController>();
			if (guildController == null || guildController.ID < 1 || guildController.Character == null)
			{
				EndIngressGuard(guardKey);
				guardKey = 0;
				guildController = null;
				return false;
			}

			return true;
		}
	}
}
