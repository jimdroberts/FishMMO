using System.Collections.Generic;
using FishMMO.Server.Implementation.World.SceneServer;
using FishMMO.Shared;
using FishMMO.Database.Data;
using NUnit.Framework;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Proofs for the rules that decide who may edit or delete a guild rank.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <see cref="GuildRules.CanEditRank"/> is the escalation surface of the whole permission
	/// model: an edit is the only way a rank's mask changes, so every "may not grant what you do
	/// not hold" and "may not touch a seat at or above your own" rule is exercised here. The
	/// tests pin each refusal separately, because each one is a distinct escalation when absent.
	/// </para>
	/// <para>
	/// One relaxation is pinned alongside them. A rank's own holder may RENAME it — the mask sent
	/// must be identical to the one stored — because without that the guild's top rank could
	/// never be called anything but what it was seeded as: nobody outranks the leader, so nobody
	/// could ever edit the leader's row.
	/// </para>
	/// </remarks>
	public class GuildRankEditTests
	{
		/// <summary>Guild the fixtures below belong to.</summary>
		private const long GuildID = 11;

		/// <summary>The ladder a freshly seeded guild has: 1, 2 and 3.</summary>
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

		/// <summary>A standing on the given ladder at the given order with the given powers.</summary>
		private static GuildAuthority Standing(byte rankOrder, GuildPermissions permissions, IReadOnlyList<GuildRankData> ladder = null)
		{
			ladder = ladder ?? SeededLadder();

			byte leader = 0;
			for (int i = 0; i < ladder.Count; ++i)
			{
				if (ladder[i].RankOrder > leader)
				{
					leader = ladder[i].RankOrder;
				}
			}

			return new GuildAuthority(
				isMember: true,
				guildID: GuildID,
				characterID: 100,
				rankOrder: rankOrder,
				permissions: permissions,
				leaderRankOrder: leader,
				membershipVersion: 1,
				ladder: ladder);
		}

		/// <summary>The leader of a guild that has never edited its ranks.</summary>
		private static GuildAuthority Leader()
		{
			return Standing(GuildRankDefaults.DefaultLeaderRankOrder, GuildRankDefaults.LeaderPermissions);
		}

		/// <summary>An officer who has been given rank administration.</summary>
		private static GuildAuthority RankEditingOfficer()
		{
			return Standing(GuildRankDefaults.OfficerRankOrder, GuildRankDefaults.OfficerPermissions | GuildPermissions.EditRanks);
		}

		// ------------------------------------------------------------------ rename of own row

		/// <summary>
		/// The relaxation: the leader may rename the leader rank, because the mask is unchanged.
		/// </summary>
		[Test]
		public void Leader_MayRenameTheirOwnRank()
		{
			GuildActionResult result = GuildRules.CanEditRank(
				Leader(),
				GuildRankDefaults.DefaultLeaderRankOrder,
				GuildRankDefaults.LeaderPermissions);

			Assert.AreEqual(GuildActionResult.Allowed, result,
				"An edit of the actor's own row that carries the row's existing mask is a rename, and a rename escalates nothing.");
		}

		/// <summary>The same relaxation applies below the top seat.</summary>
		[Test]
		public void RankEditor_MayRenameTheirOwnRank()
		{
			GuildAuthority officer = RankEditingOfficer();
			GuildPermissions stored = (GuildPermissions)SeededLadder()[1].Permissions;

			Assert.AreEqual(GuildActionResult.Allowed,
				GuildRules.CanEditRank(officer, GuildRankDefaults.OfficerRankOrder, stored));
		}

		/// <summary>
		/// The ceiling that the relaxation must not move: the actor's own MASK may not change,
		/// not even to drop a bit.
		/// </summary>
		/// <remarks>
		/// Adding a bit is the obvious escalation. Removing one is refused too, because "own row,
		/// unchanged mask" is a far simpler invariant to trust than "own row, no bit added" — and
		/// a rank editor has no business re-permissioning the seat they sit in either way.
		/// </remarks>
		[Test]
		public void NobodyMayChangeTheirOwnMask()
		{
			GuildAuthority officer = RankEditingOfficer();
			GuildPermissions stored = (GuildPermissions)SeededLadder()[1].Permissions;

			Assert.AreEqual(GuildActionResult.InsufficientRank,
				GuildRules.CanEditRank(officer, GuildRankDefaults.OfficerRankOrder, stored | GuildPermissions.EditRanks),
				"Adding a bit to your own seat is the shortest path from 'may edit ranks' to 'may do anything'.");

			Assert.AreEqual(GuildActionResult.InsufficientRank,
				GuildRules.CanEditRank(officer, GuildRankDefaults.OfficerRankOrder, stored & ~GuildPermissions.Invite),
				"Removing a bit from your own seat is still an edit of your own mask.");

			Assert.AreEqual(GuildActionResult.InsufficientRank,
				GuildRules.CanEditRank(Leader(), GuildRankDefaults.DefaultLeaderRankOrder, GuildPermissions.All & ~GuildPermissions.Kick),
				"The leader's own mask is no more editable than anybody else's.");
		}

		/// <summary>Rows ABOVE the actor stay off limits, rename included.</summary>
		[Test]
		public void NobodyMayEditASuperiorRank_EvenToRenameIt()
		{
			GuildAuthority officer = RankEditingOfficer();

			Assert.AreEqual(GuildActionResult.InsufficientRank,
				GuildRules.CanEditRank(officer, GuildRankDefaults.DefaultLeaderRankOrder, GuildRankDefaults.LeaderPermissions),
				"An unchanged mask on a SUPERIOR row is not the actor's to send.");
		}

		// ------------------------------------------------------------------ rows below the actor

		/// <summary>A rank editor may re-permission a row below them within their own powers.</summary>
		[Test]
		public void RankEditor_MayGrantWhatTheyHold_ToARankBelow()
		{
			Assert.AreEqual(GuildActionResult.Allowed,
				GuildRules.CanEditRank(RankEditingOfficer(), GuildRankDefaults.MemberRankOrder, GuildPermissions.Invite));
		}

		/// <summary>The classic escalation: granting a bit the actor does not hold.</summary>
		[Test]
		public void RankEditor_MayNotGrantWhatTheyLack()
		{
			Assert.AreEqual(GuildActionResult.InsufficientRank,
				GuildRules.CanEditRank(RankEditingOfficer(), GuildRankDefaults.MemberRankOrder, GuildPermissions.Disband));
		}

		/// <summary>
		/// A bit the row already holds and the actor lacks may be REMOVED — that is not an
		/// escalation — and it may also be left alone.
		/// </summary>
		[Test]
		public void RankEditor_MayRemoveOrKeepABitTheyLack_ButNotAddOne()
		{
			List<GuildRankData> ladder = new List<GuildRankData>(SeededLadder());
			// The member rank has somehow been granted Disband by a leader.
			ladder[0] = new GuildRankData(1, 2, GuildID, GuildRankDefaults.MemberRankOrder,
				GuildRankDefaults.MemberRankName, (long)GuildPermissions.Disband);

			GuildAuthority officer = Standing(
				GuildRankDefaults.OfficerRankOrder,
				GuildRankDefaults.OfficerPermissions | GuildPermissions.EditRanks,
				ladder);

			Assert.AreEqual(GuildActionResult.Allowed,
				GuildRules.CanEditRank(officer, GuildRankDefaults.MemberRankOrder, GuildPermissions.None),
				"Removing Disband from a rank is not an escalation, whoever does it.");

			Assert.AreEqual(GuildActionResult.Allowed,
				GuildRules.CanEditRank(officer, GuildRankDefaults.MemberRankOrder, GuildPermissions.Disband | GuildPermissions.Invite),
				"A rank that already holds a bit keeps it; only the ADDED bits are checked against the actor.");

			Assert.AreEqual(GuildActionResult.InsufficientRank,
				GuildRules.CanEditRank(officer, GuildRankDefaults.MemberRankOrder, GuildPermissions.Disband | GuildPermissions.TransferLeadership),
				"An added bit the actor lacks is refused even when an existing one is preserved.");
		}

		/// <summary>Editing needs the permission, whatever the seniority.</summary>
		[Test]
		public void WithoutEditRanks_NothingMayBeEdited()
		{
			GuildAuthority officer = Standing(GuildRankDefaults.OfficerRankOrder, GuildRankDefaults.OfficerPermissions);

			Assert.AreEqual(GuildActionResult.InsufficientRank,
				GuildRules.CanEditRank(officer, GuildRankDefaults.MemberRankOrder, GuildPermissions.None));
			Assert.AreEqual(GuildActionResult.InsufficientRank,
				GuildRules.CanEditRank(officer, GuildRankDefaults.OfficerRankOrder, GuildRankDefaults.OfficerPermissions),
				"Renaming your own rank still requires EditRanks.");
		}

		/// <summary>A position with no row is reported as such, before any seniority question.</summary>
		[Test]
		public void EditingAMissingRank_IsRankNotFound()
		{
			Assert.AreEqual(GuildActionResult.RankNotFound,
				GuildRules.CanEditRank(Leader(), 9, GuildPermissions.None));
		}

		// ------------------------------------------------------------------ soft-lock guard

		/// <summary>
		/// The last rank holding EditRanks may not give it up.
		/// </summary>
		[Test]
		public void TheLastRankAdministrator_MayNotBeStrippedOfEditRanks()
		{
			// Only the officer rank administers ranks; the leader here is a mask-only fixture
			// sitting on a ladder where the seeded leader row has lost EditRanks.
			List<GuildRankData> ladder = new List<GuildRankData>(SeededLadder());
			ladder[2] = new GuildRankData(3, 2, GuildID, GuildRankDefaults.DefaultLeaderRankOrder,
				GuildRankDefaults.LeaderRankName, (long)(GuildPermissions.All & ~GuildPermissions.EditRanks));
			ladder[1] = new GuildRankData(2, 2, GuildID, GuildRankDefaults.OfficerRankOrder,
				GuildRankDefaults.OfficerRankName, (long)(GuildRankDefaults.OfficerPermissions | GuildPermissions.EditRanks));

			GuildAuthority leader = Standing(GuildRankDefaults.DefaultLeaderRankOrder, GuildPermissions.All, ladder);

			Assert.AreEqual(GuildActionResult.WouldOrphanGuild,
				GuildRules.CanEditRank(leader, GuildRankDefaults.OfficerRankOrder, GuildRankDefaults.OfficerPermissions),
				"Taking EditRanks off the only rank that holds it soft-locks rank administration forever.");

			Assert.AreEqual(GuildActionResult.WouldOrphanGuild,
				GuildRules.CanDeleteRank(leader, GuildRankDefaults.OfficerRankOrder),
				"Deleting the only rank that holds EditRanks is the same soft-lock.");
		}

		/// <summary>The guard is about OTHER rows: with a second administrator, the edit goes through.</summary>
		[Test]
		public void EditRanks_MayBeRemoved_WhileAnotherRankHoldsIt()
		{
			List<GuildRankData> ladder = new List<GuildRankData>(SeededLadder());
			ladder[1] = new GuildRankData(2, 2, GuildID, GuildRankDefaults.OfficerRankOrder,
				GuildRankDefaults.OfficerRankName, (long)(GuildRankDefaults.OfficerPermissions | GuildPermissions.EditRanks));

			GuildAuthority leader = Standing(GuildRankDefaults.DefaultLeaderRankOrder, GuildPermissions.All, ladder);

			Assert.AreEqual(GuildActionResult.Allowed,
				GuildRules.CanEditRank(leader, GuildRankDefaults.OfficerRankOrder, GuildRankDefaults.OfficerPermissions),
				"The seeded leader row still holds EditRanks, so nobody is orphaned.");
		}

		// ------------------------------------------------------------------ delete

		/// <summary>A leader may delete an unoccupied subordinate rank on a three-rung ladder.</summary>
		[Test]
		public void Leader_MayDeleteASubordinateRank()
		{
			Assert.AreEqual(GuildActionResult.Allowed,
				GuildRules.CanDeleteRank(Leader(), GuildRankDefaults.OfficerRankOrder));
		}

		/// <summary>Nobody may delete their own seat or one above it.</summary>
		[Test]
		public void NobodyMayDeleteTheirOwnSeatOrASuperiorOne()
		{
			Assert.AreEqual(GuildActionResult.InsufficientRank,
				GuildRules.CanDeleteRank(Leader(), GuildRankDefaults.DefaultLeaderRankOrder),
				"The leader's seat is the one row no edit path may remove.");

			Assert.AreEqual(GuildActionResult.InsufficientRank,
				GuildRules.CanDeleteRank(RankEditingOfficer(), GuildRankDefaults.OfficerRankOrder));
			Assert.AreEqual(GuildActionResult.InsufficientRank,
				GuildRules.CanDeleteRank(RankEditingOfficer(), GuildRankDefaults.DefaultLeaderRankOrder));
		}

		/// <summary>A guild keeps at least a seat to lead from and a seat to admit into.</summary>
		[Test]
		public void ATwoRungLadder_MayNotShrink()
		{
			List<GuildRankData> ladder = new List<GuildRankData>()
			{
				new GuildRankData(1, 1, GuildID, 1, "Member", (long)GuildPermissions.None),
				new GuildRankData(2, 1, GuildID, 2, "Leader", (long)GuildPermissions.All),
			};

			GuildAuthority leader = Standing(2, GuildPermissions.All, ladder);

			Assert.AreEqual(GuildActionResult.WouldOrphanGuild, GuildRules.CanDeleteRank(leader, 1));
		}

		/// <summary>Deleting a rank that does not exist is reported as such.</summary>
		[Test]
		public void DeletingAMissingRank_IsRankNotFound()
		{
			Assert.AreEqual(GuildActionResult.RankNotFound, GuildRules.CanDeleteRank(Leader(), 9));
		}
	}
}
