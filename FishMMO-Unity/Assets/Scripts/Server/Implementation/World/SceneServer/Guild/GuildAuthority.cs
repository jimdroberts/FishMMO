using System.Collections.Generic;
using FishMMO.Database.Data;
using FishMMO.Shared;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// One character's authoritative standing in one guild, resolved from the database.
	/// </summary>
	/// <remarks>
	/// <para>
	/// This is what every server-side guild permission decision consults. It is deliberately a
	/// value produced by a database read rather than anything cached on the character: the
	/// controller's copy of a player's rank is a client-visible convenience that can be a
	/// broadcast out of date, and "a broadcast out of date" is precisely the window in which a
	/// demoted officer would still be able to kick.
	/// </para>
	/// <para>
	/// The permission model this carries replaces the old <c>GuildRank</c> enum comparisons.
	/// A rank no longer implies its powers by its NAME or its position — it holds a mask, and the
	/// position is used for one thing only: seniority, so that "you cannot act on somebody at or
	/// above you" still has an answer.
	/// </para>
	/// </remarks>
	public readonly struct GuildAuthority
	{
		/// <summary>Whether the character actually holds a membership row in this guild.</summary>
		/// <remarks>
		/// False is the value every failure resolves to — no membership, wrong guild, database
		/// unavailable. A caller that forgets to check it gets <see cref="Permissions"/> of
		/// <see cref="GuildPermissions.None"/> and a <see cref="RankOrder"/> of zero, so the
		/// failure mode of forgetting is refusal rather than escalation.
		/// </remarks>
		public readonly bool IsMember;

		/// <summary>The guild this standing is in.</summary>
		public readonly long GuildID;

		/// <summary>The character the standing belongs to.</summary>
		public readonly long CharacterID;

		/// <summary>The character's position on the ladder. Higher is more senior.</summary>
		public readonly byte RankOrder;

		/// <summary>The permissions the character's rank holds.</summary>
		public readonly GuildPermissions Permissions;

		/// <summary>The highest rank order that exists in this guild — the leader's seat.</summary>
		public readonly byte LeaderRankOrder;

		/// <summary>The concurrency token of the character's membership row.</summary>
		public readonly long MembershipVersion;

		/// <summary>The guild's whole rank ladder, ordered by rank order ascending.</summary>
		/// <remarks>
		/// Carried along because almost every caller that needs one rank needs to validate a
		/// second one in the same breath — "does the rank I am promoting into exist", "what mask
		/// does it hold" — and re-reading the ladder per question would turn one round trip into
		/// four, each able to observe a different edit.
		/// </remarks>
		public readonly IReadOnlyList<GuildRankData> Ladder;

		/// <summary>
		/// Creates a resolved standing.
		/// </summary>
		/// <param name="isMember">Whether a membership row was found.</param>
		/// <param name="guildID">The guild.</param>
		/// <param name="characterID">The character.</param>
		/// <param name="rankOrder">Ladder position.</param>
		/// <param name="permissions">Permission mask.</param>
		/// <param name="leaderRankOrder">Highest ladder position in the guild.</param>
		/// <param name="membershipVersion">Membership row concurrency token.</param>
		/// <param name="ladder">The guild's rank rows.</param>
		public GuildAuthority(bool isMember, long guildID, long characterID, byte rankOrder, GuildPermissions permissions, byte leaderRankOrder, long membershipVersion, IReadOnlyList<GuildRankData> ladder)
		{
			IsMember = isMember;
			GuildID = guildID;
			CharacterID = characterID;
			RankOrder = rankOrder;
			Permissions = permissions;
			LeaderRankOrder = leaderRankOrder;
			MembershipVersion = membershipVersion;
			Ladder = ladder;
		}

		/// <summary>
		/// A standing that permits nothing.
		/// </summary>
		/// <param name="guildID">The guild that was asked about.</param>
		/// <param name="characterID">The character that was asked about.</param>
		/// <returns>A non-member standing.</returns>
		public static GuildAuthority None(long guildID, long characterID)
		{
			return new GuildAuthority(false, guildID, characterID, 0, GuildPermissions.None, 0, 0, System.Array.Empty<GuildRankData>());
		}

		/// <summary>
		/// Whether this standing holds every bit of the requested permission.
		/// </summary>
		/// <param name="permission">The permission, or combination, required.</param>
		/// <returns>True when the holder may proceed.</returns>
		/// <remarks>
		/// Non-membership is folded in rather than left to the caller. A struct that answered
		/// "yes, this non-member has Invite" because its default mask happened to be non-empty is
		/// a bug waiting for one careless construction.
		/// </remarks>
		public bool Has(GuildPermissions permission)
		{
			return IsMember && (Permissions & permission) == permission;
		}

		/// <summary>
		/// Whether this standing is strictly senior to the given ladder position.
		/// </summary>
		/// <param name="otherRankOrder">The other character's ladder position.</param>
		/// <returns>True when the holder outranks the position.</returns>
		/// <remarks>
		/// STRICTLY. Equal ranks may not act on each other — two officers demoting each other in
		/// a loop is not a feature, and the leader seat being unique is what keeps a guild from
		/// having two people able to disband it out from under one another.
		/// </remarks>
		public bool Outranks(byte otherRankOrder)
		{
			return IsMember && RankOrder > otherRankOrder;
		}

		/// <summary>
		/// Whether this standing occupies the guild's top seat.
		/// </summary>
		/// <remarks>
		/// Computed from the ladder rather than compared against a constant, because a guild that
		/// has added ranks above the seeded three has a leader seat this file cannot know.
		/// </remarks>
		public bool IsLeader
		{
			get { return IsMember && LeaderRankOrder > 0 && RankOrder >= LeaderRankOrder; }
		}

		/// <summary>
		/// Finds a rank row by its ladder position.
		/// </summary>
		/// <param name="rankOrder">The position to look up.</param>
		/// <param name="rank">The matching row.</param>
		/// <returns>True when the guild has a rank at that position.</returns>
		public bool TryGetRank(byte rankOrder, out GuildRankData rank)
		{
			if (Ladder != null)
			{
				for (int i = 0; i < Ladder.Count; ++i)
				{
					if (Ladder[i].RankOrder == rankOrder)
					{
						rank = Ladder[i];
						return true;
					}
				}
			}

			rank = default;
			return false;
		}

		/// <summary>
		/// The permission mask held by a given ladder position in this guild.
		/// </summary>
		/// <param name="rankOrder">The position to look up.</param>
		/// <returns>The mask, or <see cref="GuildPermissions.None"/> when no such rank exists.</returns>
		/// <remarks>
		/// A position with no row returns nothing rather than falling back to the seeded defaults.
		/// The fallback exists for a guild whose ladder has not been written at all — a guild that
		/// HAS a ladder and is being asked about a position outside it is being asked about a rank
		/// that does not exist, and inventing permissions for it is how a deleted rank would keep
		/// working.
		/// </remarks>
		public GuildPermissions PermissionsAt(byte rankOrder)
		{
			return TryGetRank(rankOrder, out GuildRankData rank)
				? (GuildPermissions)rank.Permissions
				: GuildPermissions.None;
		}
	}
	/// <summary>
	/// The outcome of a guild permission decision.
	/// </summary>
	/// <remarks>
	/// Deliberately NOT <c>GuildResultType</c>. That type lives in the shared assembly alongside
	/// the broadcasts and drags the networking stack in with it; keeping the decision functions
	/// free of it is what lets them be compiled and exercised on their own, which is the whole
	/// point of having pulled them out of the async database methods. The handlers map these onto
	/// <c>GuildResultType</c> at the edge.
	/// </remarks>
	public enum GuildActionResult : byte
	{
		/// <summary>The action is permitted.</summary>
		Allowed = 0,
		/// <summary>The actor's rank does not hold the required permission, or is not senior enough.</summary>
		InsufficientRank = 1,
		/// <summary>The named rank does not exist in this guild.</summary>
		RankNotFound = 2,
		/// <summary>The action would leave the guild unable to administer itself.</summary>
		WouldOrphanGuild = 3,
	}

	/// <summary>
	/// The guild permission rules, as pure functions of a resolved standing.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Every server-side guild permission decision is one of these. They were originally inline in
	/// the async handlers, which meant the only way to exercise them was to stand up a database, a
	/// network connection and a scene server — so in practice they were not exercised at all, and
	/// a permission model nobody can test is a permission model nobody can trust.
	/// </para>
	/// <para>
	/// Pulled out here they take a <see cref="GuildAuthority"/> and some plain values, touch
	/// nothing else, and are called by exactly one place each. The handler keeps the IO; this
	/// keeps the policy.
	/// </para>
	/// </remarks>
	public static class GuildRules
	{
		/// <summary>
		/// May the actor remove the member holding the given rank?
		/// </summary>
		/// <param name="actor">The actor's standing.</param>
		/// <param name="targetRankOrder">The target's ladder position.</param>
		/// <returns>The decision.</returns>
		public static GuildActionResult CanKick(GuildAuthority actor, byte targetRankOrder)
		{
			if (!actor.Has(GuildPermissions.Kick))
			{
				return GuildActionResult.InsufficientRank;
			}

			// Strictly senior. Equal ranks may not remove each other.
			if (!actor.Outranks(targetRankOrder))
			{
				return GuildActionResult.InsufficientRank;
			}

			return GuildActionResult.Allowed;
		}

		/// <summary>
		/// May the actor move a member from one rank to another?
		/// </summary>
		/// <param name="actor">The actor's standing.</param>
		/// <param name="currentRankOrder">The member's present ladder position.</param>
		/// <param name="newRankOrder">The requested ladder position.</param>
		/// <returns>The decision.</returns>
		/// <remarks>
		/// Four rules. The destination must exist; neither the top seat may be entered nor left
		/// through this path (that is a leadership transfer, which demotes the outgoing holder in
		/// the same breath); the actor must outrank the member's CURRENT rank; and the actor must
		/// outrank the DESTINATION.
		///
		/// The last is the one that is easy to omit and is a direct escalation: without it an
		/// officer able to promote could move somebody — or a second character of their own —
		/// into a rank above their own, and be outranked by a seat they created.
		/// </remarks>
		public static GuildActionResult CanChangeMemberRank(GuildAuthority actor, byte currentRankOrder, byte newRankOrder)
		{
			if (!actor.Has(GuildPermissions.Promote))
			{
				return GuildActionResult.InsufficientRank;
			}

			if (!actor.TryGetRank(newRankOrder, out _))
			{
				return GuildActionResult.RankNotFound;
			}

			if (actor.LeaderRankOrder > 0 &&
				(currentRankOrder >= actor.LeaderRankOrder || newRankOrder >= actor.LeaderRankOrder))
			{
				return GuildActionResult.InsufficientRank;
			}

			if (!actor.Outranks(currentRankOrder) || !actor.Outranks(newRankOrder))
			{
				return GuildActionResult.InsufficientRank;
			}

			return GuildActionResult.Allowed;
		}

		/// <summary>
		/// May the actor change a rank's name and permissions?
		/// </summary>
		/// <param name="actor">The actor's standing.</param>
		/// <param name="rankOrder">The rank being edited.</param>
		/// <param name="proposed">The mask the rank would end up with.</param>
		/// <returns>The decision.</returns>
		/// <remarks>
		/// <para>
		/// The grant check compares only the bits being ADDED against the actor's own mask. A rank
		/// that already holds a permission the actor lacks keeps it: removing it is not an
		/// escalation, and refusing the whole edit over it would make such a rank uneditable
		/// forever by anyone below the person who granted it.
		/// </para>
		/// <para>
		/// <b>The actor's own row may be RENAMED, never re-permissioned.</b> Seniority is strict
		/// for any change to a mask, because editing the seat you occupy is the shortest path
		/// from "may edit ranks" to "may do anything". A rename carries no such path — the mask
		/// is compared bit for bit and must be identical — and without the exception the guild's
		/// top rank could never be called anything but what it was seeded as, since nobody
		/// outranks the leader.
		/// </para>
		/// </remarks>
		public static GuildActionResult CanEditRank(GuildAuthority actor, byte rankOrder, GuildPermissions proposed)
		{
			if (!actor.Has(GuildPermissions.EditRanks))
			{
				return GuildActionResult.InsufficientRank;
			}

			if (!actor.TryGetRank(rankOrder, out GuildRankData existing))
			{
				return GuildActionResult.RankNotFound;
			}

			GuildPermissions current = (GuildPermissions)existing.Permissions;

			/* Strictly below the actor's own rank for any change to the MASK. The one thing
			 * permitted on the actor's own row is a rename: the seat's powers are untouched, so
			 * there is nothing to escalate into. Rows above the actor stay off limits entirely —
			 * a rank administrator renaming the leader's rank is not theirs to do. */
			if (!actor.Outranks(rankOrder))
			{
				bool isOwnRow = actor.IsMember && rankOrder == actor.RankOrder;
				if (!isOwnRow || proposed != current)
				{
					return GuildActionResult.InsufficientRank;
				}
			}

			GuildPermissions added = proposed & ~current;
			if ((added & ~actor.Permissions) != GuildPermissions.None)
			{
				return GuildActionResult.InsufficientRank;
			}

			if (WouldOrphanRankAdministration(actor.Ladder, rankOrder, proposed))
			{
				return GuildActionResult.WouldOrphanGuild;
			}

			return GuildActionResult.Allowed;
		}

		/// <summary>
		/// May the actor insert a rank at the given position with the given permissions?
		/// </summary>
		/// <param name="actor">The actor's standing.</param>
		/// <param name="rankOrder">The position the new rank would take.</param>
		/// <param name="proposed">The requested mask.</param>
		/// <returns>The decision.</returns>
		/// <remarks>
		/// <para><b>The position is an insertion point, not a free slot.</b> Everything already at
		/// or above <paramref name="rankOrder"/> moves up one rung — see
		/// <c>IGuildRankService.InsertAsync</c>. It has to work that way: a guild is seeded with
		/// three contiguous ranks and the actor may only create below their own seat, so a leader
		/// at order 3 has orders 1 and 2 to choose from and both are already taken. Requiring a
		/// free position meant no guild could ever add a rank.</para>
		///
		/// <para><b>Which is why the actor's OWN order is allowed here</b>, where
		/// <see cref="CanEditRank"/> refuses it. The two are not the same act. Editing your own
		/// row is the shortest path from "may edit ranks" to "may do anything": you add the bits
		/// you want to the seat you already occupy. Inserting at your own order adds a rank
		/// BELOW you and moves your row up with the rest of the ladder — it cannot change your own
		/// permissions, and every seniority comparison in the guild is preserved because every
		/// rank at or above the point moves by the same one. Refusing it would mean the most
		/// useful placement, a new tier directly under the person creating it, was the one
		/// placement nobody could ask for.</para>
		///
		/// <para>Headroom is NOT checked here. Whether the top of the ladder can move up one is a
		/// fact about rows this struct does not necessarily hold in full, and the service settles
		/// it inside the same transaction that performs the shift.</para>
		/// </remarks>
		public static GuildActionResult CanCreateRank(GuildAuthority actor, byte rankOrder, GuildPermissions proposed)
		{
			if (!actor.Has(GuildPermissions.EditRanks))
			{
				return GuildActionResult.InsufficientRank;
			}

			if (rankOrder < GuildRankDefaults.MinRankOrder || rankOrder > GuildRankDefaults.MaxRankOrder)
			{
				return GuildActionResult.RankNotFound;
			}

			/* At or below the actor's own seat. Above it would let a rank administrator create a
			 * seat senior to their own and then be promoted into it — the escalation the strict
			 * comparison in CanEditRank exists to stop. */
			if (!actor.IsMember || actor.RankOrder < rankOrder)
			{
				return GuildActionResult.InsufficientRank;
			}

			/* A NEW rank has no existing permissions to preserve, so the whole proposed mask is
			 * being granted and the whole of it must be within the actor's own. */
			if ((proposed & ~actor.Permissions) != GuildPermissions.None)
			{
				return GuildActionResult.InsufficientRank;
			}

			return GuildActionResult.Allowed;
		}

		/// <summary>
		/// May the actor remove a rank?
		/// </summary>
		/// <param name="actor">The actor's standing.</param>
		/// <param name="rankOrder">The rank to remove.</param>
		/// <returns>The decision.</returns>
		/// <remarks>
		/// Whether the rank still has MEMBERS is not decided here — that is a race the database
		/// settles in the same statement as the delete. What is decided here is whether the guild
		/// would still work afterwards.
		/// </remarks>
		public static GuildActionResult CanDeleteRank(GuildAuthority actor, byte rankOrder)
		{
			if (!actor.Has(GuildPermissions.EditRanks))
			{
				return GuildActionResult.InsufficientRank;
			}

			if (!actor.TryGetRank(rankOrder, out _))
			{
				return GuildActionResult.RankNotFound;
			}

			if (!actor.Outranks(rankOrder))
			{
				return GuildActionResult.InsufficientRank;
			}

			/* A guild needs a seat to admit new members into and a seat to lead from. Below two
			 * rungs there is nowhere to put somebody who joins. */
			if (actor.Ladder != null && actor.Ladder.Count <= 2)
			{
				return GuildActionResult.WouldOrphanGuild;
			}

			// Removing the rank takes its permissions with it.
			if (WouldOrphanRankAdministration(actor.Ladder, rankOrder, GuildPermissions.None))
			{
				return GuildActionResult.WouldOrphanGuild;
			}

			return GuildActionResult.Allowed;
		}

		/// <summary>
		/// May the actor hand the guild's top seat to somebody else?
		/// </summary>
		/// <param name="actor">The actor's standing.</param>
		/// <returns>The decision.</returns>
		/// <remarks>
		/// Stricter than the permission alone: the actor must also currently OCCUPY the top seat.
		/// A rank editor could grant <c>TransferLeadership</c> to a subordinate rank, and giving
		/// away the leader's own seat is not something a subordinate should be able to do.
		/// </remarks>
		public static GuildActionResult CanTransferLeadership(GuildAuthority actor)
		{
			if (!actor.Has(GuildPermissions.TransferLeadership) || !actor.IsLeader)
			{
				return GuildActionResult.InsufficientRank;
			}

			return GuildActionResult.Allowed;
		}

		/// <summary>
		/// May the actor perform an action gated by a single permission and nothing else?
		/// </summary>
		/// <param name="actor">The actor's standing.</param>
		/// <param name="permission">The permission required.</param>
		/// <returns>The decision.</returns>
		/// <remarks>
		/// Covers invite, disband, the two text edits, the two note edits, recruitment editing and
		/// the application queue — every action whose only question is "do you hold the bit".
		/// </remarks>
		public static GuildActionResult CanUse(GuildAuthority actor, GuildPermissions permission)
		{
			return actor.Has(permission) ? GuildActionResult.Allowed : GuildActionResult.InsufficientRank;
		}

		/// <summary>
		/// Would this change leave no rank able to administer ranks?
		/// </summary>
		/// <param name="ladder">The guild's rank rows.</param>
		/// <param name="rankOrder">The rank being changed or removed.</param>
		/// <param name="newPermissions">The mask that rank would end up with.</param>
		/// <returns>True when the change must be refused.</returns>
		/// <remarks>
		/// <para>
		/// The soft-lock guard. A guild whose only rank holding <c>EditRanks</c> gives it up can
		/// never get it back — editing ranks is the permission that would be needed — and cannot
		/// promote anybody into a rank that has it, because there is no such rank. The guild is
		/// then frozen in whatever shape it was in, permanently, with no in-game recovery.
		/// </para>
		/// <para>
		/// Only <c>EditRanks</c> is protected this way. It is the one permission whose absence
		/// prevents its own restoration; a guild that loses every <c>Invite</c> can still edit a
		/// rank to grant it back.
		/// </para>
		/// </remarks>
		public static bool WouldOrphanRankAdministration(IReadOnlyList<GuildRankData> ladder, byte rankOrder, GuildPermissions newPermissions)
		{
			if ((newPermissions & GuildPermissions.EditRanks) == GuildPermissions.EditRanks)
			{
				return false;
			}

			if (ladder == null)
			{
				return true;
			}

			for (int i = 0; i < ladder.Count; ++i)
			{
				GuildRankData rank = ladder[i];
				if (rank.RankOrder == rankOrder)
				{
					// The rank under change, evaluated with its PROPOSED mask, which lacks the flag.
					continue;
				}

				if (((GuildPermissions)rank.Permissions & GuildPermissions.EditRanks) == GuildPermissions.EditRanks)
				{
					return false;
				}
			}

			return true;
		}
	}
}
