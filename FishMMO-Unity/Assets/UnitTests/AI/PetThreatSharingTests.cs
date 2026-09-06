using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using FishMMO.UnitTests.Harness;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests.AI
{
	/// <summary>
	/// A pet and its owner share threat: hitting either is a hit on both.
	/// </summary>
	/// <remarks>
	/// The rule lives in <see cref="AggressionDispatcher"/>, keyed on the owner link that
	/// <c>Pet.PetOwner</c> maintains, so it holds for any owner. These pin the pure resolution —
	/// who receives a hit's threat besides the defender — and the link lifecycle, and check by
	/// source that the pet declares the link and the dispatcher consults it on the damage path.
	/// </remarks>
	[TestFixture]
	public class PetThreatSharingTests
	{
		private readonly List<ICharacter> sharers = new List<ICharacter>();

		[SetUp]
		public void SetUp() => AggressionDispatcher.Clear();

		[TearDown]
		public void TearDown() => AggressionDispatcher.Clear();

		[Test]
		public void HittingThePetThreatensTheOwner()
		{
			StubCharacter owner = new StubCharacter { ID = 1 };
			StubCharacter pet = new StubCharacter { ID = 2 };
			StubCharacter attacker = new StubCharacter { ID = 3 };
			AggressionDispatcher.LinkPet(pet, owner);

			int count = AggressionDispatcher.CollectThreatSharers(pet, attacker, sharers);

			LogAssert.IsTrue(count == 1 && ReferenceEquals(sharers[0], owner),
				"a hit on the pet must be threat against its owner");
		}

		[Test]
		public void HittingTheOwnerThreatensEveryPet()
		{
			StubCharacter owner = new StubCharacter { ID = 1 };
			StubCharacter petA = new StubCharacter { ID = 2 };
			StubCharacter petB = new StubCharacter { ID = 4 };
			StubCharacter attacker = new StubCharacter { ID = 3 };
			AggressionDispatcher.LinkPet(petA, owner);
			AggressionDispatcher.LinkPet(petB, owner);

			int count = AggressionDispatcher.CollectThreatSharers(owner, attacker, sharers);

			LogAssert.IsTrue(count == 2 && sharers.Contains(petA) && sharers.Contains(petB),
				"a hit on the owner must be threat against each of its pets");
		}

		[Test]
		public void TheAttackerNeverSharesItsOwnThreat()
		{
			StubCharacter owner = new StubCharacter { ID = 1 };
			StubCharacter pet = new StubCharacter { ID = 2 };
			AggressionDispatcher.LinkPet(pet, owner);

			LogAssert.IsTrue(AggressionDispatcher.CollectThreatSharers(pet, owner, sharers) == 0,
				"an owner hitting its own pet must not become its own aggressor");
			LogAssert.IsTrue(AggressionDispatcher.CollectThreatSharers(owner, pet, sharers) == 0,
				"a pet hitting its owner must not become its own aggressor");
		}

		[Test]
		public void UnlinkedCharactersShareNothing()
		{
			StubCharacter owner = new StubCharacter { ID = 1 };
			StubCharacter pet = new StubCharacter { ID = 2 };
			StubCharacter attacker = new StubCharacter { ID = 3 };
			AggressionDispatcher.LinkPet(pet, owner);
			AggressionDispatcher.UnlinkPet(pet);

			LogAssert.IsTrue(AggressionDispatcher.CollectThreatSharers(pet, attacker, sharers) == 0,
				"a dismissed pet must no longer pass threat to its former owner");
			LogAssert.IsTrue(AggressionDispatcher.CollectThreatSharers(owner, attacker, sharers) == 0,
				"a former owner must no longer pass threat to a dismissed pet");
			LogAssert.IsTrue(!AggressionDispatcher.TryGetPetOwner(pet, out _), "the link must be gone");
		}

		[Test]
		public void RelinkingMovesThePet()
		{
			StubCharacter first = new StubCharacter { ID = 1 };
			StubCharacter second = new StubCharacter { ID = 5 };
			StubCharacter pet = new StubCharacter { ID = 2 };
			StubCharacter attacker = new StubCharacter { ID = 3 };
			AggressionDispatcher.LinkPet(pet, first);
			AggressionDispatcher.LinkPet(pet, second);

			LogAssert.IsTrue(AggressionDispatcher.CollectThreatSharers(first, attacker, sharers) == 0,
				"the previous owner must not keep the pet");
			LogAssert.IsTrue(AggressionDispatcher.CollectThreatSharers(second, attacker, sharers) == 1,
				"the new owner must have it");
			LogAssert.IsTrue(AggressionDispatcher.TryGetPetOwner(pet, out ICharacter owner) && ReferenceEquals(owner, second),
				"the pet must resolve to its new owner");
		}

		[Test]
		public void APetsHitIsCreditedToItsOwnerToo()
		{
			StubCharacter owner = new StubCharacter { ID = 1 };
			StubCharacter pet = new StubCharacter { ID = 2 };
			AggressionDispatcher.LinkPet(pet, owner);

			int count = AggressionDispatcher.CollectThreatSources(pet, sharers);

			LogAssert.IsTrue(count == 2 && ReferenceEquals(sharers[0], pet) && ReferenceEquals(sharers[1], owner),
				"an NPC hit by a pet must hate the pet and its owner alike; the summoner cannot stand innocent behind it");
		}

		[Test]
		public void AnOwnersHitIsCreditedOnlyToTheOwner()
		{
			StubCharacter owner = new StubCharacter { ID = 1 };
			StubCharacter pet = new StubCharacter { ID = 2 };
			StubCharacter stranger = new StubCharacter { ID = 6 };
			AggressionDispatcher.LinkPet(pet, owner);

			LogAssert.IsTrue(AggressionDispatcher.CollectThreatSources(owner, sharers) == 1 && ReferenceEquals(sharers[0], owner),
				"credit flows from pet to owner, never from owner down to the pet");
			LogAssert.IsTrue(AggressionDispatcher.CollectThreatSources(stranger, sharers) == 1 && ReferenceEquals(sharers[0], stranger),
				"an unlinked attacker is credited alone");
		}

		[Test]
		public void PetDeclaresTheLinkAndTheDispatcherConsultsIt()
		{
			string scripts = Path.Combine(Directory.GetCurrentDirectory(), "Assets/Scripts/Shared/Implementation/Entity/NPC");
			string pet = File.ReadAllText(Path.Combine(scripts, "Pet/Pet.cs"));
			string dispatcher = File.ReadAllText(Path.Combine(scripts, "AI/AggressionDispatcher.cs"));

			LogAssert.IsTrue(pet.Contains("AggressionDispatcher.LinkPet(this, value);") &&
				pet.Contains("AggressionDispatcher.UnlinkPet(this);"),
				"Pet.PetOwner must link on assignment and unlink on clear");

			int damaged = dispatcher.IndexOf("private static void OnCharacterDamaged(", System.StringComparison.Ordinal);
			int share = dispatcher.IndexOf("ShareThreat(attacker, defender, amount);", damaged, System.StringComparison.Ordinal);
			LogAssert.IsTrue(damaged >= 0 && share > damaged,
				"the damage path must share the hit's threat with the defender's owner or pets");

			int shareBody = dispatcher.IndexOf("private static void ShareThreat(", System.StringComparison.Ordinal);
			LogAssert.IsTrue(shareBody >= 0 &&
				dispatcher.IndexOf("CollectThreatSources(attacker, sources);", shareBody, System.StringComparison.Ordinal) > shareBody &&
				dispatcher.IndexOf("CollectThreatSharers(defender, attacker, sharers);", shareBody, System.StringComparison.Ordinal) > shareBody,
				"sharing must resolve pets on both sides of the hit: the sources it is credited to and the recipients it lands on");
		}
	}
}
