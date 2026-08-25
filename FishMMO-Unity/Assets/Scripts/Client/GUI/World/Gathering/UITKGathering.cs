using FishNet.Transporting;
using UnityEngine;
using UnityEngine.UIElements;
using FishMMO.Shared;

namespace FishMMO.Client
{
	/// <summary>
	/// UI Toolkit implementation of the gathering progress bar.
	/// Binds to <c>UIGathering.uxml</c> / <c>UIGathering.uss</c>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Purely cosmetic, and honest about it. The server grants the gathered item the moment it
	/// handles the interaction — it does not wait for this bar — so the bar is showing the flavour
	/// of an action that has already resolved. It is not a progress indicator the outcome depends
	/// on, and nothing here can cancel or influence the harvest.
	/// </para>
	/// <para>
	/// That also means it must never gate input or block the player: it opens, runs for the
	/// template's gather time, and closes itself.
	/// </para>
	/// </remarks>
	public class UITKGathering : UITKCharacterControl
	{
		private const string LABEL_NAME = "gather-label";
		private const string FILL_NAME = "gather-bar-fill";

		/// <summary>Seconds the current bar runs for.</summary>
		private float duration;

		/// <summary>Seconds elapsed since the bar opened.</summary>
		private float elapsed;

		/// <summary>Name of the node being gathered.</summary>
		private string nodeName = string.Empty;

		/// <inheritdoc />
		protected override void OnAfterStarting()
		{
			base.OnAfterStarting();
			ApplyPerOpenContent();
		}

		/// <inheritdoc />
		protected override void OnAfterShow()
		{
			ApplyPerOpenContent();
		}

		/// <inheritdoc />
		public override void OnClientSet()
		{
			Client.NetworkManager.ClientManager.RegisterBroadcast<GatheringNodeBroadcast>(OnClientGatheringNodeBroadcastReceived);
		}

		/// <inheritdoc />
		public override void OnClientUnset()
		{
			Client.NetworkManager.ClientManager.UnregisterBroadcast<GatheringNodeBroadcast>(OnClientGatheringNodeBroadcastReceived);
		}

		/// <summary>
		/// Starts the bar for one harvest.
		/// </summary>
		private void OnClientGatheringNodeBroadcastReceived(GatheringNodeBroadcast msg, Channel channel)
		{
			GatheringNodeTemplate template = GatheringNodeTemplate.Get<GatheringNodeTemplate>(msg.TemplateID);
			nodeName = template != null ? template.Name : "Gathering";

			/* Trust the message's duration over the template's. They are normally the same, but
			 * the server is what decides how long the action took and a content hot-fix could
			 * leave the two disagreeing — in which case the bar should match the server. */
			duration = Mathf.Max(0.1f, msg.GatherTimeSeconds);
			elapsed = 0.0f;

			if (!Visible)
			{
				Show();
			}
			else
			{
				ApplyPerOpenContent();
			}
		}

		/// <summary>
		/// Advances the bar and closes it when it fills.
		/// </summary>
		protected override void OnTick()
		{
			if (!Visible)
			{
				return;
			}

			elapsed += Time.deltaTime;
			if (elapsed >= duration)
			{
				Hide();
				return;
			}

			ApplyFill();
		}

		/// <summary>
		/// Writes everything that has to survive the visual tree being re-cloned.
		/// </summary>
		private void ApplyPerOpenContent()
		{
			Label label = Root?.Q<Label>(LABEL_NAME);
			if (label != null)
			{
				label.text = nodeName;
			}

			ApplyFill();
		}

		/// <summary>
		/// Sets the fill width from the elapsed fraction.
		/// </summary>
		private void ApplyFill()
		{
			VisualElement fill = Root?.Q(FILL_NAME);
			if (fill == null)
			{
				return;
			}

			float fraction = duration > 0.0f ? Mathf.Clamp01(elapsed / duration) : 1.0f;
			fill.style.width = Length.Percent(fraction * 100.0f);
		}
	}
}
