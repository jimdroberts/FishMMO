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
				return GuildAuthority.None(guildID, characterID);
			}

			DatabaseResult<CharacterGuildData?> membershipResult = await charGuildService.FetchAsync(characterID);
			if (!membershipResult.IsSuccess || !membershipResult.Data.HasValue)
			{
				return GuildAuthority.None(guildID, characterID);
			}

			CharacterGuildData membership = membershipResult.Data.Value;
			if (membership.GuildID != guildID)
			{
				// In a guild, but not this one. Nothing to grant here.
				return GuildAuthority.None(guildID, characterID);
			}

			IReadOnlyList<GuildRankData> ladder = await FetchOrSeedLadderAsync(guildID, rankService);
			if (ladder == null)
			{
				return GuildAuthority.None(guildID, characterID);
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
				return null;
			}

			return reReadResult.Data;
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
				GuildID = authority.GuildID,
				Ranks = BuildRankEntries(authority.Ladder),
				ViewerRankOrder = authority.RankOrder,
				ViewerPermissions = (long)authority.Permissions,
				LeaderRankOrder = authority.LeaderRankOrder,
			};

			long guildID = authority.GuildID;

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
			});
		}

		/// <summary>
		/// Re-resolves and re-sends the rank ladder to every member of a guild on this server.
		/// </summary>
		/// <param name="guildID">The guild whose ladder changed.</param>
		/// <returns>Asynchronous publish task.</returns>
		/// <remarks>
		/// Called after any edit to the ladder. Each member's standing is resolved individually
		/// rather than broadcasting one shared message, because the message carries the
		/// RECIPIENT's own permission mask — a shared copy would tell every member they hold
		/// whatever the first member holds.
		/// </remarks>
		private async Task PublishGuildRankLadderAsync(long guildID)
		{
			if (guildID < 1)
			{
				return;
			}

			List<(NetworkConnection conn, long characterID)> recipients = new List<(NetworkConnection, long)>();

			TaskCompletionSource<bool> gathered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

			bool enqueued = TryEnqueueMainThread(() =>
			{
				try
				{
					if (Server != null &&
						Server.DataContainerRegistry.TryGet<IGuildCharacterMappingData>(out var mappingData) &&
						Server.DataContainerRegistry.TryGet<ICharacterMappingData<NetworkConnection>>(out var characterMappingData) &&
						mappingData.GuildCharacterTracker.TryGetValue(guildID, out HashSet<long> memberIDs))
					{
						foreach (long memberID in memberIDs)
						{
							if (characterMappingData.CharactersByID.TryGetValue(memberID, out IPlayerCharacter member) &&
								member?.Owner != null)
							{
								recipients.Add((member.Owner, memberID));
							}
						}
					}
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

			for (int i = 0; i < recipients.Count; ++i)
			{
				GuildAuthority authority = await ResolveGuildAuthorityAsync(guildID, recipients[i].characterID);
				SendGuildRankList(recipients[i].conn, authority);
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
		private static GuildAddBroadcast BuildRosterEntry(CharacterGuildData member, bool includeOfficerNote)
		{
			return new GuildAddBroadcast()
			{
				GuildID = member.GuildID,
				CharacterID = member.CharacterID,
				RankOrder = member.Rank,
				Location = member.Location ?? string.Empty,
				RaceID = member.RaceID,
				Level = member.Level,
				PublicNote = member.PublicNote ?? string.Empty,
				OfficerNote = includeOfficerNote ? (member.OfficerNote ?? string.Empty) : string.Empty,
				LastOnlineUtcTicks = member.LastOnlineUtc.Ticks,
			};
		}

		/// <summary>
		/// Projects a whole roster onto the wire for one class of recipient.
		/// </summary>
		/// <param name="members">The guild's membership rows.</param>
		/// <param name="includeOfficerNotes">Whether the recipients may read officer notes.</param>
		/// <returns>The roster broadcast.</returns>
		/// <remarks>
		/// Built twice per guild rather than once per member: there are exactly two versions of
		/// this message — with officer notes and without — so a guild of a hundred needs two
		/// arrays, not a hundred.
		/// </remarks>
		private static GuildAddMultipleBroadcast BuildRoster(IReadOnlyList<CharacterGuildData> members, bool includeOfficerNotes)
		{
			GuildAddBroadcast[] entries = new GuildAddBroadcast[members?.Count ?? 0];
			for (int i = 0; i < entries.Length; ++i)
			{
				entries[i] = BuildRosterEntry(members[i], includeOfficerNotes);
			}

			return new GuildAddMultipleBroadcast()
			{
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
		/// <returns>The bottom rung, or the seeded member order when the ladder is unreadable.</returns>
		/// <remarks>
		/// A new member joins the BOTTOM of the ladder as the guild defines it, not a constant.
		/// The fallback reproduces the historical behaviour rather than refusing the join: a
		/// database hiccup during an accept should not cost the player their invitation, and the
		/// pump reconciles the rank from the row on its next pass regardless.
		/// </remarks>
		private async Task<byte> ResolveLowestRankOrderAsync(long guildID)
		{
			if (!TryGetDbService(out IGuildRankService rankService))
			{
				return GuildRankDefaults.MemberRankOrder;
			}

			IReadOnlyList<GuildRankData> ladder = await FetchOrSeedLadderAsync(guildID, rankService);
			if (ladder == null || ladder.Count == 0)
			{
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
