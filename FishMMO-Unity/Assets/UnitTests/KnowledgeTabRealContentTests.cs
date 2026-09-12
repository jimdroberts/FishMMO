using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;
using FishMMO.Client;
using FishMMO.Shared;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The Knowledge tab against the content a merchant actually sells, rather than a
	/// <c>CreateInstance</c> stand-in.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <see cref="AbilitiesPanelTests"/> mounts the real panel but feeds it bare
	/// <c>ScriptableObject.CreateInstance&lt;AbilityTemplate&gt;</c> templates — no events, no
	/// conditions, no actions, no icon, and crucially an <c>ID</c> of zero because nothing ever
	/// called <c>AddToCache</c> on them. Every link that depends on real content or on a real cached
	/// id is therefore untested: the lookup the panel performs
	/// (<c>BaseAbilityTemplate.Get</c>), the whole <c>AbilitySummary.Compose</c> walk over a
	/// template's own events and their conditions, and the row that comes out the far side.
	/// </para>
	/// <para>
	/// These tests read the merchant's own asset, so they break loudly if a template it sells is
	/// deleted, renamed, or stops resolving — which is the same failure the panel would show as an
	/// empty Knowledge tab.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class KnowledgeTabRealContentTests
	{
		private const string MerchantPath =
			"Assets/Templates/Entity/Interactables/NPCs/Human/Merchants/GeneralGoods/GeneralGoods.asset";
		private const string PanelSettingsPath = "Assets/UI Toolkit/PanelSettings.asset";
		private const string UxmlPath = "Assets/Scripts/Client/GUI/World/Ability/UIAbilities.uxml";

		private GameObject host;
		private UITKAbilities panel;
		private UIDocument document;
		private PanelSettings sharedSettings;

		/// <summary>Entries pushed into the static cache by a test, removed again on teardown.</summary>
		private readonly List<ICachedObject> cached = new List<ICachedObject>();

		[SetUp]
		public void SetUp()
		{
			PanelSettings settings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
			VisualTreeAsset uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
			LogAssert.IsNotNull(settings, $"panel settings must exist at {PanelSettingsPath}");
			LogAssert.IsNotNull(uxml, $"the abilities UXML must exist at {UxmlPath}");

			sharedSettings = Object.Instantiate(settings);

			host = new GameObject("UIAbilities");
			document = host.AddComponent<UIDocument>();
			document.panelSettings = sharedSettings;
			document.visualTreeAsset = uxml;
			document.enabled = false;

			panel = host.AddComponent<UITKAbilities>();
			panel.Document = document;
			panel.StartOpen = false;
			panel.IsAlwaysOpen = false;
			panel.CloseOnQuitToMenu = true;
			panel.ReleasesCursor = false;
			panel.CloseOnEscape = true;

			MethodInfo awake = typeof(UITKControl).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic);
			LogAssert.IsNotNull(awake, "UITKControl must still declare Awake");
			awake.Invoke(panel, null);

			panel.Show();
			LogAssert.IsTrue(panel.Visible, "the panel must be up before anything is asserted about its tree");
		}

		[TearDown]
		public void TearDown()
		{
			foreach (ICachedObject entry in cached)
			{
				entry.RemoveFromCache();
			}
			cached.Clear();

			if (host != null)
			{
				Object.DestroyImmediate(host);
			}
			if (sharedSettings != null)
			{
				Object.DestroyImmediate(sharedSettings);
			}
		}

		private VisualElement Live => document.rootVisualElement;

		/// <summary>The rows currently displayed, in order.</summary>
		private List<VisualElement> VisibleRows()
		{
			List<VisualElement> rows = new List<VisualElement>();
			VisualElement list = Live.Q("ability-list");
			LogAssert.IsNotNull(list, "the row container must exist in the tree the player sees");

			foreach (VisualElement child in list.Children())
			{
				if (child.style.display.value != DisplayStyle.None)
				{
					rows.Add(child);
				}
			}
			return rows;
		}

		/// <summary>The merchant template a player buys ability templates from.</summary>
		private static MerchantTemplate Merchant()
		{
			MerchantTemplate merchant = AssetDatabase.LoadAssetAtPath<MerchantTemplate>(MerchantPath);
			LogAssert.IsNotNull(merchant, $"the merchant template must exist at {MerchantPath}");
			return merchant;
		}

		/// <summary>
		/// Registers a template the way the addressables loader does, so its <c>ID</c> is real.
		/// </summary>
		/// <remarks>
		/// <c>AddToCache</c> is what assigns <c>ICachedObject.ID</c>, and nothing in edit mode calls
		/// it — an asset loaded through <c>AssetDatabase</c> reports zero until this runs. Every id
		/// that travels the wire is the value this assigns, so a test that skips it is not testing
		/// the same object the game does.
		/// </remarks>
		private T Cached<T>(T template) where T : ScriptableObject, ICachedObject
		{
			template.AddToCache(template.name);
			cached.Add(template);
			return template;
		}

		[Test]
		public void TheMerchantSellsTemplatesAtAll()
		{
			/* Guards the fixture itself. If this list is ever emptied the two tests below would
			 * pass over nothing and read as green. */
			MerchantTemplate merchant = Merchant();
			LogAssert.IsNotNull(merchant.Abilities, "the merchant must declare an abilities list");
			LogAssert.IsTrue(merchant.Abilities.Count > 0, "the merchant must actually sell something");

			foreach (AbilityTemplate template in merchant.Abilities)
			{
				LogAssert.IsNotNull(template, "the merchant must not carry an empty offer slot");
			}
		}

		[Test]
		public void EveryTemplateTheMerchantSellsResolvesThroughBothCaches()
		{
			/* The two panels resolve the SAME KnownBaseAbilities set through different type keys:
			 * the abilities panel asks BaseAbilityTemplate, the crafter asks AbilityTemplate. Both
			 * have to answer, because a template that only one of them can name shows as knowledge
			 * and cannot be crafted with, or the reverse. */
			foreach (AbilityTemplate template in Merchant().Abilities)
			{
				Cached(template);

				LogAssert.AreNotEqual(0, template.ID,
					$"'{template.name}' must receive a deterministic id, or every id on the wire is zero");
				LogAssert.AreSame(template, BaseAbilityTemplate.Get<BaseAbilityTemplate>(template.ID),
					$"the abilities panel's lookup must find '{template.name}'");
				LogAssert.AreSame(template, AbilityTemplate.Get<AbilityTemplate>(template.ID),
					$"the crafter panel's lookup must find '{template.name}'");
			}
		}

		[Test]
		public void EveryTemplateTheMerchantSellsBuildsItsDescriptionAndSummary()
		{
			/* The row's description is composed from the template's own events, their conditions and
			 * their actions — real objects with real content, which is exactly what the stand-in
			 * templates in AbilitiesPanelTests do not have. A throw anywhere in that walk escapes
			 * the controller's OnAddKnownAbility handler, and the row is simply never created. */
			foreach (AbilityTemplate template in Merchant().Abilities)
			{
				Cached(template);

				TooltipContent content = template.BuildContent();
				LogAssert.IsNotNull(content, $"'{template.name}' must produce tooltip content");

				AbilitySummary summary = AbilitySummary.Compose(template);
				LogAssert.IsNotNull(summary, $"'{template.name}' must compose a summary");
				LogAssert.AreEqual(template, summary.Template, "the summary must name its own template");

				LogAssert.IsTrue(content.Rows.Count > 0,
					$"'{template.name}' must describe itself — an empty content draws a blank row");
			}
		}

		[Test]
		public void EveryTemplateTheMerchantSellsGetsAVisibleKnowledgeRow()
		{
			/* The end of the chain, in the tree the player is looking at: one visible row per
			 * offer, on the Knowledge tab, carrying the badge that says it is not usable yet. */
			List<AbilityTemplate> sold = new List<AbilityTemplate>(Merchant().Abilities);
			int before = VisibleRows().Count;

			foreach (AbilityTemplate template in sold)
			{
				Cached(template);
				panel.AddKnownAbility(template);
			}

			panel.SwitchTab(AbilityTabType.Knowledge);
			panel.SwitchFilter(KnowledgeFilterType.All);

			LogAssert.AreEqual(sold.Count, VisibleRows().Count,
				$"every one of the {sold.Count} templates the merchant sells must draw a row; " +
				$"{before} were showing before they were added");

			foreach (AbilityTemplate template in sold)
			{
				bool found = false;
				foreach (VisualElement row in VisibleRows())
				{
					Label name = row.Q<Label>(className: "ability-entry__name");
					if (name != null && name.text == template.Name)
					{
						found = true;
						break;
					}
				}
				LogAssert.IsTrue(found, $"'{template.name}' must be one of the rows on the Knowledge tab");
			}

			Label count = Live.Q<Label>("ability-count");
			LogAssert.IsNotNull(count, "the header count badge must exist");
			LogAssert.AreEqual(sold.Count.ToString(), count.text,
				"the header must agree with the list about how much knowledge there is");
		}
	}
}
