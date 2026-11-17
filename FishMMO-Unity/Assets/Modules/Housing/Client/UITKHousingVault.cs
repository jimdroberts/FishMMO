using System;
using System.Collections.Generic;
using FishNet.Transporting;
using UnityEngine;
using UnityEngine.UIElements;
using FishMMO.Shared;

namespace FishMMO.Client
{
	/// <summary>
	/// The house vault: what stood on land its owner lost or gave up. Opened from the bank.
	/// Binds to <c>UIHousingVault.uxml</c> / <c>UIHousingVault.uss</c>.
	/// </summary>
	/// <remarks>
	/// <para>Things only come out. Retrieving pays the entry's fee and delivers its structures to the
	/// bank as kits (<see cref="StructureKitTemplate"/>), or to the bags when the bank is full; giving
	/// an entry up deletes it for nothing. No path puts anything into the vault from here — the vault
	/// fills only when a house loses its land.</para>
	///
	/// <para>Giving up takes two presses, like giving up land: one stray click must not destroy a house.</para>
	/// </remarks>
	public class UITKHousingVault : UITKCharacterControl
	{
		/// <summary>The control's registered name: the bank opens it by this.</summary>
		public const string ControlName = "UIHousingVault";

		private const string CLOSE_BTN_NAME = "close-button";
		private const string LIST_NAME = "vault-list";
		private const string EMPTY_NAME = "vault-empty";
		private const string MESSAGE_NAME = "vault-message";
		private const string CSS_HIDDEN = "vault-hidden";

		/// <summary>Seconds a first press of "Give up" stays armed.</summary>
		private const float ForfeitArmSeconds = 5f;

		private HousingVaultEntry[] entries;
		private string message = string.Empty;
		private long armedForfeit;
		private float armedFor;

		/// <summary>Raised when the vault's contents arrive, with whether it holds anything.</summary>
		public static event Action<bool> OnVaultKnown;

		/// <inheritdoc />
		public override void OnStarting()
		{
			Button close = Root?.Q<Button>(CLOSE_BTN_NAME);
			if (close != null)
			{
				close.clicked += Hide;
			}
		}

		/// <inheritdoc />
		protected override void OnAfterStarting()
		{
			base.OnAfterStarting();
			Fill();
		}

		/// <inheritdoc />
		protected override void OnAfterShow()
		{
			armedForfeit = 0;
			Fill();
		}

		/// <inheritdoc />
		public override void OnClientSet()
		{
			Client.NetworkManager.ClientManager.RegisterBroadcast<HousingVaultBroadcast>(OnVault);
			Client.NetworkManager.ClientManager.RegisterBroadcast<HousingResultBroadcast>(OnHousingResult);
		}

		/// <inheritdoc />
		public override void OnClientUnset()
		{
			Client.NetworkManager.ClientManager.UnregisterBroadcast<HousingVaultBroadcast>(OnVault);
			Client.NetworkManager.ClientManager.UnregisterBroadcast<HousingResultBroadcast>(OnHousingResult);
		}

		/// <summary>Asks the server for the vault's contents (the bank does this whenever it opens).</summary>
		public static void Request()
		{
			Client.Broadcast(new HousingVaultRequestBroadcast(), Channel.Reliable);
		}

		/// <summary>Opens the window and refreshes it.</summary>
		public void Open()
		{
			message = string.Empty;
			if (!Visible)
			{
				Show();
			}
			Request();
		}

		private void OnVault(HousingVaultBroadcast msg, Channel channel)
		{
			entries = msg.Entries ?? Array.Empty<HousingVaultEntry>();
			OnVaultKnown?.Invoke(entries.Length > 0);
			if (Visible)
			{
				Fill();
			}
		}

		private void OnHousingResult(HousingResultBroadcast msg, Channel channel)
		{
			// Vault answers carry plot 0; plot answers belong to the plot panel.
			if (msg.PlotID != 0)
			{
				return;
			}
			if (msg.Result == HousingResult.Disabled)
			{
				OnVaultKnown?.Invoke(false);
			}
			if (!Visible)
			{
				return;
			}
			message = Describe(msg.Result);
			Fill();
			if (msg.Result != HousingResult.Disabled)
			{
				Request();
			}
		}

		private void Retrieve(HousingVaultEntry entry)
		{
			message = "Retrieving…";
			Fill();
			Client.Broadcast(new HousingVaultRetrieveBroadcast { VaultID = entry.VaultID }, Channel.Reliable);
		}

		private void Forfeit(HousingVaultEntry entry)
		{
			if (armedForfeit != entry.VaultID || armedFor <= 0f)
			{
				armedForfeit = entry.VaultID;
				armedFor = ForfeitArmSeconds;
				message = "Press Give up again to destroy it for good.";
				Fill();
				return;
			}
			armedForfeit = 0;
			message = "Giving it up…";
			Fill();
			Client.Broadcast(new HousingVaultForfeitBroadcast { VaultID = entry.VaultID }, Channel.Reliable);
		}

		/// <inheritdoc />
		protected override void OnTick()
		{
			if (!Visible || armedForfeit == 0)
			{
				return;
			}
			armedFor -= Time.deltaTime;
			if (armedFor <= 0f)
			{
				armedForfeit = 0;
				Fill();
			}
		}

		private void Fill()
		{
			VisualElement root = Root;
			if (root == null)
			{
				return;
			}
			ScrollView list = root.Q<ScrollView>(LIST_NAME);
			Label empty = root.Q<Label>(EMPTY_NAME);
			Label status = root.Q<Label>(MESSAGE_NAME);
			if (status != null)
			{
				status.text = message;
			}
			if (list == null)
			{
				return;
			}
			list.Clear();
			int count = entries != null ? entries.Length : 0;
			if (empty != null)
			{
				empty.text = entries == null ? "Opening the vault…" : "Your vault is empty.";
				empty.EnableInClassList(CSS_HIDDEN, count > 0);
			}
			for (int i = 0; i < count; i++)
			{
				HousingVaultEntry entry = entries[i];
				PlotStructureTemplate structure = PlotStructureTemplate.Get<PlotStructureTemplate>(entry.TemplateID);
				bool retrievable = structure != null && structure.Kit != null;

				var row = new VisualElement();
				row.AddToClassList("vault-row");
				row.Add(MakeLabel(structure != null ? structure.name : $"Structure #{entry.TemplateID}", "vault-row__name"));
				row.Add(MakeLabel($"×{entry.Amount}", "vault-row__amount"));
				row.Add(MakeLabel(entry.Fee > 0 ? $"{entry.Fee:N0} gold" : "free", "vault-row__fee", "fish-label--numeric"));

				HousingVaultEntry captured = entry;
				var retrieve = new Button(() => Retrieve(captured)) { text = "Retrieve" };
				retrieve.AddToClassList("fish-button");
				retrieve.AddToClassList("fish-button--primary");
				retrieve.AddToClassList("vault-row__button");
				retrieve.SetEnabled(retrievable);
				if (!retrievable)
				{
					retrieve.tooltip = "This has no kit to retrieve it as yet.";
				}
				var forfeit = new Button(() => Forfeit(captured)) { text = armedForfeit == entry.VaultID ? "Confirm" : "Give up" };
				forfeit.AddToClassList("fish-button");
				forfeit.AddToClassList("fish-button--danger");
				forfeit.AddToClassList("vault-row__button");

				row.Add(retrieve);
				row.Add(forfeit);
				list.Add(row);
			}
		}

		private static Label MakeLabel(string text, string layoutClass, string extraClass = null)
		{
			var label = new Label(text);
			label.AddToClassList("fish-label");
			label.AddToClassList(layoutClass);
			if (extraClass != null)
			{
				label.AddToClassList(extraClass);
			}
			return label;
		}

		private static string Describe(HousingResult result)
		{
			switch (result)
			{
				case HousingResult.Success: return "Done.";
				case HousingResult.Disabled: return "Housing is not open on this server.";
				case HousingResult.CannotAfford: return "You cannot afford the fee.";
				case HousingResult.NothingStored: return "That is no longer in your vault.";
				case HousingResult.NoRoom: return "There is no room in your bank or bags.";
				case HousingResult.NotRetrievable: return "That cannot be taken out of the vault yet.";
				default: return "Something went wrong. Nothing was changed.";
			}
		}
	}
}
