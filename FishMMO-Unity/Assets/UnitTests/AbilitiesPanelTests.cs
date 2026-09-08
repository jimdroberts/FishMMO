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
	/// The abilities panel, mounted on a real <see cref="UIDocument"/>, holding what issue #247
	/// asked for: an entry that cannot be dragged has to look different, say so on hover, and say
	/// why when it is clicked.
	/// </summary>
	/// <remarks>
	/// A template bought from a merchant lands on the Templates tab and is not an ability until it
	/// is crafted. That tab's slots were indistinguishable from the Abilities tab's, a click on one
	/// did nothing at all, and the player concluded dragging was broken. These tests hold the
	/// three fixes — the slot mark, the tooltip suffix, and the status line — against the tree
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

		/// <summary>The first slot object in a private slot list, as an untyped object.</summary>
		private object FirstSlot(string listField)
		{
			System.Collections.IList list = Field<System.Collections.IList>(listField);
			LogAssert.IsNotNull(list, $"{listField} must be a list");
			LogAssert.IsTrue(list.Count > 0, $"{listField} must hold the slot that was just added");
			return list[0];
		}

		private static T SlotField<T>(object slot, string name)
		{
			FieldInfo info = slot.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public);
			LogAssert.IsNotNull(info, $"AbilitySlot must still declare {name}");
			return (T)info.GetValue(slot);
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
		public void TheTemplatesTabIsCalledTemplates()
		{
			/* "Known" named the container the template lives in rather than what it is. A player
			 * reading "Known" next to "Abilities" has no reason to think the two hold different
			 * kinds of thing. */
			Button tab = Live.Q<Button>("ability-tab-known");
			LogAssert.IsNotNull(tab, "the templates tab must exist");
			LogAssert.AreEqual("Templates", tab.text, "the tab must name what it holds");
		}

		[Test]
		public void ATemplateSlotIsMarkedAndAnAbilitySlotIsNot()
		{
			AbilityTemplate template = NewTemplate("Test Template");
			panel.AddKnownAbility(template);
			panel.AddAbility(new Ability(1L, template));

			VisualElement known = Field<VisualElement>("knownList");
			VisualElement usable = Field<VisualElement>("abilityList");
			LogAssert.AreEqual(1, known.childCount, "the template landed on the templates list");
			LogAssert.AreEqual(1, usable.childCount, "the ability landed on the abilities list");

			VisualElement templateSlot = known[0];
			VisualElement abilitySlot = usable[0];
			LogAssert.IsTrue(templateSlot.ClassListContains("ability-slot--template"),
				"a template slot must carry the class the USS dims and borders");
			LogAssert.IsFalse(abilitySlot.ClassListContains("ability-slot--template"),
				"a usable ability must not");

			Label badge = templateSlot.Q<Label>(className: "ability-slot__badge");
			LogAssert.IsNotNull(badge, "a template slot carries a corner badge");
			LogAssert.AreEqual("craft", badge.text, "and the badge says what comes next");
			LogAssert.IsNull(abilitySlot.Q<Label>(className: "ability-slot__badge"),
				"a usable ability carries no badge");
		}

		[Test]
		public void ATemplateTooltipSaysWhereItGoesNext()
		{
			panel.AddKnownAbility(NewTemplate("Test Template"));

			string tooltip = SlotField<string>(FirstSlot("knownAbilities"), "Tooltip");
			LogAssert.IsTrue(tooltip.Contains("Ability Crafter"),
				"the template tooltip must name the crafter");
			LogAssert.IsTrue(tooltip.Contains("cannot be placed on the hotkey bar"),
				"and must say outright that it cannot be hotkeyed");
		}

		[Test]
		public void AnEffectSlotIsMarkedAndItsTooltipExplains()
		{
			panel.AddKnownAbilityEvent(NewEvent("Test Effect"));

			VisualElement events = Field<VisualElement>("eventsList");
			LogAssert.AreEqual(1, events.childCount, "the effect landed on the effects list");
			LogAssert.IsTrue(events[0].ClassListContains("ability-slot--effect"), "an effect slot is marked");

			string tooltip = SlotField<string>(FirstSlot("knownAbilityEvents"), "Tooltip");
			LogAssert.IsTrue(tooltip.Contains("Ability Crafter"), "the effect tooltip names the crafter");
		}

		[Test]
		public void ARefusedPickupWritesWhyToTheStatusLine()
		{
			/* The whole report in one assertion: clicking a template used to do nothing at all. */
			panel.AddKnownAbility(NewTemplate("Test Template"));
			object slot = FirstSlot("knownAbilities");

			MethodInfo explain = typeof(UITKAbilities).GetMethod("ExplainRefusedPickup", BindingFlags.Instance | BindingFlags.NonPublic);
			LogAssert.IsNotNull(explain, "UITKAbilities must still declare ExplainRefusedPickup");
			explain.Invoke(panel, new[] { slot });

			Label status = Live.Q<Label>("ability-status");
			LogAssert.IsNotNull(status, "the status line must exist in the tree the player sees");
			LogAssert.AreEqual(Constant("TEMPLATE_REFUSAL"), status.text,
				"a click on a template must say it is a template and where to take it");
		}

		[Test]
		public void SwitchingTabsRestoresTheTabsHintAndEmptyWording()
		{
			Label status = Live.Q<Label>("ability-status");
			Label empty = Live.Q<Label>("ability-empty");

			panel.SwitchTab(AbilityTabType.KnownAbility);
			LogAssert.AreEqual(Constant("TEMPLATE_TAB_HINT"), status.text, "the templates tab shows its standing hint");
			LogAssert.AreEqual(Constant("TEMPLATE_EMPTY"), empty.text, "and an empty templates tab does not say 'No abilities learned'");

			panel.SwitchTab(AbilityTabType.Ability);
			LogAssert.AreEqual(Constant("ABILITY_TAB_HINT"), status.text, "the abilities tab shows how to hotkey");
			LogAssert.AreEqual(Constant("ABILITY_EMPTY"), empty.text, "and its own empty wording");
		}
	}
}
