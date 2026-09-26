using System;
using System.IO;
using System.Reflection;
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
	/// The client's display of the world server's scene-routing queue: its wording and progress
	/// arithmetic as tables, and the panel mounted on a real UI Toolkit panel.
	/// </summary>
	/// <remarks>
	/// The mounted half runs without a client, so the buttons that would reach the network only
	/// take the panel down; that is how these tests see a click got as far as it could.
	/// </remarks>
	[TestFixture]
	public class WorldQueueDisplayTests
	{
		private const string PanelSettingsPath = "Assets/UI Toolkit/PanelSettings.asset";
		private const string UxmlPath = "Assets/Scripts/Client/GUI/Login/UIWorldQueueDisplay.uxml";

		// ── Wording ──────────────────────────────────────────────────────────────────────────

		[Test]
		public void EveryReason_HasItsOwnHeadline_AndItsOwnEndedHeadline()
		{
			var headlines = new System.Collections.Generic.HashSet<string>();
			var ended = new System.Collections.Generic.HashSet<string>();
			foreach (WorldSceneQueueReason reason in Enum.GetValues(typeof(WorldSceneQueueReason)))
			{
				LogAssert.IsTrue(!string.IsNullOrEmpty(WorldQueuePresentation.Headline(reason)), $"{reason} has a headline");
				LogAssert.IsTrue(!string.IsNullOrEmpty(WorldQueuePresentation.Detail(reason)), $"{reason} has a detail line");
				LogAssert.IsTrue(!string.IsNullOrEmpty(WorldQueuePresentation.EndedDetail(reason)), $"{reason} says what to do when it ends");
				headlines.Add(WorldQueuePresentation.Headline(reason));
				ended.Add(WorldQueuePresentation.EndedHeadline(reason));
			}

			int count = Enum.GetValues(typeof(WorldSceneQueueReason)).Length;
			LogAssert.AreEqual(count, headlines.Count, "the three waits mean different things and must not read the same");
			LogAssert.AreEqual(count, ended.Count, "and a failed wait says which wait failed");
		}

		[Test]
		public void TryingAgainAfterAFullWorld_SaysThePlaceIsKept()
		{
			/* This pinned the opposite until the world server began holding a place: a dropped or
			 * purged wait now keeps the account's place for a grace window, and Try again resumes
			 * it (O4). Telling the player they go to the back would now be the lie. */
			string detail = WorldQueuePresentation.EndedDetail(WorldSceneQueueReason.Capacity);
			LogAssert.IsFalse(detail.Contains("from the back"), "the retry no longer starts at the back");
			LogAssert.IsTrue(detail.Contains("place back in line") && detail.Contains("soon"),
				"and the player is told the place is kept, and that it is kept for a while, not forever");
			LogAssert.IsTrue(WorldQueuePresentation.Note(QueueKind.World).Contains("gives up your place"),
				"while leaving on purpose is what costs it");
		}

		[Test]
		public void TheLoginQueue_HasItsOwnWording_AndNoRetry()
		{
			LogAssert.AreEqual(WorldQueuePresentation.LoginWaitingTitle, WorldQueuePresentation.Title(QueueKind.Login, false), "login title");
			LogAssert.AreEqual(WorldQueuePresentation.LoginEndedTitle, WorldQueuePresentation.Title(QueueKind.Login, true), "login ended title");
			LogAssert.AreEqual(WorldQueuePresentation.WaitingTitle, WorldQueuePresentation.Title(QueueKind.World, false), "world title unchanged");
			LogAssert.AreEqual(WorldQueuePresentation.EndedTitle, WorldQueuePresentation.Title(QueueKind.World, true), "world ended title unchanged");

			foreach (WorldSceneQueueReason reason in Enum.GetValues(typeof(WorldSceneQueueReason)))
			{
				LogAssert.AreEqual(WorldQueuePresentation.LoginHeadline, WorldQueuePresentation.Headline(QueueKind.Login, reason), "the login queue has one wait");
				LogAssert.AreEqual(WorldQueuePresentation.Headline(reason), WorldQueuePresentation.Headline(QueueKind.World, reason), "the world queue keeps its three");
				LogAssert.AreEqual(WorldQueuePresentation.EndedDetail(reason), WorldQueuePresentation.EndedDetail(QueueKind.World, reason), "and its ended wording");
			}

			LogAssert.IsTrue(WorldQueuePresentation.OffersRetry(QueueKind.World), "a world wait can be rejoined: the session is held");
			LogAssert.IsFalse(WorldQueuePresentation.OffersRetry(QueueKind.Login), "a login wait cannot: that is signing in again");
			LogAssert.IsTrue(WorldQueuePresentation.EndedDetail(QueueKind.Login, WorldSceneQueueReason.Capacity).Contains("from the back"),
				"the login queue keeps nothing across connections, and says so");
			LogAssert.IsFalse(WorldQueuePresentation.Note(QueueKind.Login).Contains("place"), "and its note promises nothing about a place");
		}

		[Test]
		public void AnEstimateTheServerCouldNotMake_IsShownAsUnknown_NeverAsNoWait()
		{
			LogAssert.AreEqual(WorldQueuePresentation.UnknownEstimate, WorldQueuePresentation.EstimateText(0), "0 means the queue is not draining");
			LogAssert.AreEqual(WorldQueuePresentation.UnknownEstimate, WorldQueuePresentation.EstimateText(-5), "nor does a negative");
		}

		[Test]
		public void Estimates_AreRoundedTheWayAPersonWouldSayThem()
		{
			LogAssert.AreEqual("A few seconds", WorldQueuePresentation.EstimateText(4));
			LogAssert.AreEqual("A few seconds", WorldQueuePresentation.EstimateText(10));
			LogAssert.AreEqual("About 15s", WorldQueuePresentation.EstimateText(11));
			LogAssert.AreEqual("About 1 minute", WorldQueuePresentation.EstimateText(60));
			LogAssert.AreEqual("About 2 minutes", WorldQueuePresentation.EstimateText(61), "rounded up, never promising less than the server said");
		}

		[Test]
		public void Position_ReadsAsOneOfMany_AndNeverAsZero()
		{
			LogAssert.AreEqual("3 of 12", WorldQueuePresentation.PositionText(3, 12));
			LogAssert.AreEqual("1 of 1", WorldQueuePresentation.PositionText(1, 1));
			LogAssert.AreEqual("4", WorldQueuePresentation.PositionText(4, 2), "a total smaller than the position says nothing, so it is left out");
			LogAssert.AreEqual("1", WorldQueuePresentation.PositionText(0, 0), "position 0 is 'routed', never something to display");
		}

		[Test]
		public void Elapsed_IsAClock()
		{
			LogAssert.AreEqual("0:00", WorldQueuePresentation.ElapsedText(0));
			LogAssert.AreEqual("0:07", WorldQueuePresentation.ElapsedText(7.9));
			LogAssert.AreEqual("2:05", WorldQueuePresentation.ElapsedText(125));
			LogAssert.AreEqual("1:01:01", WorldQueuePresentation.ElapsedText(3661));
			LogAssert.AreEqual("0:00", WorldQueuePresentation.ElapsedText(-3));
			LogAssert.AreEqual("0:00", WorldQueuePresentation.ElapsedText(double.NaN));
		}

		[Test]
		public void Progress_IsHowMuchOfTheLineAheadHasCleared()
		{
			AssertProgress(0f, 10, 10, "nobody ahead has moved yet");
			AssertProgress(0.5f, 11, 6, "half of the ten ahead have gone");
			AssertProgress(1f, 10, 1, "at the front");
			AssertProgress(1f, 1, 1, "a wait that started at the front is full from the start");
			AssertProgress(0f, 5, 9, "a position behind the start re-baselines rather than going negative");
			AssertProgress(1f, 5, 0, "position 0 is clamped to the front");
		}

		private static void AssertProgress(float expected, int start, int position, string message)
		{
			float actual = WorldQueuePresentation.Progress(start, position);
			LogAssert.IsTrue(Math.Abs(expected - actual) < 1e-5f, $"{message}: expected {expected}, got {actual}");
		}

		// ── Layer ────────────────────────────────────────────────────────────────────────────

		[Test]
		public void TheDisplaySitsAboveTheLoadingOverlay_AndBelowTheReconnectDisplay()
		{
			LogAssert.IsTrue((int)UITKPanelLayer.SystemStatus > (int)UITKPanelLayer.System,
				"the wait it reports happens behind the loading overlay on every return through the world server");
			LogAssert.IsTrue((int)UITKPanelLayer.SystemStatus < (int)UITKPanelLayer.SystemAlert,
				"the reconnect display describes the connection actually running, and wins by rule, not by registration order");
		}

		// ── The mounted panel ────────────────────────────────────────────────────────────────

		private GameObject host;
		private UIDocument document;
		private UITKWorldQueueDisplay display;
		private PanelSettings sharedSettings;

		private void Mount()
		{
			PanelSettings settings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
			VisualTreeAsset uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
			LogAssert.IsNotNull(settings, $"panel settings must exist at {PanelSettingsPath}");
			LogAssert.IsNotNull(uxml, $"the display UXML must exist at {UxmlPath}");

			sharedSettings = Object.Instantiate(settings);

			host = new GameObject(UITKWorldQueueDisplay.PanelName);
			document = host.AddComponent<UIDocument>();
			document.panelSettings = sharedSettings;
			document.visualTreeAsset = uxml;
			document.enabled = false;

			display = host.AddComponent<UITKWorldQueueDisplay>();
			display.Document = document;
			display.StartOpen = false;
			display.IsAlwaysOpen = false;
			display.CloseOnQuitToMenu = true;
			display.ReleasesCursor = false;
			display.CloseOnEscape = false;

			MethodInfo awake = typeof(UITKControl).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic);
			LogAssert.IsNotNull(awake, "UITKControl must still declare Awake");
			awake.Invoke(display, null);
		}

		[TearDown]
		public void TearDown()
		{
			if (host != null)
			{
				Object.DestroyImmediate(host);
				host = null;
			}
			if (sharedSettings != null)
			{
				Object.DestroyImmediate(sharedSettings);
				sharedSettings = null;
			}
		}

		private VisualElement Live => document.rootVisualElement;

		private T Q<T>(string name) where T : VisualElement
		{
			T element = Live.Q<T>(name);
			LogAssert.IsNotNull(element, $"{name} must exist in the cloned tree");
			return element;
		}

		private static bool Shown(VisualElement element) => element.style.display.value != DisplayStyle.None;

		private static void Click(Button button)
		{
			using (NavigationSubmitEvent evt = NavigationSubmitEvent.GetPooled())
			{
				evt.target = button;
				button.SendEvent(evt);
			}
		}

		[Test]
		public void AWait_ShowsWhereThePlayerStands_AndOneWayOut()
		{
			Mount();
			display.ShowWaiting(3, 12, 45, WorldSceneQueueReason.Capacity);

			LogAssert.IsTrue(display.Visible, "shown");
			LogAssert.AreEqual(UITKWorldQueueDisplay.DisplayState.Waiting, display.State, "waiting");
			LogAssert.AreEqual(WorldQueuePresentation.WaitingTitle, Q<Label>("world-queue-title").text, "title");
			LogAssert.AreEqual(WorldQueuePresentation.Headline(WorldSceneQueueReason.Capacity), Q<Label>("world-queue-headline").text, "headline names the wait");
			LogAssert.AreEqual("3 of 12", Q<Label>("world-queue-position").text, "position");
			LogAssert.AreEqual(WorldQueuePresentation.EstimateText(45), Q<Label>("world-queue-estimate").text, "estimate");
			LogAssert.IsTrue(Shown(Q<VisualElement>("world-queue-stats")), "the numbers are up");
			LogAssert.IsFalse(Shown(Q<Button>("world-queue-retry-btn")), "there is nothing to retry while waiting");
			LogAssert.AreEqual("Leave queue", Q<Button>("world-queue-leave-btn").text, "the one way out");
			LogAssert.IsTrue(display.ReleasesCursor, "and the cursor to reach it, even from gameplay");
		}

		[Test]
		public void UpdatesWhileWaiting_MoveTheNumbers_AndTheBar()
		{
			Mount();
			display.ShowWaiting(11, 11, 0, WorldSceneQueueReason.Capacity);
			display.ShowWaiting(6, 20, 30, WorldSceneQueueReason.Capacity);

			LogAssert.AreEqual("6 of 20", Q<Label>("world-queue-position").text, "the latest position");
			Length width = Q<VisualElement>("world-queue-progress-fill").style.width.value;
			LogAssert.IsTrue(Math.Abs(width.value - 50f) < 0.01f && width.unit == LengthUnit.Percent,
				$"five of the ten ahead have cleared; the bar read {width.value}{width.unit}");
		}

		[Test]
		public void AReasonChange_IsReportedWithoutRestartingTheWait()
		{
			Mount();
			display.ShowWaiting(2, 2, 0, WorldSceneQueueReason.SceneLoading);
			display.ShowWaiting(1, 2, 0, WorldSceneQueueReason.Capacity);

			LogAssert.AreEqual(WorldQueuePresentation.Headline(WorldSceneQueueReason.Capacity), Q<Label>("world-queue-headline").text, "the headline follows the reason");
			LogAssert.AreEqual(UITKWorldQueueDisplay.DisplayState.Waiting, display.State, "still the same wait");
		}

		[Test]
		public void AnEndedWait_SaysWhichWaitFailed_AndOffersBothWays()
		{
			Mount();
			display.ShowWaiting(4, 9, 0, WorldSceneQueueReason.SceneLoading);
			display.ShowWaitEnded(WorldSceneQueueReason.SceneLoading);

			LogAssert.AreEqual(UITKWorldQueueDisplay.DisplayState.Ended, display.State, "ended");
			LogAssert.AreEqual(WorldQueuePresentation.EndedTitle, Q<Label>("world-queue-title").text, "title");
			LogAssert.AreEqual(WorldQueuePresentation.EndedHeadline(WorldSceneQueueReason.SceneLoading), Q<Label>("world-queue-headline").text, "names the wait that failed");
			LogAssert.IsFalse(Shown(Q<VisualElement>("world-queue-stats")), "the numbers are history now");
			LogAssert.IsFalse(Shown(Q<VisualElement>("world-queue-progress")), "and so is the bar");
			LogAssert.IsTrue(Shown(Q<Button>("world-queue-retry-btn")), "try again is offered");
			LogAssert.AreEqual("Return to login", Q<Button>("world-queue-leave-btn").text, "and so is going back, by its real name");
		}

		[Test]
		public void AnEndedWait_CanBeShownWithoutAWaitBeforeIt()
		{
			Mount();
			display.ShowWaitEnded(WorldSceneQueueReason.Capacity);

			LogAssert.IsTrue(display.Visible, "a -1 that arrives before any position is still explained");
			LogAssert.AreEqual(WorldQueuePresentation.EndedHeadline(WorldSceneQueueReason.Capacity), Q<Label>("world-queue-headline").text, "headline");
		}

		[Test]
		public void Dismiss_TakesThePanelDown_AndHandsTheCursorBack()
		{
			Mount();
			display.ShowWaiting(1, 1, 0, WorldSceneQueueReason.Capacity);
			display.Dismiss();

			LogAssert.IsFalse(display.Visible, "hidden");
			LogAssert.AreEqual(UITKWorldQueueDisplay.DisplayState.Hidden, display.State, "no state survives");
			LogAssert.IsFalse(display.ReleasesCursor, "a hidden panel must not keep the cursor released");
		}

		[Test]
		public void ANewWait_AfterAnEndedOne_StartsFresh()
		{
			Mount();
			display.ShowWaitEnded(WorldSceneQueueReason.Capacity);
			display.Dismiss();
			display.ShowWaiting(40, 40, 0, WorldSceneQueueReason.Capacity);

			LogAssert.IsTrue(Shown(Q<VisualElement>("world-queue-stats")), "the numbers come back");
			LogAssert.IsFalse(Shown(Q<Button>("world-queue-retry-btn")), "and try again goes away");
			Length width = Q<VisualElement>("world-queue-progress-fill").style.width.value;
			LogAssert.IsTrue(Math.Abs(width.value) < 0.01f, $"a new wait's bar starts empty; it read {width.value}");
		}

		[Test]
		public void LeavingTheQueue_TakesThePanelDown()
		{
			Mount();
			display.ShowWaiting(2, 5, 0, WorldSceneQueueReason.Capacity);
			Click(Q<Button>("world-queue-leave-btn"));

			LogAssert.IsFalse(display.Visible, "with no client to quit through, the click still ends the panel's part in it");
		}

		[Test]
		public void ALoginWait_IsShownWithTheLoginQueuesWords()
		{
			Mount();
			display.ShowLoginWaiting(7, 40, 2);

			LogAssert.IsTrue(display.Visible, "shown");
			LogAssert.AreEqual(QueueKind.Login, display.Kind, "as the login queue");
			LogAssert.AreEqual(WorldQueuePresentation.LoginWaitingTitle, Q<Label>("world-queue-title").text, "title");
			LogAssert.AreEqual(WorldQueuePresentation.LoginHeadline, Q<Label>("world-queue-headline").text, "headline");
			LogAssert.AreEqual(WorldQueuePresentation.Note(QueueKind.Login), Q<Label>("world-queue-note").text, "note");
			LogAssert.AreEqual("7 of 40", Q<Label>("world-queue-position").text, "position");
			LogAssert.AreEqual("Leave queue", Q<Button>("world-queue-leave-btn").text, "the same way out");
		}

		[Test]
		public void AnEndedLoginWait_OffersOnlyClose()
		{
			Mount();
			display.ShowLoginWaiting(3, 3, 0);
			display.ShowLoginWaitEnded();

			LogAssert.AreEqual(UITKWorldQueueDisplay.DisplayState.Ended, display.State, "ended");
			LogAssert.AreEqual(WorldQueuePresentation.LoginEndedTitle, Q<Label>("world-queue-title").text, "title");
			LogAssert.AreEqual(WorldQueuePresentation.LoginEndedHeadline, Q<Label>("world-queue-headline").text, "headline");
			LogAssert.IsFalse(Shown(Q<Button>("world-queue-retry-btn")), "no Try again: rejoining means signing in again");
			LogAssert.AreEqual(WorldQueuePresentation.EndedLeaveLabel(QueueKind.Login), Q<Button>("world-queue-leave-btn").text, "Close");

			Click(Q<Button>("world-queue-leave-btn"));
			LogAssert.IsFalse(display.Visible, "and closing it takes it down");
		}

		[Test]
		public void TheWorldQueueAfterTheLoginQueue_IsANewWait()
		{
			Mount();
			display.ShowLoginWaiting(2, 2, 0);
			display.ShowWaiting(30, 30, 0, WorldSceneQueueReason.Capacity);

			LogAssert.AreEqual(QueueKind.World, display.Kind, "the world queue now");
			LogAssert.AreEqual(WorldQueuePresentation.WaitingTitle, Q<Label>("world-queue-title").text, "with its own title");
			LogAssert.AreEqual(WorldQueuePresentation.Note(QueueKind.World), Q<Label>("world-queue-note").text, "and its own note");
			Length width = Q<VisualElement>("world-queue-progress-fill").style.width.value;
			LogAssert.IsTrue(Math.Abs(width.value) < 0.01f, $"its progress is measured from its own start, not the login queue's; it read {width.value}");
		}

		// ── The client's side of the wire ────────────────────────────────────────────────────

		[Test]
		public void LeavingTheWorldQueue_TellsTheWorldServerBeforeTheConnectionCloses()
		{
			string client = File.ReadAllText("Assets/Scripts/Client/Client.cs").Replace("\r\n", "\n");
			int start = client.IndexOf("public void LeaveWorldQueue()", StringComparison.Ordinal);
			LogAssert.IsTrue(start >= 0, "Client.LeaveWorldQueue exists");
			string body = client.Substring(start, client.IndexOf("\n\t\t}", start, StringComparison.Ordinal) - start);
			int announce = body.IndexOf("new WorldSceneQueueLeaveBroadcast()", StringComparison.Ordinal);
			int quit = body.IndexOf("QuitToLogin()", StringComparison.Ordinal);
			LogAssert.IsTrue(announce >= 0 && quit > announce,
				"the leave is written before QuitToLogin, whose disconnect waits for the outgoing bundle to flush; a player who left must not keep a place");

			string panel = File.ReadAllText("Assets/Scripts/Client/GUI/Login/UITKWorldQueueDisplay.cs");
			LogAssert.IsTrue(panel.Contains("Client?.LeaveWorldQueue();"), "the panel's Leave queue goes through it");
		}
	}
}
