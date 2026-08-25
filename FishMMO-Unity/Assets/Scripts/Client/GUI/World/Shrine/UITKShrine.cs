using FishNet.Transporting;
using UnityEngine;
using UnityEngine.UIElements;
using FishMMO.Shared;

namespace FishMMO.Client
{
	/// <summary>
	/// UI Toolkit implementation of the shrine feedback line.
	/// Binds to <c>UIShrine.uxml</c> / <c>UIShrine.uss</c>.
	/// </summary>
	/// <remarks>
	/// Exists because a shrine can now refuse. The cooldown lives on the server and is never
	/// replicated, so the client always believes the shrine is ready and always sends the request;
	/// the server is where the refusal happens, and without something to show for it the player
	/// presses the key and the world does not react.
	/// </remarks>
	public class UITKShrine : UITKCharacterControl
	{
		private const string TITLE_NAME = "shrine-title";
		private const string MESSAGE_NAME = "shrine-message";

		/// <summary>Seconds the line stays on screen.</summary>
		private const float DISPLAY_SECONDS = 3.0f;

		/// <summary>Seconds left before the line hides itself.</summary>
		private float remaining;

		/// <summary>Name of the shrine that answered.</summary>
		private string shrineName = string.Empty;

		/// <summary>What to tell the player.</summary>
		private string message = string.Empty;

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
			Client.NetworkManager.ClientManager.RegisterBroadcast<ShrineBroadcast>(OnClientShrineBroadcastReceived);
		}

		/// <inheritdoc />
		public override void OnClientUnset()
		{
			Client.NetworkManager.ClientManager.UnregisterBroadcast<ShrineBroadcast>(OnClientShrineBroadcastReceived);
		}

		/// <summary>
		/// Shows what the shrine did, or why it did nothing.
		/// </summary>
		private void OnClientShrineBroadcastReceived(ShrineBroadcast msg, Channel channel)
		{
			ShrineTemplate template = ShrineTemplate.Get<ShrineTemplate>(msg.TemplateID);
			shrineName = template != null ? template.Name : "Shrine";
			message = BuildMessage(msg, template);
			remaining = DISPLAY_SECONDS;

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
		/// Turns the server's answer into a line of text.
		/// </summary>
		/// <remarks>
		/// The cooldown is reported as a duration rather than a timestamp because the client has
		/// no synchronised clock to render an absolute time against, and "ready in 4m" answers the
		/// only question the player is asking.
		/// </remarks>
		private static string BuildMessage(ShrineBroadcast msg, ShrineTemplate template)
		{
			if (msg.Success)
			{
				if (template == null)
				{
					return "Restored.";
				}

				if (template.HealHealth && template.HealMana)
				{
					return "Health and mana restored.";
				}
				if (template.HealHealth)
				{
					return "Health restored.";
				}
				if (template.HealMana)
				{
					return "Mana restored.";
				}
				return "Blessed.";
			}

			if (msg.RemainingCooldownSeconds > 0.0f)
			{
				return "Not ready for another " + DescribeDuration(msg.RemainingCooldownSeconds) + ".";
			}

			// The other refusal the server can send is "not while in combat".
			return "Not while in combat.";
		}

		/// <summary>
		/// Renders a duration the way a player reads one.
		/// </summary>
		private static string DescribeDuration(float seconds)
		{
			int total = Mathf.CeilToInt(seconds);
			if (total < 60)
			{
				return total + "s";
			}

			int minutes = total / 60;
			int rest = total % 60;
			return rest > 0 ? minutes + "m " + rest + "s" : minutes + "m";
		}

		/// <summary>
		/// Counts the line down and hides it.
		/// </summary>
		protected override void OnTick()
		{
			if (!Visible)
			{
				return;
			}

			remaining -= Time.deltaTime;
			if (remaining <= 0.0f)
			{
				Hide();
			}
		}

		/// <summary>
		/// Writes everything that has to survive the visual tree being re-cloned.
		/// </summary>
		private void ApplyPerOpenContent()
		{
			VisualElement root = Root;
			if (root == null)
			{
				return;
			}

			Label title = root.Q<Label>(TITLE_NAME);
			if (title != null)
			{
				title.text = shrineName;
			}

			Label body = root.Q<Label>(MESSAGE_NAME);
			if (body != null)
			{
				body.text = message;
			}
		}
	}
}
