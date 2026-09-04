using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using FishNet.Transporting;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.Client
{
	/// <summary>
	/// UI Toolkit arena HUD: the phase, the start countdown, the team scores and the clock, drawn
	/// from <see cref="ArenaMatchStateBroadcast"/> while the player is in a match.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Also the client's end of two contracts. It publishes the roster to
	/// <see cref="ArenaTeamRegistry"/> so the client's own targeting agrees with the server about
	/// who is an enemy, and it fires the arena's cues — the template's countdown triggers on the
	/// local character, and <see cref="ArenaClientEvents"/> — from the server-timed state, so a
	/// sound at three seconds plays at the server's three seconds on every client.
	/// </para>
	/// <para>
	/// Hidden when the player is not in a match. Starts closed and shows itself on the first
	/// state; its broadcast is registered when the client is set, not when the tree is built.
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

		private const string ROOT_LIVE_CLASS = "arenahud-root--live";
		private const string COUNTDOWN_HIDDEN_CLASS = "arenahud-countdown--hidden";
		private const string SCORE_CLASS = "arenahud-score";
		private const string SCORE_MINE_CLASS = "arenahud-score--mine";

		protected override UITKPanelLayer Layer => UITKPanelLayer.Hud;

		private Label phaseLabel;
		private Label countdownLabel;
		private Label clockLabel;
		private VisualElement scoresRow;
		private Label hintLabel;
		private VisualElement objectivesBox;

		private ArenaMatchStateBroadcast state;
		private bool hasState;
		private int lastCueSecond = int.MinValue;
		private ArenaMatchPhase lastPhase = ArenaMatchPhase.Cancelled;
		private int[] lastScores;

		/// <summary>Local clock for the seconds between server updates, so the display counts down smoothly.</summary>
		private float secondsRemainingAt;
		private int secondsRemainingBase;

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
		}

		public override void OnClientSet()
		{
			Client.NetworkManager.ClientManager.RegisterBroadcast<ArenaMatchStateBroadcast>(OnClientArenaMatchStateBroadcastReceived);
		}

		public override void OnClientUnset()
		{
			Client.NetworkManager.ClientManager.UnregisterBroadcast<ArenaMatchStateBroadcast>(OnClientArenaMatchStateBroadcastReceived);
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

			PublishRoster();
			FireCues(previous);

			if (msg.Phase == ArenaMatchPhase.Ended || msg.Phase == ArenaMatchPhase.Cancelled)
			{
				// The results screen takes over; the strip has nothing more to say.
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
			ArenaTeamRegistry.Publish(handle, roster, state.Phase == ArenaMatchPhase.Live);
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
			lastPhase = state.Phase;
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
				switch (state.Phase)
				{
					case ArenaMatchPhase.Gathering: phaseLabel.text = $"{arenaName} — waiting for players"; break;
					case ArenaMatchPhase.Countdown: phaseLabel.text = $"{arenaName} — get ready"; break;
					default: phaseLabel.text = arenaName; break;
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
						score.AddToClassList(t == myTeam ? "fish-badge--accent" : "fish-badge--danger");
						score.AddToClassList(SCORE_CLASS);
						if (t == myTeam) score.AddToClassList(SCORE_MINE_CLASS);
						scoresRow.Add(score);
					}
				}
			}

			ApplyObjectives(myTeam);

			if (hintLabel != null)
			{
				int present = 0, total = 0;
				if (state.Members != null)
				{
					foreach (ArenaMemberEntry m in state.Members) { ++total; if (m.Present) ++present; }
				}
				hintLabel.text = state.Phase == ArenaMatchPhase.Gathering
					? $"{present}/{total} players here"
					: string.Empty;
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

				if (objective.Kind == FishMMO.Shared.Core.ArenaObjectiveKind.FlagStand)
				{
					string whose = objective.Team == myTeam ? "Your flag" : $"Team {objective.Team + 1} flag";
					if (objective.Progress > 0)
					{
						bool mine = Character != null && objective.Holder == Character.ID;
						line.text = mine ? $"{whose}: carried by you" : $"{whose}: taken";
						line.AddToClassList(objective.Team == myTeam ? "fish-label--danger" : "fish-label--good");
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
			if (state.Phase == ArenaMatchPhase.Countdown || state.Phase == ArenaMatchPhase.Live)
			{
				ApplyState();
			}
		}

		public override void OnPostUnsetCharacter()
		{
			base.OnPostUnsetCharacter();
			hasState = false;
			state = default;
			lastCueSecond = int.MinValue;
			lastScores = null;
			ArenaTeamRegistry.Clear();
			Hide();
		}
	}
}
