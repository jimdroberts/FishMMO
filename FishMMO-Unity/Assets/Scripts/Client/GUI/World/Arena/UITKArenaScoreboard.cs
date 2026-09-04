using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.Client
{
	/// <summary>
	/// UI Toolkit arena scoreboard: every team in the current match with its colour, its score and
	/// its players' kills, deaths and score, toggled by a hotkey during play.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Draws from <see cref="ArenaClientEvents.Current"/>, the last match state the HUD received,
	/// and redraws on every <see cref="ArenaClientEvents.OnMatchState"/>. It registers no network
	/// message of its own: one state stream, one owner, and every panel reads from it — so the
	/// scoreboard, the HUD and the team registry can never disagree.
	/// </para>
	/// <para>
	/// Opened outside a match it says so rather than doing nothing, because a key that appears to
	/// do nothing reads as broken. It does not release the cursor: it is glanced at mid-fight.
	/// </para>
	/// </remarks>
	public class UITKArenaScoreboard : UITKCharacterControl
	{
		private const string SUBTITLE_NAME = "scoreboard-subtitle";
		private const string CLOCK_NAME = "scoreboard-clock";
		private const string TEAMS_NAME = "scoreboard-teams";
		private const string EMPTY_NAME = "scoreboard-empty";
		private const string CLOSE_BUTTON_NAME = "scoreboard-close-btn";

		protected override UITKPanelLayer Layer => UITKPanelLayer.Window;

		private Label subtitleLabel;
		private Label clockLabel;
		private VisualElement teamsBox;
		private Label emptyLabel;

		/// <summary>Name labels awaiting the naming system, by character id.</summary>
		private readonly Dictionary<long, List<Label>> nameLabels = new Dictionary<long, List<Label>>();

		/// <summary>Local clock for the seconds between server updates.</summary>
		private float secondsRemainingAt;
		private int secondsRemainingBase;
		private ArenaMatchPhase clockPhase;

		public override void OnStarting()
		{
			VisualElement root = Root;
			if (root == null)
			{
				return;
			}

			subtitleLabel = root.Q<Label>(SUBTITLE_NAME);
			clockLabel = root.Q<Label>(CLOCK_NAME);
			teamsBox = root.Q(TEAMS_NAME);
			emptyLabel = root.Q<Label>(EMPTY_NAME);

			Button close = root.Q<Button>(CLOSE_BUTTON_NAME);
			if (close != null)
			{
				close.clicked += Hide;
			}
		}

		public override void OnClientSet()
		{
			/* Static event, and OnClientSet can run again on reconnect: removed before added so a
			 * second subscription never draws the board twice per update. */
			ArenaClientEvents.OnMatchState -= OnMatchState;
			ArenaClientEvents.OnMatchState += OnMatchState;
		}

		public override void OnClientUnset()
		{
			ArenaClientEvents.OnMatchState -= OnMatchState;
		}

		public override void OnDestroying()
		{
			base.OnDestroying();
			ArenaClientEvents.OnMatchState -= OnMatchState;
		}

		private void OnMatchState(ArenaMatchStateBroadcast state)
		{
			secondsRemainingBase = state.SecondsRemaining;
			secondsRemainingAt = Time.unscaledTime;
			clockPhase = state.Phase;
			if (Visible)
			{
				ApplyBoard();
			}
		}

		protected override void OnAfterShow()
		{
			ArenaMatchStateBroadcast? current = ArenaClientEvents.Current;
			if (current.HasValue)
			{
				secondsRemainingBase = current.Value.SecondsRemaining;
				secondsRemainingAt = Time.unscaledTime;
				clockPhase = current.Value.Phase;
			}
			ApplyBoard();
		}

		protected override void OnAfterStarting()
		{
			base.OnAfterStarting();
			ApplyBoard();
		}

		private void ApplyBoard()
		{
			nameLabels.Clear();
			ArenaMatchStateBroadcast? current = ArenaClientEvents.Current;

			if (teamsBox != null)
			{
				teamsBox.Clear();
			}

			if (!current.HasValue || Character == null)
			{
				if (subtitleLabel != null) subtitleLabel.text = "No match";
				if (clockLabel != null) clockLabel.text = string.Empty;
				if (emptyLabel != null)
				{
					emptyLabel.text = "You are not in an arena match.";
					emptyLabel.style.display = DisplayStyle.Flex;
				}
				return;
			}

			if (emptyLabel != null)
			{
				emptyLabel.style.display = DisplayStyle.None;
			}

			ArenaMatchStateBroadcast state = current.Value;
			ArenaTemplate template = state.ArenaTemplateID != 0 ? ArenaTemplate.Get<ArenaTemplate>(state.ArenaTemplateID) : null;

			if (subtitleLabel != null)
			{
				string phase = state.Phase switch
				{
					ArenaMatchPhase.Gathering => "Waiting for players",
					ArenaMatchPhase.Countdown => "Starting",
					ArenaMatchPhase.Live => "Live",
					_ => state.Phase.ToString(),
				};
				subtitleLabel.text = template != null
					? $"{template.ResolvedDisplayName} · {template.GetFormatName(state.Format)} · {ArenaTemplate.DescribeMode(template.Mode)} · {phase}"
					: $"Arena · {phase}";
			}

			ApplyClock();

			if (teamsBox == null || state.TeamScores == null)
			{
				return;
			}

			int myTeam = -1;
			var membersByTeam = new Dictionary<int, List<ArenaMemberEntry>>();
			if (state.Members != null)
			{
				foreach (ArenaMemberEntry member in state.Members)
				{
					if (member.CharacterID == Character.ID)
					{
						myTeam = member.Team;
					}
					if (!membersByTeam.TryGetValue(member.Team, out List<ArenaMemberEntry> list))
					{
						list = new List<ArenaMemberEntry>();
						membersByTeam[member.Team] = list;
					}
					list.Add(member);
				}
			}

			for (int t = 0; t < state.TeamScores.Length; ++t)
			{
				Color color = template != null ? template.GetTeamColor(t) : ArenaTeamColors.Default(t);
				teamsBox.Add(BuildTeam(t, state.TeamScores[t], membersByTeam.TryGetValue(t, out List<ArenaMemberEntry> members) ? members : null, color, t == myTeam));
			}
		}

		/// <summary>One team block: a coloured header with the score, then one row per player, highest score first.</summary>
		private VisualElement BuildTeam(int team, int score, List<ArenaMemberEntry> members, Color color, bool mine)
		{
			VisualElement block = new VisualElement();
			block.AddToClassList("fish-well");
			block.AddToClassList("scoreboard-team");
			block.style.borderLeftColor = color;

			VisualElement header = new VisualElement();
			header.AddToClassList("scoreboard-team__header");

			Label title = new Label(mine ? $"Team {team + 1}  (you)" : $"Team {team + 1}");
			title.AddToClassList("fish-label");
			title.AddToClassList("fish-label--title");
			title.AddToClassList("scoreboard-team__title");
			ArenaTeamStyle.ApplyText(title, color);
			header.Add(title);

			Label scoreLabel = new Label(score.ToString());
			scoreLabel.AddToClassList("fish-badge");
			scoreLabel.AddToClassList("scoreboard-team__score");
			ArenaTeamStyle.Apply(scoreLabel, color);
			header.Add(scoreLabel);

			block.Add(header);

			VisualElement columns = new VisualElement();
			columns.AddToClassList("scoreboard-columns");
			columns.Add(Column("PLAYER", "scoreboard-col--name"));
			columns.Add(Column("K", "scoreboard-col--num"));
			columns.Add(Column("D", "scoreboard-col--num"));
			columns.Add(Column("SCORE", "scoreboard-col--score"));
			block.Add(columns);

			if (members == null || members.Count == 0)
			{
				Label none = new Label("No players");
				none.AddToClassList("fish-hint");
				block.Add(none);
				return block;
			}

			members.Sort((a, b) =>
			{
				int c = b.Score.CompareTo(a.Score);
				if (c != 0) return c;
				c = b.Kills.CompareTo(a.Kills);
				if (c != 0) return c;
				c = a.Deaths.CompareTo(b.Deaths);
				return c != 0 ? c : a.CharacterID.CompareTo(b.CharacterID);
			});

			foreach (ArenaMemberEntry member in members)
			{
				VisualElement row = new VisualElement();
				row.AddToClassList("fish-row");
				row.AddToClassList("scoreboard-row");
				if (member.CharacterID == Character?.ID) row.AddToClassList("fish-row--selected");
				if (!member.Present) row.AddToClassList("fish-row--dim");

				Label name = new Label(member.Present ? "…" : "… (away)");
				name.AddToClassList("fish-row__name");
				name.AddToClassList("scoreboard-col--name");
				row.Add(name);
				RequestName(member.CharacterID, name, member.Present ? string.Empty : " (away)");

				row.Add(Value(member.Kills.ToString(), "scoreboard-col--num"));
				row.Add(Value(member.Deaths.ToString(), "scoreboard-col--num"));
				row.Add(Value(member.Score.ToString(), "scoreboard-col--score"));

				block.Add(row);
			}

			return block;
		}

		private static Label Column(string text, string layoutClass)
		{
			Label label = new Label(text);
			label.AddToClassList("fish-col-head");
			label.AddToClassList(layoutClass);
			return label;
		}

		private static Label Value(string text, string layoutClass)
		{
			Label label = new Label(text);
			label.AddToClassList("fish-row__value");
			label.AddToClassList(layoutClass);
			return label;
		}

		private void RequestName(long characterID, Label label, string suffix)
		{
			if (!nameLabels.TryGetValue(characterID, out List<Label> labels))
			{
				labels = new List<Label>(1);
				nameLabels[characterID] = labels;
			}
			labels.Add(label);

			ClientNamingSystem.SetName(NamingSystemType.CharacterName, characterID, name =>
			{
				if (nameLabels.TryGetValue(characterID, out List<Label> targets))
				{
					foreach (Label target in targets)
					{
						target.text = (string.IsNullOrWhiteSpace(name) ? "Unknown" : name) + suffix;
					}
				}
			});
		}

		private void ApplyClock()
		{
			if (clockLabel == null)
			{
				return;
			}

			int remaining = Mathf.CeilToInt(secondsRemainingBase - (Time.unscaledTime - secondsRemainingAt));
			switch (clockPhase)
			{
				case ArenaMatchPhase.Countdown:
					clockLabel.text = $"Starts in {Mathf.Max(0, remaining)}";
					break;
				case ArenaMatchPhase.Live:
					clockLabel.text = remaining > 0 ? $"{remaining / 60}:{remaining % 60:00}" : string.Empty;
					break;
				default:
					clockLabel.text = string.Empty;
					break;
			}
		}

		protected override void OnTick()
		{
			if (Visible)
			{
				ApplyClock();
			}
		}

		public override void OnPostUnsetCharacter()
		{
			base.OnPostUnsetCharacter();
			nameLabels.Clear();
			Hide();
		}
	}
}
