using System.Collections;
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
	/// The abilities panel, mounted on a real <see cref="UIDocument"/>: two tabs that keep usable
	/// abilities and craftable knowledge apart, and a details pane that says what a thing does.
	/// </summary>
	/// <remarks>
	/// A base ability bought from a merchant is not an ability until it is crafted. Its slot used
	/// to be indistinguishable from a usable ability's, a click on one did nothing at all, and the
	/// player concluded dragging was broken (issue #247). These tests hold the separation — the
	/// tab, the row mark, the description's closing hint, and the status line — against the tree
	/// the player actually sees.
	/// </remarks>
	[TestFixture]
	public class AbilitiesPanelTests
	{
		private const string PanelSettingsPath = "Assets/UI Toolkit/PanelSettings.asset";
		private const string UxmlPath = "Assets/Scripts/Client/GUI/World/Ability/UIAbilities.uxml";

		private GameObject host;
		private UITKAbilities panel;
		private UIDocument document;
		private PanelSettings sharedSettings;
		private readonly List<ScriptableObject> created = new List<ScriptableObject>();

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
			if (host != null)
			{
				Object.DestroyImmediate(host);
			}
			foreach (ScriptableObject so in created)
			{
				if (so != null)
				{
					Object.DestroyImmediate(so);
				}
			}
			created.Clear();
		}

		private VisualElement Live => document.rootVisualElement;

		private T Field<T>(string name) where T : class
		{
			FieldInfo info = typeof(UITKAbilities).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
			LogAssert.IsNotNull(info, $"UITKAbilities must still declare {name}");
			return info.GetValue(panel) as T;
		}

		private static string Constant(string name)
		{
			FieldInfo info = typeof(UITKAbilities).GetField(name, BindingFlags.Static | BindingFlags.NonPublic);
			LogAssert.IsNotNull(info, $"UITKAbilities must still declare {name}");
			return (string)info.GetValue(null);
		}

		/// <summary>The entry object at an index of the panel's private entry list.</summary>
		private object EntryAt(int index)
		{
			IList list = Field<IList>("entries");
			LogAssert.IsNotNull(list, "entries must be a list");
			LogAssert.IsTrue(list.Count > index, "the entry that was just added must be in the list");
			return list[index];
		}

		private static T EntryField<T>(object entry, string name) where T : class
		{
			FieldInfo info = entry.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public);
			LogAssert.IsNotNull(info, $"AbilityEntry must still declare {name}");
			return info.GetValue(entry) as T;
		}

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

		private AbilityTemplate NewTemplate(string name)
		{
			AbilityTemplate template = ScriptableObject.CreateInstance<AbilityTemplate>();
			template.name = name;
			created.Add(template);
			return template;
		}

		private AbilityEvent NewEvent(string name)
		{
			AbilityOnHitEvent abilityEvent = ScriptableObject.CreateInstance<AbilityOnHitEvent>();
			abilityEvent.name = name;
			created.Add(abilityEvent);
			return abilityEvent;
		}

		[Test]
		public void TheSecondTabIsCalledKnowledge()
		{
			/* "Known" and "Templates" named the container rather than what it is for. A player
			 * reading either next to "Abilities" has no reason to think the two hold different
			 * kinds of thing; "Knowledge" says it holds what you know rather than what you can
			 * cast. */
			Button tab = Live.Q<Button>("ability-tab-knowledge");
			LogAssert.IsNotNull(tab, "the knowledge tab must exist");
			LogAssert.AreEqual("Knowledge", tab.text, "the tab must name what it holds");
		}

		[Test]
		public void KnowledgeAndAbilitiesLandOnDifferentTabs()
		{
			AbilityTemplate template = NewTemplate("Test Template");
			panel.AddKnownAbility(template);
			panel.AddKnownAbilityEvent(NewEvent("Test Effect"));
			panel.AddAbility(new Ability(1L, template));

			panel.SwitchTab(AbilityTabType.Ability);
			LogAssert.AreEqual(1, VisibleRows().Count, "only the usable ability shows on the Abilities tab");

			panel.SwitchTab(AbilityTabType.Knowledge);
			LogAssert.AreEqual(2, VisibleRows().Count, "the base ability and the effect both show under Knowledge");
		}

		[Test]
		public void AKnowledgeRowIsMarkedAndAnAbilityRowIsNot()
		{
			AbilityTemplate template = NewTemplate("Test Template");
			panel.AddKnownAbility(template);
			panel.AddAbility(new Ability(1L, template));

			VisualElement knowledgeRow = EntryField<VisualElement>(EntryAt(0), "Root");
			VisualElement abilityRow = EntryField<VisualElement>(EntryAt(1), "Root");

			LogAssert.IsTrue(knowledgeRow.ClassListContains("ability-entry--knowledge"),
				"a knowledge row must carry the class the USS dims");
			LogAssert.IsFalse(abilityRow.ClassListContains("ability-entry--knowledge"),
				"a usable ability must not");

			Label badge = knowledgeRow.Q<Label>(className: "ability-entry__badge");
			LogAssert.IsNotNull(badge, "a knowledge row carries a badge");
			LogAssert.AreEqual("base", badge.text, "and the badge says what kind of part it is");
			LogAssert.IsNull(abilityRow.Q<Label>(className: "ability-entry__badge"),
				"a usable ability carries no badge");
		}

		[Test]
		public void KnowledgeDescriptionsSayWhereTheyGoNext()
		{
			panel.AddKnownAbility(NewTemplate("Test Template"));
			panel.AddKnownAbilityEvent(NewEvent("Test Effect"));

			TooltipContent baseContent = EntryField<TooltipContent>(EntryAt(0), "Content");
			TooltipContent effectContent = EntryField<TooltipContent>(EntryAt(1), "Content");

			LogAssert.IsTrue(HasHintContaining(baseContent, "Ability Crafter"),
				"a base ability's description must name the crafter");
			LogAssert.IsTrue(HasHintContaining(effectContent, "Ability Crafter"),
				"and so must an effect's");
		}

		/// <summary>Whether the content carries a hint row containing the given text.</summary>
		/// <remarks>
		/// A hint, specifically. The guidance used to be concatenated onto the end of the tooltip
		/// string, where it read as one more paragraph the item was claiming about itself; as its
		/// own row kind the renderer sets it apart as advice.
		/// </remarks>
		private static bool HasHintContaining(TooltipContent content, string text)
		{
			foreach (TooltipRow row in content.Rows)
			{
				if (row.Kind == TooltipRowKind.Hint && row.Label != null && row.Label.Contains(text))
				{
					return true;
				}
			}
			return false;
		}

		[Test]
		public void ARefusedPickupWritesWhyToTheStatusLine()
		{
			/* The whole report in one assertion: clicking a base ability used to do nothing. */
			panel.AddKnownAbility(NewTemplate("Test Template"));

			MethodInfo explain = typeof(UITKAbilities).GetMethod("ExplainRefusedPickup", BindingFlags.Instance | BindingFlags.NonPublic);
			LogAssert.IsNotNull(explain, "UITKAbilities must still declare ExplainRefusedPickup");
			explain.Invoke(panel, new[] { EntryAt(0) });

			Label status = Live.Q<Label>("ability-status");
			LogAssert.IsNotNull(status, "the status line must exist in the tree the player sees");
			LogAssert.AreEqual(Constant("TEMPLATE_REFUSAL"), status.text,
				"a click on a base ability must say it is not usable yet and where to take it");
		}

		[Test]
		public void SwitchingTabsRestoresTheTabsHintAndEmptyWording()
		{
			Label status = Live.Q<Label>("ability-status");
			Label empty = Live.Q<Label>("ability-empty");

			panel.SwitchTab(AbilityTabType.Knowledge);
			LogAssert.AreEqual(Constant("KNOWLEDGE_TAB_HINT"), status.text, "the knowledge tab shows its standing hint");
			LogAssert.AreEqual(Constant("KNOWLEDGE_EMPTY"), empty.text, "and an empty knowledge tab does not say 'No abilities learned'");

			panel.SwitchTab(AbilityTabType.Ability);
			LogAssert.AreEqual(Constant("ABILITY_TAB_HINT"), status.text, "the abilities tab shows how to hotkey");
			LogAssert.AreEqual(Constant("ABILITY_EMPTY"), empty.text, "and its own empty wording");
		}

		[Test]
		public void TheKnowledgeFilterNarrowsToOneKind()
		{
			panel.AddKnownAbility(NewTemplate("Test Template"));
			panel.AddKnownAbilityEvent(NewEvent("Test Effect"));
			panel.SwitchTab(AbilityTabType.Knowledge);

			panel.SwitchFilter(KnowledgeFilterType.BaseAbilities);
			LogAssert.AreEqual(1, VisibleRows().Count, "only base abilities show");

			panel.SwitchFilter(KnowledgeFilterType.Effects);
			LogAssert.AreEqual(1, VisibleRows().Count, "only effects show");

			panel.SwitchFilter(KnowledgeFilterType.All);
			LogAssert.AreEqual(2, VisibleRows().Count, "and All shows both again");
		}

		[Test]
		public void SelectingAnEntryRendersItInTheDetailsPane()
		{
			/* The reason the panel has a pane at all. An icon grid could show a name and nothing
			 * else, so two abilities differing only in cooldown and range looked identical until
			 * each was hovered in turn. */
			AbilityTemplate template = NewTemplate("Test Template");
			template.Cooldown = 4.0f;
			panel.AddAbility(new Ability(1L, template));

			MethodInfo select = typeof(UITKAbilities).GetMethod("Select", BindingFlags.Instance | BindingFlags.NonPublic);
			LogAssert.IsNotNull(select, "UITKAbilities must still declare Select");
			select.Invoke(panel, new[] { EntryAt(0) });

			VisualElement details = Live.Q("ability-details-content");
			LogAssert.IsNotNull(details, "the details pane must exist in the tree the player sees");
			LogAssert.IsTrue(details.childCount > 0, "selecting an entry must render it");

			Label title = details.Q<Label>(className: "tip-title");
			LogAssert.IsNotNull(title, "the rendered description leads with the entry's name");
			LogAssert.AreEqual("Test Template", title.text, "and it is the selected entry's name");
		}
	}
}
