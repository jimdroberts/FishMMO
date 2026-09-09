using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using FishMMO.Shared;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The consumables: the two concrete kinds, and the rules a shipped consumable has to satisfy
	/// to be usable at all.
	/// </summary>
	/// <remarks>
	/// The project had no concrete consumable — <c>ConsumableTemplate</c> and
	/// <c>ScrollConsumableTemplate</c> were both abstract and nothing derived from either — so the
	/// charge, cooldown and destroy machinery had never run against an authored asset.
	/// </remarks>
	[TestFixture]
	public class ConsumableTemplateTests
	{
		private const string ConsumableFolder = "Assets/Templates/Entity/Items/Consumables";

		private readonly List<ScriptableObject> created = new List<ScriptableObject>();

		[TearDown]
		public void TearDown()
		{
			foreach (ScriptableObject so in created)
			{
				if (so != null)
				{
					Object.DestroyImmediate(so);
				}
			}
			created.Clear();
		}

		private T New<T>(string name) where T : ScriptableObject
		{
			T asset = ScriptableObject.CreateInstance<T>();
			asset.name = name;
			created.Add(asset);
			return asset;
		}

		/// <summary>Every consumable asset the project ships.</summary>
		private static List<BaseItemTemplate> ShippedConsumables()
		{
			List<BaseItemTemplate> consumables = new List<BaseItemTemplate>();
			foreach (string guid in AssetDatabase.FindAssets("t:BaseItemTemplate", new[] { ConsumableFolder }))
			{
				BaseItemTemplate template = AssetDatabase.LoadAssetAtPath<BaseItemTemplate>(AssetDatabase.GUIDToAssetPath(guid));
				if (template != null)
				{
					consumables.Add(template);
				}
			}
			return consumables;
		}

		[Test]
		public void EveryShippedConsumableIsStackable()
		{
			/* Not a style rule — a hard requirement. ConsumableTemplate.CanConsume tests
			 * item.IsStackable, which is MaxStackSize > 1, so a consumable authored at the default
			 * stack size of 1 can never be consumed by anyone. It would sit in the bag looking
			 * usable and do nothing on every click. */
			List<BaseItemTemplate> consumables = ShippedConsumables();
			LogAssert.IsTrue(consumables.Count > 0, "the project must ship consumables to test");

			foreach (BaseItemTemplate template in consumables)
			{
				LogAssert.IsTrue(template.MaxStackSize > 1,
					$"'{template.name}' must stack: a consumable with MaxStackSize 1 can never be consumed");
			}
		}

		[Test]
		public void EveryShippedScrollTeachesSomething()
		{
			foreach (BaseItemTemplate template in ShippedConsumables())
			{
				if (!(template is ScrollConsumableTemplate scroll))
				{
					continue;
				}

				int abilities = scroll.AbilityTemplates?.Count ?? 0;
				int events = scroll.AbilityEvents?.Count ?? 0;
				LogAssert.IsTrue(abilities + events > 0,
					$"'{scroll.name}' teaches nothing, so reading it would consume a charge for no effect");
			}
		}

		[Test]
		public void EveryShippedPotionRestoresSomething()
		{
			foreach (BaseItemTemplate template in ShippedConsumables())
			{
				if (!(template is ResourceConsumableTemplate potion))
				{
					continue;
				}

				LogAssert.IsTrue(potion.Restores != null && potion.Restores.Count > 0,
					$"'{potion.name}' restores nothing");

				foreach (ResourceRestoration restoration in potion.Restores)
				{
					LogAssert.IsNotNull(restoration.Resource, $"'{potion.name}' names a missing resource");
					LogAssert.IsTrue(restoration.Amount != 0, $"'{potion.name}' restores zero of {restoration.Resource?.name}");
				}
			}
		}

		[Test]
		public void APotionsTooltipSaysWhatItRestores()
		{
			CharacterAttributeTemplate health = New<CharacterAttributeTemplate>("Health");
			ResourceConsumableTemplate potion = New<ResourceConsumableTemplate>("Test Potion");
			potion.MaxStackSize = 10;
			potion.Restores = new List<ResourceRestoration>
			{
				new ResourceRestoration { Resource = health, Amount = 150 },
			};

			TooltipContent content = potion.BuildContent();

			bool found = false;
			foreach (TooltipRow row in content.Rows)
			{
				if (row.Kind == TooltipRowKind.Stat && row.Label == "Health")
				{
					found = true;
					LogAssert.AreEqual("+150", row.Value, "the restored amount is signed, because it is a gain");
					LogAssert.AreEqual(TooltipTone.Good, row.Tone, "and reads as one");
				}
			}
			LogAssert.IsTrue(found, "a potion's tooltip must name what it restores");
		}

		[Test]
		public void AScrollsTooltipSeparatesAbilitiesFromEffects()
		{
			/* The two halves of crafting. A scroll could only teach base abilities before, so a
			 * tooltip had no reason to tell them apart; now that it teaches both, saying which is
			 * which is the difference between "I can craft with this" and "I cannot". */
			AbilityTemplate ability = New<AbilityTemplate>("Test Ability");
			AbilityOnHitEvent effect = New<AbilityOnHitEvent>("Test Effect");

			KnowledgeScrollTemplate scroll = New<KnowledgeScrollTemplate>("Test Scroll");
			scroll.MaxStackSize = 5;
			scroll.AbilityTemplates = new List<BaseAbilityTemplate> { ability };
			scroll.AbilityEvents = new List<AbilityEvent> { effect };

			TooltipContent content = scroll.BuildContent();

			string abilityValue = null;
			string effectValue = null;
			foreach (TooltipRow row in content.Rows)
			{
				if (row.Label == "Test Ability") abilityValue = row.Value;
				if (row.Label == "Test Effect") effectValue = row.Value;
			}

			LogAssert.AreEqual("base ability", abilityValue, "a taught base ability says so");
			LogAssert.AreEqual("effect", effectValue, "and a taught effect says so");
		}

		[Test]
		public void ScrollsCanTeachAbilityEvents()
		{
			/* The gap this closed: ScrollConsumableTemplate carried base abilities only, and called
			 * LearnBaseAbilities alone, so half the crafting vocabulary had no way into a player's
			 * hands except a merchant. */
			LogAssert.IsNotNull(typeof(ScrollConsumableTemplate).GetField("AbilityEvents"),
				"a scroll must be able to carry effects");
		}
	}
}
