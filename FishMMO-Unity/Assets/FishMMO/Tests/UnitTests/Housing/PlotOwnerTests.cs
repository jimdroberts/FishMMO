using System;
using FishMMO.Shared;
using NUnit.Framework;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Tests for <see cref="PlotOwner"/>.
	/// </summary>
	/// <remarks>
	/// The type exists to hold an invariant the database cannot: a plot has one owner, never a
	/// character and a guild at once. These tests are that invariant written down — most of them
	/// are about what the type refuses rather than what it stores.
	/// </remarks>
	[TestFixture]
	public class PlotOwnerTests
	{
		[Test]
		public void None_IsUnownedAndNamesNobody()
		{
			Assert.AreEqual(PlotOwnerType.Unowned, PlotOwner.None.Type);
			Assert.IsFalse(PlotOwner.None.IsOwned);
			Assert.AreEqual(0, PlotOwner.None.ID);
			Assert.AreEqual(0, PlotOwner.None.CharacterID);
			Assert.AreEqual(0, PlotOwner.None.GuildID);
		}

		/// <summary>
		/// The default struct value must be the unowned one, since that is what an uninitialised
		/// field or a failed <c>TryFromColumns</c> leaves behind.
		/// </summary>
		[Test]
		public void Default_IsNone()
		{
			Assert.AreEqual(PlotOwner.None, default(PlotOwner));
		}

		[Test]
		public void ForCharacter_OwnsAsACharacter()
		{
			PlotOwner owner = PlotOwner.ForCharacter(42);

			Assert.AreEqual(PlotOwnerType.Character, owner.Type);
			Assert.IsTrue(owner.IsOwned);
			Assert.AreEqual(42, owner.CharacterID);
		}

		/// <summary>
		/// The owner columns are written straight from these, so a character owner must report no
		/// guild rather than repeating its own identifier into the guild column.
		/// </summary>
		[Test]
		public void ForCharacter_ReportsNoGuild()
		{
			Assert.AreEqual(0, PlotOwner.ForCharacter(42).GuildID);
		}

		[Test]
		public void ForGuild_OwnsAsAGuild()
		{
			PlotOwner owner = PlotOwner.ForGuild(7);

			Assert.AreEqual(PlotOwnerType.Guild, owner.Type);
			Assert.IsTrue(owner.IsOwned);
			Assert.AreEqual(7, owner.GuildID);
		}

		[Test]
		public void ForGuild_ReportsNoCharacter()
		{
			Assert.AreEqual(0, PlotOwner.ForGuild(7).CharacterID);
		}

		/// <summary>
		/// An identifier of zero is a caller that has lost track of who it acts for. Folding it into
		/// <see cref="PlotOwner.None"/> would answer a claim by releasing the plot to everyone.
		/// </summary>
		[TestCase(0L)]
		[TestCase(-1L)]
		public void ForCharacter_RejectsNonPositiveIdentifiers(long characterID)
		{
			Assert.Throws<ArgumentOutOfRangeException>(() => PlotOwner.ForCharacter(characterID));
		}

		[TestCase(0L)]
		[TestCase(-1L)]
		public void ForGuild_RejectsNonPositiveIdentifiers(long guildID)
		{
			Assert.Throws<ArgumentOutOfRangeException>(() => PlotOwner.ForGuild(guildID));
		}

		[Test]
		public void TryFromColumns_ReadsUnownedLand()
		{
			Assert.IsTrue(PlotOwner.TryFromColumns(0, 0, out PlotOwner owner));
			Assert.AreEqual(PlotOwner.None, owner);
		}

		[Test]
		public void TryFromColumns_ReadsACharacterOwner()
		{
			Assert.IsTrue(PlotOwner.TryFromColumns(42, 0, out PlotOwner owner));
			Assert.AreEqual(PlotOwner.ForCharacter(42), owner);
		}

		[Test]
		public void TryFromColumns_ReadsAGuildOwner()
		{
			Assert.IsTrue(PlotOwner.TryFromColumns(0, 7, out PlotOwner owner));
			Assert.AreEqual(PlotOwner.ForGuild(7), owner);
		}

		/// <summary>
		/// The reason the type reads rows through a Try method at all.
		/// </summary>
		/// <remarks>
		/// No writer here produces both columns, so a row holding both was written by something
		/// else. Picking one would let two readers disagree about who owns the plot — one allowing
		/// the character to build on it while the other lets the guild sell it.
		/// </remarks>
		[Test]
		public void TryFromColumns_RefusesARowOwnedByBoth()
		{
			Assert.IsFalse(PlotOwner.TryFromColumns(42, 7, out PlotOwner owner));
			Assert.AreEqual(PlotOwner.None, owner);
		}

		[TestCase(-1L, 0L)]
		[TestCase(0L, -1L)]
		public void TryFromColumns_RefusesNegativeIdentifiers(long ownerCharacterID, long ownerGuildID)
		{
			Assert.IsFalse(PlotOwner.TryFromColumns(ownerCharacterID, ownerGuildID, out PlotOwner owner));
			Assert.AreEqual(PlotOwner.None, owner);
		}

		[TestCase(HousingOwnershipMode.Player, true)]
		[TestCase(HousingOwnershipMode.Both, true)]
		[TestCase(HousingOwnershipMode.Guild, false)]
		[TestCase(HousingOwnershipMode.Neither, false)]
		public void CharacterOwnership_IsGatedByTheOwnershipMode(HousingOwnershipMode mode, bool expected)
		{
			Assert.AreEqual(expected, PlotOwner.ForCharacter(42).IsAllowedBy(mode));
		}

		[TestCase(HousingOwnershipMode.Guild, true)]
		[TestCase(HousingOwnershipMode.Both, true)]
		[TestCase(HousingOwnershipMode.Player, false)]
		[TestCase(HousingOwnershipMode.Neither, false)]
		public void GuildOwnership_IsGatedByTheOwnershipMode(HousingOwnershipMode mode, bool expected)
		{
			Assert.AreEqual(expected, PlotOwner.ForGuild(7).IsAllowedBy(mode));
		}

		/// <summary>
		/// Releasing a plot has to keep working on a server that has since turned housing off, or
		/// land claimed while it was on could never be given back.
		/// </summary>
		[TestCase(HousingOwnershipMode.Neither)]
		[TestCase(HousingOwnershipMode.Player)]
		[TestCase(HousingOwnershipMode.Guild)]
		[TestCase(HousingOwnershipMode.Both)]
		public void Unowned_IsAllowedUnderEveryMode(HousingOwnershipMode mode)
		{
			Assert.IsTrue(PlotOwner.None.IsAllowedBy(mode));
		}

		/// <summary>
		/// Identifiers are only meaningful alongside their type, so the same number must not compare
		/// equal across kinds — character 5 is not guild 5.
		/// </summary>
		[Test]
		public void OwnersOfDifferentKinds_AreNotEqual()
		{
			Assert.AreNotEqual(PlotOwner.ForCharacter(5), PlotOwner.ForGuild(5));
			Assert.IsTrue(PlotOwner.ForCharacter(5) != PlotOwner.ForGuild(5));
		}

		[Test]
		public void TheSameOwner_IsEqual()
		{
			Assert.AreEqual(PlotOwner.ForCharacter(5), PlotOwner.ForCharacter(5));
			Assert.IsTrue(PlotOwner.ForCharacter(5) == PlotOwner.ForCharacter(5));
			Assert.AreEqual(PlotOwner.ForCharacter(5).GetHashCode(), PlotOwner.ForCharacter(5).GetHashCode());
		}
	}
}
