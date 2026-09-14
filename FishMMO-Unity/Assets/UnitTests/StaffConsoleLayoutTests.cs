using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using FishMMO.Auth.Core;
using FishMMO.Client;
using FishMMO.Shared;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;
using Object = UnityEngine.Object;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Resolved-geometry pins for the staff console: the boxes that drew text as a sliver, the
	/// badges that touched their neighbours, and the section rule that ran into its button.
	/// </summary>
	/// <remarks>
	/// Every assertion reads what Yoga resolved after real editor frames, never the stylesheet —
	/// each of these defects came from a rule that looked right and lost to Unity's default theme
	/// (its 11px Label box, its popup-field padding) or to a more specific project rule.
	/// </remarks>
	[TestFixture]
	public class StaffConsoleLayoutTests
	{
		private const string PanelSettingsPath = "Assets/UI Toolkit/PanelSettings.asset";
		private const string UxmlPath = "Assets/Scripts/Client/GUI/World/StaffConsole/UIStaffConsole.uxml";
		private const int SettleFrames = 10;

		private GameObject host;
		private UITKStaffConsole console;
		private UIDocument document;
		private PanelSettings sharedSettings;

		[SetUp]
		public void SetUp()
		{
			PanelSettings settings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
			VisualTreeAsset uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
			LogAssert.IsNotNull(settings, $"panel settings must exist at {PanelSettingsPath}");
			LogAssert.IsNotNull(uxml, $"the console UXML must exist at {UxmlPath}");

			sharedSettings = Object.Instantiate(settings);

			host = new GameObject("UIStaffConsole");
			document = host.AddComponent<UIDocument>();
			document.panelSettings = sharedSettings;
			document.visualTreeAsset = uxml;
			document.enabled = false;

			console = host.AddComponent<UITKStaffConsole>();
			console.Document = document;
			console.StartOpen = false;
			console.IsAlwaysOpen = false;
			console.ReleasesCursor = false;

			MethodInfo awake = typeof(UITKControl).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic);
			LogAssert.IsNotNull(awake, "UITKControl must still declare Awake");
			awake.Invoke(console, null);

			console.ApplyCatalogue(new StaffConsoleCatalogBroadcast
			{
				AccessLevel = (byte)AccessLevel.GameMaster,
				Open = true,
				Views = new[] { "players", "tickets" },
				Commands = new[]
				{
					new StaffCommandEntry
					{
						Command = "/cmd", Name = "move", Category = "People", Summary = "Moves a character somewhere.",
						Arguments = "character:Character;to:Choice=near,far;where:Text?", RosterAction = true,
					},
				},
			});
		}

		[TearDown]
		public void TearDown()
		{
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

		private static IEnumerator Settle()
		{
			for (int frame = 0; frame < SettleFrames; ++frame)
			{
				yield return null;
			}
		}

		private static float ContentHeight(VisualElement element)
		{
			IResolvedStyle s = element.resolvedStyle;
			return element.layout.height - s.paddingTop - s.paddingBottom - s.borderTopWidth - s.borderBottomWidth;
		}

		private static float TextHeight(TextElement element)
		{
			return element.MeasureTextSize(string.IsNullOrEmpty(element.text) ? "Ag" : element.text,
				0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined).y;
		}

		/// <summary>Every neighbouring pair on a row line must have clear space between their boxes.</summary>
		private static void AssertGaps(VisualElement line, string what)
		{
			VisualElement previous = null;
			foreach (VisualElement child in line.Children())
			{
				if (previous != null)
				{
					float gap = child.worldBound.xMin - previous.worldBound.xMax;
					LogAssert.IsTrue(gap >= 3f,
						$"{what}: '{(child as Label)?.text}' must stand clear of '{(previous as Label)?.text}'; the gap resolved to {gap}px");
				}
				previous = child;
			}
		}

		[UnityTest]
		public IEnumerator TheChoiceDropdown_HasRoomToDrawItsValue_AndLinesUpWithTheTextFields()
		{
			console.SelectTab(UITKStaffConsole.ConsoleTab.Commands);
			console.SelectCommand(console.FindCommand("/cmd move"));
			yield return Settle();

			TextField character = Live.Q<TextField>("staff-arg-0");
			DropdownField choice = Live.Q<DropdownField>("staff-arg-1");
			LogAssert.IsNotNull(character, "the character text field");
			LogAssert.IsNotNull(choice, "the choice dropdown");

			TextElement text = choice.Q<TextElement>(className: "unity-base-popup-field__text");
			LogAssert.IsNotNull(text, "the dropdown's value text element");
			LogAssert.AreEqual("near", text.text, "precondition: the dropdown shows its first choice");

			float fontSize = text.resolvedStyle.fontSize;
			LogAssert.IsTrue(text.layout.height >= fontSize,
				$"the dropdown's value must have a line of room; its text box resolved to {text.layout.height}px for {fontSize}px type");
			LogAssert.IsTrue(text.layout.height >= TextHeight(text) - 0.5f,
				$"and hold its measured line ({TextHeight(text)}px); it resolved to {text.layout.height}px");

			VisualElement textBox = character.Q("unity-text-input");
			VisualElement dropBox = choice.Q(className: "unity-base-popup-field__input");
			LogAssert.IsNotNull(dropBox, "the dropdown's input box");
			LogAssert.IsTrue(Mathf.Abs(dropBox.worldBound.xMin - textBox.worldBound.xMin) <= 1f,
				$"the dropdown box must start where the text field's box does: {dropBox.worldBound.xMin} vs {textBox.worldBound.xMin}");
		}

		[UnityTest]
		public IEnumerator TheHeaderTitleAndSubtitle_BoxesHoldTheirText()
		{
			yield return Settle();

			VisualElement titles = Live.Q(className: "staff-header__titles");
			LogAssert.IsNotNull(titles, "the header title column");

			foreach (string cls in new[] { "fish-panel__title", "fish-panel__subtitle" })
			{
				Label label = titles.Q<Label>(className: cls);
				LogAssert.IsNotNull(label, $"the header {cls}");
				LogAssert.IsFalse(string.IsNullOrEmpty(label.text), $"precondition: {cls} has text once a catalogue is applied");

				float fontSize = label.resolvedStyle.fontSize;
				LogAssert.IsTrue(label.layout.height >= fontSize,
					$"{cls} box must be at least its font size: {label.layout.height}px for {fontSize}px");
				LogAssert.IsTrue(ContentHeight(label) >= TextHeight(label) - 0.5f,
					$"{cls} must contain its line: content {ContentHeight(label)}px for a {TextHeight(label)}px line");
			}
		}

		[UnityTest]
		public IEnumerator RosterAndTicketBadges_StandClearOfTheTextBesideThem()
		{
			console.ApplyRoster(new StaffRosterBroadcast
			{
				SceneName = "Somewhere",
				TotalInScene = 2,
				TotalOnServer = 2,
				Characters = new[]
				{
					new StaffRosterEntry { CharacterID = 1, Name = "Bob", Account = "bob_acct", AccessLevel = (byte)AccessLevel.Player, InCombat = true, Distance = 8f },
					new StaffRosterEntry { CharacterID = 2, Name = "Ann", Account = "ann_acct", AccessLevel = (byte)AccessLevel.Player, Dead = true, Muted = true, Distance = 24f },
				},
			});
			console.SelectTab(UITKStaffConsole.ConsoleTab.Players);
			yield return Settle();

			VisualElement rows = Live.Q("staff-roster-list");
			LogAssert.AreEqual(2, rows.childCount, "precondition: two roster rows");
			for (int i = 0; i < rows.childCount; ++i)
			{
				VisualElement line = rows[i].Q(className: "staff-row__line");
				LogAssert.IsTrue(line.Query(className: "staff-badge").ToList().Count > 0, "precondition: the row carries a badge");
				AssertGaps(line, $"roster row {i}");
			}

			console.SelectTab(UITKStaffConsole.ConsoleTab.Tickets);
			console.ApplyTicketQueue(new StaffTicketQueueBroadcast
			{
				Filter = StaffTicketFilter.Unassigned,
				Page = 1,
				TotalCount = 1,
				Tickets = new[]
				{
					new StaffTicketSummary
					{
						TicketID = 42, Status = "Open", Category = "PlayerReport", Priority = 1, Subject = "Stuck",
						ReporterAccount = "rep_acct", ReporterCharacter = "Rep", AssignedTo = "", LastActivityUtcTicks = DateTime.UtcNow.Ticks,
					},
				},
			});
			yield return Settle();

			VisualElement ticketRows = Live.Q("staff-ticket-list");
			LogAssert.AreEqual(1, ticketRows.childCount, "precondition: one ticket row");
			VisualElement ticketLine = ticketRows[0].Q(className: "staff-row__line");
			AssertGaps(ticketLine, "ticket row");
			LogAssert.IsTrue(ticketLine[0].worldBound.xMin - ticketRows[0].worldBound.xMin <= rows[0].Q(className: "staff-row__line")[0].worldBound.xMin - rows[0].worldBound.xMin + 0.5f,
				"and the ticket number starts at the row's text edge, like a roster name");
		}

		[UnityTest]
		public IEnumerator TheOutputSectionRule_StopsBeforeTheClearButton()
		{
			yield return Settle();

			Label title = Live.Q<Label>(className: "staff-output__title");
			Button clear = Live.Q<Button>("staff-output-clear");
			LogAssert.IsNotNull(title, "the OUTPUT heading");
			LogAssert.IsNotNull(clear, "the Clear button");
			LogAssert.IsTrue(title.resolvedStyle.borderBottomWidth > 0f, "precondition: the heading draws its section rule");

			float gap = clear.worldBound.xMin - title.worldBound.xMax;
			LogAssert.IsTrue(gap >= 4f, $"the section rule must end clear of the Clear button; the gap resolved to {gap}px");
		}
	}
}
