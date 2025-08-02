using TMPro;
using UnityEngine;
using UnityEngine.UI;
using FishMMO.Shared;

namespace FishMMO.Client
{
	public class ServerDetailsButton : MonoBehaviour
	{
		/// <summary>
		/// The button component for selecting the server.
		/// </summary>
		public Button ServerButton;
		/// <summary>
		/// The label displaying the server name and lock status.
		/// </summary>
		public TMP_Text ServerLabel;
		/// <summary>
		/// The label displaying the server's character count.
		/// </summary>
		public TMP_Text ServerStatusLabel;

		/// <summary>
		/// The details of the world server represented by this button.
		/// </summary>
		public WorldServerDetails Details;
		/// <summary>
		/// Delegate for server selection event.
		/// </summary>
		/// <param name="button">The button that was selected.</param>
		public delegate void ServerSelectEvent(ServerDetailsButton button);
		/// <summary>
		/// Event triggered when this server button is selected.
		/// </summary>
		public event ServerSelectEvent OnServerSelected;

		/// <summary>
		/// The color used for the server label (for reset purposes).
		/// </summary>
		private Color labelColor;

		/// <summary>
		/// Initializes the button with server details and sets up labels.
		/// </summary>
		/// <param name="details">The details of the world server.</param>
		public void Initialize(WorldServerDetails details)
		{
			Details = details;
			// Show lock status and server name
			ServerLabel.text = (details.Locked ? "[Locked] " : " ") + details.Name;
			// Show character count
			ServerStatusLabel.text = details.CharacterCount.ToString();
			// Store the original label color for later reset
			labelColor = ServerLabel.color;
			// Ensure the button is active in the UI
			gameObject.SetActive(true);
		}

		/// <summary>
		/// Called when the server button is clicked. Triggers selection event.
		/// </summary>
		public void OnClick_ServerButton()
		{
			OnServerSelected?.Invoke(this);
		}

		/// <summary>
		/// Resets the label colors to their original value.
		/// </summary>
		public void ResetLabelColor()
		{
			ServerLabel.color = labelColor;
			ServerStatusLabel.color = labelColor;
		}

		/// <summary>
		/// Sets the label colors to the specified color and stores the previous color for reset.
		/// </summary>
		/// <param name="color">The color to set for the labels.</param>
		public void SetLabelColors(Color color)
		{
			labelColor = ServerLabel.color;

			ServerLabel.color = color;
			ServerStatusLabel.color = color;
		}
	}
}