using System;
using UnityEngine;
using FishMMO.Logging;

namespace FishMMO.Client
{
	/// <summary>
	/// Decides whether a link found in launcher news content may be handed to the operating
	/// system's URL handler, and opens it when it may.
	/// </summary>
	/// <remarks>
	/// This is a security boundary, not a formatting concern. The news document is fetched from
	/// <see cref="Shared.Constants.Configuration.LauncherHtmlUrl"/>, an operator-configured
	/// endpoint, so every href in it is untrusted input that ends up at
	/// <see cref="Application.OpenURL"/> — which will happily invoke a registered protocol
	/// handler for schemes like <c>javascript:</c>, <c>file:</c>, or an arbitrary
	/// application-registered scheme.
	/// <para>
	/// It lives here as a single shared implementation because both launcher views render the
	/// same news content through different mechanisms (TextMeshPro's link handler for UGUI,
	/// click callbacks on VisualElements for UI Toolkit). Two copies of an allowlist drift, and
	/// a drifted allowlist is a vulnerability — so neither view is permitted its own.
	/// </para>
	/// </remarks>
	public static class LauncherLinkPolicy
	{
		/// <summary>
		/// Returns true when <paramref name="link"/> is an absolute http or https URI, and
		/// outputs its normalised form.
		/// </summary>
		/// <remarks>
		/// Deliberately strict. An earlier implementation tested whether the string merely
		/// contained "http", which accepted <c>javascript:</c> payloads, local <c>file://</c>
		/// paths, and any custom scheme that happened to embed those four characters
		/// (<c>chrome-http-pwn://…</c>). Parse it properly and check the scheme.
		/// </remarks>
		/// <param name="link">The raw href from the news document.</param>
		/// <param name="safeUrl">The normalised absolute URI when allowed; otherwise null.</param>
		/// <returns>True when the link is safe to open.</returns>
		public static bool TryGetSafeUrl(string link, out string safeUrl)
		{
			safeUrl = null;

			if (string.IsNullOrWhiteSpace(link))
			{
				return false;
			}
			if (!Uri.TryCreate(link, UriKind.Absolute, out Uri uri))
			{
				Log.Warning("LauncherLinkPolicy", $"Refusing to open non-absolute link: {link}");
				return false;
			}
			if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
			{
				Log.Warning("LauncherLinkPolicy", $"Refusing to open link with disallowed scheme '{uri.Scheme}': {link}");
				return false;
			}

			safeUrl = uri.AbsoluteUri;
			return true;
		}

		/// <summary>
		/// Opens <paramref name="link"/> in the default browser when
		/// <see cref="TryGetSafeUrl"/> allows it. Rejected links are logged and ignored.
		/// </summary>
		/// <param name="link">The raw href from the news document.</param>
		public static void OpenIfSafe(string link)
		{
			if (TryGetSafeUrl(link, out string safeUrl))
			{
				Application.OpenURL(safeUrl);
			}
		}
	}
}
