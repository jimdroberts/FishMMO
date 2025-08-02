using System;
using System.Collections;

namespace FishMMO.Client
{
	public interface IHtmlContentFetcher
	{
		/// <summary>
		/// Asynchronously fetches HTML content from a given URL, extracts text from a specified div class,
		/// and processes it.
		/// </summary>
		/// <param name="url">The URL from which to fetch HTML.</param>
		/// <param name="divClass">The CSS class of the div element whose content should be extracted.</param>
		/// <param name="onHtmlReady">Callback invoked with the processed HTML text upon successful extraction.</param>
		/// <param name="onError">Callback invoked with an error message if fetching or processing fails.</param>
		/// <returns>An IEnumerator for use in a Unity Coroutine.</returns>
		public abstract IEnumerator FetchAndProcessHtml(string url, string divClass, Action<string> onHtmlReady, Action<string> onError);
	}
}