using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The template-to-ability reverse index survives the removal of EITHER of two abilities built
	/// from the same template.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <c>templateToAbilityID</c> maps ONE template to ONE ability id and the last learn wins. The
	/// combat audit pinned one direction — removing the earlier copy must not delete the entry that
	/// names the later one. This pins the other: removing the copy the index DOES name must hand the
	/// entry to the surviving copy, not drop it. Dropping it made <c>KnowsLearnedAbility</c> answer
	/// false for a template the character still has, and the craft gate and the merchant gate —
	/// which both ask exactly that — let a third copy through.
	/// </para>
	/// <para>
	/// Exercised against the dictionaries directly, as the audit fixture does: the controller is a
	/// <c>NetworkBehaviour</c> and cannot answer <c>IsOwner</c> unspawned.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class AbilityKnowledgeReverseIndexTests
	{
		private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

		private GameObject host;
		private AbilityTemplate template;
		private AbilityController controller;
		private FieldInfo indexField;

		[SetUp]
		public void SetUp()
		{
			host = new GameObject("ReverseIndexAbilityController");
			template = ScriptableObject.CreateInstance<AbilityTemplate>();
			template.name = "ReverseIndexAbility";
			template.AddToCache(template.name);

			controller = host.AddComponent<AbilityController>();
			if (controller.KnownAbilities == null)
			{
				controller.OnAwake();
			}

			indexField = typeof(AbilityController).GetField("templateToAbilityID", Any);
			LogAssert.IsNotNull(indexField, "the reverse index must still exist");
		}

		[TearDown]
		public void TearDown()
		{
			if (template != null)
			{
				template.RemoveFromCache();
				Object.DestroyImmediate(template);
			}
			if (host != null)
			{
				Object.DestroyImmediate(host);
			}
		}

		private Dictionary<int, long> Index => (Dictionary<int, long>)indexField.GetValue(controller);

		/// <summary>Two abilities from one template, the later one holding the index, as LearnAbility leaves it.</summary>
		private void KnowTwoCopies()
		{
			controller.KnownAbilities[100L] = new Ability(100L, template, null);
			controller.KnownAbilities[200L] = new Ability(200L, template, null);
			Index[template.ID] = 200L;
		}

		[Test]
		public void RemovingTheAbilityTheIndexNames_HandsTheEntryToTheSurvivingCopy()
		{
			KnowTwoCopies();

			controller.RemoveAbility(200L);

			LogAssert.IsTrue(Index.ContainsKey(template.ID),
				"the character still knows ability 100 from this template, so the template must still resolve");
			LogAssert.AreEqual(100L, Index[template.ID],
				"...and it must resolve to the copy that survived");
			LogAssert.IsTrue(controller.KnowsLearnedAbility(template.ID),
				"KnowsLearnedAbility is what the craft and merchant gates ask; a false here admits a third copy");
		}

		[Test]
		public void RemovingTheLastCopy_ClearsTheEntry()
		{
			KnowTwoCopies();

			controller.RemoveAbility(200L);
			controller.RemoveAbility(100L);

			LogAssert.IsFalse(Index.ContainsKey(template.ID),
				"with no copy left the template must not resolve, or the map outlives its target");
			LogAssert.IsFalse(controller.KnowsLearnedAbility(template.ID),
				"...and the gates must let the template be crafted again");
		}

		[Test]
		public void RemovingTheOtherCopy_LeavesTheEntryAlone()
		{
			KnowTwoCopies();

			controller.RemoveAbility(100L);

			LogAssert.AreEqual(200L, Index[template.ID],
				"the audit's direction still holds: removing the copy the index does not name changes nothing");
		}

		[Test]
		public void RemovingAnUnknownID_ChangesNothing()
		{
			KnowTwoCopies();

			controller.RemoveAbility(999L);

			LogAssert.AreEqual(200L, Index[template.ID], "an id nothing is bound to must not touch the index");
			LogAssert.AreEqual(2, controller.KnownAbilities.Count, "...or the set");
		}
	}
}
