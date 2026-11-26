using System.Collections.Generic;
using FishNet.Transporting;
using UnityEngine;
using UnityEngine.UIElements;
using FishMMO.Logging;
using FishMMO.Shared;

namespace FishMMO.Client
{
	/// <summary>
	/// The duel HUD (issue #250): sends a challenge, asks the challenged player, and through the duel shows the opponent,
	/// the countdown, the clock, a warning near the ground's edge, a Yield button and the result. Binds to
	/// <c>UIDuel.uxml</c> / <c>UIDuel.uss</c>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The single owner of the duel's messages on the client. It publishes the duel to <see cref="DuelRegistry"/> as the
	/// server does, so this client's predicted hits, its targeting and the finishing-blow clamp agree with the server
	/// about who may strike whom and when; and it draws the ground (<see cref="DuelGroundVisual"/>).
	/// </para>
	/// <para>
	/// The server teleports both duellists; a character faces where its camera looks, so the start turns this player's
	/// camera to the facing the server set (<see cref="DuelStartBroadcast.StartYaw"/>), towards the opponent.
	/// </para>
	/// </remarks>
	public class UITKDuel : UITKCharacterControl
	{
		/// <summary>The control's registered name.</summary>
		public const string ControlName = "UIDuel";

		private const string DIALOG_NAME = "UIDialogBox";
		private const string TOAST_NAME = "UIToast";

		private const string CSS_HIDDEN = "duel-hidden";
		private const string CSS_WON = "duel-banner--won";
		private const string CSS_LOST = "duel-banner--lost";
		private const string CSS_WARNING = "duel-hint--warning";

		/// <summary>Seconds the result stays on screen.</summary>
		private const float BannerSeconds = 5f;

		/// <summary>Seconds "Fight!" stays up after the countdown.</summary>
		private const float FightSeconds = 1.25f;

		/// <summary>Seconds a first press of Yield stays armed.</summary>
		private const float YieldArmSeconds = 3f;

		private enum Phase : byte { None, Countdown, Live, Ended }

		protected override UITKPanelLayer Layer => UITKPanelLayer.Hud;

		private VisualElement strip;
		private Label titleLabel;
		private Label clockLabel;
		private Label hintLabel;
		private Button yieldButton;
		private Label countdownLabel;
		private Label bannerLabel;

		private Phase phase;
		private long duelID;
		private long opponentID;
		private string opponentName = string.Empty;
		private DuelArena arena;
		private float countdownEndsAt;
		private float timeLimitSeconds;
		private float liveEndsAt;
		private float fightUntil;
		private float bannerUntil;
		private float yieldArmedUntil;
		private GameObject groundVisual;

		private long pendingChallengerID;
		private float pendingExpiresAt;

		/// <summary>Names of players this client challenged, for the wording of what became of each challenge.</summary>
		private readonly Dictionary<long, string> challengedNames = new Dictionary<long, string>();

		/// <summary>Whether this player is in a duel (counting down or fighting).</summary>
		public bool InDuel => phase == Phase.Countdown || phase == Phase.Live;

		// ── Lifecycle ─────────────────────────────────────────────────────────

		public override void OnStarting()
		{
			VisualElement root = Root;
			if (root == null)
			{
				return;
			}
			strip = root.Q("duel-root");
			titleLabel = root.Q<Label>("duel-title");
			clockLabel = root.Q<Label>("duel-clock");
			hintLabel = root.Q<Label>("duel-hint");
			yieldButton = root.Q<Button>("duel-yield");
			countdownLabel = root.Q<Label>("duel-countdown");
			bannerLabel = root.Q<Label>("duel-banner");
			if (yieldButton != null)
			{
				yieldButton.clicked += OnClick_Yield;
			}
		}

		protected override void OnAfterStarting()
		{
			base.OnAfterStarting();
			Render();
		}

		/// <summary>A record that came while no character was set; handed to the sheet when one is.</summary>
		private DuelRecordBroadcast? unclaimedRecord;

		public override void OnClientSet()
		{
			Client.NetworkManager.ClientManager.RegisterBroadcast<DuelInviteBroadcast>(OnInvite);
			Client.NetworkManager.ClientManager.RegisterBroadcast<DuelRequestResultBroadcast>(OnRequestResult);
			Client.NetworkManager.ClientManager.RegisterBroadcast<DuelStartBroadcast>(OnStart);
			Client.NetworkManager.ClientManager.RegisterBroadcast<DuelLiveBroadcast>(OnLive);
			Client.NetworkManager.ClientManager.RegisterBroadcast<DuelEndBroadcast>(OnEnd);
			Client.NetworkManager.ClientManager.RegisterBroadcast<DuelRecordBroadcast>(OnRecord);
		}

		public override void OnClientUnset()
		{
			Client.NetworkManager.ClientManager.UnregisterBroadcast<DuelInviteBroadcast>(OnInvite);
			Client.NetworkManager.ClientManager.UnregisterBroadcast<DuelRequestResultBroadcast>(OnRequestResult);
			Client.NetworkManager.ClientManager.UnregisterBroadcast<DuelStartBroadcast>(OnStart);
			Client.NetworkManager.ClientManager.UnregisterBroadcast<DuelLiveBroadcast>(OnLive);
			Client.NetworkManager.ClientManager.UnregisterBroadcast<DuelEndBroadcast>(OnEnd);
			Client.NetworkManager.ClientManager.UnregisterBroadcast<DuelRecordBroadcast>(OnRecord);
		}

		public override void OnPostSetCharacter()
		{
			base.OnPostSetCharacter();
			if (unclaimedRecord.HasValue && Character != null)
			{
				DuelClient.SetRecord(Character.ID, unclaimedRecord.Value);
			}
			unclaimedRecord = null;
		}

		public override void OnPostUnsetCharacter()
		{
			base.OnPostUnsetCharacter();
			ResetDuel();
			DuelClient.ClearRecord();
			unclaimedRecord = null;
			pendingChallengerID = 0;
			challengedNames.Clear();
			DuelRegistry.Clear();
			Hide();
		}

		public override void OnDestroying()
		{
			DestroyGround();
			base.OnDestroying();
		}

		// ── Challenging ───────────────────────────────────────────────────────

		/// <summary>Challenges a player (the context menu's entry). The server applies every rule and answers.</summary>
		public void Challenge(long targetCharacterID, string targetName)
		{
			if (Character == null || targetCharacterID <= 0)
			{
				return;
			}
			if (InDuel)
			{
				Toast(DuelRules.DescribeRequest(DuelRequestFailure.SelfBusy, targetName), ToastSeverity.Warning);
				return;
			}
			challengedNames[targetCharacterID] = targetName ?? string.Empty;
			Client.Broadcast(new DuelRequestBroadcast { TargetCharacterID = targetCharacterID }, Channel.Reliable);
		}

		private void OnRequestResult(DuelRequestResultBroadcast msg, Channel channel)
		{
			challengedNames.TryGetValue(msg.TargetCharacterID, out string name);
			if (msg.Failure != DuelRequestFailure.Sent)
			{
				challengedNames.Remove(msg.TargetCharacterID);
			}
			Toast(DuelRules.DescribeRequest(msg.Failure, name), msg.Failure == DuelRequestFailure.Sent ? ToastSeverity.Info : ToastSeverity.Warning);
		}

		private void OnInvite(DuelInviteBroadcast msg, Channel channel)
		{
			long challengerID = msg.ChallengerCharacterID;
			pendingChallengerID = challengerID;
			pendingExpiresAt = Time.unscaledTime + Mathf.Max(1f, msg.ExpiresInSeconds);
			string name = string.IsNullOrEmpty(msg.ChallengerName) ? "Someone" : msg.ChallengerName;

			// Declined, never dropped: the challenger is owed an answer.
			if (Character == null || InDuel)
			{
				AnswerInvite(challengerID, false);
				return;
			}

			string text = $"{name} challenges you to a duel.";
			Toast(text, ToastSeverity.Info);
			if (!UIManager.TryGetTK(DIALOG_NAME, out UITKDialogBox dialog) ||
				!dialog.Open(text, () => AnswerInvite(challengerID, true), () => AnswerInvite(challengerID, false), "Fight", "Decline"))
			{
				AnswerInvite(challengerID, false);
			}
		}

		/// <summary>Answers the challenge the prompt was opened for, and only while it is still open.</summary>
		private void AnswerInvite(long challengerID, bool accept)
		{
			if (pendingChallengerID != challengerID)
			{
				return;
			}
			pendingChallengerID = 0;
			if (accept && Time.unscaledTime > pendingExpiresAt)
			{
				Toast("That challenge has lapsed.", ToastSeverity.Warning);
				return;
			}
			Client.Broadcast(new DuelInviteResponseBroadcast { ChallengerCharacterID = challengerID, Accept = accept }, Channel.Reliable);
		}

		// ── The duel ──────────────────────────────────────────────────────────

		private void OnStart(DuelStartBroadcast msg, Channel channel)
		{
			if (Character == null)
			{
				return;
			}
			ResetDuel();
			pendingChallengerID = 0;
			challengedNames.Remove(msg.OpponentCharacterID);

			phase = Phase.Countdown;
			duelID = msg.DuelID;
			opponentID = msg.OpponentCharacterID;
			opponentName = string.IsNullOrEmpty(msg.OpponentName) ? "your opponent" : msg.OpponentName;
			arena = msg.Arena;
			countdownEndsAt = Time.unscaledTime + Mathf.Max(0f, msg.CountdownSeconds);
			timeLimitSeconds = Mathf.Max(0f, msg.TimeLimitSeconds);

			DuelRegistry.Publish(duelID, Character.ID, opponentID, false);
			groundVisual = DuelGroundVisual.Create(arena);
			FaceStart(msg.StartYaw);
			Show();
			Render();
		}

		private void OnLive(DuelLiveBroadcast msg, Channel channel)
		{
			if (phase != Phase.Countdown || msg.DuelID != duelID)
			{
				return;
			}
			phase = Phase.Live;
			DuelRegistry.SetLive(duelID, true);
			float now = Time.unscaledTime;
			fightUntil = now + FightSeconds;
			liveEndsAt = timeLimitSeconds > 0f ? now + timeLimitSeconds : 0f;
			Render();
		}

		private void OnEnd(DuelEndBroadcast msg, Channel channel)
		{
			if (msg.DuelID != duelID || !InDuel)
			{
				return;
			}
			string text = DuelRules.DescribeEnd(msg.Outcome, msg.Reason, opponentName);
			DuelOutcome outcome = msg.Outcome;
			ResetDuel();
			phase = Phase.Ended;
			bannerUntil = Time.unscaledTime + BannerSeconds;
			if (bannerLabel != null)
			{
				bannerLabel.text = outcome == DuelOutcome.Won ? "Victory" : outcome == DuelOutcome.Lost ? "Defeat" : outcome == DuelOutcome.Draw ? "Draw" : "Called off";
				bannerLabel.EnableInClassList(CSS_WON, outcome == DuelOutcome.Won);
				bannerLabel.EnableInClassList(CSS_LOST, outcome == DuelOutcome.Lost);
			}
			Toast(text, outcome == DuelOutcome.Lost ? ToastSeverity.Warning : ToastSeverity.Info);
			Show();
			Render();
		}

		private void OnRecord(DuelRecordBroadcast msg, Channel channel)
		{
			if (Character != null)
			{
				DuelClient.SetRecord(Character.ID, msg);
			}
			else
			{
				// Arrived ahead of the local character (the arrival read can beat the spawn): kept until it is set.
				unclaimedRecord = msg;
			}
			// DuelID 0: the record sent on arrival, for the character sheet; nothing happened to announce.
			if (msg.DuelID == 0)
			{
				return;
			}
			string change = msg.RatingChange > 0 ? $"+{msg.RatingChange}" : msg.RatingChange.ToString();
			Toast($"Duel rating {msg.Rating} ({change}). Record: {msg.Wins} won, {msg.Losses} lost, {msg.Draws} drawn.", ToastSeverity.Info);
		}

		private void OnClick_Yield()
		{
			if (!InDuel)
			{
				return;
			}
			float now = Time.unscaledTime;
			// Two presses: one stray click must not throw a duel away.
			if (now > yieldArmedUntil)
			{
				yieldArmedUntil = now + YieldArmSeconds;
				Render();
				return;
			}
			yieldArmedUntil = 0f;
			Client.Broadcast(new DuelYieldBroadcast(), Channel.Reliable);
		}

		/// <summary>Forgets the duel on this client: the registry's pair, the ground's visual, the clocks.</summary>
		private void ResetDuel()
		{
			if (duelID != 0)
			{
				DuelRegistry.Remove(duelID);
			}
			DestroyGround();
			phase = Phase.None;
			duelID = 0;
			opponentID = 0;
			arena = default;
			fightUntil = 0f;
			liveEndsAt = 0f;
			yieldArmedUntil = 0f;
		}

		private void DestroyGround()
		{
			if (groundVisual != null)
			{
				Destroy(groundVisual);
				groundVisual = null;
			}
		}

		/// <summary>Turns this player's camera to the facing the server set, so the next input keeps it.</summary>
		private void FaceStart(float yaw)
		{
			KCCCamera camera = Character?.KCCPlayer != null ? Character.KCCPlayer.CharacterCamera : null;
			if (camera == null)
			{
				return;
			}
			Vector3 up = Character.Motor != null ? Character.Motor.CharacterUp : Vector3.up;
			Vector3 planar = Vector3.ProjectOnPlane(Quaternion.Euler(0f, yaw, 0f) * Vector3.forward, up);
			if (planar.sqrMagnitude > 1e-6f)
			{
				camera.PlanarDirection = planar.normalized;
			}
		}

		// ── Drawing ───────────────────────────────────────────────────────────

		protected override void OnTick()
		{
			if (pendingChallengerID != 0 && Time.unscaledTime > pendingExpiresAt)
			{
				pendingChallengerID = 0;
			}
			if (phase == Phase.None)
			{
				return;
			}
			if (phase == Phase.Ended && Time.unscaledTime > bannerUntil)
			{
				phase = Phase.None;
				Hide();
				return;
			}
			Render();
		}

		private void Render()
		{
			bool inDuel = InDuel;
			float now = Time.unscaledTime;
			strip?.EnableInClassList(CSS_HIDDEN, !inDuel);
			bannerLabel?.EnableInClassList(CSS_HIDDEN, phase != Phase.Ended);

			if (titleLabel != null)
			{
				titleLabel.text = inDuel ? $"Duel: {opponentName}" : string.Empty;
			}

			if (clockLabel != null)
			{
				if (phase == Phase.Countdown)
				{
					clockLabel.text = "Get ready";
				}
				else if (phase == Phase.Live && liveEndsAt > 0f)
				{
					int left = Mathf.Max(0, Mathf.CeilToInt(liveEndsAt - now));
					clockLabel.text = $"{left / 60}:{left % 60:00}";
				}
				else
				{
					clockLabel.text = string.Empty;
				}
			}

			if (countdownLabel != null)
			{
				string count = string.Empty;
				if (phase == Phase.Countdown)
				{
					count = Mathf.Max(1, Mathf.CeilToInt(countdownEndsAt - now)).ToString();
				}
				else if (phase == Phase.Live && now < fightUntil)
				{
					count = "Fight!";
				}
				countdownLabel.text = count;
				countdownLabel.EnableInClassList(CSS_HIDDEN, count.Length == 0);
			}

			if (hintLabel != null)
			{
				bool nearEdge = inDuel && Character?.Transform != null &&
					DuelRules.DistanceToEdge(arena, Character.Transform.position) < DuelRules.EdgeWarningDistance;
				hintLabel.text = nearEdge ? "Leaving the duel ground forfeits." : inDuel ? "Stay inside the golden ward." : string.Empty;
				hintLabel.EnableInClassList(CSS_WARNING, nearEdge);
			}

			if (yieldButton != null)
			{
				yieldButton.text = now <= yieldArmedUntil ? "Yield?" : "Yield";
				yieldButton.SetEnabled(inDuel);
			}
		}

		private void Toast(string text, ToastSeverity severity)
		{
			if (UIManager.TryGetTK(TOAST_NAME, out UITKToast toast))
			{
				toast.Show(text, severity);
			}
			else
			{
				Log.Debug("UITKDuel", text);
			}
		}
	}
}
