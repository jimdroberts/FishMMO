using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using FishMMO.UnitTests.Harness;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;
using FishMMO.Server.Implementation.World.SceneServer.AI;

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

		/// <summary>
		/// An attack the defender's armour, parry or shield took all of is still an attack: threat hears it, so the pet defends
		/// its owner (and itself) and the struck NPC answers. A wolf's 5-point Punch against 5 Armor raised nothing at all, and
		/// the pet stood by. An evading NPC (leash return) still raises nothing.
		/// </summary>
		[Test]
		public void AnAttackThatDidNoDamage_StillReachesThreat()
		{
			string damage = File.ReadAllText(ProjectFiles.Path("Assets/FishMMO/Shared/Implementation/Entity/Prediction/CharacterAttribute/CharacterDamageController.cs"));
			int method = damage.IndexOf("public int Damage(ICharacter attacker, int amount", System.StringComparison.Ordinal);
			int evade = damage.IndexOf("if (Immortal || IsEvading())", method, System.StringComparison.Ordinal);
			int harm = damage.IndexOf("if (!HarmRule.Allows(attacker, Character))", method, System.StringComparison.Ordinal);
			int blocked = damage.IndexOf("if (amount < 1)", method, System.StringComparison.Ordinal);
			int raised = damage.IndexOf("RaiseAttackAbsorbed(attacker);", blocked, System.StringComparison.Ordinal);
			int consume = damage.IndexOf("ResourceInstance.Consume(amount);", blocked, System.StringComparison.Ordinal);
			LogAssert.IsTrue(method >= 0 && harm > method && blocked > harm && raised > blocked && raised < consume,
				"a hit the rule allowed and armour or negation took all of raises OnAttackAbsorbed");
			LogAssert.IsTrue(damage.IndexOf("RaiseAttackAbsorbed", evade, System.StringComparison.Ordinal) > harm,
				"an evading or immortal target raises nothing: the leash return must not re-aggro");

			string dispatcher = File.ReadAllText(ProjectFiles.Path("Assets/FishMMO/Server/Implementation/World/SceneServer/AI/AggressionDispatcher.cs"));
			LogAssert.IsTrue(dispatcher.Contains("ICharacterDamageController.OnAttackAbsorbed += OnCharacterAttackAbsorbed;") &&
				dispatcher.Contains("OnCharacterDamaged(attacker, defender, AbsorbedAttackThreat, null);"),
				"the dispatcher delivers an absorbed attack like a hit, so pets and the defender react");
			LogAssert.IsTrue(AggressionDispatcher.AbsorbedAttackThreat > 0, "an absorbed attack carries some threat");

			string ability = File.ReadAllText(ProjectFiles.Path("Assets/FishMMO/Shared/Implementation/Entity/Prediction/Ability/AbilityObject.cs"));
			int deflect = ability.IndexOf("if (DamageMitigation.TryDeflect(hitCharacter", System.StringComparison.Ordinal);
			int block = ability.IndexOf("if (DamageMitigation.TryBlockAtVolume(hitCharacter", System.StringComparison.Ordinal);
			LogAssert.IsTrue(deflect >= 0 && block > deflect &&
				ability.IndexOf("NoteAbsorbedAttack(hitCharacter);", deflect, System.StringComparison.Ordinal) < block &&
				ability.IndexOf("NoteAbsorbedAttack(hitCharacter);", block, System.StringComparison.Ordinal) > block,
				"a parried or shield-blocked hit is an attack too");
		}

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
			string root = Directory.GetCurrentDirectory();
			string pet = ProjectFiles.ReadAllText(Path.Combine(root, "Assets/FishMMO/Shared/Implementation/Entity/NPC/Pet/Pet.cs"));
			string ai = Path.Combine(root, "Assets/FishMMO/Server/Implementation/World/SceneServer/AI");
			string dispatcher = ProjectFiles.ReadAllText(Path.Combine(ai, "AggressionDispatcher.cs"));
			string host = ProjectFiles.ReadAllText(Path.Combine(ai, "AIBrainHost.cs"));

			/* The pet is the Pets module's and the dispatcher is the NPCs module's, which does not reference it, so the
			 * link travels through the NPCs module's companion seam (2026-11-20): the pet announces every owner change on
			 * AICompanions, and the dispatcher — subscribed by the brain host before any pet can exist — links on an owner
			 * and unlinks on null. */
			LogAssert.IsTrue(pet.Contains("AICompanions.RaiseOwnerChanged(this, value);"),
				"Pet.PetOwner must announce every owner change, including the clear");
			int handler = dispatcher.IndexOf("private static void OnPetOwnerChanged(IAICompanion pet, ICharacter owner)", System.StringComparison.Ordinal);
			LogAssert.IsTrue(handler >= 0 &&
				dispatcher.IndexOf("LinkPet(pet, owner);", handler, System.StringComparison.Ordinal) > handler &&
				dispatcher.IndexOf("UnlinkPet(pet);", handler, System.StringComparison.Ordinal) > handler,
				"the dispatcher must link on an owner and unlink on a clear");
			LogAssert.IsTrue(dispatcher.Contains("AICompanions.OnOwnerChanged += OnPetOwnerChanged;") &&
				host.Contains("AggressionDispatcher.EnsurePetLinksTracked();"),
				"the brain host must start the dispatcher following pet owner changes");

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
