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
	/// The staff console mounted on a real UI Toolkit panel and fed a catalogue through the same
	/// public entry points its broadcast handlers forward to.
	/// </summary>
	/// <remarks>
	/// Nothing here touches the network: the component has no client, so a line that reaches the
	/// send step reports "not connected" in the output log, which is how these tests see that a
	/// click got that far. Command names are neutral placeholders.
	/// </remarks>
	[TestFixture]
	public class StaffConsolePanelTests
	{
		private const string PanelSettingsPath = "Assets/UI Toolkit/PanelSettings.asset";
		private const string UxmlPath = "Assets/Scripts/Client/GUI/World/StaffConsole/UIStaffConsole.uxml";

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
			console.CloseOnQuitToMenu = true;
			console.ReleasesCursor = false;
			console.CloseOnEscape = true;

			MethodInfo awake = typeof(UITKControl).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic);
			LogAssert.IsNotNull(awake, "UITKControl must still declare Awake");
			awake.Invoke(console, null);
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

		private static StaffConsoleCatalogBroadcast Catalogue(params string[] views)
		{
			return new StaffConsoleCatalogBroadcast
			{
				AccessLevel = (byte)AccessLevel.GameMaster,
				Open = true,
				Views = views,
				Commands = new[]
				{
					new StaffCommandEntry
					{
						Command = "/cmd", Name = "nudge", Category = "People", Summary = "Moves a character.",
						Arguments = "character:Character;distance:Integer?", RosterAction = true,
					},
					new StaffCommandEntry
					{
						Command = "/cmd", Name = "note", Category = "Support", Summary = "Adds a note to a ticket.",
						Arguments = "ticket:Ticket;text:Text", TicketAction = true,
					},
					new StaffCommandEntry
					{
						Command = "/cmd", Name = "wipe", Category = "People", Summary = "Cannot be undone.",
						Arguments = "character:Character?;mode:Choice=soft,hard", RosterAction = true, Destructive = true,
					},
				},
			};
		}

		private static void Click(Button button)
		{
			using (NavigationSubmitEvent evt = NavigationSubmitEvent.GetPooled())
			{
				evt.target = button;
				button.SendEvent(evt);
			}
		}

		private static void ClickRow(VisualElement row)
		{
			using (ClickEvent evt = ClickEvent.GetPooled())
			{
				evt.target = row;
				row.SendEvent(evt);
			}
		}

		private Button Q(string name)
		{
			Button button = Live.Q<Button>(name);
			LogAssert.IsNotNull(button, $"{name} must exist");
			return button;
		}

		private static bool Shown(VisualElement element) => element.style.display.value != DisplayStyle.None;

		private int IndexOf(string title)
		{
			int index = console.FindCommand(title);
			LogAssert.IsTrue(index >= 0, $"{title} must be in the catalogue");
			return index;
		}

		private string Composed()
		{
			bool ok = console.TryComposeSelected(out string line, out string error);
			LogAssert.IsTrue(ok, $"the form must compose; it failed with: {error}");
			return line;
		}

		private List<Button> ButtonsIn(string containerName)
		{
			VisualElement container = Live.Q(containerName);
			LogAssert.IsNotNull(container, $"{containerName} must exist");
			return container.Query<Button>().ToList();
		}

		// ── Gating ──────────────────────────────────────────────────────────────────────────

		[Test]
		public void WithoutACatalogue_TheConsoleRefusesToShow()
		{
			console.Show();
			LogAssert.IsFalse(console.Visible, "an empty console must not appear");
			LogAssert.IsFalse(console.HasCatalogue, "no catalogue");

			console.ReceiveChat(new ChatBroadcast { Channel = ChatChannel.System, Text = "hello" });
			LogAssert.AreEqual(0, console.OutputLines.Count, "and it collects nothing before one arrives");
		}

		[Test]
		public void ACatalogueBelowGameMaster_IsNotPresented()
		{
			StaffConsoleCatalogBroadcast catalogue = Catalogue("players", "tickets");
			catalogue.AccessLevel = (byte)AccessLevel.Player;
			console.ApplyCatalogue(catalogue);

			LogAssert.IsFalse(console.Visible, "not shown");
			LogAssert.IsFalse(console.HasCatalogue, "not kept");
		}

		[Test]
		public void ClearingTheCatalogue_HidesTheConsole_AndForgetsEverything()
		{
			console.ApplyCatalogue(Catalogue("players", "tickets"));
			console.ReceiveChat(new ChatBroadcast { Channel = ChatChannel.System, Text = "hello" });
			LogAssert.IsTrue(console.Visible, "precondition: open");

			console.ClearCatalogue();

			LogAssert.IsFalse(console.Visible, "hidden");
			LogAssert.IsFalse(console.HasCatalogue, "no catalogue");
			LogAssert.AreEqual(0, console.CommandCount, "no commands");
			LogAssert.AreEqual(0, console.OutputLines.Count, "no output");

			console.Show();
			LogAssert.IsFalse(console.Visible, "and it cannot be reopened without a new catalogue");
		}

		// ── Building from the catalogue ─────────────────────────────────────────────────────

		[Test]
		public void ACatalogueWithOpen_ShowsTheConsole_WithTabsAndGroupedCommands()
		{
			console.ApplyCatalogue(Catalogue("players", "tickets"));

			LogAssert.IsTrue(console.Visible, "Open=true opens it");
			LogAssert.IsTrue(Shown(Q("staff-tab-players")), "players tab");
			LogAssert.IsTrue(Shown(Q("staff-tab-tickets")), "tickets tab");
			LogAssert.IsTrue(Shown(Q("staff-tab-commands")), "commands tab");
			LogAssert.AreEqual(UITKStaffConsole.ConsoleTab.Players, console.ActiveTab, "the roster is the first view");

			VisualElement list = Live.Q("staff-command-list");
			LogAssert.AreEqual(5, list.childCount, "two category headings and three commands");
			LogAssert.AreEqual("PEOPLE", ((Label)list[0]).text, "first category as it first appeared");
			LogAssert.AreEqual("/cmd nudge", list[1].Q<Label>().text, "grouped: the first People command");
			LogAssert.AreEqual("/cmd wipe", list[2].Q<Label>().text, "grouped: the second People command follows it");
			LogAssert.AreEqual("SUPPORT", ((Label)list[3]).text, "second category");
			LogAssert.AreEqual("/cmd note", list[4].Q<Label>().text, "its command");
		}

		[Test]
		public void ThePlayersTab_IsAbsent_WhenTheCatalogueDoesNotListIt()
		{
			console.ApplyCatalogue(Catalogue("tickets"));

			LogAssert.IsFalse(Shown(Q("staff-tab-players")), "no players tab");
			LogAssert.IsFalse(Shown(Live.Q("staff-page-players")), "and no players page");
			LogAssert.IsTrue(Shown(Q("staff-tab-tickets")), "tickets tab");
			LogAssert.IsTrue(Shown(Q("staff-tab-commands")), "commands tab is always there");
			LogAssert.AreEqual(UITKStaffConsole.ConsoleTab.Tickets, console.ActiveTab, "opens on the first listed view");

			console.SelectTab(UITKStaffConsole.ConsoleTab.Players);
			LogAssert.AreEqual(UITKStaffConsole.ConsoleTab.Commands, console.ActiveTab, "asking for it lands on Commands");
		}

		[Test]
		public void WithNoViews_OnlyTheCommandsTabShows()
		{
			console.ApplyCatalogue(Catalogue());

			LogAssert.IsFalse(Shown(Q("staff-tab-players")), "no players tab");
			LogAssert.IsFalse(Shown(Q("staff-tab-tickets")), "no tickets tab");
			LogAssert.IsTrue(Shown(Q("staff-tab-commands")), "commands tab");
			LogAssert.AreEqual(UITKStaffConsole.ConsoleTab.Commands, console.ActiveTab, "on Commands");
		}

		[Test]
		public void SelectingACommand_BuildsOneInputPerArgument_AndComposesItsLine()
		{
			console.ApplyCatalogue(Catalogue("players"));
			console.SelectTab(UITKStaffConsole.ConsoleTab.Commands);
			console.SelectCommand(IndexOf("/cmd wipe"));

			TextField character = Live.Q<TextField>("staff-arg-0");
			DropdownField picker = Live.Q<DropdownField>("staff-arg-0-pick");
			DropdownField mode = Live.Q<DropdownField>("staff-arg-1");
			LogAssert.IsNotNull(character, "a character argument is a text field");
			LogAssert.IsNotNull(picker, "with a roster picker beside it");
			LogAssert.IsNotNull(mode, "a choice argument is a dropdown");
			LogAssert.IsTrue(character.ClassListContains("fish-input--compact"), "a pinned-height input carries the compact class");
			LogAssert.AreEqual(2, mode.choices.Count, "a required choice offers exactly its words");
			LogAssert.AreEqual("soft", mode.value, "starting on the first");
			LogAssert.IsNull(Live.Q("staff-arg-2"), "and nothing more");

			character.value = "Bob";
			LogAssert.AreEqual("/cmd wipe Bob soft", Composed(), "the typed name and the default choice");

			mode.value = "hard";
			character.value = "";
			LogAssert.AreEqual("/cmd wipe hard", Composed(), "a blank optional character is left out");
			LogAssert.AreEqual("/cmd wipe hard", Live.Q<Label>("staff-command-preview").text, "and the preview shows the line");
		}

		[Test]
		public void AnOverlongLine_IsRefused_WithTheReasonShown()
		{
			console.ApplyCatalogue(Catalogue());
			console.SelectCommand(IndexOf("/cmd note"));

			Live.Q<TextField>("staff-arg-0").value = "42";
			TextField text = Live.Q<TextField>("staff-arg-1");
			text.maxLength = 500;
			text.value = new string('x', ChatBroadcast.MaxTextLength);

			LogAssert.IsFalse(console.TryComposeSelected(out _, out string error), "over the chat limit");
			Label status = Live.Q<Label>("staff-command-status");
			LogAssert.AreEqual(error, status.text, "the status line says why");
			LogAssert.IsTrue(status.ClassListContains("staff-status--error"), "styled as an error");

			Click(Q("staff-run"));
			IReadOnlyList<string> lines = console.OutputLines;
			LogAssert.AreEqual(1, lines.Count, "one refusal line");
			LogAssert.IsTrue(lines[0].StartsWith("Not sent:"), $"and it is a refusal, not a send: {lines[0]}");
		}

		// ── Roster ──────────────────────────────────────────────────────────────────────────

		[Test]
		public void ARosterRow_OffersRosterActions_ThatPrefillTheCharacter()
		{
			console.ApplyCatalogue(Catalogue("players", "tickets"));
			console.ApplyRoster(new StaffRosterBroadcast
			{
				SceneName = "Somewhere",
				TotalInScene = 2,
				TotalOnServer = 9,
				Characters = new[]
				{
					new StaffRosterEntry { CharacterID = 1, Name = "Bob", Account = "bob_acct", AccessLevel = (byte)AccessLevel.Player, Dead = true, Muted = true, Distance = 3.4f },
					new StaffRosterEntry { CharacterID = 2, Name = "Ann", Account = "ann_acct", AccessLevel = (byte)AccessLevel.Admin, InCombat = true, Distance = 40f },
				},
			});

			VisualElement rows = Live.Q("staff-roster-list");
			LogAssert.AreEqual(2, rows.childCount, "one row per character");

			List<string> texts = rows[0].Query<Label>().ToList().ConvertAll(l => l.text);
			LogAssert.IsTrue(texts.Contains("Bob"), "name");
			LogAssert.IsTrue(texts.Contains("Dead"), "dead badge");
			LogAssert.IsTrue(texts.Contains("Muted"), "muted badge");
			LogAssert.IsFalse(texts.Contains("In combat"), "no combat badge for Bob");
			LogAssert.IsTrue(texts.Contains("3 m"), "distance");
			LogAssert.IsTrue(texts.Contains("bob_acct · Player"), "account and access level word");
			LogAssert.IsTrue(rows[1].Query<Label>().ToList().ConvertAll(l => l.text).Contains("ann_acct · Admin"), "Ann is an Admin");

			LogAssert.AreEqual(0, ButtonsIn("staff-roster-actions").Count, "no actions before a selection");

			ClickRow(rows[0]);
			List<Button> actions = ButtonsIn("staff-roster-actions");
			LogAssert.AreEqual(2, actions.Count, "the two roster actions, and not the ticket action");
			LogAssert.AreEqual("nudge", actions[0].text, "labelled by sub-command word from the catalogue");

			Click(actions[0]);
			LogAssert.AreEqual(UITKStaffConsole.ConsoleTab.Commands, console.ActiveTab, "the form opens on Commands");
			LogAssert.AreEqual("Bob", Live.Q<TextField>("staff-arg-0").value, "with the character filled in");
			LogAssert.AreEqual("/cmd nudge Bob", Composed(), "a blank optional distance is left out");

			List<string> pickerChoices = Live.Q<DropdownField>("staff-arg-0-pick").choices;
			LogAssert.AreEqual(2, pickerChoices.Count, "the picker offers the roster's names");
		}

		// ── Tickets ─────────────────────────────────────────────────────────────────────────

		[Test]
		public void ATicket_ShowsInternalNotesDistinctly_AndTicketActionsPrefillItsNumber()
		{
			console.ApplyCatalogue(Catalogue("players", "tickets"));
			console.SelectTab(UITKStaffConsole.ConsoleTab.Tickets);

			long now = DateTime.UtcNow.Ticks;
			StaffTicketSummary summary = new StaffTicketSummary
			{
				TicketID = 42, Status = "Open", Category = "Bug", Priority = 1, Subject = "Stuck in a wall",
				ReporterAccount = "rep_acct", ReporterCharacter = "Rep", AssignedTo = "", LastActivityUtcTicks = now - TimeSpan.TicksPerMinute * 5,
			};

			console.ApplyTicketQueue(new StaffTicketQueueBroadcast
			{
				Filter = StaffTicketFilter.AllOpen, Page = 1, TotalCount = 1, Tickets = new[] { summary },
			});
			LogAssert.AreEqual(0, Live.Q("staff-ticket-list").childCount, "a page for a filter that is not selected is stale");

			console.ApplyTicketQueue(new StaffTicketQueueBroadcast
			{
				Filter = StaffTicketFilter.Unassigned, Page = 1, TotalCount = 1, Tickets = new[] { summary },
			});
			VisualElement ticketRows = Live.Q("staff-ticket-list");
			LogAssert.AreEqual(1, ticketRows.childCount, "one ticket row");
			List<string> rowTexts = ticketRows[0].Query<Label>().ToList().ConvertAll(l => l.text);
			LogAssert.IsTrue(rowTexts.Contains("#42"), "number");
			LogAssert.IsTrue(rowTexts.Contains("Stuck in a wall"), "subject");
			LogAssert.IsTrue(rowTexts.Contains("5m ago"), "age");
			LogAssert.IsTrue(rowTexts.Contains("Rep → Unassigned"), "reporter and assignee");

			ClickRow(ticketRows[0]);

			StaffTicketDetailBroadcast stale = new StaffTicketDetailBroadcast { Found = true, Ticket = new StaffTicketSummary { TicketID = 7 } };
			console.ApplyTicketDetail(stale);
			LogAssert.AreEqual(0, Live.Q("staff-ticket-detail").childCount, "detail for another ticket is ignored");

			console.ApplyTicketDetail(new StaffTicketDetailBroadcast
			{
				Found = true,
				Ticket = summary,
				Body = "I walked into the wall and cannot leave.",
				SceneName = "Somewhere",
				Resolution = "",
				Messages = new[]
				{
					new StaffTicketMessageEntry { CreatedUtcTicks = now, Author = "rep_acct", Body = "Please help." },
					new StaffTicketMessageEntry { CreatedUtcTicks = now, Author = "staff_acct", AuthorIsStaff = true, Internal = true, Body = "Checked the logs." },
				},
			});

			VisualElement detail = Live.Q("staff-ticket-detail");
			List<VisualElement> internalNotes = detail.Query(className: "staff-message--internal").ToList();
			LogAssert.AreEqual(2, detail.Query(className: "staff-message").ToList().Count, "both messages are shown");
			LogAssert.AreEqual(1, internalNotes.Count, "exactly one is marked internal");
			LogAssert.IsTrue(internalNotes[0].Query<Label>().ToList().ConvertAll(l => l.text).Contains("staff note — player cannot see"),
				"and it says the player cannot see it");

			List<Button> actions = detail.Query<Button>().ToList();
			LogAssert.AreEqual(1, actions.Count, "only the ticket action is offered");
			Click(actions[0]);

			LogAssert.AreEqual(UITKStaffConsole.ConsoleTab.Commands, console.ActiveTab, "the form opens");
			LogAssert.AreEqual("42", Live.Q<TextField>("staff-arg-0").value, "with the ticket number filled in");
			Live.Q<TextField>("staff-arg-1").value = "looking into it now";
			LogAssert.AreEqual("/cmd note 42 looking into it now", Composed(), "free text consumes the rest");
		}

		// ── Running ─────────────────────────────────────────────────────────────────────────

		[Test]
		public void ADestructiveCommand_ConfirmsInPlace_BeforeItIsSent()
		{
			if (UIManager.TryGetTK("UIDialogBox", out UITKDialogBox _))
			{
				Assert.Inconclusive("A dialog box is registered, so the console would confirm through it instead.");
			}

			console.ApplyCatalogue(Catalogue());
			console.SelectCommand(IndexOf("/cmd wipe"));
			Live.Q<TextField>("staff-arg-0").value = "Bob";

			Button run = Q("staff-run");
			Click(run);
			LogAssert.AreEqual(UITKStaffConsole.ConfirmText, run.text, "the first click arms a confirmation");
			LogAssert.AreEqual(0, console.OutputLines.Count, "and sends nothing");

			Click(run);
			IReadOnlyList<string> lines = console.OutputLines;
			LogAssert.AreEqual(1, lines.Count, "the second click reaches the send step");
			LogAssert.AreEqual("Not sent: not connected.", lines[0], "which, with no client in this test, says so");
		}

		[Test]
		public void ChangingTheForm_DisarmsAConfirmation()
		{
			if (UIManager.TryGetTK("UIDialogBox", out UITKDialogBox _))
			{
				Assert.Inconclusive("A dialog box is registered, so the console would confirm through it instead.");
			}

			console.ApplyCatalogue(Catalogue());
			console.SelectCommand(IndexOf("/cmd wipe"));
			TextField character = Live.Q<TextField>("staff-arg-0");
			character.value = "Bob";

			Button run = Q("staff-run");
			Click(run);
			character.value = "Ann";
			LogAssert.AreEqual("Run", run.text, "a different line needs its own confirmation");

			Click(run);
			LogAssert.AreEqual(0, console.OutputLines.Count, "so the next click only re-arms");
		}

		[Test]
		public void SystemChatLines_ReachTheOutputLog_AndNothingElseDoes()
		{
			console.ApplyCatalogue(Catalogue());

			console.ReceiveChat(new ChatBroadcast { Channel = ChatChannel.System, Text = "Done." });
			console.ReceiveChat(new ChatBroadcast { Channel = ChatChannel.Say, Text = "hi there" });
			console.ReceiveChat(new ChatBroadcast { Channel = ChatChannel.System, Text = "   " });

			LogAssert.AreEqual(1, console.OutputLines.Count, "only the non-blank System line");
			LogAssert.AreEqual("Done.", console.OutputLines[0], "verbatim");
			LogAssert.AreEqual(1, Live.Q("staff-output-list").childCount, "and it is drawn");

			for (int i = 0; i < UITKStaffConsole.MaxOutputLines + 20; ++i)
			{
				console.ReceiveChat(new ChatBroadcast { Channel = ChatChannel.System, Text = "line " + i });
			}
			LogAssert.AreEqual(UITKStaffConsole.MaxOutputLines, console.OutputLines.Count, "the log is capped");
			LogAssert.AreEqual(UITKStaffConsole.MaxOutputLines, Live.Q("staff-output-list").childCount, "and so is its view");
		}

		// ── Scene wiring ────────────────────────────────────────────────────────────────────

		[Test]
		public void TheWorldGuiScene_CarriesTheConsole_StartingClosed()
		{
			const string scenePath = "Assets/Scenes/Client/ClientWorldGUI.unity";
			UnityEngine.SceneManagement.Scene scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
				scenePath, UnityEditor.SceneManagement.OpenSceneMode.Additive);
			try
			{
				LogAssert.IsTrue(scene.IsValid() && scene.isLoaded, $"{scenePath} must load");

				UITKStaffConsole found = null;
				foreach (GameObject rootObject in scene.GetRootGameObjects())
				{
					if (rootObject.name == "UIStaffConsole")
					{
						found = rootObject.GetComponent<UITKStaffConsole>();
					}
				}

				LogAssert.IsNotNull(found, "a root GameObject named UIStaffConsole carries the console component");
				LogAssert.IsNotNull(found.Document, "its Document is assigned");
				LogAssert.AreSame(found.gameObject, found.Document.gameObject, "to the UIDocument on the same object");
				LogAssert.AreEqual(UxmlPath, AssetDatabase.GetAssetPath(found.Document.visualTreeAsset), "rendering the console's markup");
				LogAssert.IsFalse(found.StartOpen, "it starts closed: nothing may show before a catalogue arrives");
				LogAssert.IsTrue(found.CloseOnQuitToMenu, "and closes on quit to login");
			}
			finally
			{
				UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene, true);
			}
		}

		// ── Layout ──────────────────────────────────────────────────────────────────────────

		[UnityTest]
		public IEnumerator TheFormInputs_HaveRoomToDrawText_AndTheRunButtonIsInsideThePanel()
		{
			console.ApplyCatalogue(Catalogue("players", "tickets"));
			console.SelectCommand(IndexOf("/cmd wipe"));
			console.SelectTab(UITKStaffConsole.ConsoleTab.Commands);
			Live.Q<TextField>("staff-arg-0").value = "Bob";

			for (int frame = 0; frame < 10; ++frame)
			{
				yield return null;
			}

			TextField field = Live.Q<TextField>("staff-arg-0");
			VisualElement glyph = field.Q("unity-text-input")?.Q(className: "unity-text-element");
			LogAssert.IsNotNull(glyph, "the text element inside the field");
			LogAssert.IsTrue(glyph.layout.height > 0f,
				$"the argument field must have room to draw its text; the glyph box resolved to {glyph.layout.width}x{glyph.layout.height}");

			VisualElement panel = Live.Q("staff-root");
			Rect panelBounds = panel.worldBound;
			LogAssert.IsTrue(panelBounds.width > 0f && panelBounds.height > 0f, $"the panel laid out: {panelBounds}");

			foreach (string name in new[] { "staff-run", "staff-tab-players", "staff-tab-tickets", "staff-tab-commands", "close-button", "staff-output-clear" })
			{
				Rect bounds = Live.Q(name).worldBound;
				LogAssert.IsTrue(bounds.width > 0f && bounds.height > 0f, $"{name} has a size: {bounds}");
				LogAssert.IsTrue(bounds.xMin >= panelBounds.xMin - 0.5f && bounds.xMax <= panelBounds.xMax + 0.5f &&
					bounds.yMin >= panelBounds.yMin - 0.5f && bounds.yMax <= panelBounds.yMax + 0.5f,
					$"{name} must sit inside the panel, which clips silently: {bounds} vs {panelBounds}");
			}
		}
	}
}
