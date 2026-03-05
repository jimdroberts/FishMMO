using TMPro;
using UnityEngine;
using UnityEngine.UI;
using FishMMO.Shared;

namespace FishMMO.Client
{
	/// <summary>
	/// Button component representing a single scene channel in the channel selection UI.
	/// Displays channel number and player count, and fires a selection event on click.
	/// </summary>
	public class ChannelDetailsButton : MonoBehaviour
	{
		/// <summary>Button component for user interaction.</summary>
		public Button ChannelButton;
		/// <summary>Label displaying the channel name or number.</summary>
		public TMP_Text ChannelLabel;
		/// <summary>Label displaying the current player count.</summary>
		public TMP_Text PlayerCountLabel;

		/// <summary>The channel address data associated with this button.</summary>
		public ChannelAddress Channel;

		/// <summary>Delegate type for channel selection events.</summary>
		public delegate void ChannelSelectEvent(ChannelDetailsButton button);
		/// <summary>Fired when this channel button is selected by the user.</summary>
		public event ChannelSelectEvent OnChannelSelected;

		private Color defaultLabelColor;

		/// <summary>
		/// Initializes the button with channel data and display index.
		/// </summary>
		/// <param name="channel">Channel address data to associate with this button.</param>
		/// <param name="index">Zero-based index used for display numbering.</param>
		public void Initialize(ChannelAddress channel, int index)
		{
			Channel = channel;
			ChannelLabel.text = $"Channel {index + 1}";
			PlayerCountLabel.text = channel.CharacterCount.ToString();
			defaultLabelColor = ChannelLabel.color;
			gameObject.SetActive(true);
		}

		/// <summary>
		/// Called when the channel button is clicked. Fires the selection event.
		/// </summary>
		public void OnClick_ChannelButton()
		{
			OnChannelSelected?.Invoke(this);
		}

		/// <summary>
		/// Resets the label colors to their default values.
		/// </summary>
		public void ResetLabelColor()
		{
			ChannelLabel.color = defaultLabelColor;
			PlayerCountLabel.color = defaultLabelColor;
		}

		/// <summary>
		/// Sets the label colors to the specified color, saving the current color as default.
		/// </summary>
		/// <param name="color">Color to apply to the labels.</param>
		public void SetLabelColors(Color color)
		{
			defaultLabelColor = ChannelLabel.color;
			ChannelLabel.color = color;
			PlayerCountLabel.color = color;
		}
	}
}