using System;
using System.Collections.Generic;

namespace FishMMO.Client
{
	/// <summary>
	/// Buttons modules add to the bank window's footer: the Housing module's "House vault".
	/// </summary>
	/// <remarks>
	/// The bank's own controls are in its UXML; a module's link is not, so removing the module removes the button with
	/// it. Each button starts hidden: every time the bank opens it calls <see cref="Link.OnBankShown"/> with a setter,
	/// and the module shows the button (now or when it has asked the server) only when there is something behind it.
	/// A module registers once, before the first scene loads (<c>[RuntimeInitializeOnLoadMethod]</c>).
	/// </remarks>
	public static class BankWindowLinks
	{
		/// <summary>One link.</summary>
		public sealed class Link
		{
			/// <summary>Stable id; registering the same id again replaces the link.</summary>
			public string Id;
			/// <summary>The button's text.</summary>
			public string Label;
			/// <summary>Position among the links, lowest first; ties by id.</summary>
			public int Order;
			/// <summary>What the button does.</summary>
			public Action OnClick;
			/// <summary>The bank opened: show or hide the button through the setter, now or later.</summary>
			public Action<Action<bool>> OnBankShown;
		}

		private static readonly List<Link> links = new List<Link>();

		/// <summary>The links in order.</summary>
		public static IReadOnlyList<Link> All => links;

		/// <summary>Adds (or replaces, by id) a link.</summary>
		public static void Register(string id, string label, int order, Action onClick, Action<Action<bool>> onBankShown)
		{
			if (string.IsNullOrEmpty(id) || onClick == null)
			{
				throw new ArgumentException("A bank window link needs an id and an action.");
			}
			links.RemoveAll(l => l.Id == id);
			links.Add(new Link { Id = id, Label = label ?? id, Order = order, OnClick = onClick, OnBankShown = onBankShown });
			links.Sort((a, b) => a.Order != b.Order ? a.Order.CompareTo(b.Order) : string.CompareOrdinal(a.Id, b.Id));
		}

		/// <summary>Removes a link; false when there was none with that id.</summary>
		public static bool Unregister(string id)
		{
			return links.RemoveAll(l => l.Id == id) > 0;
		}
	}
}
