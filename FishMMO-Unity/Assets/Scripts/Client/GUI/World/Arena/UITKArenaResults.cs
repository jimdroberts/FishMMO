using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using FishNet.Transporting;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.Client
{
	/// <summary>
	/// UI Toolkit arena results screen: who won, the team scores, a pedestal for the three highest
	/// scorers, and every player's line, from <see cref="ArenaResultsBroadcast"/>.
	/// </summary>
	/// <remarks>
	/// The pedestal and the list are ordered by score alone — team and group are not considered —
	/// because the screen is about who did the most, not who won; the banner says who won. Names
	/// arrive through the naming system, so lines show "…" until each name resolves. The screen
	/// closes itself when the server returns everyone to the world, and can be closed early.
	/// </remarks>
	public class UITKArenaResults : UITKCharacterControl
	{
		private const string BANNER_NAME = "results-banner";
		private const string SUBTITLE_NAME = "results-subtitle";
		private const string SCORES_NAME = "results-scores";
		private const string PEDESTAL_NAME = "results-pedestal";
		private const string LIST_NAME = "results-list";
		private const string RANK_NAME = "results-rank";
		private const string RETURN_NAME = "results-return";
		private const string CLOSE_BUTTON_NAME = "results-close-btn";

		private const string BANNER_WIN_CLASS = "results-banner--win";
		private const string BANNER_LOSS_CLASS = "results-banner--loss";

		protected override UITKPanelLayer Layer => UITKPanelLayer.Popup;

		private Label bannerLabel;
		private Label subtitleLabel;
		private VisualElement scoresRow;
		private VisualElement pedestal;
		private VisualElement list;
		private Label rankLabel;
		private Label returnLabel;

		private ArenaResultsBroadcast results;
		private bool hasResults;
		private float returnAt;

		/// <summary>Name labels awaiting the naming system, by character id.</summary>
		private readonly Dictionary<long, List<Label>> nameLabels = new Dictionary<long, List<Label>>();

		public override void OnStarting()
		{
			VisualElement root = Root;
			if (root == null)
			{
				return;
			}
			bannerLabel = root.Q<Label>(BANNER_NAME);
			subtitleLabel = root.Q<Label>(SUBTITLE_NAME);
			scoresRow = root.Q(SCORES_NAME);
			pedestal = root.Q(PEDESTAL_NAME);
			list = root.Q(LIST_NAME);
			rankLabel = root.Q<Label>(RANK_NAME);
			returnLabel = root.Q<Label>(RETURN_NAME);

			Button close = root.Q<Button>(CLOSE_BUTTON_NAME);
			if (close != null)
			{
				close.clicked += Hide;
			}
		}

		public override void OnClientSet()
		{
			Client.NetworkManager.ClientManager.RegisterBroadcast<ArenaResultsBroadcast>(OnClientArenaResultsBroadcastReceived);
		}

		public override void OnClientUnset()
		{
			Client.NetworkManager.ClientManager.UnregisterBroadcast<ArenaResultsBroadcast>(OnClientArenaResultsBroadcastReceived);
		}

		private void OnClientArenaResultsBroadcastReceived(ArenaResultsBroadcast msg, Channel channel)
		{
			if (Character == null)
			{
				return;
			}

			results = msg;
			hasResults = true;
			returnAt = Time.unscaledTime + Mathf.Max(1, msg.SecondsUntilReturn);

			ArenaClientEvents.RaiseMatchEnded(msg);

			ArenaTemplate template = msg.ArenaTemplateID != 0 ? ArenaTemplate.Get<ArenaTemplate>(msg.ArenaTemplateID) : null;
			if (template?.MatchEndTriggers != null && template.MatchEndTriggers.Count > 0)
			{
				Character.Invoke(template.MatchEndTriggers, new ArenaEventData(Character, ArenaCuePhase.Ended, 0, msg.YourTeam, msg.WinnerTeam));
			}

			if (!Visible)
			{
				Show();
				return;
			}
			ApplyResults();
		}

		protected override void OnAfterShow()
		{
			ApplyResults();
		}

		protected override void OnAfterStarting()
		{
			base.OnAfterStarting();
			ApplyResults();
		}

		private void ApplyResults()
		{
			if (!hasResults)
			{
				return;
			}

			nameLabels.Clear();
			ArenaTemplate template = results.ArenaTemplateID != 0 ? ArenaTemplate.Get<ArenaTemplate>(results.ArenaTemplateID) : null;

			if (bannerLabel != null)
			{
				bannerLabel.RemoveFromClassList(BANNER_WIN_CLASS);
				bannerLabel.RemoveFromClassList(BANNER_LOSS_CLASS);
				if (results.WinnerTeam < 0)
				{
					bannerLabel.text = "DRAW";
				}
				else if (results.WinnerTeam == results.YourTeam)
				{
					bannerLabel.text = "VICTORY";
					bannerLabel.AddToClassList(BANNER_WIN_CLASS);
				}
				else
				{
					bannerLabel.text = "DEFEAT";
					bannerLabel.AddToClassList(BANNER_LOSS_CLASS);
				}
			}

			if (subtitleLabel != null)
			{
				subtitleLabel.text = template != null
					? $"{template.ResolvedDisplayName} · {template.GetFormatName(results.Format)} · {ArenaTemplate.DescribeMode(template.Mode)}"
					: "Arena";
			}

			if (scoresRow != null)
			{
				scoresRow.Clear();
				if (results.TeamScores != null)
				{
					for (int t = 0; t < results.TeamScores.Length; ++t)
					{
						Label score = new Label($"Team {t + 1}  {results.TeamScores[t]}");
						score.AddToClassList("fish-badge");
						score.AddToClassList(t == results.WinnerTeam ? "fish-badge--good" : (t == results.YourTeam ? "fish-badge--accent" : "fish-badge--danger"));
						score.AddToClassList("results-score");
						scoresRow.Add(score);
					}
				}
			}

			BuildPedestal();
			BuildList();

			if (rankLabel != null)
			{
				rankLabel.text = results.RankDelta == 0
					? "PvP rank unchanged"
					: (results.RankDelta > 0 ? $"PvP rank +{results.RankDelta}" : $"PvP rank {results.RankDelta}");
			}

			ApplyReturn();
		}

		/// <summary>Three pedestals: second, first, third, as podiums are drawn.</summary>
		private void BuildPedestal()
		{
			if (pedestal == null)
			{
				return;
			}
			pedestal.Clear();

			ArenaMemberEntry[] placements = results.Placements ?? new ArenaMemberEntry[0];
			int[] order = { 1, 0, 2 };
			foreach (int index in order)
			{
				if (index >= placements.Length)
				{
					continue;
				}

				ArenaMemberEntry entry = placements[index];
				VisualElement column = new VisualElement();
				column.AddToClassList("results-podium");
				column.AddToClassList($"results-podium--{index + 1}");

				Label name = new Label("…");
				name.AddToClassList("fish-label");
				name.AddToClassList("results-podium__name");
				if (entry.CharacterID == Character?.ID) name.AddToClassList("fish-label--accent");
				column.Add(name);
				RequestName(entry.CharacterID, name);

				Label score = new Label(entry.Score.ToString());
				score.AddToClassList("fish-label");
				score.AddToClassList("fish-label--numeric");
				score.AddToClassList("results-podium__score");
				column.Add(score);

				VisualElement block = new VisualElement();
				block.AddToClassList("fish-well");
				block.AddToClassList("results-podium__block");
				Label place = new Label(Ordinal(index + 1));
				place.AddToClassList("fish-label--title");
				block.Add(place);
				column.Add(block);

				pedestal.Add(column);
			}
		}

		private void BuildList()
		{
			if (list == null)
			{
				return;
			}
			list.Clear();

			ArenaMemberEntry[] placements = results.Placements ?? new ArenaMemberEntry[0];
			for (int i = 0; i < placements.Length; ++i)
			{
				ArenaMemberEntry entry = placements[i];
				VisualElement row = new VisualElement();
				row.AddToClassList("fish-row");
				row.AddToClassList("results-row");
				if (entry.CharacterID == Character?.ID) row.AddToClassList("fish-row--selected");

				Label place = new Label((i + 1).ToString());
				place.AddToClassList("fish-row__meta");
				place.AddToClassList("results-row__place");
				row.Add(place);

				Label name = new Label("…");
				name.AddToClassList("fish-row__name");
				name.AddToClassList("results-row__name");
				row.Add(name);
				RequestName(entry.CharacterID, name);

				Label team = new Label($"T{entry.Team + 1}");
				team.AddToClassList("fish-row__meta");
				team.AddToClassList("results-row__team");
				row.Add(team);

				Label kd = new Label($"{entry.Kills} / {entry.Deaths}");
				kd.AddToClassList("fish-row__value");
				kd.AddToClassList("results-row__kd");
				row.Add(kd);

				Label score = new Label(entry.Score.ToString());
				score.AddToClassList("fish-row__value");
				score.AddToClassList("results-row__score");
				row.Add(score);

				list.Add(row);
			}
		}

		private void RequestName(long characterID, Label label)
		{
			if (!nameLabels.TryGetValue(characterID, out List<Label> labels))
			{
				labels = new List<Label>(2);
				nameLabels[characterID] = labels;
			}
			labels.Add(label);

			ClientNamingSystem.SetName(NamingSystemType.CharacterName, characterID, name =>
			{
				if (nameLabels.TryGetValue(characterID, out List<Label> targets))
				{
					foreach (Label target in targets)
					{
						target.text = string.IsNullOrWhiteSpace(name) ? "Unknown" : name;
					}
				}
			});
		}

		private static string Ordinal(int place)
		{
			switch (place)
			{
				case 1: return "1st";
				case 2: return "2nd";
				case 3: return "3rd";
				default: return place + "th";
			}
		}

		private void ApplyReturn()
		{
			if (returnLabel == null)
			{
				return;
			}
			int seconds = Mathf.Max(0, Mathf.CeilToInt(returnAt - Time.unscaledTime));
			returnLabel.text = $"Returning to the world in {seconds}s";
		}

		protected override void OnTick()
		{
			if (!Visible || !hasResults)
			{
				return;
			}
			ApplyReturn();
			if (Time.unscaledTime >= returnAt + 2.0f)
			{
				// The server has taken everyone home; if this screen is still up, it is stale.
				Hide();
			}
		}

		public override void OnPostUnsetCharacter()
		{
			base.OnPostUnsetCharacter();
			hasResults = false;
			nameLabels.Clear();
			Hide();
		}
	}
}
