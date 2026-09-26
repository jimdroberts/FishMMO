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
	/// Guild permission resolution: the single place the server decides what a character may do
	/// in a guild, and the lazy migration that gives a pre-permissions guild its rank ladder.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Split out of <c>GuildSystem.cs</c> because it is the security surface of the whole feature.
	/// Every operation in the main file now funnels through
	/// <see cref="ResolveGuildAuthorityAsync"/>, and a reviewer asking "can a member kick a
	/// leader?" should be able to answer it from one file rather than from thirty scattered
	/// comparisons.
	/// </para>
	/// <para>
	/// The old model compared a <c>GuildRank</c> enum: <c>Rank &gt;= GuildRank.Officer</c> and
	/// friends. That is gone. A rank is a row a guild owns, carrying a name it chose and a
	/// permission mask; the row's ORDER is used for seniority alone.
	/// </para>
	/// </remarks>
	public partial class GuildSystem
	{
		/// <summary>
		/// Resolves a character's authoritative standing in a guild from the database.
		/// </summary>
		/// <param name="guildID">The guild to resolve standing in.</param>
		/// <param name="characterID">The character whose standing is wanted.</param>
		/// <returns>The resolved standing, or a standing that permits nothing.</returns>
		/// <remarks>
		/// <para>
		/// Two reads: the membership row, and the guild's ladder. Both are needed for every
		/// decision — the mask says what the character may do and the ladder says who they may do
		/// it to — so they are fetched together rather than lazily, which also means one decision
		/// never sees a ladder edited halfway through it.
		/// </para>
		/// <para>
		/// The guild ID is re-derived from the MEMBERSHIP ROW, not taken from the caller. A caller
		/// that passes a guild the character does not belong to gets a non-member standing rather
		/// than a standing in the guild they do belong to, which is the difference between a
		/// refused cross-guild request and a granted one.
		/// </para>
		/// </remarks>
		private async Task<GuildAuthority> ResolveGuildAuthorityAsync(long guildID, long characterID)
		{
			if (guildID < 1 || characterID < 1)
			{
				return GuildAuthority.None(guildID, characterID);
			}

			if (!TryGetDbService(out ICharacterGuildService charGuildService) ||
				!TryGetDbService(out IGuildRankService rankService))
			{
				return GuildAuthority.Unavailable(guildID, characterID);
			}

			/* A read that FAILED and a read that found nothing both refuse, but they are not the
			 * same answer: the first is logged here, where its cause is still known, and comes back
			 * as Unavailable so the caller can tell the player "failed" rather than "your rank is
			 * too low". */
			DatabaseResult<CharacterGuildData?> membershipResult = await charGuildService.FetchAsync(characterID);
			if (!membershipResult.IsSuccess)
			{
				await Log.Warning("GuildSystem", $"ResolveGuildAuthorityAsync membership fetch failed (CharID={characterID}, GuildID={guildID}): {membershipResult.ErrorCode} - {membershipResult.ErrorMessage}");
				return GuildAuthority.Unavailable(guildID, characterID);
			}

			if (!membershipResult.Data.HasValue)
			{
				return GuildAuthority.None(guildID, characterID);
			}

			CharacterGuildData membership = membershipResult.Data.Value;
			if (membership.GuildID != guildID)
			{
				// In a guild, but not this one. Nothing to grant here.
				return GuildAuthority.None(guildID, characterID);
			}

			// FetchOrSeedLadderAsync logs its own failures.
			IReadOnlyList<GuildRankData> ladder = await FetchOrSeedLadderAsync(guildID, rankService);
			if (ladder == null)
			{
				return GuildAuthority.Unavailable(guildID, characterID);
			}

			byte leaderRankOrder = 0;
			GuildPermissions permissions = GuildPermissions.None;
			bool foundOwnRank = false;

			for (int i = 0; i < ladder.Count; ++i)
			{
				GuildRankData rank = ladder[i];
				if (rank.RankOrder > leaderRankOrder)
				{
					leaderRankOrder = rank.RankOrder;
				}
				if (rank.RankOrder == membership.Rank)
				{
					permissions = (GuildPermissions)rank.Permissions;
					foundOwnRank = true;
				}
			}

			if (!foundOwnRank)
			{
				/* The member holds a rank order with no row. DeleteAsync refuses to remove an
				 * occupied rank in the same statement that checks occupancy, so this should not
				 * happen — but "should not happen" is not a permission decision. Granting the
				 * seeded default for the order would hand out powers from a rank that does not
				 * exist; granting nothing leaves the member able to chat and leave, which is the
				 * safe direction to fail in. It is logged because it means a row is missing. */
				await Log.Warning("GuildSystem", $"ResolveGuildAuthorityAsync: CharID={characterID} holds rank order {membership.Rank} in GuildID={guildID}, which has no rank row. Granting no permissions.");
			}

			return new GuildAuthority(
				true,
				guildID,
				characterID,
				membership.Rank,
				permissions,
				leaderRankOrder,
				membership.Version,
				ladder);
		}

		/// <summary>
		/// Fetches a guild's rank ladder, seeding the legacy defaults if it has none.
		/// </summary>
		/// <param name="guildID">The guild.</param>
		/// <param name="rankService">The rank service.</param>
		/// <returns>The ladder, or null when the database is unreachable.</returns>
		/// <remarks>
		/// <para>
		/// THIS IS THE MIGRATION. Guilds created before ranks were rows have no rank rows, and
		/// there is no migration script: the first time anybody asks about such a guild, it grows
		/// the three seeded ranks whose masks reproduce exactly what the enum used to allow.
		/// A leader (order 3) keeps every power, an officer (order 2) keeps invite, kick and the
		/// text edits, and a member (order 1) gains nothing — see <c>GuildRankDefaults</c>, whose
		/// masks were transcribed site by site from the pre-change checks.
		/// </para>
		/// <para>
		/// Idempotent twice over: the seed only runs when the fetch came back EMPTY, and the
		/// insert itself is <c>ON CONFLICT DO NOTHING</c> on <c>(guild_id, rank_order)</c>. Two
		/// scene servers resolving the same guild at the same moment both seed, both conflict, and
		/// both read back the same three rows.
		/// </para>
		/// <para>
		/// Empty is also the only trigger. Calling the seed on every resolve — which the service
		/// is safe for — would put three no-op INSERTs in front of every permission check in the
		/// game for the sake of guilds that were migrated the first time they were touched.
		/// </para>
		/// </remarks>
		private async Task<IReadOnlyList<GuildRankData>> FetchOrSeedLadderAsync(long guildID, IGuildRankService rankService)
		{
			DatabaseResult<IReadOnlyList<GuildRankData>> fetchResult = await rankService.FetchManyAsync(guildID);
			if (!fetchResult.IsSuccess)
			{
				await Log.Warning("GuildSystem", $"FetchOrSeedLadderAsync fetch failed (GuildID={guildID}): {fetchResult.ErrorCode} - {fetchResult.ErrorMessage}");
				return null;
			}

			if (fetchResult.Data != null && fetchResult.Data.Count > 0)
			{
				return fetchResult.Data;
			}

			DatabaseResult<int> seedResult = await rankService.EnsureDefaultsAsync(guildID, BuildDefaultLadder(guildID));
			if (!seedResult.IsSuccess)
			{
				await Log.Warning("GuildSystem", $"FetchOrSeedLadderAsync seed failed (GuildID={guildID}): {seedResult.ErrorCode} - {seedResult.ErrorMessage}");
				return null;
			}

			DatabaseResult<IReadOnlyList<GuildRankData>> reReadResult = await rankService.FetchManyAsync(guildID);
			if (!reReadResult.IsSuccess || reReadResult.Data == null)
			{
				await Log.Warning("GuildSystem", $"FetchOrSeedLadderAsync re-read after seeding failed (GuildID={guildID}): {reReadResult.ErrorCode} - {reReadResult.ErrorMessage}");
				return null;
			}

			return reReadResult.Data;
		}

		/// <summary>
		/// The refusal to report when a resolved standing does not permit an action.
		/// </summary>
		/// <param name="authority">The standing the request was decided on.</param>
		/// <returns>
		/// <see cref="GuildResultType.Failed"/> when the standing could not be read at all,
		/// otherwise <see cref="GuildResultType.InsufficientRank"/>.
		/// </returns>
		private static GuildResultType AuthorityRefusal(GuildAuthority authority)
		{
			return authority.LookupFailed ? GuildResultType.Failed : GuildResultType.InsufficientRank;
		}

		/// <summary>
		/// The three rank rows a guild is seeded with.
		/// </summary>
		/// <param name="guildID">The guild the rows belong to.</param>
		/// <returns>The default ladder.</returns>
		private static IReadOnlyList<GuildRankData> BuildDefaultLadder(long guildID)
		{
			return new List<GuildRankData>()
			{
				new GuildRankData(0, 1, guildID, GuildRankDefaults.MemberRankOrder, GuildRankDefaults.MemberRankName, (long)GuildRankDefaults.MemberPermissions),
				new GuildRankData(0, 1, guildID, GuildRankDefaults.OfficerRankOrder, GuildRankDefaults.OfficerRankName, (long)GuildRankDefaults.OfficerPermissions),
				new GuildRankData(0, 1, guildID, GuildRankDefaults.DefaultLeaderRankOrder, GuildRankDefaults.LeaderRankName, (long)GuildRankDefaults.LeaderPermissions),
			};
		}

		/// <summary>
		/// Sends a guild's rank ladder, and the recipient's own standing in it, to one connection.
		/// </summary>
		/// <param name="conn">The connection to inform.</param>
		/// <param name="authority">The recipient's resolved standing.</param>
		/// <remarks>
		/// The viewer's own mask is computed here and sent, rather than left for the client to
		/// derive by finding its rank in the ladder. Two implementations of "what may this player
		/// do" would eventually disagree, and the client's copy is the one that draws the buttons
		/// — so a disagreement would show as a button that does nothing rather than as a greyed
		/// one. Presentation follows the server's answer.
		/// </remarks>
		private void SendGuildRankList(NetworkConnection conn, GuildAuthority authority)
		{
			if (conn == null || !authority.IsMember)
			{
				return;
			}

			GuildRankListBroadcast broadcast = new GuildRankListBroadcast()
			{
				Ranks = BuildRankEntries(authority.Ladder),
				ViewerRankOrder = authority.RankOrder,
				ViewerPermissions = (long)authority.Permissions,
				LeaderRankOrder = authority.LeaderRankOrder,
			};

			long guildID = authority.GuildID;
			long characterID = authority.CharacterID;

			TryEnqueueMainThread(() =>
			{
				if (conn == null || !conn.IsActive || Server == null || conn.FirstObject == null)
				{
					return;
				}

				/* Re-checked on delivery. The resolve was asynchronous and the recipient may have
				 * been kicked while it was in flight; a ladder is harmless but the VIEWER
				 * PERMISSIONS in it are not, and an ex-member should not be handed a mask. */
				IGuildController guildController = conn.FirstObject.GetComponent<IGuildController>();
				if (guildController == null || guildController.ID != guildID)
				{
					return;
				}

				/* The server's own cache of this member's standing, refreshed from the same
				 * resolve the message carries. It is only ever a pre-filter — every operation
				 * re-resolves before deciding — but inserting a rank RENUMBERS the ladder, so a
				 * cache left on the pre-insert order would disagree with the panel the player is
				 * looking at until the guild update pump next ran. */
				guildController.RankOrder = broadcast.ViewerRankOrder;
				guildController.Permissions = (GuildPermissions)broadcast.ViewerPermissions;
				guildController.LeaderRankOrder = broadcast.LeaderRankOrder;

				Server.NetworkWrapper.Broadcast(conn, broadcast, true, Channel.Reliable);

				/* This ladder came from a resolve of its own, which may be older or newer than the
				 * one the update pump last delivered. Whatever the pump recorded this client as
				 * holding no longer holds, so its next delivery sends the rank list again rather
				 * than trusting a record this message may just have overwritten. */
				guildRecipientBaselines.ForgetLadder(characterID);
			});
		}

		/// <summary>
		/// Re-reads the ladder once and re-sends it to every member of a guild on this server.
		/// </summary>
		/// <param name="guildID">The guild whose ladder changed.</param>
		/// <returns>Asynchronous publish task.</returns>
		/// <remarks>
		/// <para>
		/// Called after any edit to the ladder. The standings are built from ONE snapshot — the
		/// roster and the ladder, read once each — rather than by resolving every member
		/// individually. Resolving is two reads per member, which made a rank edit in a guild with a
		/// hundred members online cost two hundred round trips; and two hundred separate reads can
		/// straddle a second edit, so half the guild would be sent one ladder and half another.
		/// </para>
		/// <para>
		/// <b>One multicast per rank, not one message per member.</b> The message carries the
		/// viewer's own rank and permission mask, so it is the same for everybody holding one rank:
		/// members are grouped by the rank the snapshot gives them and each group is sent one copy,
		/// serialised once — the update pump's delivery does the same, through the same
		/// <see cref="DeliverRankListAudiences"/>. It used to be a separate main-thread hop, a
		/// separate wire array and a separate send for every member.
		/// </para>
		/// <para>
		/// <b>The ladder baseline is recorded, not forgotten.</b> The ladder read here becomes the
		/// guild's delivered ladder (<see cref="AdvanceDeliveredLadder"/>), and each recipient is
		/// recorded as holding its generation at their rank. So when the update pump then processes
		/// the guild update this edit writes, a member who already holds that ladder is not sent it a
		/// second time; the per-member send forgot every recipient's baseline instead, and the pump
		/// sent the whole guild the same list again a second later. A member already holding this
		/// generation at this rank is skipped here for the same reason.
		/// </para>
		/// </remarks>
		private async Task PublishGuildRankLadderAsync(long guildID)
		{
			if (guildID < 1)
			{
				return;
			}

			if (!TryGetDbService(out ICharacterGuildService charGuildService) ||
				!TryGetDbService(out IGuildRankService rankService))
			{
				return;
			}

			/* Whether anybody here is owed the ladder at all, gathered on the main thread before the
			 * reads: a guild with no member on this server costs nothing. */
			bool anyLocalMember = false;

			TaskCompletionSource<bool> gathered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

			bool enqueued = TryEnqueueMainThread(() =>
			{
				try
				{
					anyLocalMember = Server != null &&
						Server.DataContainerRegistry.TryGet<IGuildCharacterMappingData>(out var mappingData) &&
						mappingData.GuildCharacterTracker.TryGetValue(guildID, out HashSet<long> memberIDs) &&
						memberIDs.Count > 0;
				}
				finally
				{
					gathered.TrySetResult(true);
				}
			});

			if (!enqueued)
			{
				return;
			}

			await gathered.Task;

			if (!anyLocalMember)
			{
				return;
			}

			DatabaseResult<IReadOnlyList<CharacterGuildData>> membersResult = await charGuildService.FetchManyAsync(guildID);
			if (!membersResult.IsSuccess || membersResult.Data == null)
			{
				await Log.Warning("GuildSystem", $"PublishGuildRankLadderAsync roster fetch failed (GuildID={guildID}): {membersResult.ErrorCode} - {membersResult.ErrorMessage}");
				return;
			}

			IReadOnlyList<GuildRankData> ladder = await FetchOrSeedLadderAsync(guildID, rankService);
			if (ladder == null)
			{
				return;
			}

			IReadOnlyList<CharacterGuildData> roster = membersResult.Data;
			TryEnqueueMainThread(() => DeliverPublishedLadder(guildID, roster, ladder));
		}

		/// <summary>
		/// Delivers a freshly read ladder to this server's members of a guild, one multicast per
		/// rank. Main thread only.
		/// </summary>
		/// <param name="guildID">The guild.</param>
		/// <param name="roster">The guild's membership rows, read with the ladder.</param>
		/// <param name="ladder">The ladder.</param>
		/// <remarks>
		/// Every recipient is re-checked here, on delivery. The reads were asynchronous and a member
		/// may have been kicked, or have left, while they were in flight; a ladder is harmless but
		/// the VIEWER PERMISSIONS in it are not, and an ex-member must not be handed a mask. A local
		/// member with no row in the roster was removed between the gather and the read, and gets
		/// nothing: the update pump is already on its way to tell them they have left.
		/// </remarks>
		private void DeliverPublishedLadder(long guildID, IReadOnlyList<CharacterGuildData> roster, IReadOnlyList<GuildRankData> ladder)
		{
			if (Server == null ||
				!Server.DataContainerRegistry.TryGet<IGuildCharacterMappingData>(out var mappingData) ||
				!Server.DataContainerRegistry.TryGet<ICharacterMappingData<NetworkConnection>>(out var characterMappingData))
			{
				return;
			}

			/* A guild no longer tracked here has nobody here to deliver to, and recording its ladder
			 * as delivered would leave a baseline behind for a guild nothing pumps. */
			if (!mappingData.GuildCharacterTracker.TryGetValue(guildID, out HashSet<long> memberIDs) ||
				memberIDs.Count < 1)
			{
				return;
			}

			byte leaderRankOrder = LeaderRankOrderOf(ladder);
			long generation = AdvanceDeliveredLadder(guildID, ladder, out GuildRankEntry[] rankEntries);

			ReleaseGuildAudiences();
			try
			{
				for (int i = 0; i < roster.Count; ++i)
				{
					CharacterGuildData membership = roster[i];
					if (membership.GuildID != guildID ||
						!memberIDs.Contains(membership.CharacterID) ||
						!characterMappingData.CharactersByID.TryGetValue(membership.CharacterID, out IPlayerCharacter member) ||
						member == null ||
						!member.TryGet(out IGuildController guildController) ||
						guildController.ID != guildID)
					{
						continue;
					}

					/* The server's own cache of this member's standing, refreshed from the same
					 * snapshot the message carries. It is only ever a pre-filter — every operation
					 * re-resolves before deciding — but inserting a rank RENUMBERS the ladder, so a
					 * cache left on the pre-insert order would disagree with the panel the player is
					 * looking at until the guild update pump next ran. */
					guildController.RankOrder = membership.Rank;
					guildController.Permissions = PermissionsForOrder(ladder, membership.Rank);
					guildController.LeaderRankOrder = leaderRankOrder;

					NetworkConnection owner = member.Owner;
					if (owner == null || !owner.IsActive)
					{
						continue;
					}

					if (!guildRecipientBaselines.HasLadder(membership.CharacterID, guildID, generation, membership.Rank))
					{
						RankListAudience(membership.Rank).Add(owner, membership.CharacterID);
					}
				}

				DeliverRankListAudiences(guildID, ladder, rankEntries, generation, leaderRankOrder);
			}
			finally
			{
				ReleaseGuildAudiences();
			}
		}

		/// <summary>
		/// The permission mask a ladder position holds, without a resolved standing to hand.
		/// </summary>
		/// <param name="ladder">The guild's rank rows.</param>
		/// <param name="rankOrder">The position to look up.</param>
		/// <returns>The mask, or <see cref="GuildPermissions.None"/> when the position has no row.</returns>
		private static GuildPermissions PermissionsForOrder(IReadOnlyList<GuildRankData> ladder, byte rankOrder)
		{
			if (ladder != null)
			{
				for (int i = 0; i < ladder.Count; ++i)
				{
					if (ladder[i].RankOrder == rankOrder)
					{
						return (GuildPermissions)ladder[i].Permissions;
					}
				}
			}

			return GuildPermissions.None;
		}

		/// <summary>
		/// The leader's seat on a ladder: the highest rank order that exists.
		/// </summary>
		/// <param name="ladder">The guild's rank rows.</param>
		/// <returns>The highest rank order, or 0 for an empty or missing ladder.</returns>
		internal static byte LeaderRankOrderOf(IReadOnlyList<GuildRankData> ladder)
		{
			byte leaderRankOrder = 0;
			if (ladder != null)
			{
				for (int i = 0; i < ladder.Count; ++i)
				{
					if (ladder[i].RankOrder > leaderRankOrder)
					{
						leaderRankOrder = ladder[i].RankOrder;
					}
				}
			}
			return leaderRankOrder;
		}

		/// <summary>
		/// Projects a character's OWN membership row onto the wire, for the immediate add sent to
		/// that character the moment they found or join a guild.
		/// </summary>
		/// <param name="character">
		/// The member's live character, or null when it could not be resolved from the connection.
		/// </param>
		/// <param name="characterID">
		/// The member's character identifier. Taken from the caller rather than from
		/// <paramref name="character"/> so the row is still labelled correctly when the component
		/// lookup fails; the character only supplies the race.
		/// </param>
		/// <param name="rankOrder">The member's position on the guild's rank ladder.</param>
		/// <param name="location">The member's location label.</param>
		/// <returns>The roster entry to send.</returns>
		/// <remarks>
		/// <para>
		/// The counterpart of <see cref="BuildRosterEntry"/> for a member who has no database row
		/// to project yet: the create and join paths answer the caller before the roster pump has
		/// read anything back, so the only place their race can come from is the live character —
		/// the same field the character table is written from, so the row the member is sent and
		/// the row the next roster read produces agree.
		/// </para>
		/// <para>
		/// It exists as a helper rather than as two inline initialisers because those two had
		/// already drifted: both omitted <see cref="GuildAddEntry.RaceID"/>, so a founder's own
		/// row carried 0 — which the client renders as an em dash — until something re-read the
		/// roster from the database.
		/// </para>
		/// <para>
		/// The notes and last-seen columns are deliberately left at their defaults: a member who
		/// has just founded or joined a guild has no note yet, and is on this server by
		/// definition, so <paramref name="location"/> is what the row displays.
		/// </para>
		/// </remarks>
		internal static GuildAddEntry BuildSelfRosterEntry(IPlayerCharacter character, long characterID, byte rankOrder, string location)
		{
			return new GuildAddEntry()
			{
				CharacterID = characterID,
				RankOrder = rankOrder,
				Location = location ?? string.Empty,
				RaceID = character != null ? character.RaceID : 0,
			};
		}

		/// <summary>
		/// Projects one membership row onto the wire, applying the officer-note filter.
		/// </summary>
		/// <param name="member">The membership row.</param>
		/// <param name="includeOfficerNote">Whether the RECIPIENT may read officer notes.</param>
		/// <returns>The roster entry to send.</returns>
		/// <remarks>
		/// The filter is a function of the recipient, not of the member described — an officer
		/// reading the roster sees every officer note, including notes about members senior to
		/// them, because the note is a tool for administering the guild rather than a private
		/// message. What matters is that a client which may not read them never receives them:
		/// the column is simply not written into the message, so it is not in the packet to be
		/// recovered by anybody inspecting the stream.
		/// </remarks>
		private static GuildAddEntry BuildRosterEntry(CharacterGuildData member, bool includeOfficerNote)
		{
			return new GuildAddEntry()
			{
				CharacterID = member.CharacterID,
				RankOrder = member.Rank,
				Location = member.Location ?? string.Empty,
				RaceID = member.RaceID,
				PublicNote = member.PublicNote ?? string.Empty,
				OfficerNote = includeOfficerNote ? (member.OfficerNote ?? string.Empty) : string.Empty,
				LastOnlineUnixSeconds = ToUnixSeconds(member.LastOnlineUtc),
			};
		}

		/// <summary>
		/// Projects a UTC timestamp onto the wire's last-seen unit.
		/// </summary>
		/// <param name="value">The timestamp, which may be <c>default</c> when never recorded.</param>
		/// <returns>Unix seconds, or 0 when the timestamp is unset or predates the epoch.</returns>
		/// <remarks>
		/// Zero is the "unknown" sentinel the client's last-seen column tests for, so a default
		/// <c>DateTime</c> — which is 1/1/0001 and a large NEGATIVE Unix second — has to collapse
		/// to it rather than being sent as a date in antiquity.
		/// </remarks>
		internal static long ToUnixSeconds(DateTime value)
		{
			if (value <= DateTime.UnixEpoch)
			{
				return 0;
			}

			return new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)).ToUnixTimeSeconds();
		}

		/// <summary>
		/// Projects one full roster row for one class of recipient.
		/// </summary>
		/// <param name="fullRow">The row with its officer note, as <see cref="BuildRosterEntry"/> built it for an officer.</param>
		/// <param name="includeOfficerNote">Whether the recipient may read officer notes.</param>
		/// <returns>The row to send: unchanged for an officer, with the officer note emptied otherwise.</returns>
		/// <remarks>
		/// The pump builds each row ONCE, in the full projection, because that is the form its
		/// delivered-roster baseline is kept in; the copy a recipient is sent is projected from it
		/// here, so the full roster and a delta cannot apply the officer-note filter differently.
		/// </remarks>
		internal static GuildAddEntry ProjectRosterEntry(GuildAddEntry fullRow, bool includeOfficerNote)
		{
			if (!includeOfficerNote)
			{
				fullRow.OfficerNote = string.Empty;
			}
			return fullRow;
		}

		/// <summary>
		/// Projects a whole roster onto the wire for one class of recipient.
		/// </summary>
		/// <param name="guildID">The guild every row belongs to, carried once on the payload.</param>
		/// <param name="fullRows">The guild's rows in the full projection.</param>
		/// <param name="includeOfficerNotes">Whether the recipients may read officer notes.</param>
		/// <returns>The roster broadcast.</returns>
		/// <remarks>
		/// Built at most twice per guild rather than once per member: there are exactly two
		/// versions of this message — with officer notes and without — so a guild of a hundred
		/// needs two arrays, not a hundred.
		/// </remarks>
		private static GuildAddMultipleBroadcast ProjectRoster(long guildID, GuildAddEntry[] fullRows, bool includeOfficerNotes)
		{
			GuildAddEntry[] entries = new GuildAddEntry[fullRows?.Length ?? 0];
			for (int i = 0; i < entries.Length; ++i)
			{
				entries[i] = ProjectRosterEntry(fullRows[i], includeOfficerNotes);
			}

			return new GuildAddMultipleBroadcast()
			{
				GuildID = guildID,
				Members = entries,
			};
		}
		/// <summary>
		/// Projects a rank ladder onto the wire.
		/// </summary>
		/// <param name="ladder">The guild's rank rows, or null.</param>
		/// <returns>The ladder entries, empty when there are none.</returns>
		private static GuildRankEntry[] BuildRankEntries(IReadOnlyList<GuildRankData> ladder)
		{
			GuildRankEntry[] entries = new GuildRankEntry[ladder?.Count ?? 0];
			for (int i = 0; i < entries.Length; ++i)
			{
				GuildRankData rank = ladder[i];
				entries[i] = new GuildRankEntry()
				{
					RankOrder = rank.RankOrder,
					Name = rank.Name ?? string.Empty,
					Permissions = rank.Permissions,
				};
			}

			return entries;
		}
		/// <summary>
		/// The rank order immediately below a given position on a ladder.
		/// </summary>
		/// <param name="ladder">The guild's rank rows.</param>
		/// <param name="rankOrder">The position to step down from.</param>
		/// <returns>
		/// The next lower position that exists, or <paramref name="rankOrder"/> itself when there
		/// is none.
		/// </returns>
		/// <remarks>
		/// Returning the SAME order when nothing is below it is deliberate. The caller is
		/// demoting an outgoing leader in a guild that turns out to have exactly one rank; there
		/// is nowhere to demote them to, and returning zero would write a rank of zero, which
		/// means "not in a guild" and would silently strip the membership.
		/// </remarks>
		private static byte FindNextRankBelow(IReadOnlyList<GuildRankData> ladder, byte rankOrder)
		{
			byte best = 0;

			if (ladder != null)
			{
				for (int i = 0; i < ladder.Count; ++i)
				{
					byte candidate = ladder[i].RankOrder;
					if (candidate < rankOrder && candidate > best)
					{
						best = candidate;
					}
				}
			}

			return best > 0 ? best : rankOrder;
		}

		/// <summary>
		/// The lowest rank order a guild actually has, for admitting a new member.
		/// </summary>
		/// <param name="guildID">The guild being joined.</param>
		/// <returns>The bottom rung, or null when the ladder could not be read.</returns>
		/// <remarks>
		/// <para>
		/// A new member joins the BOTTOM of the ladder as the guild defines it, not a constant.
		/// </para>
		/// <para>
		/// An unreadable ladder is NOT answered with the seeded member order any more. That
		/// fallback was defended on two grounds and neither held: refusing does not cost the
		/// player their invitation — the join path leaves it pending on every refusal but a
		/// vanished guild, so they can simply accept again — and the pump does not reconcile the
		/// rank, it re-reads the row this would have written. A guild that had deleted its order-1
		/// rank admitted the member into a rank with no row, which holds no permissions.
		/// </para>
		/// </remarks>
		private async Task<byte?> ResolveLowestRankOrderAsync(long guildID)
		{
			if (!TryGetDbService(out IGuildRankService rankService))
			{
				return null;
			}

			// FetchOrSeedLadderAsync logs its own failures.
			IReadOnlyList<GuildRankData> ladder = await FetchOrSeedLadderAsync(guildID, rankService);
			if (ladder == null)
			{
				return null;
			}

			if (ladder.Count == 0)
			{
				/* Read successfully, seeded, and still empty: the guild row is gone, because the
				 * seed inserts only for a guild that exists. Nothing to join, and the persist that
				 * follows will say so; the seeded order is as good an answer as any to hand it. */
				return GuildRankDefaults.MemberRankOrder;
			}

			byte lowest = byte.MaxValue;
			for (int i = 0; i < ladder.Count; ++i)
			{
				if (ladder[i].RankOrder < lowest)
				{
					lowest = ladder[i].RankOrder;
				}
			}

			return lowest == byte.MaxValue ? GuildRankDefaults.MemberRankOrder : lowest;
		}
	}
}
