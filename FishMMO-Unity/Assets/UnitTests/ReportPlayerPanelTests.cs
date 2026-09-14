using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using FishMMO.Client;
using FishMMO.Shared;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;
using Object = UnityEngine.Object;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The report-a-player form (issue #252, item 7) mounted on a real UI Toolkit panel.
	/// </summary>
	/// <remarks>
	/// What is pinned: the markup carries the controls the component reads, Submit stays disabled
	/// until a reason is chosen and something is written, the counter counts against the wire bound,
	/// a refusal keeps the typed text and a filing closes the form, the scene carries the panel, and
	/// the server half files through the chat commands' one filing body without a CanAct gate.
	/// Nothing here touches the network; the component has no client.
	/// </remarks>
	[TestFixture]
	public class ReportPlayerPanelTests
	{
		private const string PanelSettingsPath = "Assets/UI Toolkit/PanelSettings.asset";
		private const string UxmlPath = "Assets/Scripts/Client/GUI/World/ReportPlayer/UIReportPlayer.uxml";
		private const string HandlerPath = "Assets/Scripts/Server/Implementation/World/SceneServer/Chat/ChatSystem.ReportPlayer.cs";
		private const string SupportCommandsPath = "Assets/Scripts/Server/Implementation/World/SceneServer/Chat/ChatSystem.SupportCommands.cs";

		private GameObject host;
		private UITKReportPlayer panel;
		private UIDocument document;
		private PanelSettings sharedSettings;

		[SetUp]
		public void SetUp()
		{
			PanelSettings settings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
			VisualTreeAsset uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
			LogAssert.IsNotNull(settings, $"panel settings must exist at {PanelSettingsPath}");
			LogAssert.IsNotNull(uxml, $"the report UXML must exist at {UxmlPath}");

			sharedSettings = Object.Instantiate(settings);

			host = new GameObject(UITKReportPlayer.PanelName);
			document = host.AddComponent<UIDocument>();
			document.panelSettings = sharedSettings;
			document.visualTreeAsset = uxml;
			document.enabled = false;

			panel = host.AddComponent<UITKReportPlayer>();
			panel.Document = document;
			panel.StartOpen = false;
			panel.IsAlwaysOpen = false;
			panel.CloseOnQuitToMenu = true;
			panel.ReleasesCursor = false;
			panel.CloseOnEscape = true;

			MethodInfo awake = typeof(UITKControl).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic);
			LogAssert.IsNotNull(awake, "UITKControl must still declare Awake");
			awake.Invoke(panel, null);
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

		private DropdownField Reason => Live.Q<DropdownField>("report-reason");
		private TextField Description => Live.Q<TextField>("report-description");
		private Button SubmitButton => Live.Q<Button>("report-submit");

		private void SetAwaiting()
		{
			FieldInfo field = typeof(UITKReportPlayer).GetField("awaitingResult", BindingFlags.Instance | BindingFlags.NonPublic);
			LogAssert.IsNotNull(field, "the panel still tracks an in-flight report in awaitingResult");
			field.SetValue(panel, true);
		}

		private static string ReadSource(string relativePath)
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), relativePath);
			LogAssert.IsTrue(File.Exists(path), $"{relativePath} not found at {path}.");
			return File.ReadAllText(path).Replace("\r\n", "\n");
		}

		/// <summary>The source with comment lines removed, so prose about a call is not the call.</summary>
		private static string CodeOnly(string source)
		{
			return string.Join("\n", source.Split('\n').Where(line =>
			{
				string trimmed = line.TrimStart();
				return !(trimmed.StartsWith("//") || trimmed.StartsWith("/*") || trimmed.StartsWith("*"));
			}));
		}

		// ── Markup ──────────────────────────────────────────────────────────────────────────

		[Test]
		public void TheMarkup_CarriesTheReason_TheDescription_AndSubmit()
		{
			panel.Open(22, "Bob");

			LogAssert.IsTrue(panel.Visible, "the form is up");
			LogAssert.IsNotNull(Reason, "the reason dropdown exists");
			LogAssert.IsNotNull(Description, "the description field exists");
			LogAssert.IsNotNull(SubmitButton, "the submit button exists");
			LogAssert.IsNotNull(Live.Q<Button>("report-cancel"), "the cancel button exists");
			LogAssert.IsNotNull(Live.Q<Label>("report-counter"), "the character counter exists");
			LogAssert.IsTrue(Description.multiline, "the description is a text area");
			LogAssert.AreEqual(ReportPlayerBroadcast.MaxDescriptionLength, Description.maxLength, "typing stops at the wire bound");
			LogAssert.AreEqual(PlayerReportReasons.Count, Reason.choices.Count, "every reason is offered");
			LogAssert.AreEqual("Report Bob", Live.Q<Label>("report-title").text, "the title names the target");
		}

		[Test]
		public void TheReasonNames_CoverEveryEnumValue()
		{
			LogAssert.AreEqual(Enum.GetValues(typeof(PlayerReportReason)).Length, PlayerReportReasons.Count, "Count moves with the enum");
			LogAssert.IsFalse(PlayerReportReasons.IsDefined((PlayerReportReason)PlayerReportReasons.Count), "one past the end is refused");
			LogAssert.AreEqual("Offensive name", PlayerReportReasons.DisplayName(PlayerReportReason.OffensiveName), "names are the player's words");
		}

		// ── Submit gating ───────────────────────────────────────────────────────────────────

		[Test]
		public void Submit_IsDisabled_UntilAReasonIsChosen_AndSomethingIsWritten()
		{
			panel.Open(22, "Bob");

			LogAssert.IsFalse(SubmitButton.enabledSelf, "an empty form cannot be sent");
			LogAssert.IsFalse(panel.CanSubmit, "and the model agrees");

			Reason.value = PlayerReportReasons.DisplayName(PlayerReportReason.Cheating);
			LogAssert.IsFalse(SubmitButton.enabledSelf, "a reason alone is not a report");

			Description.value = "   ";
			LogAssert.IsFalse(SubmitButton.enabledSelf, "whitespace is not a description");

			Description.value = "Speed hacking at the east gate.";
			LogAssert.IsTrue(SubmitButton.enabledSelf, "a reason and a description can be sent");
			LogAssert.AreEqual("31 / 512", Live.Q<Label>("report-counter").text, "the counter counts what was typed");

			Reason.value = string.Empty;
			LogAssert.IsFalse(SubmitButton.enabledSelf, "a description without a reason cannot be sent");
		}

		[Test]
		public void Submit_WithoutAClient_SaysSo_AndKeepsTheText()
		{
			panel.Open(22, "Bob");
			Reason.value = PlayerReportReasons.DisplayName(PlayerReportReason.Spam);
			Description.value = "Trade channel spam.";

			panel.Submit();

			LogAssert.IsFalse(panel.AwaitingResult, "nothing was sent");
			LogAssert.AreEqual("Not connected.", panel.StatusText, "the player is told why");
			LogAssert.AreEqual("Trade channel spam.", Description.value, "the text survives");
		}

		// ── Results ─────────────────────────────────────────────────────────────────────────

		[Test]
		public void ARefusal_KeepsTheTypedText_AndShowsTheServersMessage()
		{
			panel.Open(22, "Bob");
			Reason.value = PlayerReportReasons.DisplayName(PlayerReportReason.Harassment);
			Description.value = "Followed me for an hour.";
			SetAwaiting();

			panel.ApplyResult(new ReportPlayerResultBroadcast { Filed = false, Message = "You have just filed a ticket." });

			LogAssert.IsTrue(panel.Visible, "a refusal leaves the form up");
			LogAssert.AreEqual("You have just filed a ticket.", Live.Q<Label>("report-status").text, "the server's words, verbatim");
			LogAssert.AreEqual("Followed me for an hour.", Description.value, "the typed text is kept");
			LogAssert.IsTrue(SubmitButton.enabledSelf, "and can be sent again");
		}

		[Test]
		public void AFiling_ClosesTheForm_AndDiscardsTheDraft()
		{
			panel.Open(22, "Bob");
			Reason.value = PlayerReportReasons.DisplayName(PlayerReportReason.Scam);
			Description.value = "Took the gold and left.";
			SetAwaiting();

			panel.ApplyResult(new ReportPlayerResultBroadcast { Filed = true, TicketID = 9, Message = "Ticket #9 filed." });

			LogAssert.IsFalse(panel.Visible, "a filing closes the form");
			LogAssert.IsFalse(panel.CanSubmit, "and the draft is gone");

			panel.Open(22, "Bob");
			LogAssert.AreEqual(string.Empty, Description.value, "reopening starts empty");
		}

		[Test]
		public void OpeningForSomebodyElse_StartsAFreshDraft()
		{
			panel.Open(22, "Bob");
			Reason.value = PlayerReportReasons.DisplayName(PlayerReportReason.Botting);
			Description.value = "Same path for six hours.";

			panel.Open(23, "Carol");

			LogAssert.AreEqual(23L, panel.TargetCharacterID, "the target moved");
			LogAssert.AreEqual("Report Carol", Live.Q<Label>("report-title").text, "and the title with it");
			LogAssert.AreEqual(string.Empty, Description.value, "Bob's report is not Carol's");
			LogAssert.IsFalse(SubmitButton.enabledSelf, "so there is nothing to send");
		}

		// ── Scene wiring ────────────────────────────────────────────────────────────────────

		[Test]
		public void TheWorldGuiScene_CarriesTheReportPanel_StartingClosed()
		{
			const string scenePath = "Assets/Scenes/Client/ClientWorldGUI.unity";
			UnityEngine.SceneManagement.Scene scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
				scenePath, UnityEditor.SceneManagement.OpenSceneMode.Additive);
			try
			{
				LogAssert.IsTrue(scene.IsValid() && scene.isLoaded, $"{scenePath} must load");

				UITKReportPlayer found = null;
				foreach (GameObject rootObject in scene.GetRootGameObjects())
				{
					if (rootObject.name == UITKReportPlayer.PanelName)
					{
						found = rootObject.GetComponent<UITKReportPlayer>();
					}
				}

				LogAssert.IsNotNull(found, "a root GameObject named UIReportPlayer carries the report component");
				LogAssert.IsNotNull(found.Document, "its Document is assigned");
				LogAssert.AreSame(found.gameObject, found.Document.gameObject, "to the UIDocument on the same object");
				LogAssert.AreEqual(UxmlPath, AssetDatabase.GetAssetPath(found.Document.visualTreeAsset), "rendering the report markup");
				LogAssert.IsFalse(found.StartOpen, "it starts closed");
				LogAssert.IsTrue(found.CloseOnEscape, "Escape closes it");
			}
			finally
			{
				UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene, true);
			}
		}

		// ── Server half ─────────────────────────────────────────────────────────────────────

		[Test]
		public void TheHandler_SkipsCanAct_AndFilesThroughTheOneFilingBody()
		{
			string handler = CodeOnly(ReadSource(HandlerPath));
			string commands = CodeOnly(ReadSource(SupportCommandsPath));

			LogAssert.IsTrue(handler.Contains("PlayerRequestGate.SkipCanAct"), "a dead or stunned player can still report");
			LogAssert.IsFalse(handler.Contains("RequireCanAct"), "and nothing puts the gate back");
			LogAssert.IsTrue(handler.Contains("SubmitTicket("), "the panel files through SubmitTicket");
			LogAssert.IsFalse(handler.Contains("CreateAsync"), "and never through a second copy of the create");
			LogAssert.AreEqual(1, Regex.Matches(commands, @"\.CreateAsync\(").Count, "there is exactly one create call");
		}
	}
}
