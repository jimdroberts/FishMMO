using System;
using UnityEngine;

namespace FishMMO.Client
{
	/// <summary>
	/// What the Housing module adds to the client outside its own windows: the bank's "House vault" link, shown only
	/// while the vault holds something.
	/// </summary>
	public static class HousingClient
	{
		/// <summary>The vault link's id in <see cref="BankWindowLinks"/>.</summary>
		public const string VaultLinkId = "housing.vault";

		/// <summary>The setter of the bank window that asked last; the vault's answer arrives later.</summary>
		private static Action<bool> showVaultLink;

		/// <summary>Registers the link. Runs before the first scene loads; safe to run again.</summary>
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		public static void Install()
		{
			BankWindowLinks.Register(VaultLinkId, "House vault", 100, OpenVault, OnBankShown);
			UITKHousingVault.OnVaultKnown -= OnVaultKnown;
			UITKHousingVault.OnVaultKnown += OnVaultKnown;
		}

		/// <summary>The bank opened: hide the link and ask whether the vault holds anything.</summary>
		private static void OnBankShown(Action<bool> setVisible)
		{
			showVaultLink = setVisible;
			setVisible(false);
			if (Client.NetworkManager != null && Client.NetworkManager.ClientManager.Started)
			{
				UITKHousingVault.Request();
			}
		}

		private static void OnVaultKnown(bool holdsSomething)
		{
			showVaultLink?.Invoke(holdsSomething);
		}

		private static void OpenVault()
		{
			if (UIManager.TryGetTK(UITKHousingVault.ControlName, out UITKHousingVault vault))
			{
				vault.Open();
			}
		}
	}
}
