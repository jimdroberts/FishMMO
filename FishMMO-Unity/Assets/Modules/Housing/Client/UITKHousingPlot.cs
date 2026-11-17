using System;
using FishNet.Transporting;
using UnityEngine;
using UnityEngine.UIElements;
using FishMMO.Shared;

namespace FishMMO.Client
{
	/// <summary>
	/// The housing plot panel: what a deed post says when a player walks up to it, and the buttons
	/// for what they may do there. Binds to <c>UIHousingPlot.uxml</c> / <c>UIHousingPlot.uss</c>.
	/// </summary>
	/// <remarks>
	/// <para>Everything shown comes from the server's <see cref="HousingPlotInfoBroadcast"/>; nothing
	/// about ownership is decided here. Buying sends the price the panel showed, so a price changed
	/// under an open panel is refused rather than charged.</para>
	///
	/// <para>Giving land up takes two presses: the first arms the button and says what will happen,
	/// the second within a few seconds sends it. One stray click must not hand a house back.</para>
	/// </remarks>
	public class UITKHousingPlot : UITKCharacterControl
	{
		private const string TITLE_NAME = "header-title";
		private const string CLOSE_BTN_NAME = "close-button";
		private const string SIZE_NAME = "plot-size";
		private const string OWNER_ROW_NAME = "plot-owner-row";
		private const string OWNER_NAME = "plot-owner";
		private const string PRICE_ROW_NAME = "plot-price-row";
		private const string PRICE_NAME = "plot-price";
		private const string RENT_NAME = "plot-rent";
		private const string STATUS_NAME = "plot-status";
		private const string MESSAGE_NAME = "plot-message";
		private const string BUY_NAME = "plot-buy";
		private const string FINISH_NAME = "plot-finish";
		private const string RELEASE_NAME = "plot-release";
		private const string GUESTS_TOGGLE_NAME = "plot-guests-toggle";
		private const string GUESTS_NAME = "plot-guests";
		private const string GUEST_NAME_FIELD = "plot-guest-name";
		private const string GUEST_GRANT_NAME = "plot-guest-grant";
		private const string GUEST_LIST_NAME = "plot-guest-list";
		private const string GUESTS_EMPTY_NAME = "plot-guests-empty";
		private const string PREPAY_NAME = "plot-prepay";
		private const string PREPAY_LESS_NAME = "plot-prepay-less";
		private const string PREPAY_MORE_NAME = "plot-prepay-more";
		private const string PREPAY_PERIODS_NAME = "plot-prepay-periods";
		private const string PREPAY_PAY_NAME = "plot-prepay-pay";
		private const string CSS_HIDDEN = "plot-hidden";

		/// <summary>The four guest-permission toggles, in <see cref="PlotPermission"/> bit order.</summary>
		private static readonly (string name, PlotPermission flag)[] GuestFlags =
		{
			("plot-guest-enter", PlotPermission.Enter),
			("plot-guest-place", PlotPermission.PlaceItems),
			("plot-guest-remove", PlotPermission.RemoveItems),
			("plot-guest-invite", PlotPermission.InviteFriends),
		};

		/// <summary>Seconds a first press of "Give up land" stays armed.</summary>
		private const float ReleaseArmSeconds = 5f;

		private HousingPlotInfoBroadcast info;
		private bool hasInfo;
		private string message = string.Empty;
		private float releaseArmedFor;
		private int prepayPeriods = 1;
		private bool guestsOpen;
		private HousingAccessEntry[] guests;
		private bool clearGuestNameOnSuccess;

		/// <inheritdoc />
		public override void OnStarting()
		{
			VisualElement root = Root;
			if (root == null)
			{
				return;
			}
			Button close = root.Q<Button>(CLOSE_BTN_NAME);
			if (close != null)
			{
				close.clicked += Hide;
			}
			Button buy = root.Q<Button>(BUY_NAME);
			if (buy != null)
			{
				buy.clicked += OnBuyClicked;
			}
			Button finish = root.Q<Button>(FINISH_NAME);
			if (finish != null)
			{
				finish.clicked += OnFinishClicked;
			}
			Button release = root.Q<Button>(RELEASE_NAME);
			if (release != null)
			{
				release.clicked += OnReleaseClicked;
			}
			Wire(root, GUESTS_TOGGLE_NAME, OnGuestsToggled);
			Wire(root, GUEST_GRANT_NAME, OnGrantClicked);
			Wire(root, PREPAY_LESS_NAME, () => StepPrepay(-1));
			Wire(root, PREPAY_MORE_NAME, () => StepPrepay(1));
			Wire(root, PREPAY_PAY_NAME, OnPrepayClicked);
		}

		private static void Wire(VisualElement root, string name, Action action)
		{
			Button button = root.Q<Button>(name);
			if (button != null)
			{
				button.clicked += action;
			}
		}

		/// <inheritdoc />
		protected override void OnAfterStarting()
		{
			base.OnAfterStarting();
			ApplyContent();
		}

		/// <inheritdoc />
		protected override void OnAfterShow()
		{
			releaseArmedFor = 0f;
			ApplyContent();
		}

		/// <inheritdoc />
		public override void OnClientSet()
		{
			Client.NetworkManager.ClientManager.RegisterBroadcast<HousingPlotInfoBroadcast>(OnPlotInfo);
			Client.NetworkManager.ClientManager.RegisterBroadcast<HousingResultBroadcast>(OnHousingResult);
			Client.NetworkManager.ClientManager.RegisterBroadcast<HousingAccessListBroadcast>(OnAccessList);
		}

		/// <inheritdoc />
		public override void OnClientUnset()
		{
			Client.NetworkManager.ClientManager.UnregisterBroadcast<HousingPlotInfoBroadcast>(OnPlotInfo);
			Client.NetworkManager.ClientManager.UnregisterBroadcast<HousingResultBroadcast>(OnHousingResult);
			Client.NetworkManager.ClientManager.UnregisterBroadcast<HousingAccessListBroadcast>(OnAccessList);
		}

		private void OnPlotInfo(HousingPlotInfoBroadcast msg, Channel channel)
		{
			bool samePlot = hasInfo && info.PlotID == msg.PlotID;
			info = msg;
			hasInfo = true;
			if (!samePlot)
			{
				message = string.Empty;
				releaseArmedFor = 0f;
				prepayPeriods = 1;
				guestsOpen = false;
				guests = null;
			}
			prepayPeriods = Mathf.Clamp(prepayPeriods, 1, Mathf.Max(1, msg.PrepayPeriodsAllowed));
			if (!Visible)
			{
				Show();
			}
			else
			{
				ApplyContent();
			}
		}

		private void OnHousingResult(HousingResultBroadcast msg, Channel channel)
		{
			// A deed post's refusal carries plot -1; vault answers carry 0 and belong to the vault window.
			const long PostRefusal = -1;
			bool aboutThisPlot = hasInfo && msg.PlotID == info.PlotID;
			if (msg.PlotID == PostRefusal)
			{
				/* A post that answered with a refusal instead of a description (housing switched
				 * off, land not surveyed yet): open on the reason, or the key press reads as ignored. */
				hasInfo = false;
				message = Describe(msg.Result);
				if (!Visible)
				{
					Show();
				}
				else
				{
					ApplyContent();
				}
				return;
			}
			if (!Visible || !aboutThisPlot)
			{
				return;
			}
			message = Describe(msg.Result);
			if (msg.Result == HousingResult.Success && clearGuestNameOnSuccess)
			{
				TextField field = Root?.Q<TextField>(GUEST_NAME_FIELD);
				if (field != null)
				{
					field.SetValueWithoutNotify(string.Empty);
				}
			}
			clearGuestNameOnSuccess = false;
			ApplyContent();
			// Whatever happened may have changed the plot; ask again (the server also re-sends after a purchase).
			if (hasInfo && msg.Result != HousingResult.Disabled && msg.Result != HousingResult.UnknownPlot)
			{
				Client.Broadcast(new HousingPlotInfoRequestBroadcast { PlotID = info.PlotID }, Channel.Reliable);
			}
		}

		private void OnAccessList(HousingAccessListBroadcast msg, Channel channel)
		{
			if (!hasInfo || msg.PlotID != info.PlotID)
			{
				return;
			}
			guests = msg.Entries ?? Array.Empty<HousingAccessEntry>();
			ApplyContent();
		}

		private void OnGuestsToggled()
		{
			if (!hasInfo)
			{
				return;
			}
			guestsOpen = !guestsOpen;
			if (guestsOpen)
			{
				Client.Broadcast(new HousingAccessListRequestBroadcast { PlotID = info.PlotID }, Channel.Reliable);
			}
			ApplyContent();
		}

		private void OnGrantClicked()
		{
			if (!hasInfo || Root == null)
			{
				return;
			}
			TextField field = Root.Q<TextField>(GUEST_NAME_FIELD);
			string name = field != null ? field.value?.Trim() : null;
			if (string.IsNullOrEmpty(name))
			{
				message = "Type the name of the character to give a key to.";
				ApplyContent();
				return;
			}
			int permissions = 0;
			foreach ((string toggleName, PlotPermission flag) in GuestFlags)
			{
				Toggle toggle = Root.Q<Toggle>(toggleName);
				if (toggle != null && toggle.value)
				{
					permissions |= (int)flag;
				}
			}
			if (permissions == 0)
			{
				message = "Tick at least one thing they may do.";
				ApplyContent();
				return;
			}
			clearGuestNameOnSuccess = true;
			message = $"Giving {name} a key…";
			ApplyContent();
			Client.Broadcast(new HousingGrantAccessBroadcast { PlotID = info.PlotID, CharacterID = 0, CharacterName = name, Permissions = permissions }, Channel.Reliable);
		}

		private void Revoke(long characterID, string name)
		{
			if (!hasInfo)
			{
				return;
			}
			message = $"Taking {(string.IsNullOrEmpty(name) ? "their" : name + "'s")} key back…";
			ApplyContent();
			Client.Broadcast(new HousingRevokeAccessBroadcast { PlotID = info.PlotID, CharacterID = characterID }, Channel.Reliable);
		}

		private void StepPrepay(int step)
		{
			if (!hasInfo)
			{
				return;
			}
			prepayPeriods = Mathf.Clamp(prepayPeriods + step, 1, Mathf.Max(1, info.PrepayPeriodsAllowed));
			ApplyContent();
		}

		private void OnPrepayClicked()
		{
			if (!hasInfo || info.PrepayPeriodsAllowed <= 0 || info.RentPerPeriod <= 0)
			{
				return;
			}
			message = "Paying rent…";
			ApplyContent();
			Client.Broadcast(new HousingPrepayRentBroadcast
			{
				PlotID = info.PlotID,
				Periods = prepayPeriods,
				QuotedAmount = prepayPeriods * info.RentPerPeriod,
			}, Channel.Reliable);
		}

		private void OnBuyClicked()
		{
			if (!hasInfo)
			{
				return;
			}
			message = "Buying…";
			ApplyContent();
			Client.Broadcast(new HousingClaimBroadcast { PlotID = info.PlotID, QuotedPrice = info.Price }, Channel.Reliable);
		}

		private void OnFinishClicked()
		{
			if (!hasInfo)
			{
				return;
			}
			Client.Broadcast(new HousingFinishBuildingBroadcast { PlotID = info.PlotID }, Channel.Reliable);
		}

		private void OnReleaseClicked()
		{
			if (!hasInfo)
			{
				return;
			}
			if (releaseArmedFor <= 0f)
			{
				releaseArmedFor = ReleaseArmSeconds;
				message = "Press again to give this land up. Anything built on it goes to your house vault.";
				ApplyContent();
				return;
			}
			releaseArmedFor = 0f;
			message = "Giving up the land…";
			ApplyContent();
			Client.Broadcast(new HousingReleaseBroadcast { PlotID = info.PlotID }, Channel.Reliable);
		}

		/// <inheritdoc />
		protected override void OnTick()
		{
			if (!Visible || releaseArmedFor <= 0f)
			{
				return;
			}
			releaseArmedFor -= Time.deltaTime;
			if (releaseArmedFor <= 0f)
			{
				ApplyContent();
			}
		}

		private void ApplyContent()
		{
			VisualElement root = Root;
			if (root == null)
			{
				return;
			}

			PlotState state = hasInfo ? (PlotState)info.State : PlotState.Empty;
			bool owner = hasInfo && info.Relation == PlotRelation.Owner;
			bool owned = state == PlotState.Building || state == PlotState.Occupied;

			SetText(root, TITLE_NAME, hasInfo ? TitleFor(state, info.OwnerName, owner) : "Plot");
			SetText(root, SIZE_NAME, hasInfo ? $"{info.Width:0} × {info.Depth:0} m" : string.Empty);

			root.Q(OWNER_ROW_NAME)?.EnableInClassList(CSS_HIDDEN, !hasInfo || !owned);
			SetText(root, OWNER_NAME, owner ? "You" : string.IsNullOrEmpty(info.OwnerName) ? "—" : info.OwnerName);

			bool forSale = hasInfo && state.IsClaimable();
			root.Q(PRICE_ROW_NAME)?.EnableInClassList(CSS_HIDDEN, !forSale);
			string currency = CurrencyName();
			SetText(root, PRICE_NAME, hasInfo ? $"{info.Price:N0} {currency}" : string.Empty);
			SetText(root, RENT_NAME, hasInfo ? RentText(currency) : string.Empty);
			SetText(root, STATUS_NAME, hasInfo ? StatusText(state, owner, forSale) : string.Empty);
			SetText(root, MESSAGE_NAME, message);

			Show(root, BUY_NAME, forSale && !info.OwnsLand);
			Show(root, FINISH_NAME, owner && state == PlotState.Building);
			Show(root, RELEASE_NAME, owner && owned);
			Button release = root.Q<Button>(RELEASE_NAME);
			if (release != null)
			{
				release.text = releaseArmedFor > 0f ? "Confirm give up" : "Give up land";
			}

			bool prepay = owner && owned && info.RentPerPeriod > 0 && info.PrepayPeriodsAllowed > 0;
			Show(root, PREPAY_NAME, prepay);
			if (prepay)
			{
				SetText(root, PREPAY_PERIODS_NAME, prepayPeriods == 1 ? "1 period" : $"{prepayPeriods} periods");
				Button pay = root.Q<Button>(PREPAY_PAY_NAME);
				if (pay != null)
				{
					pay.text = $"Pay {prepayPeriods * info.RentPerPeriod:N0} {currency} ahead";
				}
			}

			bool mayInvite = hasInfo && owned && ((PlotPermission)info.Permissions & PlotPermission.InviteFriends) != 0;
			if (!mayInvite)
			{
				guestsOpen = false;
			}
			Show(root, GUESTS_TOGGLE_NAME, mayInvite);
			Button guestsToggle = root.Q<Button>(GUESTS_TOGGLE_NAME);
			if (guestsToggle != null)
			{
				guestsToggle.text = guestsOpen ? "Hide guests" : "Guests";
			}
			Show(root, GUESTS_NAME, guestsOpen);
			if (guestsOpen)
			{
				FillGuests(root);
			}
		}

		/// <summary>Rebuilds the guest rows from the last list the server sent.</summary>
		private void FillGuests(VisualElement root)
		{
			ScrollView list = root.Q<ScrollView>(GUEST_LIST_NAME);
			if (list == null)
			{
				return;
			}
			list.Clear();
			int count = guests != null ? guests.Length : 0;
			SetText(root, GUESTS_EMPTY_NAME, guests == null ? "Loading…" : "Nobody holds a key.");
			Show(root, GUESTS_EMPTY_NAME, count == 0);
			for (int i = 0; i < count; i++)
			{
				HousingAccessEntry entry = guests[i];
				var row = new VisualElement();
				row.AddToClassList("plot-guest-row");
				var name = new Label(string.IsNullOrEmpty(entry.Name) ? $"#{entry.CharacterID}" : entry.Name);
				name.AddToClassList("fish-label");
				name.AddToClassList("plot-guest-row__name");
				var flags = new Label(DescribeFlags((PlotPermission)entry.Permissions));
				flags.AddToClassList("fish-label");
				flags.AddToClassList("fish-label--secondary");
				flags.AddToClassList("plot-guest-row__flags");
				long id = entry.CharacterID;
				string who = entry.Name;
				var revoke = new Button(() => Revoke(id, who)) { text = "Revoke" };
				revoke.AddToClassList("fish-button");
				revoke.AddToClassList("fish-button--danger");
				revoke.AddToClassList("plot-guest-row__revoke");
				row.Add(name);
				row.Add(flags);
				row.Add(revoke);
				list.Add(row);
			}
		}

		private static string DescribeFlags(PlotPermission permissions)
		{
			var parts = new System.Collections.Generic.List<string>(4);
			if ((permissions & PlotPermission.Enter) != 0) parts.Add("enter");
			if ((permissions & PlotPermission.PlaceItems) != 0) parts.Add("place");
			if ((permissions & PlotPermission.RemoveItems) != 0) parts.Add("remove");
			if ((permissions & PlotPermission.InviteFriends) != 0) parts.Add("invite");
			return parts.Count == 0 ? "—" : string.Join(", ", parts);
		}

		private static string TitleFor(PlotState state, string ownerName, bool owner)
		{
			switch (state)
			{
				case PlotState.Building:
					return owner ? "Your building site" : "Building site";
				case PlotState.Occupied:
					return owner ? "Your house" : string.IsNullOrEmpty(ownerName) ? "A house" : ownerName + "'s house";
				case PlotState.Abandoned:
					return "Abandoned house — for sale";
				default:
					return "Plot for sale";
			}
		}

		private string RentText(string currency)
		{
			if (info.RentPerPeriod <= 0)
			{
				return "None";
			}
			return $"{info.RentPerPeriod:N0} {currency} every {Duration(info.RentPeriodHours * 3600.0)}";
		}

		private string StatusText(PlotState state, bool owner, bool forSale)
		{
			if (owner)
			{
				if (info.UnpaidForSeconds > 0.0)
				{
					double left = info.RentGraceHours * 3600.0 - info.UnpaidForSeconds;
					return left > 0.0
						? $"Rent unpaid for {Duration(info.UnpaidForSeconds)}. Pay within {Duration(left)} or the land is taken back."
						: "Rent unpaid past the grace period. The land is about to be taken back.";
				}
				string due = info.RentPerPeriod > 0 && info.RentDueInSeconds != 0.0
					? $"Rent is taken from your purse in {Duration(Math.Max(0.0, info.RentDueInSeconds))}."
					: string.Empty;
				string building = state == PlotState.Building
					? "This is a building site. Press Finish building once your house is done."
					: string.Empty;
				return (building + " " + due).Trim();
			}
			if (forSale && info.OwnsLand)
			{
				return "You already own land. One house each.";
			}
			if (forSale && state == PlotState.Abandoned)
			{
				return "Its last owner stopped paying. Whatever stood here went to their vault.";
			}
			if (!forSale && info.Relation == PlotRelation.Guest)
			{
				return "You have a key to this house.";
			}
			return string.Empty;
		}

		private string CurrencyName()
		{
			CharacterAttributeTemplate template = hasInfo && info.CurrencyTemplateID != 0
				? CharacterAttributeTemplate.Get<CharacterAttributeTemplate>(info.CurrencyTemplateID)
				: null;
			// The shipped currency attribute is named "Currency", which reads as a label rather than a coin.
			return template == null || string.IsNullOrEmpty(template.Name) || template.Name == "Currency" ? "gold" : template.Name;
		}

		/// <summary>A duration the way a player reads one: the largest two units.</summary>
		private static string Duration(double seconds)
		{
			var span = TimeSpan.FromSeconds(Math.Max(0.0, seconds));
			if (span.TotalDays >= 1.0)
			{
				return span.Hours > 0 ? $"{(int)span.TotalDays}d {span.Hours}h" : $"{(int)span.TotalDays}d";
			}
			if (span.TotalHours >= 1.0)
			{
				return span.Minutes > 0 ? $"{(int)span.TotalHours}h {span.Minutes}m" : $"{(int)span.TotalHours}h";
			}
			if (span.TotalMinutes >= 1.0)
			{
				return $"{(int)span.TotalMinutes}m";
			}
			return $"{Math.Max(1, (int)span.TotalSeconds)}s";
		}

		private static string Describe(HousingResult result)
		{
			switch (result)
			{
				case HousingResult.Success: return "Done.";
				case HousingResult.Disabled: return "Housing is not open on this server.";
				case HousingResult.UnknownPlot: return "This plot is still being surveyed. Try again in a moment.";
				case HousingResult.NotTheOwner: return "Only the owner can do that.";
				case HousingResult.NotPermitted: return "You are not allowed to do that here.";
				case HousingResult.WrongState: return "The plot has changed since you looked. Look again.";
				case HousingResult.CannotAfford: return "You cannot afford it.";
				case HousingResult.AlreadyClaimed: return "Somebody else owns this plot now.";
				case HousingResult.AlreadyOwnsLand: return "You already own land. One house each.";
				case HousingResult.PriceChanged: return "The price has changed. Check it and buy again.";
				case HousingResult.TooFar: return "Stand by the deed post to buy.";
				case HousingResult.NoSuchCharacter: return "No character of that name lives on this world.";
				case HousingResult.NoRoom: return "There is no room in your bank or bags.";
				case HousingResult.NotRetrievable: return "That cannot be taken out of the vault yet.";
				default: return "Something went wrong. Nothing was changed.";
			}
		}

		private static void SetText(VisualElement root, string name, string text)
		{
			Label label = root.Q<Label>(name);
			if (label != null)
			{
				label.text = text ?? string.Empty;
			}
		}

		private static void Show(VisualElement root, string name, bool shown)
		{
			root.Q(name)?.EnableInClassList(CSS_HIDDEN, !shown);
		}
	}
}
