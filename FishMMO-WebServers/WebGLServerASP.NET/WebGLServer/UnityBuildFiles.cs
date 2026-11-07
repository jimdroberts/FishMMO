using System.Collections.Concurrent;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Net.Http.Headers;

namespace FishMMO.WebServer
{
	/// <summary>
	/// Serves Unity's pre-compressed web build files the way the Unity loader expects: as stored on
	/// disk, with the <c>Content-Encoding</c> they already carry and their real <c>Content-Length</c>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Unity compresses the build when it is made (gzip or Brotli). With "decompression fallback" the
	/// files are named <c>*.unityweb</c> and the name does not say which; otherwise they end in
	/// <c>.gz</c> or <c>.br</c>. Sent with the matching header, the browser decompresses them natively;
	/// sent without one, the loader decompresses them itself in JavaScript, much more slowly.
	/// </para>
	/// <para>
	/// They must never be compressed again on the fly. That was the old behaviour (every
	/// <c>application/octet-stream</c> response went through response compression): the 900 MB data
	/// file was Brotli-compressed a second time on every download, at a large CPU cost for nothing, and
	/// sent chunked, without a <c>Content-Length</c>, so the loader could not measure progress and the
	/// page showed no download bar. Response compression skips a response that already has a
	/// <c>Content-Encoding</c>, so labelling the file here also keeps it out of that path.
	/// </para>
	/// </remarks>
	internal static class UnityBuildFiles
	{
		private const string GzipEncoding = "gzip";
		private const string BrotliEncoding = "br";

		/// <summary>The marker Unity writes into the head of a Brotli-compressed build file (see the loader's hasUnityMarker).</summary>
		private static readonly byte[] BrotliMarker = Encoding.ASCII.GetBytes("UnityWeb Compressed Content (brotli)");

		/// <summary>Bytes read to classify a <c>.unityweb</c> file: the Brotli marker sits within the first few dozen.</summary>
		private const int SniffLength = 128;

		/// <summary>Detected encodings, by physical path, size and modification time (a rebuilt file is classified again).</summary>
		private static readonly ConcurrentDictionary<(string Path, long Length, DateTimeOffset Modified), string?> Detected = new();

		/// <summary>
		/// Labels a static-file response for a pre-compressed Unity build file. Call from
		/// <c>StaticFileOptions.OnPrepareResponse</c>, which runs before any of the body is written.
		/// </summary>
		public static void PrepareResponse(HttpContext context, IFileInfo file)
		{
			string name = file.Name;
			string? encoding = StoredEncoding(file, name);
			if (encoding == null) return;

			context.Response.Headers.Append(HeaderNames.Vary, HeaderNames.AcceptEncoding);
			if (!Accepts(context.Request, encoding))
			{
				// Sent as stored, unlabelled: the loader's decompression fallback (for .unityweb) unpacks it.
				return;
			}
			context.Response.Headers[HeaderNames.ContentEncoding] = encoding;
			string? type = DecodedContentType(name);
			if (type != null)
			{
				// application/wasm on the decoded stream lets the browser compile while it downloads.
				context.Response.ContentType = type;
			}
		}

		/// <summary>The encoding a build file is stored in, or null when it is not a pre-compressed build file.</summary>
		internal static string? StoredEncoding(IFileInfo file, string name)
		{
			if (name.EndsWith(".gz", StringComparison.OrdinalIgnoreCase)) return GzipEncoding;
			if (name.EndsWith(".br", StringComparison.OrdinalIgnoreCase)) return BrotliEncoding;
			if (!name.EndsWith(".unityweb", StringComparison.OrdinalIgnoreCase) || file.PhysicalPath == null) return null;

			return Detected.GetOrAdd((file.PhysicalPath, file.Length, file.LastModified), key => Sniff(key.Path));
		}

		/// <summary>Reads the head of a <c>.unityweb</c> file: gzip by its signature, Brotli by Unity's marker.</summary>
		internal static string? Sniff(string physicalPath)
		{
			byte[] head = new byte[SniffLength];
			int read;
			using (var stream = new FileStream(physicalPath, FileMode.Open, FileAccess.Read, FileShare.Read))
			{
				read = stream.ReadAtLeast(head, SniffLength, throwOnEndOfStream: false);
			}
			if (read >= 2 && head[0] == 0x1f && head[1] == 0x8b) return GzipEncoding;
			if (head.AsSpan(0, read).IndexOf(BrotliMarker) >= 0) return BrotliEncoding;
			return null;
		}

		/// <summary>The type of the decoded content, from the name without its compression suffix.</summary>
		internal static string? DecodedContentType(string name)
		{
			string inner = Path.GetFileNameWithoutExtension(name);
			if (inner.EndsWith(".wasm", StringComparison.OrdinalIgnoreCase)) return "application/wasm";
			if (inner.EndsWith(".js", StringComparison.OrdinalIgnoreCase)) return "application/javascript";
			if (inner.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) return "application/json";
			if (inner.EndsWith(".data", StringComparison.OrdinalIgnoreCase)) return "application/octet-stream";
			return null;
		}

		/// <summary>True when the request's Accept-Encoding admits <paramref name="encoding"/> (q=0 refuses it).</summary>
		private static bool Accepts(HttpRequest request, string encoding)
		{
			if (!StringWithQualityHeaderValue.TryParseList(request.Headers.AcceptEncoding, out var accepted)) return false;
			foreach (var value in accepted)
			{
				if ((value.Value.Equals(encoding, StringComparison.OrdinalIgnoreCase) || value.Value.Equals("*", StringComparison.Ordinal)) &&
					(value.Quality ?? 1) > 0)
				{
					return true;
				}
			}
			return false;
		}
	}
}
