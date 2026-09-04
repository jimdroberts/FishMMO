using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using FishNet.Transporting;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.Client
{
	/// <summary>
	/// UI Toolkit arena HUD: the phase, the ready check, the start countdown, the team scores, the
	/// clock, the objectives, the kill feed and the announcer, drawn from the server's broadcasts
	/// while the player is in a match.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Also the client's end of three contracts. It publishes the roster to
	/// <see cref="ArenaTeamRegistry"/> so the client's own targeting agrees with the server about
	/// who is an enemy; it fires the arena's cues — the template's countdown and event triggers on
	/// the local character, and <see cref="ArenaClientEvents"/> — from the server-timed state, so a
	/// sound at three seconds plays at the server's three seconds on every client; and it drives
	/// <see cref="ArenaFlagVisuals"/> from the objectives it is sent.
	/// </para>
	/// <para>
	/// Hidden when the player is not in a match. Starts closed and shows itself on the first
	/// state; its broadcasts are registered when the client is set, not when the tree is built.
	/// A player standing in a match with no seat is a spectator and is offered the free camera.
	/// </para>
	/// </remarks>
	public class UITKArenaHud : UITKCharacterControl
	{
		private const string PHASE_NAME = "arenahud-phase";
		private const string COUNTDOWN_NAME = "arenahud-countdown";
		private const string CLOCK_NAME = "arenahud-clock";
		private const string SCORES_NAME = "arenahud-scores";
		private const string HINT_NAME = "arenahud-hint";
		private const string OBJECTIVES_NAME = "arenahud-objectives";
		private const string READY_NAME = "arenahud-ready";
		private const string READY_TEXT_NAME = "arenahud-ready-text";
		private const string READY_ACCEPT_NAME = "arenahud-ready-accept";
		private const string READY_DECLINE_NAME = "arenahud-ready-decline";
		private const string FREECAM_NAME = "arenahud-freecam";
		private const string FEED_NAME = "arenahud-feed";
		private const string ANNOUNCE_NAME = "arenahud-announce";

		private const string ROOT_LIVE_CLASS = "arenahud-root--live";
		private const string COUNTDOWN_HIDDEN_CLASS = "arenahud-countdown--hidden";
		private const string READY_HIDDEN_CLASS = "arenahud-ready--hidden";
		private const string FREECAM_HIDDEN_CLASS = "arenahud-freecam--hidden";
		private const string ANNOUNCE_HIDDEN_CLASS = "arenahud-announce--hidden";
		private const string SCORE_CLASS = "arenahud-score";
		private const string SCORE_MINE_CLASS = "arenahud-score--mine";
		private const string FEED_LINE_CLASS = "arenahud-feed__line";

		/// <summary>Lines the kill feed keeps.</summary>
		private const int FeedLines = 6;

		/// <summary>Seconds a feed line stays before it is dropped.</summary>
		private const float FeedLineSeconds = 8.0f;

		/// <summary>Seconds an announcement stays on screen.</summary>
		private const float AnnounceSeconds = 2.5f;

		protected override UITKPanelLayer Layer => UITKPanelLayer.Hud;

		private Label phaseLabel;
		private Label countdownLabel;
		private Label clockLabel;
		private VisualElement scoresRow;
		private Label hintLabel;
		private VisualElement objectivesBox;
		private VisualElement readyBox;
		private Label readyLabel;
		private Button readyAcceptButton;
		private Button readyDeclineButton;
		private Button freeCamButton;
		private VisualElement feedBox;
		private Label announceLabel;

		private ArenaMatchStateBroadcast state;
		private bool hasState;
		private int lastCueSecond = int.MinValue;
		private int[] lastScores;

		/// <summary>Local clock for the seconds between server updates, so the display counts down smoothly.</summary>
		private float secondsRemainingAt;
		private int secondsRemainingBase;

		private ArenaReadyCheckBroadcast readyCheck;
		private bool hasReadyCheck;
		private bool readyAnswered;

		private readonly List<(Label label, float expiresAt)> feed = new List<(Label, float)>();
		private float announceUntil;

		public override void OnStarting()
		{
			VisualElement root = Root;
			if (root == null)
			{
				return;
			}
			phaseLabel = root.Q<Label>(PHASE_NAME);
			countdownLabel = root.Q<Label>(COUNTDOWN_NAME);
			clockLabel = root.Q<Label>(CLOCK_NAME);
			scoresRow = root.Q(SCORES_NAME);
			hintLabel = root.Q<Label>(HINT_NAME);
			objectivesBox = root.Q(OBJECTIVES_NAME);
			readyBox = root.Q(READY_NAME);
			readyLabel = root.Q<Label>(READY_TEXT_NAME);
			readyAcceptButton = root.Q<Button>(READY_ACCEPT_NAME);
			readyDeclineButton = root.Q<Button>(READY_DECLINE_NAME);
			freeCamButton = root.Q<Button>(FREECAM_NAME);
			feedBox = root.Q(FEED_NAME);
			announceLabel = root.Q<Label>(ANNOUNCE_NAME);

			if (readyAcceptButton != null) readyAcceptButton.clicked += () => AnswerReadyCheck(true);
			if (readyDeclineButton != null) readyDeclineButton.clicked += () => AnswerReadyCheck(false);
			if (freeCamButton != null) freeCamButton.clicked += OnClick_FreeCamera;
		}

		public override void OnClientSet()
		{
			Client.NetworkManager.ClientManager.RegisterBroadcast<ArenaMatchStateBroadcast>(OnClientArenaMatchStateBroadcastReceived);
			Client.NetworkManager.ClientManager.RegisterBroadcast<ArenaReadyCheckBroadcast>(OnClientArenaReadyCheckReceived);
			Client.NetworkManager.ClientManager.RegisterBroadcast<ArenaEventBroadcast>(OnClientArenaEventReceived);
		}

		public override void OnClientUnset()
		{
			Client.NetworkManager.ClientManager.UnregisterBroadcast<ArenaMatchStateBroadcast>(OnClientArenaMatchStateBroadcastReceived);
			Client.NetworkManager.ClientManager.UnregisterBroadcast<ArenaReadyCheckBroadcast>(OnClientArenaReadyCheckReceived);
			Client.NetworkManager.ClientManager.UnregisterBroadcast<ArenaEventBroadcast>(OnClientArenaEventReceived);
		}

		private void OnClientArenaMatchStateBroadcastReceived(ArenaMatchStateBroadcast msg, Channel channel)
		{
			if (Character == null)
			{
				return;
			}

			ArenaMatchPhase previous = hasState ? state.Phase : ArenaMatchPhase.Cancelled;
			state = msg;
			hasState = true;
			secondsRemainingBase = msg.SecondsRemaining;
			secondsRemainingAt = Time.unscaledTime;

			if (msg.Phase != ArenaMatchPhase.ReadyCheck)
			{
				hasReadyCheck = false;
				readyAnswered = false;
			}

			PublishRoster();
			FireCues(previous);

			ArenaTemplate template = msg.ArenaTemplateID != 0 ? ArenaTemplate.Get<ArenaTemplate>(msg.ArenaTemplateID) : null;
			ArenaFlagVisuals.Apply(msg, template);

			bool over = msg.Phase == ArenaMatchPhase.Ended || msg.Phase == ArenaMatchPhase.Cancelled;
			ArenaClientEvents.SetMatchState(over ? (ArenaMatchStateBroadcast?)null : msg);
			ArenaClientEvents.IsSpectating = !over && MyTeam() < 0;

			if (over)
			{
				// The results screen takes over; the strip has nothing more to say.
				if (ArenaSpectatorCamera.Active) ArenaSpectatorCamera.Disable();
				ArenaClientEvents.IsSpectating = false;
				ClearFeed();
				Hide();
				return;
			}

			if (!Visible)
			{
				Show();
				return;
			}
			ApplyState();
		}

		/// <summary>Publishes the roster to the client-side team registry for the local character's scene.</summary>
		private void PublishRoster()
		{
			if (Character?.GameObject == null)
			{
				return;
			}

			int handle = Character.GameObject.scene.handle;
			if (state.Phase == ArenaMatchPhase.Ended || state.Phase == ArenaMatchPhase.Cancelled)
			{
				ArenaTeamRegistry.Unpublish(handle);
				return;
			}

			var roster = new Dictionary<long, int>(state.Members?.Length ?? 0);
			if (state.Members != null)
			{
				foreach (ArenaMemberEntry member in state.Members)
				{
					roster[member.CharacterID] = member.Team;
				}
			}
			ArenaTemplate template = state.ArenaTemplateID != 0 ? ArenaTemplate.Get<ArenaTemplate>(state.ArenaTemplateID) : null;
			ArenaTeamRegistry.Publish(handle, roster, state.Phase == ArenaMatchPhase.Live, template?.ResolveTeamColors());
		}

		/// <summary>Fires the template's cue triggers and the client events for what just changed.</summary>
		private void FireCues(ArenaMatchPhase previous)
		{
			ArenaTemplate template = state.ArenaTemplateID != 0 ? ArenaTemplate.Get<ArenaTemplate>(state.ArenaTemplateID) : null;
			int myTeam = MyTeam();

			if (state.Phase == ArenaMatchPhase.Countdown)
			{
				int second = state.SecondsRemaining;
				if (second != lastCueSecond)
				{
					lastCueSecond = second;
					ArenaClientEvents.RaiseCountdownTick(second);
					InvokeCountdownCues(template, second, myTeam);
				}
			}
			else if (state.Phase == ArenaMatchPhase.Live && previous != ArenaMatchPhase.Live)
			{
				// The start itself: second zero.
				if (lastCueSecond != 0)
				{
					lastCueSecond = 0;
					ArenaClientEvents.RaiseCountdownTick(0);
					InvokeCountdownCues(template, 0, myTeam);
				}
				ArenaClientEvents.RaiseMatchLive();
				Announce("FIGHT!");
			}

			if (state.Phase == ArenaMatchPhase.Live && state.TeamScores != null)
			{
				if (lastScores != null && lastScores.Length == state.TeamScores.Length)
				{
					for (int t = 0; t < state.TeamScores.Length; ++t)
					{
						if (state.TeamScores[t] != lastScores[t])
						{
							ArenaClientEvents.RaiseTeamScored(t, state.TeamScores[t]);
						}
					}
				}
				lastScores = (int[])state.TeamScores.Clone();
			}

			if (state.Phase != ArenaMatchPhase.Countdown && state.Phase != ArenaMatchPhase.Live)
			{
				lastCueSecond = int.MinValue;
				lastScores = null;
			}
		}

		private void InvokeCountdownCues(ArenaTemplate template, int second, int myTeam)
		{
			if (template?.CountdownCues == null || Character == null)
			{
				return;
			}

			foreach (ArenaCountdownCue cue in template.CountdownCues)
			{
				if (cue != null && cue.SecondsRemaining == second && cue.Triggers != null && cue.Triggers.Count > 0)
				{
					Character.Invoke(cue.Triggers, new ArenaEventData(Character, ArenaCuePhase.Countdown, second, myTeam, -1));
				}
			}
		}

		private int MyTeam()
		{
			if (Character == null || state.Members == null)
			{
				return -1;
			}
			foreach (ArenaMemberEntry member in state.Members)
			{
				if (member.CharacterID == Character.ID)
				{
					return member.Team;
				}
			}
			return -1;
		}

		// ──────────────────────────────────────────────────────────────────
		//  Ready check
		// ──────────────────────────────────────────────────────────────────

		private void OnClientArenaReadyCheckReceived(ArenaReadyCheckBroadcast msg, Channel channel)
		{
			readyCheck = msg;
			hasReadyCheck = true;
			if (msg.YouAnswered)
			{
				readyAnswered = true;
			}
			ArenaClientEvents.RaiseReadyCheck(msg);
			if (!Visible)
			{
				Show();
				return;
			}
			ApplyReadyCheck();
		}

		private void AnswerReadyCheck(bool accept)
		{
			if (!hasReadyCheck || readyAnswered || Client == null || Client.NetworkManager == null || !Client.NetworkManager.IsClientStarted)
			{
				return;
			}
			readyAnswered = true;
			Client.Broadcast(new ArenaReadyResponseBroadcast { MatchID = readyCheck.MatchID, Accept = accept });
			ApplyReadyCheck();
		}

		private void ApplyReadyCheck()
		{
			if (readyBox == null)
			{
				return;
			}

			bool showing = hasState && state.Phase == ArenaMatchPhase.ReadyCheck && hasReadyCheck && MyTeam() >= 0;
			if (!showing)
			{
				readyBox.AddToClassList(READY_HIDDEN_CLASS);
				return;
			}
			readyBox.RemoveFromClassList(READY_HIDDEN_CLASS);

			int seconds = Mathf.Max(0, RemainingSeconds());
			if (readyLabel != null)
			{
				readyLabel.text = readyAnswered
					? $"Waiting for the others… {readyCheck.Accepted}/{readyCheck.Total} accepted ({seconds}s)"
					: $"Everyone is here. Ready? {readyCheck.Accepted}/{readyCheck.Total} accepted ({seconds}s)";
			}
			if (readyAcceptButton != null) readyAcceptButton.style.display = readyAnswered ? DisplayStyle.None : DisplayStyle.Flex;
			if (readyDeclineButton != null) readyDeclineButton.style.display = readyAnswered ? DisplayStyle.None : DisplayStyle.Flex;
		}

		// ──────────────────────────────────────────────────────────────────
		//  Kill feed and announcer
		// ──────────────────────────────────────────────────────────────────

		private void OnClientArenaEventReceived(ArenaEventBroadcast msg, Channel channel)
		{
			if (Character == null)
			{
				return;
			}

			ArenaClientEvents.RaiseArenaEvent(msg);

			ArenaTemplate template = hasState && state.ArenaTemplateID != 0 ? ArenaTemplate.Get<ArenaTemplate>(state.ArenaTemplateID) : null;
			int myTeam = MyTeam();
			bool isActor = msg.ActorID != 0 && msg.ActorID == Character.ID;
			bool isTarget = msg.TargetID != 0 && msg.TargetID == Character.ID;

			string line = DescribeEvent(msg, isActor, isTarget, out string shout);
			if (!string.IsNullOrEmpty(line))
			{
				Color color = msg.Team >= 0
					? (template != null ? template.GetTeamColor(msg.Team) : ArenaTeamColors.Default(msg.Team))
					: (msg.ActorTeam >= 0 ? (template != null ? template.GetTeamColor(msg.ActorTeam) : ArenaTeamColors.Default(msg.ActorTeam)) : Color.gray);
				AddFeedLine(line, color);
			}
			if (!string.IsNullOrEmpty(shout))
			{
				Announce(shout);
			}

			// Designer cues for this moment.
			if (template?.EventCues != null && Character != null)
			{
				foreach (ArenaEventCue cue in template.EventCues)
				{
					if (cue == null || cue.Kind != msg.Kind || cue.Triggers == null || cue.Triggers.Count == 0)
					{
						continue;
					}
					if (cue.ActorOnly && !isActor)
					{
						continue;
					}
					Character.Invoke(cue.Triggers, new ArenaEventData(Character, ArenaCuePhase.Event, 0, myTeam, -1, msg.Kind, isActor, isTarget, msg.Value));
				}
			}
		}

		/// <summary>One line of feed text for an event, and the announcer's shout when it earns one.</summary>
		private static string DescribeEvent(ArenaEventBroadcast e, bool isActor, bool isTarget, out string shout)
		{
			shout = null;
			string actor = string.IsNullOrEmpty(e.ActorName) ? "Someone" : e.ActorName;
			string target = string.IsNullOrEmpty(e.TargetName) ? "someone" : e.TargetName;
			switch (e.Kind)
			{
				case ArenaEventKind.Kill:
					return e.ActorID != 0 ? $"{actor} killed {target}" : $"{target} died";
				case ArenaEventKind.FirstBlood:
					shout = "FIRST BLOOD"; return $"{actor} drew first blood";
				case ArenaEventKind.KillingSpree:
					shout = isActor ? $"KILLING SPREE ×{e.Value}" : null; return $"{actor} is on a killing spree ({e.Value})";
				case ArenaEventKind.SpreeEnded:
					return $"{actor} ended {target}'s spree of {e.Value}";
				case ArenaEventKind.FlagTaken:
					shout = $"TEAM {e.Team + 1} FLAG TAKEN"; return $"{actor} took team {e.Team + 1}'s flag";
				case ArenaEventKind.FlagDropped:
					return $"{actor} dropped team {e.Team + 1}'s flag";
				case ArenaEventKind.FlagReturned:
					shout = "FLAG RETURNED"; return e.ActorID != 0 ? $"{actor} returned team {e.Team + 1}'s flag" : $"Team {e.Team + 1}'s flag returned";
				case ArenaEventKind.FlagCaptured:
					shout = $"TEAM {e.Team + 1} SCORES"; return $"{actor} captured the flag for team {e.Team + 1}";
				case ArenaEventKind.PointCaptured:
					shout = $"TEAM {e.Team + 1} TAKES THE POINT"; return $"{actor} captured the point for team {e.Team + 1}";
				case ArenaEventKind.PlayerDisconnected:
					return $"{target} disconnected ({e.Value}s to return)";
				case ArenaEventKind.PlayerReconnected:
					return $"{target} is back";
				case ArenaEventKind.PlayerForfeited:
					return $"{target} left the match";
				case ArenaEventKind.PlayerBackfilled:
					return $"{target} joined team {e.Team + 1}";
				case ArenaEventKind.NearScoreLimit:
					shout = $"TEAM {e.Team + 1} NEEDS {e.Value}"; return $"Team {e.Team + 1} is {e.Value} from victory";
				case ArenaEventKind.TimeWarning:
					shout = e.Value >= 60 ? $"{e.Value / 60} MINUTE{(e.Value >= 120 ? "S" : "")} LEFT" : $"{e.Value} SECONDS LEFT"; return null;
				default:
					return null;
			}
		}

		private void AddFeedLine(string text, Color color)
		{
			if (feedBox == null)
			{
				return;
			}
			Label line = new Label(text);
			line.AddToClassList("fish-hint");
			line.AddToClassList(FEED_LINE_CLASS);
			line.style.borderLeftColor = color;
			feedBox.Add(line);
			feed.Add((line, Time.unscaledTime + FeedLineSeconds));
			while (feed.Count > FeedLines)
			{
				feed[0].label.RemoveFromHierarchy();
				feed.RemoveAt(0);
			}
		}

		private void TickFeed()
		{
			float now = Time.unscaledTime;
			while (feed.Count > 0 && feed[0].expiresAt <= now)
			{
				feed[0].label.RemoveFromHierarchy();
				feed.RemoveAt(0);
			}
			if (announceLabel != null && announceUntil > 0f && now >= announceUntil)
			{
				announceUntil = 0f;
				announceLabel.AddToClassList(ANNOUNCE_HIDDEN_CLASS);
			}
		}

		private void ClearFeed()
		{
			foreach ((Label label, float _) in feed)
			{
				label.RemoveFromHierarchy();
			}
			feed.Clear();
			announceUntil = 0f;
			announceLabel?.AddToClassList(ANNOUNCE_HIDDEN_CLASS);
		}

		private void Announce(string text)
		{
			if (announceLabel == null || string.IsNullOrEmpty(text))
			{
				return;
			}
			announceLabel.text = text;
			announceLabel.RemoveFromClassList(ANNOUNCE_HIDDEN_CLASS);
			announceUntil = Time.unscaledTime + AnnounceSeconds;
		}

		// ──────────────────────────────────────────────────────────────────
		//  Spectator
		// ──────────────────────────────────────────────────────────────────

		private void OnClick_FreeCamera()
		{
			if (!ArenaClientEvents.IsSpectating)
			{
				ArenaSpectatorCamera.Disable();
				return;
			}
			ArenaSpectatorCamera.Toggle();
			ApplySpectator();
		}

		private void ApplySpectator()
		{
			if (freeCamButton == null)
			{
				return;
			}
			bool spectating = ArenaClientEvents.IsSpectating;
			if (spectating) freeCamButton.RemoveFromClassList(FREECAM_HIDDEN_CLASS);
			else freeCamButton.AddToClassList(FREECAM_HIDDEN_CLASS);
			freeCamButton.text = ArenaSpectatorCamera.Active ? "Character camera" : "Free camera (RMB look, WASD, Q/E)";
			if (!spectating && ArenaSpectatorCamera.Active)
			{
				ArenaSpectatorCamera.Disable();
			}
		}

		// ──────────────────────────────────────────────────────────────────
		//  Drawing
		// ──────────────────────────────────────────────────────────────────

		protected override void OnAfterShow()
		{
			ApplyState();
		}

		protected override void OnAfterStarting()
		{
			base.OnAfterStarting();
			ApplyState();
		}

		private void ApplyState()
		{
			if (!hasState)
			{
				return;
			}

			ArenaTemplate template = state.ArenaTemplateID != 0 ? ArenaTemplate.Get<ArenaTemplate>(state.ArenaTemplateID) : null;
			int myTeam = MyTeam();

			VisualElement root = Root;
			if (root != null && root.childCount > 0)
			{
				if (state.Phase == ArenaMatchPhase.Live) root[0].AddToClassList(ROOT_LIVE_CLASS);
				else root[0].RemoveFromClassList(ROOT_LIVE_CLASS);
			}

			if (phaseLabel != null)
			{
				string arenaName = template != null ? template.ResolvedDisplayName : "Arena";
				if (template != null && template.IsRankedFormat(state.Format)) arenaName += " ★";
				switch (state.Phase)
				{
					case ArenaMatchPhase.Gathering: phaseLabel.text = $"{arenaName} — waiting for players"; break;
					case ArenaMatchPhase.ReadyCheck: phaseLabel.text = $"{arenaName} — ready check"; break;
					case ArenaMatchPhase.Countdown: phaseLabel.text = $"{arenaName} — get ready"; break;
					default: phaseLabel.text = myTeam < 0 ? $"{arenaName} — spectating" : arenaName; break;
				}
			}

			if (countdownLabel != null)
			{
				bool counting = state.Phase == ArenaMatchPhase.Countdown;
				countdownLabel.text = counting ? Mathf.Max(0, RemainingSeconds()).ToString() : string.Empty;
				if (counting) countdownLabel.RemoveFromClassList(COUNTDOWN_HIDDEN_CLASS);
				else countdownLabel.AddToClassList(COUNTDOWN_HIDDEN_CLASS);
			}

			if (clockLabel != null)
			{
				int remaining = RemainingSeconds();
				clockLabel.text = state.Phase == ArenaMatchPhase.Live && remaining > 0
					? $"{remaining / 60}:{remaining % 60:00}"
					: string.Empty;
			}

			if (scoresRow != null)
			{
				scoresRow.Clear();
				if (state.TeamScores != null)
				{
					for (int t = 0; t < state.TeamScores.Length; ++t)
					{
						Label score = new Label($"Team {t + 1}  {state.TeamScores[t]}");
						score.AddToClassList("fish-badge");
						score.AddToClassList(SCORE_CLASS);
						if (t == myTeam) score.AddToClassList(SCORE_MINE_CLASS);
						ArenaTeamStyle.Apply(score, template != null ? template.GetTeamColor(t) : ArenaTeamColors.Default(t));
						scoresRow.Add(score);
					}
				}
			}

			ApplyObjectives(myTeam);
			ApplyReadyCheck();
			ApplySpectator();

			if (hintLabel != null)
			{
				int present = 0, total = 0, reconnecting = 0;
				if (state.Members != null)
				{
					foreach (ArenaMemberEntry m in state.Members)
					{
						++total;
						if (m.Present) ++present;
						if (m.Reconnecting) ++reconnecting;
					}
				}
				if (state.Phase == ArenaMatchPhase.Gathering)
				{
					hintLabel.text = $"{present}/{total} players here";
				}
				else if (state.Phase == ArenaMatchPhase.Live && reconnecting > 0)
				{
					hintLabel.text = reconnecting == 1 ? "1 player reconnecting…" : $"{reconnecting} players reconnecting…";
				}
				else
				{
					hintLabel.text = string.Empty;
				}
			}
		}

		/// <summary>One line per flag or control point: whose it is, who has it, how far a capture has got.</summary>
		private void ApplyObjectives(int myTeam)
		{
			if (objectivesBox == null)
			{
				return;
			}

			objectivesBox.Clear();
			if (state.Objectives == null || state.Objectives.Length == 0)
			{
				objectivesBox.style.display = DisplayStyle.None;
				return;
			}
			objectivesBox.style.display = DisplayStyle.Flex;

			foreach (ArenaObjectiveEntry objective in state.Objectives)
			{
				Label line = new Label();
				line.AddToClassList("fish-hint");
				line.AddToClassList("arenahud-objective");

				if (objective.Kind == ArenaObjectiveKind.FlagStand)
				{
					string whose = objective.Team == myTeam ? "Your flag" : $"Team {objective.Team + 1} flag";
					var flag = (ArenaFlagState)Mathf.Clamp(objective.Progress, 0, 2);
					if (flag == ArenaFlagState.Carried)
					{
						bool mine = Character != null && objective.Holder == Character.ID;
						line.text = mine ? $"{whose}: carried by you" : $"{whose}: taken";
						line.AddToClassList(objective.Team == myTeam ? "fish-label--danger" : "fish-label--good");
					}
					else if (flag == ArenaFlagState.Dropped)
					{
						line.text = objective.Team == myTeam ? $"{whose}: dropped — touch it to return it" : $"{whose}: dropped — pick it up";
						line.AddToClassList("fish-label--accent");
					}
					else
					{
						line.text = $"{whose}: home";
					}
				}
				else
				{
					string owner = objective.Team < 0 ? "neutral" : (objective.Team == myTeam ? "held by your team" : $"held by team {objective.Team + 1}");
					string progress = objective.Holder >= 0 && objective.Progress > 0 ? $" · team {objective.Holder + 1} capturing ({objective.Progress})" : string.Empty;
					line.text = $"Control point: {owner}{progress}";
					if (objective.Team >= 0)
					{
						line.AddToClassList(objective.Team == myTeam ? "fish-label--good" : "fish-label--danger");
					}
				}

				objectivesBox.Add(line);
			}
		}

		private int RemainingSeconds()
		{
			return Mathf.CeilToInt(secondsRemainingBase - (Time.unscaledTime - secondsRemainingAt));
		}

		protected override void OnTick()
		{
			if (!Visible || !hasState)
			{
				return;
			}
			TickFeed();
			if (state.Phase == ArenaMatchPhase.Countdown || state.Phase == ArenaMatchPhase.Live || state.Phase == ArenaMatchPhase.ReadyCheck)
			{
				ApplyState();
			}
		}

		public override void OnPostUnsetCharacter()
		{
			base.OnPostUnsetCharacter();
			hasState = false;
			state = default;
			hasReadyCheck = false;
			readyAnswered = false;
			lastCueSecond = int.MinValue;
			lastScores = null;
			ClearFeed();
			ArenaFlagVisuals.Clear();
			ArenaSpectatorCamera.Disable();
			ArenaClientEvents.IsSpectating = false;
			ArenaTeamRegistry.Clear();
			ArenaClientEvents.SetMatchState(null);
			Hide();
		}
	}
}
