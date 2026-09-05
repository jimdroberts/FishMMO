using FishMMO.Shared;
using NUnit.Framework;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Tests for who may do what on a plot.
	/// </summary>
	/// <remarks>
	/// The rule these pin is "locked by default": a plot admits its owner, admits exactly the people
	/// the owner named, and admits nobody else. Every way that could go wrong lets somebody into a
	/// house they were not invited to, and none of them announce themselves — an access bug looks
	/// like nothing at all until a player notices a stranger in their living room.
	/// </remarks>
	[TestFixture]
	public class PlotAccessTests
	{
		private const long Owner = 100;
		private const long Friend = 200;
		private const long Stranger = 300;
		private const long OwningGuild = 900;
		private const long OtherGuild = 901;

		private static PlotOwner CharacterOwned => PlotOwner.ForCharacter(Owner);
		private static PlotOwner GuildOwned => PlotOwner.ForGuild(OwningGuild);

		[Test]
		public void Owner_HoldsEveryPermission_OnAnOccupiedPlot()
		{
			PlotPermission held = PlotAccess.Resolve(PlotState.Occupied, CharacterOwned, Owner, 0, PlotPermission.None);

			Assert.AreEqual(PlotPermission.All, held, "The owner should hold every permission without needing a grant.");
		}

		[Test]
		public void Stranger_HoldsNothing_OnAnOccupiedPlot()
		{
			PlotPermission held = PlotAccess.Resolve(PlotState.Occupied, CharacterOwned, Stranger, 0, PlotPermission.None);

			Assert.AreEqual(PlotPermission.None, held, "Houses are locked by default; an uninvited player holds nothing.");
		}

		[Test]
		public void Friend_HoldsExactlyWhatTheyWereGranted()
		{
			PlotPermission granted = PlotPermission.Enter | PlotPermission.PlaceItems;

			PlotPermission held = PlotAccess.Resolve(PlotState.Occupied, CharacterOwned, Friend, 0, granted);

			Assert.AreEqual(granted, held, "A grant should be honoured exactly, neither widened nor narrowed.");
		}

		[Test]
		public void EmptyLot_AdmitsAnybody_ButGrantsNothingElse()
		{
			PlotPermission held = PlotAccess.Resolve(PlotState.Empty, PlotOwner.None, Stranger, 0, PlotPermission.None);

			Assert.AreEqual(PlotPermission.Enter, held,
				"An unclaimed lot is a piece of the public world, so people may cross it — and do nothing more.");
		}

		[Test]
		public void AbandonedPlot_AdmitsNobody()
		{
			/* Including somebody holding a grant from the previous owner. This is the case the state
			 * check exists for: reclamation clears grants, but that is a write that can fail, and an
			 * abandoned house must not stay open to the old owner's friends if it does. */
			PlotPermission held = PlotAccess.Resolve(PlotState.Abandoned, PlotOwner.None, Friend, 0, PlotPermission.All);

			Assert.AreEqual(PlotPermission.None, held, "An abandoned plot has no owner to admit anybody.");
		}

		[Test]
		public void BuildingPlot_ShutsOutEvenAGrantedFriend()
		{
			PlotPermission held = PlotAccess.Resolve(PlotState.Building, CharacterOwned, Friend, 0, PlotPermission.All);

			Assert.AreEqual(PlotPermission.None, held,
				"A building site is closed to visitors: structures are appearing and vanishing around them.");
		}

		[Test]
		public void BuildingPlot_StillAdmitsItsOwner()
		{
			PlotPermission held = PlotAccess.Resolve(PlotState.Building, CharacterOwned, Owner, 0, PlotPermission.None);

			Assert.AreEqual(PlotPermission.All, held, "The owner is the one person who must be able to enter a building site.");
		}

		[Test]
		public void GuildMember_IsTheOwnerOfGuildLand()
		{
			PlotPermission held = PlotAccess.Resolve(PlotState.Occupied, GuildOwned, Stranger, OwningGuild, PlotPermission.None);

			Assert.AreEqual(PlotPermission.All, held, "A member of the owning guild owns the plot.");
		}

		[Test]
		public void MemberOfAnotherGuild_IsNotTheOwner()
		{
			PlotPermission held = PlotAccess.Resolve(PlotState.Occupied, GuildOwned, Stranger, OtherGuild, PlotPermission.None);

			Assert.AreEqual(PlotPermission.None, held, "Belonging to some guild is not belonging to the owning one.");
		}

		[Test]
		public void NoGuild_NeverMatchesGuildOwnedLand()
		{
			/* Zero is the "no guild" sentinel and the owner column can never be zero for guild-owned
			 * land — but a comparison that let them meet would hand every guildless player in the
			 * game the keys to a guild hall. */
			Assert.IsFalse(PlotAccess.IsOwner(GuildOwned, Stranger, 0),
				"A character in no guild must never match guild ownership.");
		}

		[Test]
		public void UnknownPermissionBits_AreDropped()
		{
			int withNonsense = (int)PlotPermission.Enter | (1 << 20);

			Assert.AreEqual(PlotPermission.Enter, PlotAccess.Sanitize(withNonsense),
				"A bit this build has no name for must not survive to be reinterpreted later.");
		}

		[Test]
		public void Granting_RequiresTheInvitePermission()
		{
			PlotPermission granted = PlotAccess.ClampGrant(PlotPermission.Enter | PlotPermission.PlaceItems, PlotPermission.Enter);

			Assert.AreEqual(PlotPermission.None, granted, "Somebody without InviteFriends may grant nothing at all.");
		}

		[Test]
		public void Granting_CannotExceedWhatTheGranterHolds()
		{
			/* The attack this closes: a friend given only Enter and InviteFriends hands themselves,
			 * or a stranger, the right to strip the house. Without the clamp the owner's careful
			 * choice of what to trust somebody with means nothing. */
			PlotPermission granterHolds = PlotPermission.Enter | PlotPermission.InviteFriends;

			PlotPermission granted = PlotAccess.ClampGrant(granterHolds, PlotPermission.All);

			Assert.AreEqual(granterHolds, granted, "A grant must be the intersection of what was asked and what is held.");
		}

		[Test]
		public void Owner_GrantingEverything_IsNotClamped()
		{
			PlotPermission granted = PlotAccess.ClampGrant(PlotPermission.All, PlotPermission.All);

			Assert.AreEqual(PlotPermission.All, granted, "The owner is the source of the authority, not a holder of it.");
		}

		[Test]
		public void AllowsEntry_TracksTheEnterFlag()
		{
			Assert.IsTrue(PlotAccess.AllowsEntry(PlotState.Occupied, CharacterOwned, Friend, 0, PlotPermission.Enter));
			Assert.IsFalse(PlotAccess.AllowsEntry(PlotState.Occupied, CharacterOwned, Friend, 0, PlotPermission.PlaceItems),
				"A friend trusted to decorate but not given Enter cannot get in to do it — the flags are independent.");
		}
	}
}
