using System.Text;
using FishNet.Transporting;
using UnityEngine.UIElements;
using FishMMO.Shared;

namespace FishMMO.Client
{
	/// <summary>
	/// UI Toolkit implementation of the lore object panel.
	/// Binds to <c>UILore.uxml</c> / <c>UILore.uss</c>.
	/// </summary>
	/// <remarks>
	/// Display only. The server has already granted whatever the lore object gives out by the time
	/// this message arrives — abilities, events and a one-time item grant — so nothing here sends
	/// anything back. The footer names what was granted purely so the player can see that reading
	/// the thing did something.
	/// </remarks>
	public class UITKLore : UITKCharacterControl
	{
		private const string TITLE_NAME = "header-title";
		private const string TEXT_NAME = "lore-text";
		private const string GRANTS_NAME = "lore-grants";
		private const string FOOTER_NAME = "panel-footer";
		private const string CLOSE_BTN_NAME = "close-button";

		private const string CSS_HIDDEN = "lore-hidden";

		/// <summary>Title of the lore currently displayed.</summary>
		private string loreTitle = string.Empty;

		/// <summary>Body text of the lore currently displayed.</summary>
		private string loreText = string.Empty;

		/// <summary>A one-line summary of what this lore object granted, or empty.</summary>
		private string grantSummary = string.Empty;

		/// <inheritdoc />
		public override void OnStarting()
		{
			Button closeBtn = Root?.Q<Button>(CLOSE_BTN_NAME);
			if (closeBtn != null)
			{
				closeBtn.clicked += Hide;
			}
		}

		/// <inheritdoc />
		protected override void OnAfterStarting()
		{
			base.OnAfterStarting();
			ApplyPerOpenContent();
		}

		/// <summary>
		/// Fills the panel on every show.
		/// </summary>
		/// <remarks>
		/// Enabling the document re-clones the UXML, so anything written before <c>Show()</c> is
		/// discarded — and this panel is only ever opened by a server broadcast, which is exactly
		/// the case <c>OnAfterStarting</c> alone does not cover.
		/// </remarks>
		protected override void OnAfterShow()
		{
			ApplyPerOpenContent();
		}

		/// <inheritdoc />
		public override void OnClientSet()
		{
			Client.NetworkManager.ClientManager.RegisterBroadcast<LoreObjectBroadcast>(OnClientLoreObjectBroadcastReceived);
		}

		/// <inheritdoc />
		public override void OnClientUnset()
		{
			Client.NetworkManager.ClientManager.UnregisterBroadcast<LoreObjectBroadcast>(OnClientLoreObjectBroadcastReceived);
		}

		/// <summary>
		/// Shows the lore the server says the player just read.
		/// </summary>
		private void OnClientLoreObjectBroadcastReceived(LoreObjectBroadcast msg, Channel channel)
		{
			LoreObjectTemplate template = LoreObjectTemplate.Get<LoreObjectTemplate>(msg.TemplateID);
			if (template == null)
			{
				/* The client's template cache disagrees with the server's. There is nothing to
				 * show and an empty window would read as a bug, so decline to open. */
				return;
			}

			loreTitle = !string.IsNullOrWhiteSpace(template.Title) ? template.Title : "Lore";
			loreText = template.LoreText ?? string.Empty;
			grantSummary = BuildGrantSummary(template);

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
				title.text = loreTitle;
			}

			Label text = root.Q<Label>(TEXT_NAME);
			if (text != null)
			{
				text.text = loreText;
			}

			Label grants = root.Q<Label>(GRANTS_NAME);
			if (grants != null)
			{
				grants.text = grantSummary;
			}

			VisualElement footer = root.Q(FOOTER_NAME);
			footer?.EnableInClassList(CSS_HIDDEN, string.IsNullOrEmpty(grantSummary));
		}

		/// <summary>
		/// Describes what a lore object hands out, for the footer line.
		/// </summary>
		/// <remarks>
		/// Built from the template rather than from the server message, which carries no grant
		/// list. That is a display-only approximation and is allowed to be: the authoritative
		/// grants have already happened, and the ability broadcasts that accompany them are what
		/// actually update the player's spellbook. It can overstate on a repeat read — the items
		/// are one-time per character and the abilities are skipped if already known — so it is
		/// phrased as what the object offers, not as what was just received.
		/// </remarks>
		private static string BuildGrantSummary(LoreObjectTemplate template)
		{
			int abilities = template.GrantAbilities != null ? template.GrantAbilities.Count : 0;
			int events = template.GrantAbilityEvents != null ? template.GrantAbilityEvents.Count : 0;
			int items = template.GrantItems != null ? template.GrantItems.Count : 0;

			if (abilities < 1 && events < 1 && items < 1)
			{
				return string.Empty;
			}

			StringBuilder builder = new StringBuilder("Teaches: ");
			bool first = true;

			if (abilities > 0)
			{
				builder.Append(abilities).Append(abilities == 1 ? " ability" : " abilities");
				first = false;
			}
			if (events > 0)
			{
				if (!first) builder.Append(", ");
				builder.Append(events).Append(events == 1 ? " ability event" : " ability events");
				first = false;
			}
			if (items > 0)
			{
				if (!first) builder.Append(", ");
				builder.Append(items).Append(items == 1 ? " item" : " items");
			}

			return builder.ToString();
		}
	}
}
