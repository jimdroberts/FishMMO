using System;
using System.Collections;
using HtmlAgilityPack;

namespace FishMMO.Client
{
	/// <summary>
	/// Contract for fetching remote HTML content and extracting the launcher's news fragment
	/// from it.
	/// </summary>
	public interface IHtmlContentFetcher
	{
		/// <summary>
		/// Asynchronously fetches HTML from a URL and extracts the element identified by
		/// <paramref name="divClass"/>.
		/// </summary>
		/// <remarks>
		/// Yields the parsed node rather than formatted text. Formatting is the active view's
		/// responsibility because the two launcher views share no common text format —
		/// TextMeshPro and UI Toolkit disagree on rich-text tag casing and alignment syntax,
		/// and UI Toolkit has no equivalent of TMP's <c>&lt;link&gt;</c> tag at all, so news
		/// links have to become real elements there. See <see cref="ILauncherView.SetNewsContent"/>.
		/// </remarks>
		/// <param name="url">The URL from which to fetch HTML.</param>
		/// <param name="divClass">The CSS class of the div element whose content should be extracted.</param>
		/// <param name="onContentReady">Callback invoked with the extracted node on success.</param>
		/// <param name="onError">Callback invoked with an error message if fetching or parsing fails.</param>
		/// <returns>An IEnumerator for use in a Unity Coroutine.</returns>
		IEnumerator FetchAndExtract(string url, string divClass, Action<HtmlNode> onContentReady, Action<string> onError);
	}
}
