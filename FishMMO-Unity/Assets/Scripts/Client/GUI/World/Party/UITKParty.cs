using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using FishNet.Transporting;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.Client
{
	/// <summary>
	/// UI Toolkit party panel. Renders party members as horizontal bands — identity, stacked
	/// health/mana/stamina bars, per-encounter damage and healing meters, and the member's buffs
	/// and debuffs — and handles create/leave/invite plus a per-member context menu
	/// (message / add friend / promote / kick). The shared dialog and context-menu overlays are
	/// resolved by name through the <see cref="UIManager"/> rather than referenced directly.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The roster is split into a MODEL (<see cref="roster"/>, plain data, owned by the character)
	/// and a VIEW (<see cref="rows"/>, elements owned by one visual tree). <c>UIDocument</c>
	/// re-clones the UXML on every enable, so the view is destroyed on each hide/show. Keeping
	/// only the view — as this panel used to — meant the party vanished permanently after the
	/// first close, because the data that would have redrawn it was inside the discarded elements.
	/// See <see cref="OnAfterStarting"/>.
	/// </para>
	/// <para>
	/// <b>Two sources feed the model, and they are not equal.</b> The roster broadcast
	/// (<c>PartyAddBroadcast</c>) is rebuilt from the party database rows, which are written on
	/// connect and on disconnect and at no other time — it is authoritative about WHO is in the
	/// party and what rank they hold, and its health figure is whatever the member logged in with.
	/// The vitals broadcast is pushed from the scene server's in-memory controllers once a second
	/// and is authoritative about everything that moves. Each field is taken from exactly one of
	/// them; see <see cref="OnPartyAddMember"/> for why the roster's health is only ever a seed.
	/// </para>
	/// <para>
	/// <b>Absence from the vitals payload is a fact, not a gap.</b> The server sends every party
	/// member who shares the recipient's scene, so a roster member missing from the latest payload
	/// is in another zone, another dungeon instance, on another scene server, or offline. Those
	/// rows are drawn as a greyscale facade holding their last known values rather than blanked or
	/// hidden — see <see cref="ApplyPresence"/>.
	/// </para>
	/// </remarks>
	public class UITKParty : UITKCharacterControl
	{
		/// <summary>Name of the container that holds the generated member rows.</summary>
		private const string MEMBER_LIST_NAME = "party-member-list";

		/// <summary>Name of the create-party button.</summary>
		private const string CREATE_BUTTON_NAME = "party-create";

		/// <summary>Name of the leave-party button.</summary>
		private const string LEAVE_BUTTON_NAME = "party-leave";

		/// <summary>Name of the invite-to-party button.</summary>
		private const string INVITE_BUTTON_NAME = "party-invite";

		/// <summary>USS class applied to each generated member row.</summary>
		private const string ROW_CLASS = "party-member";

		/// <summary>USS class applied to a row whose member is not in the local scene.</summary>
		private const string ROW_AWAY_CLASS = "party-member--away";

		/// <summary>Name of the label showing party state in the header.</summary>
		private const string SUBTITLE_NAME = "party-subtitle";

		/// <summary>Name of the badge showing member count.</summary>
		private const string COUNT_NAME = "party-count";

		/// <summary>Name of the label shown when the player is not in a party.</summary>
		private const string EMPTY_NAME = "party-empty";

		/// <summary>Name of the column caption strip.</summary>
		private const string COLUMNS_NAME = "party-columns";

		/// <summary>Name of the shared tooltip overlay registered with the UIManager.</summary>
		private const string TOOLTIP_NAME = "UITooltip";

		/// <summary>
		/// Party size shown in the header badge.
		/// </summary>
		/// <remarks>
		/// Display only. The server owns the real cap; this is the denominator a player reads,
		/// and showing a count with no ceiling tells them nothing about how full the party is.
		/// </remarks>
		private const int MAX_PARTY_DISPLAY = 6;

		/// <summary>
		/// One party member's data. Holds no <c>VisualElement</c> — see the class remarks.
		/// </summary>
		private sealed class MemberModel
		{
			/// <summary>The member's character ID. The ONLY identity any action may use.</summary>
			public long CharacterID;
			/// <summary>The member's party rank.</summary>
			public PartyRank Rank;
			/// <summary>
			/// Last resolved character name, cached so a rebuilt row renders without flashing
			/// blank. Display only — nothing resolves a target from it.
			/// </summary>
			public string Name = string.Empty;
			/// <summary>The member's health fraction, 0-1.</summary>
			public float HealthPCT;
			/// <summary>The member's mana fraction, 0-1.</summary>
			public float ManaPCT;
			/// <summary>The member's stamina fraction, 0-1.</summary>
			public float StaminaPCT;
			/// <summary>Damage per second for the member's current encounter.</summary>
			public float DamagePerSecond;
			/// <summary>Healing per second for the member's current encounter.</summary>
			public float HealPerSecond;
			/// <summary>The member's visible buffs and debuffs, as the server last sent them.</summary>
			public readonly List<ObservedBuffEntry> Buffs = new List<ObservedBuffEntry>();
			/// <summary>Unscaled time <see cref="Buffs"/> arrived, for the local countdown.</summary>
			public float BuffsReceivedTime;
			/// <summary>
			/// Consecutive vitals payloads this member has been missing from.
			/// </summary>
			/// <remarks>
			/// A count rather than a flag, for two reasons that pull the same way. A member who
			/// has only just been added to the roster has not had a chance to appear in a payload
			/// yet, and a bare flag would grey them for the second before the next one arrives —
			/// so somebody joining the party while standing next to you flashes as out-of-zone.
			/// And a single payload lost in transit would grey the whole party for a second, which
			/// reads as everyone zoning at once. Two consecutive misses is still under two seconds
			/// on the server's one-second pump, and neither of those artefacts survives it.
			/// </remarks>
			public int VitalsMisses;
			/// <summary>
			/// True once live vitals have ever arrived for this member.
			/// </summary>
			/// <remarks>
			/// Gates the roster payload out of the live fields. Without it the roster's login-time
			/// health figure, which is re-broadcast on every party update, would overwrite the
			/// live value once a second — so a member's bar would jump back to what they logged in
			/// with and then be corrected again, forever.
			/// </remarks>
			public bool HasVitals;
		}

		/// <summary>
		/// Visual elements backing a single party member row.
		/// </summary>
		private sealed class MemberRow
		{
			/// <summary>Root container for the row.</summary>
			public VisualElement Root;
			/// <summary>Member name label.</summary>
			public Label Name;
			/// <summary>Member rank badge.</summary>
			public Label Rank;
			/// <summary>Health bar fill.</summary>
			public VisualElement HealthFill;
			/// <summary>Mana bar fill.</summary>
			public VisualElement ManaFill;
			/// <summary>Stamina bar fill.</summary>
			public VisualElement StaminaFill;
			/// <summary>Health percentage overlay.</summary>
			public Label HealthLabel;
			/// <summary>Mana percentage overlay.</summary>
			public Label ManaLabel;
			/// <summary>Stamina percentage overlay.</summary>
			public Label StaminaLabel;
			/// <summary>Damage-per-second readout.</summary>
			public Label DamageValue;
			/// <summary>Heal-per-second readout.</summary>
			public Label HealValue;
			/// <summary>Strip holding the member's buff icons.</summary>
			public VisualElement BuffStrip;
			/// <summary>Strip holding the member's debuff icons.</summary>
			public VisualElement DebuffStrip;
			/// <summary>Icons currently attached to the buff strip.</summary>
			public readonly List<BuffIcon> ActiveBuffIcons = new List<BuffIcon>();
			/// <summary>Icons currently attached to the debuff strip.</summary>
			public readonly List<BuffIcon> ActiveDebuffIcons = new List<BuffIcon>();
			/// <summary>
			/// The away state currently written onto <see cref="Root"/>.
			/// </summary>
			/// <remarks>
			/// Tracked so the class is only toggled when it actually changes. Presence is
			/// re-evaluated for every member on every payload — once a second per member — and
			/// <c>EnableInClassList</c> dirties the element's style resolution whether or not the
			/// value differs.
			/// </remarks>
			public bool Away;
		}

		/// <summary>
		/// Visual elements backing one buff or debuff icon. Pooled across every row.
		/// </summary>
		private sealed class BuffIcon
		{
			/// <summary>Root container for the icon.</summary>
			public VisualElement Root;
			/// <summary>Depleting duration fill.</summary>
			public VisualElement Fill;
			/// <summary>Icon sprite element.</summary>
			public VisualElement Icon;
			/// <summary>Stack count label.</summary>
			public Label Label;
			/// <summary>The template this icon is currently bound to.</summary>
			public BaseBuffTemplate Template;
			/// <summary>True while this icon carries the debuff modifier class.</summary>
			public bool IsDebuff;
			/// <summary>The buff's full duration in seconds, or 0 when permanent.</summary>
			public float TotalSeconds;
			/// <summary>Seconds remaining when the server sent this entry.</summary>
			public float RemainingSeconds;
			/// <summary>Stack count this icon's label was built from.</summary>
			public int Stacks;
			/// <summary>
			/// Whole-percent fill height currently written onto <see cref="Fill"/>, or -1 when
			/// nothing has been written yet.
			/// </summary>
			/// <remarks>
			/// The countdown is animated every frame for every icon in the panel — six members
			/// times a dozen effects is several thousand style writes a second, each of which
			/// dirties that element's layout and repaint. A duration bar twenty pixels tall has a
			/// hundred distinguishable states, so a buff with a thirty-second timer genuinely
			/// changes about three times a second and the rest of those writes paint the picture
			/// that is already on screen.
			/// </remarks>
			public int FillPercent;
		}

		/// <summary>The roster MODEL, keyed by character ID. Survives tree rebuilds.</summary>
		private readonly Dictionary<long, MemberModel> roster = new Dictionary<long, MemberModel>();

		/// <summary>The roster VIEW, keyed by character ID. Belongs to one visual tree.</summary>
		private readonly Dictionary<long, MemberRow> rows = new Dictionary<long, MemberRow>();

		/// <summary>Detached buff icons available for reuse by any row.</summary>
		private readonly List<BuffIcon> iconPool = new List<BuffIcon>();

		/// <summary>Scratch set of the member IDs in the payload being applied.</summary>
		private readonly HashSet<long> presentMembers = new HashSet<long>();

		/// <summary>
		/// True once any vitals payload has arrived for the current party.
		/// </summary>
		/// <remarks>
		/// Until it does, nobody is greyed out. Absence from a payload means "somewhere else", and
		/// that reading is only available once there has been a payload to be absent from —
		/// otherwise the whole party would flash grey for the first second after it forms.
		/// </remarks>
		private bool receivedVitals;

		/// <summary>
		/// Consecutive missed vitals payloads before a member is drawn as out-of-zone.
		/// </summary>
		/// <remarks>See <see cref="MemberModel.VitalsMisses"/> for why this is not one.</remarks>
		private const int AWAY_MISS_THRESHOLD = 2;

		/// <summary>The container element that holds the generated member rows.</summary>
		private VisualElement memberList;
		/// <summary>Header label describing the current party state.</summary>
		private Label subtitleLabel;
		/// <summary>Header badge showing the member count.</summary>
		private Label countLabel;
		/// <summary>Label shown in place of the roster when there is no party.</summary>
		private Label emptyLabel;
		/// <summary>Column caption strip, hidden while the roster is empty.</summary>
		private VisualElement columns;
		/// <summary>Create-party button.</summary>
		private Button createButton;
		/// <summary>Invite-to-party button.</summary>
		private Button inviteButton;
		/// <summary>Leave-party button.</summary>
		private Button leaveButton;

		/// <summary>
		/// Queries the member list and wires up the action buttons.
		/// </summary>
		/// <remarks>
		/// Runs against a fresh tree every time, so it drops the old view first — those elements
		/// belong to a tree that no longer exists. The buttons are new objects, so <c>+=</c>
		/// cannot accumulate handlers across rebuilds.
		/// </remarks>
		public override void OnStarting()
		{
			rows.Clear();
			iconPool.Clear();

			VisualElement root = Root;
			if (root == null)
			{
				return;
			}

			memberList = root.Q(MEMBER_LIST_NAME);
			subtitleLabel = root.Q<Label>(SUBTITLE_NAME);
			countLabel = root.Q<Label>(COUNT_NAME);
			emptyLabel = root.Q<Label>(EMPTY_NAME);
			columns = root.Q(COLUMNS_NAME);

			createButton = root.Q<Button>(CREATE_BUTTON_NAME);
			if (createButton != null)
			{
				createButton.clicked += OnButtonCreateParty;
			}

			leaveButton = root.Q<Button>(LEAVE_BUTTON_NAME);
			if (leaveButton != null)
			{
				leaveButton.clicked += OnButtonLeaveParty;
			}

			inviteButton = root.Q<Button>(INVITE_BUTTON_NAME);
			if (inviteButton != null)
			{
				inviteButton.clicked += OnButtonInviteToParty;
			}
		}

		/// <summary>
		/// Re-applies the roster after the visual tree was rebuilt.
		/// </summary>
		/// <remarks>
		/// The base implementation re-runs the character pre/post pair, which is what
		/// re-subscribes this panel to the party controller; the rebuild below then redraws
		/// whatever the model already holds.
		/// </remarks>
		protected override void OnAfterStarting()
		{
			base.OnAfterStarting();

			RebuildRosterView();
		}

		/// <summary>
		/// Redraws the roster whenever the panel is shown.
		/// </summary>
		/// <remarks>
		/// <c>Show()</c> re-clones the tree, so anything written before it is discarded. On the
		/// very first open <c>OnAfterStarting</c> has not run yet and this is the only pass that
		/// happens; on later opens both run and writing the same state twice is harmless.
		/// </remarks>
		protected override void OnAfterShow()
		{
			RebuildRosterView();
		}

		/// <summary>
		/// Clears all member rows when the control is destroyed.
		/// </summary>
		public override void OnDestroying()
		{
			ClearRoster();

			base.OnDestroying();
		}

		/// <summary>
		/// Unsubscribes from the outgoing character's party controller.
		/// </summary>
		/// <remarks>
		/// <c>UITKCharacterControl.OnAfterStarting</c> calls Pre then Post on every tree rebuild
		/// so the pair cancels out. Leaving this un-overridden made the Pre call a no-op, so
		/// every reopen stacked another subscription onto the same controller.
		/// </remarks>
		public override void OnPreSetCharacter()
		{
			base.OnPreSetCharacter();

			UnsubscribePartyEvents();
		}

		/// <summary>
		/// Subscribes to party controller events after the character is set.
		/// </summary>
		public override void OnPostSetCharacter()
		{
			base.OnPostSetCharacter();

			if (Character != null && Character.TryGet(out IPartyController partyController))
			{
				partyController.OnPartyCreated += OnPartyCreated;
				partyController.OnReceivePartyInvite += PartyController_OnReceivePartyInvite;
				partyController.OnAddPartyMember += OnPartyAddMember;
				partyController.OnUpdatePartyVitals += OnPartyUpdateVitals;
				partyController.OnValidatePartyMembers += PartyController_OnValidatePartyMembers;
				partyController.OnRemovePartyMember += OnPartyRemoveMember;
				partyController.OnLeaveParty += OnLeaveParty;
			}
		}

		/// <summary>
		/// Unsubscribes from party controller events before the character is unset.
		/// </summary>
		public override void OnPreUnsetCharacter()
		{
			base.OnPreUnsetCharacter();

			UnsubscribePartyEvents();
		}

		/// <summary>
		/// Drops the roster once the character is gone.
		/// </summary>
		/// <remarks>
		/// The model outlives the visual tree on purpose, so nothing else would clear it — and a
		/// partyless character generates no roster traffic to overwrite it, which is what left the
		/// previous character's party on screen after a character switch.
		/// </remarks>
		public override void OnPostUnsetCharacter()
		{
			base.OnPostUnsetCharacter();

			ClearRoster();
		}

		/// <summary>
		/// Removes this panel's handlers from the current character's party controller.
		/// </summary>
		private void UnsubscribePartyEvents()
		{
			if (Character == null || !Character.TryGet(out IPartyController partyController))
			{
				return;
			}

			partyController.OnPartyCreated -= OnPartyCreated;
			partyController.OnReceivePartyInvite -= PartyController_OnReceivePartyInvite;
			partyController.OnAddPartyMember -= OnPartyAddMember;
			partyController.OnUpdatePartyVitals -= OnPartyUpdateVitals;
			partyController.OnValidatePartyMembers -= PartyController_OnValidatePartyMembers;
			partyController.OnRemovePartyMember -= OnPartyRemoveMember;
			partyController.OnLeaveParty -= OnLeaveParty;
		}

		/// <summary>
		/// Refreshes the local player's own bars and animates every buff countdown.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The server pushes vitals once a second, which is the right rate for five other people
		/// and far too slow for your own health bar — a player who takes a hit would watch their
		/// own row disagree with the resource bar beside it for most of a second. The local
		/// character's attributes are right here and are the same values the server is reading, so
		/// the self row is refreshed every frame from those and the payload merely confirms it.
		/// </para>
		/// <para>
		/// Buff durations count down locally between pushes for the same reason the target frame's
		/// do: the server sends a list only when the SET changes, and each entry carries the
		/// seconds remaining at send time. Rows for members in another zone are skipped — nothing
		/// about them is being updated, and a duration ticking down under a greyed-out facade
		/// would be the one part of it still claiming to be live.
		/// </para>
		/// </remarks>
		protected override void OnTick()
		{
			if (!Visible || roster.Count == 0)
			{
				return;
			}

			RefreshLocalMemberVitals();

			float now = Time.unscaledTime;

			foreach (KeyValuePair<long, MemberRow> kvp in rows)
			{
				if (!roster.TryGetValue(kvp.Key, out MemberModel model) || IsAway(model))
				{
					continue;
				}

				MemberRow row = kvp.Value;
				float elapsed = now - model.BuffsReceivedTime;

				TickIconDurations(row.ActiveBuffIcons, elapsed);
				TickIconDurations(row.ActiveDebuffIcons, elapsed);
			}
		}

		/// <summary>
		/// Re-reads the local character's own resources into its roster row.
		/// </summary>
		private void RefreshLocalMemberVitals()
		{
			if (Character == null ||
				!roster.TryGetValue(Character.ID, out MemberModel model) ||
				!Character.TryGet(out ICharacterAttributeController attributeController))
			{
				return;
			}

			float health = attributeController.GetHealthResourceAttributeCurrentPercentage();
			float mana = attributeController.GetManaResourceAttributeCurrentPercentage();
			float stamina = attributeController.GetStaminaResourceAttributeCurrentPercentage();

			if (Mathf.Approximately(health, model.HealthPCT) &&
				Mathf.Approximately(mana, model.ManaPCT) &&
				Mathf.Approximately(stamina, model.StaminaPCT))
			{
				return;
			}

			model.HealthPCT = health;
			model.ManaPCT = mana;
			model.StaminaPCT = stamina;

			/* Marked as live so the roster broadcast's login-time health cannot overwrite what was
			 * just read from the authoritative local controller. */
			model.HasVitals = true;

			ApplyBars(model);
		}

		/// <summary>
		/// Advances one strip's depleting duration fills.
		/// </summary>
		/// <param name="icons">The icons to animate.</param>
		/// <param name="elapsed">Seconds since the entries were received.</param>
		private static void TickIconDurations(List<BuffIcon> icons, float elapsed)
		{
			for (int i = 0; i < icons.Count; ++i)
			{
				BuffIcon icon = icons[i];
				if (icon.TotalSeconds <= 0.0f || icon.Fill == null)
				{
					// Permanent: the fill stays full rather than draining to nothing.
					continue;
				}

				float fraction = Mathf.Clamp01((icon.RemainingSeconds - elapsed) / icon.TotalSeconds);
				SetIconFill(icon, fraction);
			}
		}

		/// <summary>
		/// Writes a duration fill, skipping the write when it would not change the picture.
		/// </summary>
		/// <param name="icon">The icon to fill.</param>
		/// <param name="fraction">Remaining duration fraction, 0-1.</param>
		private static void SetIconFill(BuffIcon icon, float fraction)
		{
			if (icon.Fill == null)
			{
				return;
			}

			int percent = Mathf.RoundToInt(Mathf.Clamp01(fraction) * 100.0f);
			if (icon.FillPercent == percent)
			{
				return;
			}

			icon.FillPercent = percent;
			icon.Fill.style.height = Length.Percent(percent);
		}

		/// <summary>
		/// Prompts the local player to accept or decline a received party invite.
		/// </summary>
		/// <param name="inviterCharacterID">The inviter's character ID.</param>
		/// <remarks>
		/// The inviter's ID travels back on the answer so the server can confirm the player is
		/// answering the invitation they were actually shown. See
		/// <see cref="PartyAcceptInviteBroadcast"/>.
		/// </remarks>
		public void PartyController_OnReceivePartyInvite(long inviterCharacterID)
		{
			/* Declined outright, and declined rather than dropped. "Ignore party invites" has to
			 * answer the server: an invitation that is silently discarded leaves the inviter
			 * staring at a prompt that never resolves until the server's invitation TTL expires,
			 * which looks to them like the other player is deliberately ignoring them — and to the
			 * server like an invitation still outstanding, which blocks the next one. */
			if (ClientSettings.GetGameplayToggle(ClientSettings.IgnorePartyInvitesKey))
			{
				Client.Broadcast(new PartyDeclineInviteBroadcast()
				{
					InviterCharacterID = inviterCharacterID,
				}, Channel.Reliable);
				return;
			}

			ClientNamingSystem.SetName(NamingSystemType.CharacterName, inviterCharacterID, (n) =>
			{
				if (UIManager.TryGetTK("UIDialogBox", out UITKDialogBox uiTooltip))
				{
					uiTooltip.Open("You have been invited to join " + n + "'s party. Would you like to join?",
					() =>
					{
						Client.Broadcast(new PartyAcceptInviteBroadcast()
						{
							InviterCharacterID = inviterCharacterID,
						}, Channel.Reliable);
					},
					() =>
					{
						Client.Broadcast(new PartyDeclineInviteBroadcast()
						{
							InviterCharacterID = inviterCharacterID,
						}, Channel.Reliable);
					});
				}
			});
		}

		/// <summary>
		/// Removes member rows that are no longer part of the validated member set.
		/// </summary>
		/// <param name="newMembers">The set of valid member IDs.</param>
		public void PartyController_OnValidatePartyMembers(HashSet<long> newMembers)
		{
			foreach (long id in new List<long>(roster.Keys))
			{
				if (!newMembers.Contains(id))
				{
					OnPartyRemoveMember(id);
				}
			}
		}

		/// <summary>
		/// Adds the local player as a member row when a party is created.
		/// </summary>
		/// <param name="location">Location string (unused).</param>
		public void OnPartyCreated(string location)
		{
			if (Character == null ||
				!Character.TryGet(out IPartyController partyController))
			{
				return;
			}

			MemberModel model = GetOrCreateModel(Character.ID);
			model.Name = Character.CharacterName;
			model.Rank = partyController.Rank;
			model.VitalsMisses = 0;

			if (Character.TryGet(out ICharacterAttributeController attributeController))
			{
				model.HealthPCT = attributeController.GetHealthResourceAttributeCurrentPercentage();
				model.ManaPCT = attributeController.GetManaResourceAttributeCurrentPercentage();
				model.StaminaPCT = attributeController.GetStaminaResourceAttributeCurrentPercentage();
				model.HasVitals = true;
			}

			GetOrCreateRow(model);
			ApplyModelToRow(model);
			RefreshHeader();
		}

		/// <summary>
		/// Clears all member rows when leaving the party.
		/// </summary>
		public void OnLeaveParty()
		{
			ClearRoster();
		}

		/// <summary>
		/// Adds or updates a member from the roster payload.
		/// </summary>
		/// <param name="characterID">The member's character ID.</param>
		/// <param name="rank">The member's party rank.</param>
		/// <param name="healthPCT">The member's health percentage (0-1).</param>
		/// <remarks>
		/// <paramref name="healthPCT"/> is a SEED, not an update. It comes from the party database
		/// row, which is written on connect and on disconnect and at no other time, so it is the
		/// value the member logged in with — useful for a member who has just appeared in the
		/// roster and about whom nothing else is known yet, and actively wrong for one whose live
		/// vitals are already arriving. Applying it unconditionally made every bar jump back to
		/// its login value once per party update and then be corrected again a fraction of a
		/// second later.
		/// </remarks>
		public void OnPartyAddMember(long characterID, PartyRank rank, float healthPCT)
		{
			/* Captured before the model is touched, so a rank that MOVED can be told from one that
			 * has merely arrived. Leadership can now change without anybody asking it to — the
			 * server hands it on when the holder has been gone long enough — and a player whose
			 * party quietly acquires a new leader deserves to be told which. */
			bool existed = roster.ContainsKey(characterID);

			MemberModel model = GetOrCreateModel(characterID);

			PartyRank previousRank = model.Rank;
			model.Rank = rank;

			if (existed &&
				previousRank != PartyRank.None &&
				previousRank != rank &&
				rank == PartyRank.Leader)
			{
				AnnounceNewLeader(model);
			}

			if (!model.HasVitals)
			{
				model.HealthPCT = healthPCT;
			}

			/* The name lookup may complete after the tree has been replaced, so the callback
			 * writes into the MODEL and re-reads the view rather than closing over an element. */
			ClientNamingSystem.SetName(NamingSystemType.CharacterName, characterID, (n) =>
			{
				if (roster.TryGetValue(characterID, out MemberModel target))
				{
					target.Name = n;
					ApplyIdentity(target);
				}
			});

			GetOrCreateRow(model);
			ApplyModelToRow(model);
			RefreshHeader();
		}

		/// <summary>
		/// Writes a chat line naming the party's new leader.
		/// </summary>
		/// <param name="model">The member who now holds the rank.</param>
		/// <remarks>
		/// <para>
		/// Driven from the roster payload rather than sent by the server, and that is the point:
		/// every member of the party receives the same payload wherever they are, so every member
		/// sees the same line. A server-side announcement could only reach the members the
		/// promoting scene server happens to host, which would tell half a party something the
		/// other half never heard.
		/// </para>
		/// <para>
		/// Only for a rank that MOVED. A member appearing in the roster for the first time already
		/// holds whatever rank they hold, and announcing that would make joining a party read as a
		/// leadership change — including for the player who just formed one.
		/// </para>
		/// </remarks>
		private void AnnounceNewLeader(MemberModel model)
		{
			if (!UIManager.TryGetTK("UIChat", out UITKChat chat))
			{
				return;
			}

			if (Character != null && model.CharacterID == Character.ID)
			{
				chat.InstantiateChatMessage(ChatChannel.System, "", "You are now the party leader.");
				return;
			}

			/* Falls back to the ID only if the name has not resolved yet, which it will have for
			 * anybody already on the roster — the lookup is fired the moment they are added. */
			string displayName = string.IsNullOrEmpty(model.Name)
				? model.CharacterID.ToString()
				: model.Name;

			chat.InstantiateChatMessage(ChatChannel.System, "", $"{displayName} is now the party leader.");
		}

		/// <summary>
		/// Applies a live vitals payload from the scene server.
		/// </summary>
		/// <param name="entries">Live state for every party member sharing the local scene.</param>
		/// <remarks>
		/// The payload is treated as an authoritative SET. Everyone in it is present and gets
		/// their live values; everyone in the roster who is not in it is somewhere the local
		/// scene server cannot see, and their row becomes a greyscale facade over whatever was
		/// last known about them. An entry for somebody not in the roster is ignored rather than
		/// creating a row — the roster payload owns membership, and inventing a member here would
		/// let the two disagree about who is in the party.
		/// </remarks>
		public void OnPartyUpdateVitals(PartyMemberVitalsEntry[] entries)
		{
			if (entries == null)
			{
				return;
			}

			receivedVitals = true;
			presentMembers.Clear();

			float now = Time.unscaledTime;

			for (int i = 0; i < entries.Length; ++i)
			{
				PartyMemberVitalsEntry entry = entries[i];

				if (!roster.TryGetValue(entry.CharacterID, out MemberModel model))
				{
					continue;
				}

				presentMembers.Add(entry.CharacterID);

				model.HasVitals = true;
				model.VitalsMisses = 0;
				model.HealthPCT = entry.HealthPCT;
				model.ManaPCT = entry.ManaPCT;
				model.StaminaPCT = entry.StaminaPCT;
				model.DamagePerSecond = entry.DamagePerSecond;
				model.HealPerSecond = entry.HealPerSecond;

				model.Buffs.Clear();
				if (entry.Buffs != null)
				{
					for (int b = 0; b < entry.Buffs.Length; ++b)
					{
						model.Buffs.Add(entry.Buffs[b]);
					}
				}
				model.BuffsReceivedTime = now;

				ApplyModelToRow(model);
			}

			foreach (MemberModel model in roster.Values)
			{
				if (presentMembers.Contains(model.CharacterID))
				{
					continue;
				}

				/* Counted, then applied unconditionally.
				 *
				 * Unconditionally because a member who has been elsewhere since before the panel
				 * had any payload starts at zero misses, so a "did it change?" test here would
				 * never fire for them — and the FIRST payload is also what makes absence
				 * meaningful at all, so that is exactly the member the facade exists for.
				 * ApplyPresence does its own change test against what the row is actually
				 * wearing, which is the state that matters. */
				if (model.VitalsMisses < AWAY_MISS_THRESHOLD)
				{
					++model.VitalsMisses;
				}

				ApplyPresence(model);
			}
		}

		/// <summary>
		/// Removes a member.
		/// </summary>
		/// <param name="characterID">The member's character ID.</param>
		public void OnPartyRemoveMember(long characterID)
		{
			bool removed = roster.Remove(characterID);

			if (rows.TryGetValue(characterID, out MemberRow row))
			{
				ReleaseIcons(row.ActiveBuffIcons);
				ReleaseIcons(row.ActiveDebuffIcons);
				row.Root?.RemoveFromHierarchy();
				rows.Remove(characterID);
			}

			if (removed)
			{
				RefreshHeader();
			}
		}

		/// <summary>
		/// Broadcasts a create-party request when not already in a party.
		/// </summary>
		public void OnButtonCreateParty()
		{
			if (Character != null &&
				Character.TryGet(out IPartyController partyController) &&
				partyController.ID < 1)
			{
				Client.Broadcast(new PartyCreateBroadcast(), Channel.Reliable);
			}
		}

		/// <summary>
		/// Confirms then broadcasts a leave-party request.
		/// </summary>
		public void OnButtonLeaveParty()
		{
			if (Character != null &&
				Character.TryGet(out IPartyController partyController) &&
				partyController.ID > 0)
			{
				if (UIManager.TryGetTK("UIDialogBox", out UITKDialogBox tooltip))
				{
					tooltip.Open("Are you sure you want to leave your party?", () =>
					{
						Client.Broadcast(new PartyLeaveBroadcast(), Channel.Reliable);
					}, () => { });
				}
			}
		}

		/// <summary>
		/// Invites the current target, or prompts for a name, to the party.
		/// </summary>
		public void OnButtonInviteToParty()
		{
			if (Character != null &&
				Character.TryGet(out IPartyController partyController) &&
				partyController.ID > 0 &&
				Client.NetworkManager.IsClientStarted)
			{
				if (Character.TryGet(out ITargetController targetController) &&
					targetController.Current.Target != null)
				{
					IPlayerCharacter targetCharacter = targetController.Current.Target.GetComponent<IPlayerCharacter>();
					if (targetCharacter != null)
					{
						Client.Broadcast(new PartyInviteBroadcast()
						{
							TargetCharacterID = targetCharacter.ID,
						}, Channel.Reliable);

						return;
					}
				}

				if (UIManager.TryGetTK("UIDialogInputBox", out UITKDialogInputBox tooltip))
				{
					tooltip.Open("Please type the name of the person you wish to invite.", (s) =>
					{
						if (Authentication.IsAllowedCharacterName(s))
						{
							ClientNamingSystem.GetCharacterID(s, (id) =>
							{
								if (id != 0)
								{
									if (Character != null && Character.ID != id)
									{
										Client.Broadcast(new PartyInviteBroadcast()
										{
											TargetCharacterID = id,
										}, Channel.Reliable);
									}
									else if (UIManager.TryGetTK("UIChat", out UITKChat chat))
									{
										chat.InstantiateChatMessage(ChatChannel.System, "", "You can't invite yourself to the party.");
									}
								}
								else if (UIManager.TryGetTK("UIChat", out UITKChat chat))
								{
									chat.InstantiateChatMessage(ChatChannel.System, "", "A person with that name could not be found.");
								}
							});
						}
					}, null);
				}
			}
		}

		/// <summary>
		/// Returns the existing model for a member, or registers a new one.
		/// </summary>
		/// <param name="characterID">The member's character ID.</param>
		/// <returns>The member model.</returns>
		private MemberModel GetOrCreateModel(long characterID)
		{
			if (!roster.TryGetValue(characterID, out MemberModel model))
			{
				model = new MemberModel
				{
					CharacterID = characterID,
				};
				roster.Add(characterID, model);
			}
			return model;
		}

		/// <summary>
		/// Returns the existing row for a member, or builds one into the current tree.
		/// </summary>
		/// <param name="model">The member the row renders.</param>
		/// <returns>The member row, or null when there is no tree to build into.</returns>
		private MemberRow GetOrCreateRow(MemberModel model)
		{
			if (rows.TryGetValue(model.CharacterID, out MemberRow existing))
			{
				return existing;
			}

			if (memberList == null)
			{
				/* No tree yet — the panel has never been shown. The model still holds the member,
				 * and OnAfterStarting builds the row as soon as there is somewhere to put it. */
				return null;
			}

			VisualElement rowRoot = new VisualElement();
			/* The theme class supplies the hover state and the leading accent rail every
			 * roster in the game shares; the panel class only carries geometry. */
			rowRoot.AddToClassList("fish-row");
			rowRoot.AddToClassList(ROW_CLASS);

			// ── Identity ────────────────────────────────────────────────
			VisualElement identity = new VisualElement();
			identity.AddToClassList("party-member__identity");

			Label name = new Label();
			name.AddToClassList("fish-row__name");
			name.AddToClassList("party-member__name");
			identity.Add(name);

			Label rank = new Label();
			rank.AddToClassList("fish-badge");
			rank.AddToClassList("fish-badge--accent");
			rank.AddToClassList("party-member__rank");
			identity.Add(rank);

			rowRoot.Add(identity);

			// ── Resource bars ───────────────────────────────────────────
			VisualElement bars = new VisualElement();
			bars.AddToClassList("party-member__bars");

			VisualElement healthFill = BuildBar(bars, "fish-bar__fill--hp", null, out Label healthLabel);
			VisualElement manaFill = BuildBar(bars, "fish-bar__fill--mp", "party-bar--mana", out Label manaLabel);
			VisualElement staminaFill = BuildBar(bars, "fish-bar__fill--stam", "party-bar--stamina", out Label staminaLabel);

			rowRoot.Add(bars);

			// ── Output meters ───────────────────────────────────────────
			VisualElement meters = new VisualElement();
			meters.AddToClassList("party-member__meters");

			Label damageValue = BuildMeter(meters, "DPS", "party-meter__value--damage", null);
			Label healValue = BuildMeter(meters, "HPS", "party-meter__value--heal", "party-meter--heal");

			rowRoot.Add(meters);

			// ── Buffs and debuffs ───────────────────────────────────────
			VisualElement effects = new VisualElement();
			effects.AddToClassList("party-member__effects");

			VisualElement buffStrip = new VisualElement();
			buffStrip.AddToClassList("buff-list");
			buffStrip.AddToClassList("party-effects__strip");
			effects.Add(buffStrip);

			VisualElement debuffStrip = new VisualElement();
			debuffStrip.AddToClassList("buff-list");
			debuffStrip.AddToClassList("party-effects__strip");
			debuffStrip.AddToClassList("party-effects__strip--debuff");
			effects.Add(debuffStrip);

			rowRoot.Add(effects);

			MemberRow row = new MemberRow
			{
				Root = rowRoot,
				Name = name,
				Rank = rank,
				HealthFill = healthFill,
				ManaFill = manaFill,
				StaminaFill = staminaFill,
				HealthLabel = healthLabel,
				ManaLabel = manaLabel,
				StaminaLabel = staminaLabel,
				DamageValue = damageValue,
				HealValue = healValue,
				BuffStrip = buffStrip,
				DebuffStrip = debuffStrip,
			};

			/* Captured by ID, never by element — a row's elements are replaced on every rebuild
			 * and the ID is the only identity that stays true across one. */
			long characterID = model.CharacterID;
			rowRoot.RegisterCallback<PointerDownEvent>(evt => OnMemberPointerDown(evt, characterID));

			memberList.Add(rowRoot);
			rows.Add(characterID, row);
			return row;
		}

		/// <summary>
		/// Builds one resource bar into a member row's bar column.
		/// </summary>
		/// <param name="parent">The bar column.</param>
		/// <param name="fillModifierClass">Theme class carrying the resource's colour.</param>
		/// <param name="trackModifierClass">Optional panel class carrying the bar's spacing.</param>
		/// <param name="valueLabel">Receives the percentage overlay label.</param>
		/// <returns>The fill element, whose width is driven from C#.</returns>
		private static VisualElement BuildBar(VisualElement parent, string fillModifierClass, string trackModifierClass, out Label valueLabel)
		{
			VisualElement track = new VisualElement();
			track.AddToClassList("fish-bar");
			track.AddToClassList("party-bar");
			if (!string.IsNullOrEmpty(trackModifierClass))
			{
				track.AddToClassList(trackModifierClass);
			}

			VisualElement fill = new VisualElement();
			fill.AddToClassList("fish-bar__fill");
			fill.AddToClassList(fillModifierClass);
			fill.AddToClassList("party-bar__fill");
			track.Add(fill);

			valueLabel = new Label();
			valueLabel.AddToClassList("fish-bar__label");
			valueLabel.AddToClassList("party-bar__label");
			valueLabel.pickingMode = PickingMode.Ignore;
			track.Add(valueLabel);

			parent.Add(track);
			return fill;
		}

		/// <summary>
		/// Builds one captioned meter readout into a member row's meter column.
		/// </summary>
		/// <param name="parent">The meter column.</param>
		/// <param name="caption">Short caption, e.g. "DPS".</param>
		/// <param name="valueModifierClass">Panel class carrying the value's colour.</param>
		/// <param name="rowModifierClass">Optional panel class carrying the meter's spacing.</param>
		/// <returns>The value label.</returns>
		private static Label BuildMeter(VisualElement parent, string caption, string valueModifierClass, string rowModifierClass)
		{
			VisualElement meterRow = new VisualElement();
			meterRow.AddToClassList("party-meter");
			if (!string.IsNullOrEmpty(rowModifierClass))
			{
				meterRow.AddToClassList(rowModifierClass);
			}

			Label captionLabel = new Label(caption);
			captionLabel.AddToClassList("party-meter__caption");
			meterRow.Add(captionLabel);

			Label valueLabel = new Label();
			valueLabel.AddToClassList("party-meter__value");
			valueLabel.AddToClassList(valueModifierClass);
			meterRow.Add(valueLabel);

			parent.Add(meterRow);
			return valueLabel;
		}

		/// <summary>
		/// Writes one member's whole model into its row, if the row currently exists.
		/// </summary>
		/// <param name="model">The member to render.</param>
		private void ApplyModelToRow(MemberModel model)
		{
			ApplyIdentity(model);
			ApplyBars(model);
			ApplyMeters(model);
			ApplyBuffs(model);
			ApplyPresence(model);
		}

		/// <summary>
		/// Writes a member's name and rank badge into its row.
		/// </summary>
		/// <param name="model">The member to render.</param>
		private void ApplyIdentity(MemberModel model)
		{
			if (!rows.TryGetValue(model.CharacterID, out MemberRow row))
			{
				return;
			}

			row.Name.text = model.Name;

			/* Hidden, not removed. `display: none` would take the badge out of the layout and
			 * let the identity column collapse to the height of the name alone — so a leader's
			 * name would sit higher than everybody else's, and the column a player scans down
			 * would be the one column not aligned. Visibility draws nothing and keeps the
			 * baseline. */
			bool isLeader = model.Rank == PartyRank.Leader;
			row.Rank.text = isLeader ? "LEADER" : string.Empty;
			row.Rank.style.visibility = isLeader ? Visibility.Visible : Visibility.Hidden;
		}

		/// <summary>
		/// Writes a member's three resource fractions into its bars.
		/// </summary>
		/// <param name="model">The member to render.</param>
		private void ApplyBars(MemberModel model)
		{
			if (!rows.TryGetValue(model.CharacterID, out MemberRow row))
			{
				return;
			}

			ApplyBar(row.HealthFill, row.HealthLabel, model.HealthPCT);
			ApplyBar(row.ManaFill, row.ManaLabel, model.ManaPCT);
			ApplyBar(row.StaminaFill, row.StaminaLabel, model.StaminaPCT);
		}

		/// <summary>
		/// Writes one fraction into one bar.
		/// </summary>
		/// <param name="fill">The fill element.</param>
		/// <param name="label">The percentage overlay.</param>
		/// <param name="fraction">The resource fraction, 0-1.</param>
		private static void ApplyBar(VisualElement fill, Label label, float fraction)
		{
			float clamped = Mathf.Clamp01(fraction);

			if (fill != null)
			{
				fill.style.width = Length.Percent(clamped * 100.0f);
			}
			if (label != null)
			{
				label.text = Mathf.RoundToInt(clamped * 100.0f) + "%";
			}
		}

		/// <summary>
		/// Writes a member's encounter rates into its meter column.
		/// </summary>
		/// <param name="model">The member to render.</param>
		private void ApplyMeters(MemberModel model)
		{
			if (!rows.TryGetValue(model.CharacterID, out MemberRow row))
			{
				return;
			}

			ApplyMeter(row.DamageValue, model.DamagePerSecond);
			ApplyMeter(row.HealValue, model.HealPerSecond);
		}

		/// <summary>
		/// Writes one rate into one meter readout.
		/// </summary>
		/// <param name="label">The value label.</param>
		/// <param name="rate">The rate, per second.</param>
		private static void ApplyMeter(Label label, float rate)
		{
			if (label == null)
			{
				return;
			}

			bool idle = rate < 0.5f;
			label.text = idle ? "—" : FormatRate(rate);

			// The idle class removes the colour rather than the text; see UIParty.uss.
			label.EnableInClassList("party-meter__value--idle", idle);
		}

		/// <summary>
		/// Formats a per-second rate for a 40-pixel-wide readout.
		/// </summary>
		/// <param name="rate">The rate, per second.</param>
		/// <returns>A short display string.</returns>
		/// <remarks>
		/// Abbreviated above a thousand because the column cannot hold six digits and because the
		/// last three of them are noise: nobody reads a damage meter to the unit.
		/// </remarks>
		private static string FormatRate(float rate)
		{
			if (rate >= 10000.0f)
			{
				return Mathf.RoundToInt(rate / 1000.0f) + "k";
			}
			if (rate >= 1000.0f)
			{
				return (rate / 1000.0f).ToString("0.0") + "k";
			}
			return Mathf.RoundToInt(rate).ToString();
		}

		/// <summary>
		/// Reconciles a member's buff and debuff strips with its model.
		/// </summary>
		/// <param name="model">The member to render.</param>
		/// <remarks>
		/// Icons are POOLED across every row in the panel, so a party of six trading buffs
		/// allocates nothing after the pool has grown to the busiest moment so far: no
		/// <c>VisualElement</c>, no callback closure, no style object. Buffs and debuffs are
		/// separated here rather than on the wire, from the template's own <c>IsDebuff</c> flag —
		/// duplicating that flag into two arrays would let the two disagree with the template.
		/// </remarks>
		private void ApplyBuffs(MemberModel model)
		{
			if (!rows.TryGetValue(model.CharacterID, out MemberRow row) ||
				row.BuffStrip == null ||
				row.DebuffStrip == null)
			{
				return;
			}

			/* Fast path: the same effects as last time, so only their clocks moved.
			 *
			 * A vitals payload arrives once a second per member and almost none of them change
			 * anybody's buff SET — a fight is mostly the same auras ticking down. Rebuilding
			 * regardless would detach and re-attach every icon in the panel once a second, which
			 * costs a strip re-layout for no visible change and, worse, pulls the element out from
			 * under a hovering pointer: PointerLeave does not fire for an element that has been
			 * removed, so the tooltip it opened would be left on screen with nothing to close it.
			 *
			 * The signature is template ID and stack count in order, which is exactly what
			 * decides what an icon LOOKS like. Durations are not part of it because they change on
			 * every payload by definition; they are written straight onto the icons below. */
			if (TryRefreshIconClocks(row, model))
			{
				return;
			}

			ReleaseIcons(row.ActiveBuffIcons);
			ReleaseIcons(row.ActiveDebuffIcons);

			float elapsed = Time.unscaledTime - model.BuffsReceivedTime;

			for (int i = 0; i < model.Buffs.Count; ++i)
			{
				ObservedBuffEntry entry = model.Buffs[i];

				BaseBuffTemplate template = BaseBuffTemplate.Get<BaseBuffTemplate>(entry.TemplateID);
				if (template == null)
				{
					continue;
				}

				BuffIcon icon = RentIcon();
				BindIcon(icon, template, entry, elapsed);

				if (template.IsDebuff)
				{
					row.DebuffStrip.Add(icon.Root);
					row.ActiveDebuffIcons.Add(icon);
				}
				else
				{
					row.BuffStrip.Add(icon.Root);
					row.ActiveBuffIcons.Add(icon);
				}
			}

			/* Collapsed when empty so the surviving strip centres in the band instead of sitting
			 * against the top of a 22px gap left by the other one. */
			row.BuffStrip.style.display = row.ActiveBuffIcons.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
			row.DebuffStrip.style.display = row.ActiveDebuffIcons.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
		}

		/// <summary>
		/// Re-bases the durations on a row's existing icons when the effect set has not changed.
		/// </summary>
		/// <param name="row">The row to refresh.</param>
		/// <param name="model">The member whose effects to compare against.</param>
		/// <returns>True when the icons matched and were refreshed in place.</returns>
		/// <remarks>
		/// The comparison walks the model's entries in order and expects to meet them in the same
		/// order across the two strips — which holds because the strips were built from that same
		/// order, splitting on the template's <c>IsDebuff</c> flag, and a template's flag does not
		/// change at runtime. Any mismatch at all falls back to a rebuild rather than trying to
		/// patch the difference: a partial reconcile of two interleaved strips is far more code
		/// than the rebuild it would save, for a case that only arises when the set really did
		/// change and a rebuild is warranted anyway.
		/// </remarks>
		private static bool TryRefreshIconClocks(MemberRow row, MemberModel model)
		{
			/* A member with nothing on them takes the rebuild path, which for an empty list is a
			 * handful of no-ops and the two strip-visibility writes this shortcut skips. Returning
			 * true here instead would leave a freshly built row's empty strips displayed. */
			if (model.Buffs.Count < 1 || row.ActiveBuffIcons.Count + row.ActiveDebuffIcons.Count < 1)
			{
				return false;
			}

			/* An upper bound rather than an equality: unresolvable templates are skipped below and
			 * produce no icon, so the icon count is at most the entry count. The exact test is the
			 * one at the end, which requires every icon on both strips to have been matched. */
			if (row.ActiveBuffIcons.Count + row.ActiveDebuffIcons.Count > model.Buffs.Count)
			{
				return false;
			}

			int buffIndex = 0;
			int debuffIndex = 0;

			for (int i = 0; i < model.Buffs.Count; ++i)
			{
				ObservedBuffEntry entry = model.Buffs[i];

				/* Skipped exactly as the rebuild skips it. An entry whose template this client
				 * cannot resolve — a server running content this build does not have — produces no
				 * icon, so counting it against the icons would make the count test below fail
				 * every time and defeat this whole path permanently for that member: a full strip
				 * rebuild, once a second, for as long as they carry the effect. The two paths have
				 * to filter identically or the comparison is not comparing like with like. */
				if (BaseBuffTemplate.Get<BaseBuffTemplate>(entry.TemplateID) == null)
				{
					continue;
				}

				BuffIcon icon;
				if (buffIndex < row.ActiveBuffIcons.Count &&
					row.ActiveBuffIcons[buffIndex].Template != null &&
					row.ActiveBuffIcons[buffIndex].Template.ID == entry.TemplateID)
				{
					icon = row.ActiveBuffIcons[buffIndex++];
				}
				else if (debuffIndex < row.ActiveDebuffIcons.Count &&
					row.ActiveDebuffIcons[debuffIndex].Template != null &&
					row.ActiveDebuffIcons[debuffIndex].Template.ID == entry.TemplateID)
				{
					icon = row.ActiveDebuffIcons[debuffIndex++];
				}
				else
				{
					return false;
				}

				// The label is built from the stack count, so a change in it changes the icon.
				if (icon.Stacks != entry.Stacks)
				{
					return false;
				}

				icon.TotalSeconds = entry.TotalSeconds;
				icon.RemainingSeconds = entry.RemainingSeconds;
			}

			return buffIndex == row.ActiveBuffIcons.Count && debuffIndex == row.ActiveDebuffIcons.Count;
		}

		/// <summary>
		/// Applies the greyscale facade to a member who is not in the local scene.
		/// </summary>
		/// <param name="model">The member to render.</param>
		/// <remarks>
		/// One class on the row root; every colour override hangs off it in USS. Nothing is hidden
		/// and nothing is blanked — the row keeps its place and its last known values, but stops
		/// being drawn in the colours that would claim those values are current. See the
		/// <c>.party-member--away</c> block in UIParty.uss.
		/// </remarks>
		private void ApplyPresence(MemberModel model)
		{
			if (!rows.TryGetValue(model.CharacterID, out MemberRow row))
			{
				return;
			}

			bool away = IsAway(model);
			if (row.Away == away)
			{
				return;
			}

			row.Away = away;
			row.Root.EnableInClassList(ROW_AWAY_CLASS, away);
		}

		/// <summary>
		/// Reports whether a member should be drawn as being outside the local scene.
		/// </summary>
		/// <param name="model">The member to test.</param>
		/// <returns>True when the member's state is known to be not live.</returns>
		/// <remarks>
		/// Gated on having received a payload at all. Absence from the vitals set is only
		/// meaningful once there has been a set to be absent from; without the gate the whole
		/// party would be drawn grey for the first second after it forms, which is exactly the
		/// moment a player is looking at it. It then takes <see cref="AWAY_MISS_THRESHOLD"/>
		/// consecutive misses rather than one, so neither a member who has just been added nor a
		/// single dropped payload puts the facade up.
		/// </remarks>
		private bool IsAway(MemberModel model)
		{
			/* Never the local player. They are, definitionally, in the scene they are standing in,
			 * and their own bars are refreshed from their own controller every frame — so a
			 * dropped or late payload greying their row would put a "this is not live" facade over
			 * the one row on the panel that is live to the frame. */
			if (Character != null && model.CharacterID == Character.ID)
			{
				return false;
			}

			return receivedVitals && model.VitalsMisses >= AWAY_MISS_THRESHOLD;
		}

		/// <summary>
		/// Rebuilds every row from the model into the current visual tree.
		/// </summary>
		private void RebuildRosterView()
		{
			foreach (MemberRow row in rows.Values)
			{
				ReleaseIcons(row.ActiveBuffIcons);
				ReleaseIcons(row.ActiveDebuffIcons);
			}
			rows.Clear();

			/* The pool holds elements belonging to the tree that was just replaced. Reusing them
			 * would parent dead elements into the new one. */
			iconPool.Clear();

			if (memberList != null)
			{
				memberList.Clear();
			}

			foreach (MemberModel model in roster.Values)
			{
				GetOrCreateRow(model);
				ApplyModelToRow(model);
			}

			RefreshHeader();
		}

		/// <summary>
		/// Drops both the model and the view.
		/// </summary>
		private void ClearRoster()
		{
			foreach (MemberRow row in rows.Values)
			{
				ReleaseIcons(row.ActiveBuffIcons);
				ReleaseIcons(row.ActiveDebuffIcons);
				row.Root?.RemoveFromHierarchy();
			}
			rows.Clear();
			roster.Clear();

			/* Reset with the roster, not with the payload. Left set, the first member of the NEXT
			 * party would be drawn as out-of-zone until that party's first vitals push arrived. */
			receivedVitals = false;

			RefreshHeader();
		}

		/// <summary>
		/// Updates the header count, state line and empty placeholder from the roster.
		/// </summary>
		/// <remarks>
		/// Called after every add, remove and clear rather than on a timer: the roster is the
		/// only thing these three read, and a header that disagrees with the list under it is
		/// worse than no header at all.
		/// </remarks>
		private void RefreshHeader()
		{
			int count = roster.Count;
			bool inParty = count > 0;

			if (countLabel != null)
			{
				countLabel.text = $"{count}/{MAX_PARTY_DISPLAY}";
				countLabel.EnableInClassList("fish-badge--accent", inParty);
			}

			if (subtitleLabel != null)
			{
				subtitleLabel.text = inParty
					? (count == 1 ? "1 member" : $"{count} members")
					: "Not in a party";
			}

			if (emptyLabel != null)
			{
				emptyLabel.style.display = inParty ? DisplayStyle.None : DisplayStyle.Flex;
			}

			if (columns != null)
			{
				// Column captions over an empty list name fields that are not there.
				columns.style.display = inParty ? DisplayStyle.Flex : DisplayStyle.None;
			}

			/* Same argument as the columns above, applied to the footer. OnButtonCreateParty
			 * already refuses unless ID < 1, and Leave and Invite unless ID > 0, so showing all
			 * three at once offers two buttons that are guaranteed no-ops. Draw what membership
			 * actually permits instead. */
			if (createButton != null)
			{
				createButton.style.display = inParty ? DisplayStyle.None : DisplayStyle.Flex;
			}

			if (leaveButton != null)
			{
				leaveButton.style.display = inParty ? DisplayStyle.Flex : DisplayStyle.None;
			}

			/* Invite answers to rank as well as to membership, because the server does:
			 * PartySystem.OnServerPartyInviteBroadcastReceived drops the request unless the
			 * inviter is the Leader, and drops it *silently* — so a member offered this button
			 * gets a prompt, sends a broadcast and sees nothing happen, which is the exact class
			 * of dead control the rest of this block removes. The guild panel gates its own
			 * Invite on GuildPermissions.Invite for the same reason. Rank is kept current on
			 * create, on add (which is also how a promotion arrives) and on leave. */
			if (inviteButton != null)
			{
				bool canInvite = inParty &&
								 Character != null &&
								 Character.TryGet(out IPartyController inviteController) &&
								 inviteController.Rank == PartyRank.Leader;

				inviteButton.style.display = canInvite ? DisplayStyle.Flex : DisplayStyle.None;
			}
		}

		#region Buff icon pool

		/// <summary>
		/// Detaches every icon in a list and returns them to the shared pool.
		/// </summary>
		/// <param name="icons">The icons to release.</param>
		private void ReleaseIcons(List<BuffIcon> icons)
		{
			if (icons.Count < 1)
			{
				return;
			}

			/* Any tooltip these icons opened is closed first. PointerLeave does not fire for an
			 * element that has been removed from the hierarchy, so a tooltip opened by hovering an
			 * icon that is then released has nothing left to dismiss it and stays on screen until
			 * the player happens to hover another one.
			 *
			 * Only while this panel is showing. An icon cannot have been hovered otherwise, so
			 * there is nothing to close — and this method also runs during teardown, where
			 * reaching across the UIManager into another control that may already have been
			 * destroyed is a way to turn a clean shutdown into a MissingReferenceException. */
			UITKTooltip tooltip = null;
			if (Visible)
			{
				UIManager.TryGetTK(TOOLTIP_NAME, out tooltip);
			}

			for (int i = 0; i < icons.Count; ++i)
			{
				BuffIcon icon = icons[i];
				tooltip?.HideFor(icon.Root);
				icon.Root?.RemoveFromHierarchy();
				icon.Template = null;
				iconPool.Add(icon);
			}
			icons.Clear();
		}

		/// <summary>
		/// Takes an icon from the pool, creating one only if the pool is empty.
		/// </summary>
		/// <returns>An icon ready to bind.</returns>
		private BuffIcon RentIcon()
		{
			int last = iconPool.Count - 1;
			if (last >= 0)
			{
				BuffIcon pooled = iconPool[last];
				iconPool.RemoveAt(last);
				return pooled;
			}
			return CreateIcon();
		}

		/// <summary>
		/// Builds the visual elements for one pooled buff icon.
		/// </summary>
		/// <returns>The new icon.</returns>
		/// <remarks>
		/// The hover callbacks are registered ONCE, here, and read the icon's current template at
		/// invocation time. Registering them per bind would attach a new closure every time a buff
		/// list changed — once a second per member — and leak one handler per push onto an element
		/// that is never destroyed.
		/// </remarks>
		private BuffIcon CreateIcon()
		{
			VisualElement groupRoot = new VisualElement();
			groupRoot.AddToClassList("buff-group");

			VisualElement fill = new VisualElement();
			fill.AddToClassList("buff-group__fill");
			groupRoot.Add(fill);

			VisualElement iconElement = new VisualElement();
			iconElement.AddToClassList("buff-group__icon");
			groupRoot.Add(iconElement);

			Label label = new Label(string.Empty);
			label.AddToClassList("buff-group__label");
			label.pickingMode = PickingMode.Ignore;
			groupRoot.Add(label);

			BuffIcon icon = new BuffIcon
			{
				Root = groupRoot,
				Fill = fill,
				Icon = iconElement,
				Label = label,
			};

			groupRoot.RegisterCallback<PointerEnterEvent>(evt => OnIconPointerEnter(icon));
			groupRoot.RegisterCallback<PointerLeaveEvent>(evt => OnIconPointerLeave(icon));

			return icon;
		}

		/// <summary>
		/// Binds a pooled icon to one observed buff entry.
		/// </summary>
		/// <param name="icon">The icon to bind.</param>
		/// <param name="template">The buff template.</param>
		/// <param name="entry">The observed entry, as the server sent it.</param>
		/// <param name="elapsed">Seconds since the entry was received.</param>
		private static void BindIcon(BuffIcon icon, BaseBuffTemplate template, ObservedBuffEntry entry, float elapsed)
		{
			icon.Template = template;
			icon.TotalSeconds = entry.TotalSeconds;
			icon.RemainingSeconds = entry.RemainingSeconds;
			icon.Stacks = entry.Stacks;

			/* Invalidated before the fill is written. A pooled icon carries the percentage its
			 * PREVIOUS buff was left at, and the skip-if-unchanged gate would then swallow the
			 * write whenever a different buff happened to land on the same rounded value. */
			icon.FillPercent = -1;

			if (icon.IsDebuff != template.IsDebuff)
			{
				icon.IsDebuff = template.IsDebuff;
				icon.Root.EnableInClassList("buff-group--debuff", template.IsDebuff);
			}

			if (icon.Icon != null)
			{
				icon.Icon.style.backgroundImage = template.Icon != null
					? new StyleBackground(template.Icon)
					: new StyleBackground();
			}

			SetIconFill(icon, entry.TotalSeconds > 0.0f
				? (entry.RemainingSeconds - elapsed) / entry.TotalSeconds
				: 1.0f);

			if (icon.Label != null)
			{
				// Stacks counts applications ABOVE the base one, so one application shows nothing.
				icon.Label.text = entry.Stacks > 0 ? (entry.Stacks + 1).ToString() : string.Empty;
			}
		}

		/// <summary>
		/// Opens the buff tooltip for a hovered icon.
		/// </summary>
		/// <param name="icon">The hovered icon.</param>
		private void OnIconPointerEnter(BuffIcon icon)
		{
			if (icon.Template == null)
			{
				return;
			}

			if (UIManager.TryGetTK(TOOLTIP_NAME, out UITKTooltip tooltip))
			{
				tooltip.Open(icon.Template.Tooltip(), icon.Root);
			}
		}

		/// <summary>
		/// Closes the buff tooltip for an icon.
		/// </summary>
		/// <param name="icon">The icon the pointer left.</param>
		private void OnIconPointerLeave(BuffIcon icon)
		{
			if (UIManager.TryGetTK(TOOLTIP_NAME, out UITKTooltip tooltip))
			{
				tooltip.HideFor(icon.Root);
			}
		}

		#endregion

		/// <summary>
		/// Opens the per-member context menu on right-click.
		/// </summary>
		/// <param name="evt">The pointer-down event.</param>
		/// <param name="characterID">The member's character ID.</param>
		/// <remarks>
		/// Right-click through the shared context menu, replacing the dropdown. The dropdown call
		/// hid the panel and then added its entries, but hiding disables the document — so every
		/// entry was added to a tree the following <c>Show()</c> discarded, and the menu could
		/// never appear with anything in it.
		/// </remarks>
		private void OnMemberPointerDown(PointerDownEvent evt, long characterID)
		{
			// 1 is the right button. Left-click is left free for selection.
			if (evt.button != 1)
			{
				return;
			}

			evt.StopPropagation();

			OpenMemberContextMenu(characterID);
		}

		/// <summary>
		/// Builds and opens the context menu for one party member.
		/// </summary>
		/// <param name="characterID">The member's character ID.</param>
		/// <remarks>
		/// Every entry closes over <paramref name="characterID"/>. The previous version read the
		/// target back out of the row's name LABEL and pushed it through an asynchronous name
		/// lookup to recover an ID it already had — so a roster that changed between the click and
		/// the reply could kick a different member than the one clicked.
		/// </remarks>
		private void OpenMemberContextMenu(long characterID)
		{
			if (Character == null ||
				Character.ID == characterID ||
				!UIManager.TryGetTK("UIContextMenu", out UITKContextMenu contextMenu) ||
				!Character.TryGet(out IPartyController partyController) ||
				partyController.ID < 1 ||
				!roster.TryGetValue(characterID, out MemberModel model))
			{
				return;
			}

			List<(string label, Action callback)> entries = new List<(string, Action)>();

			string displayName = model.Name;
			if (!string.IsNullOrEmpty(displayName))
			{
				entries.Add(("Message", () =>
				{
					if (UIManager.TryGetTK("UIChat", out UITKChat uiChat))
					{
						uiChat.SetInputText($"/tell {displayName} ");
					}
				}
				));
			}

			entries.Add(("Add Friend", () =>
			{
				Client.Broadcast(new FriendAddNewBroadcast()
				{
					CharacterID = characterID,
				}, Channel.Reliable);
			}
			));

			/* Drawing decisions only. The server re-derives both ranks from its own state before
			 * it acts, so an entry a client should not have offered is refused, not obeyed. */
			if (model.Rank < partyController.Rank)
			{
				entries.Add(("Promote to Leader", () =>
				{
					Client.Broadcast(new PartyChangeRankBroadcast()
					{
						CharacterID = characterID,
						Rank = PartyRank.Leader,
					}, Channel.Reliable);
				}
				));

				entries.Add(("Kick", () =>
				{
					if (UIManager.TryGetTK("UIDialogBox", out UITKDialogBox dialog))
					{
						dialog.Open($"Remove {displayName} from the party?", () =>
						{
							Client.Broadcast(new PartyRemoveBroadcast()
							{
								CharacterID = characterID,
							}, Channel.Reliable);
						}, () => { });
					}
				}
				));
			}

			contextMenu.Open(entries);
		}
	}
}
