using System.Collections.Generic;
using FishMMO.Server.Implementation.World.SceneServer;
using FishMMO.Shared;
using FishMMO.Database.Data;
using NUnit.Framework;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Proofs for the rule that decides where a new guild rank may be inserted.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A guild is seeded with three CONTIGUOUS rank orders — member 1, officer 2, leader 3 — and
	/// <c>character_guild.rank</c> stores the same byte, which is why the ladder is anchored at
	/// those numbers rather than renumbered into something with gaps in it.
	/// </para>
	/// <para>
	/// The consequence is the bug these tests pin. While <see cref="GuildRules.CanCreateRank"/>
	/// required the new rank to sit STRICTLY below the actor, a leader at order 3 could choose
	/// only from orders 1 and 2 and both were already occupied — so no guild that had never
	/// deleted a rank could ever add one, and the client hid its own Add Rank button permanently
	/// because it could find no free slot to offer. Creating a rank is therefore an INSERT that
	/// shifts the ladder up, and the position may equal the actor's own seat.
	/// </para>
	/// <para>
	/// What must NOT relax with it is the ceiling. A rank administrator who could insert above
	/// their own seat could create a seat senior to their own and be promoted into it, which is
	/// the escalation the strict comparison in <c>CanEditRank</c> still exists to prevent.
	/// </para>
	/// </remarks>
	public class GuildRankInsertTests
	{
		/// <summary>Guild the fixtures below belong to.</summary>
		private const long GuildID = 7;

		/// <summary>The ladder a freshly seeded guild has: 1, 2 and 3, with no gaps.</summary>
		private static IReadOnlyList<GuildRankData> SeededLadder()
		{
			return new List<GuildRankData>()
			{
				new GuildRankData(1, 1, GuildID, GuildRankDefaults.MemberRankOrder,
					GuildRankDefaults.MemberRankName, (long)GuildRankDefaults.MemberPermissions),
				new GuildRankData(2, 1, GuildID, GuildRankDefaults.OfficerRankOrder,
					GuildRankDefaults.OfficerRankName, (long)GuildRankDefaults.OfficerPermissions),
				new GuildRankData(3, 1, GuildID, GuildRankDefaults.DefaultLeaderRankOrder,
					GuildRankDefaults.LeaderRankName, (long)GuildRankDefaults.LeaderPermissions),
			};
		}

		/// <summary>A standing on the seeded ladder at the given order with the given powers.</summary>
		private static GuildAuthority Standing(byte rankOrder, GuildPermissions permissions)
		{
			return new GuildAuthority(
				isMember: true,
				guildID: GuildID,
				characterID: 100,
				rankOrder: rankOrder,
				permissions: permissions,
				leaderRankOrder: GuildRankDefaults.DefaultLeaderRankOrder,
				membershipVersion: 1,
				ladder: SeededLadder());
		}

		/// <summary>The leader of a guild that has never edited its ranks.</summary>
		private static GuildAuthority Leader()
		{
			return Standing(GuildRankDefaults.DefaultLeaderRankOrder, GuildRankDefaults.LeaderPermissions);
		}

		/// <summary>
		/// The regression itself: a leader of a seeded guild can create a rank.
		/// </summary>
		/// <remarks>
		/// Every order below the leader's is occupied, so this passes only because the position is
		/// an insertion point rather than a free slot.
		/// </remarks>
		[Test]
		public void Leader_MayInsertARankAtTheirOwnOrder()
		{
			GuildActionResult result = GuildRules.CanCreateRank(
				Leader(),
				GuildRankDefaults.DefaultLeaderRankOrder,
				GuildPermissions.None);

			Assert.AreEqual(GuildActionResult.Allowed, result,
				"A leader on the seeded 1/2/3 ladder must be able to add a rank; every order below them is taken.");
		}

		/// <summary>An occupied position lower down is still a legal insertion point.</summary>
		[Test]
		public void Leader_MayInsertBetweenExistingRanks()
		{
			Assert.AreEqual(GuildActionResult.Allowed,
				GuildRules.CanCreateRank(Leader(), GuildRankDefaults.MemberRankOrder, GuildPermissions.None));
			Assert.AreEqual(GuildActionResult.Allowed,
				GuildRules.CanCreateRank(Leader(), GuildRankDefaults.OfficerRankOrder, GuildPermissions.None));
		}

		/// <summary>
		/// The ceiling that did not move: nobody may insert above their own seat.
		/// </summary>
		/// <remarks>
		/// An officer who could insert at the leader's order would be creating a seat senior to
		/// their own — one promotion away from owning the guild.
		/// </remarks>
		[Test]
		public void Officer_MayNotInsertAboveTheirOwnSeat()
		{
			GuildAuthority officer = Standing(
				GuildRankDefaults.OfficerRankOrder,
				GuildRankDefaults.OfficerPermissions | GuildPermissions.EditRanks);

			Assert.AreEqual(GuildActionResult.InsufficientRank,
				GuildRules.CanCreateRank(officer, GuildRankDefaults.DefaultLeaderRankOrder, GuildPermissions.None),
				"Inserting above your own seat creates a rank senior to you.");
		}

		/// <summary>An officer may still insert at or below their own seat.</summary>
		[Test]
		public void Officer_MayInsertAtOrBelowTheirOwnSeat()
		{
			GuildAuthority officer = Standing(
				GuildRankDefaults.OfficerRankOrder,
				GuildRankDefaults.OfficerPermissions | GuildPermissions.EditRanks);

			Assert.AreEqual(GuildActionResult.Allowed,
				GuildRules.CanCreateRank(officer, GuildRankDefaults.OfficerRankOrder, GuildPermissions.None));
			Assert.AreEqual(GuildActionResult.Allowed,
				GuildRules.CanCreateRank(officer, GuildRankDefaults.MemberRankOrder, GuildPermissions.None));
		}

		/// <summary>Rank administration is still required, whatever the position.</summary>
		[Test]
		public void WithoutEditRanks_NothingMayBeInserted()
		{
			GuildAuthority member = Standing(GuildRankDefaults.MemberRankOrder, GuildPermissions.None);

			Assert.AreEqual(GuildActionResult.InsufficientRank,
				GuildRules.CanCreateRank(member, GuildRankDefaults.MemberRankOrder, GuildPermissions.None));
		}

		/// <summary>
		/// A new rank may hold only permissions the creator already holds.
		/// </summary>
		/// <remarks>
		/// The whole proposed mask is checked, not the difference: a new rank has no existing
		/// permissions to preserve, so all of it is being granted.
		/// </remarks>
		[Test]
		public void ANewRankMayNotHoldWhatTheCreatorLacks()
		{
			GuildAuthority officer = Standing(
				GuildRankDefaults.OfficerRankOrder,
				GuildRankDefaults.OfficerPermissions | GuildPermissions.EditRanks);

			Assert.AreEqual(GuildActionResult.InsufficientRank,
				GuildRules.CanCreateRank(officer, GuildRankDefaults.MemberRankOrder, GuildPermissions.Disband),
				"An officer without Disband must not be able to create a rank that has it.");

			Assert.AreEqual(GuildActionResult.Allowed,
				GuildRules.CanCreateRank(officer, GuildRankDefaults.MemberRankOrder, GuildPermissions.Invite),
				"A permission the creator holds may be granted.");
		}

		/// <summary>Positions outside the legal range are refused before anything else is decided.</summary>
		[Test]
		public void PositionsOutsideTheLadderAreRefused()
		{
			Assert.AreEqual(GuildActionResult.RankNotFound,
				GuildRules.CanCreateRank(Leader(), 0, GuildPermissions.None),
				"Zero means 'not in a guild' and is never a rank.");
		}

		/// <summary>A non-member's standing permits nothing, whatever mask it carries.</summary>
		[Test]
		public void ANonMemberMayNotInsert()
		{
			Assert.AreEqual(GuildActionResult.InsufficientRank,
				GuildRules.CanCreateRank(GuildAuthority.None(GuildID, 100), 1, GuildPermissions.None));
		}
	}
}
