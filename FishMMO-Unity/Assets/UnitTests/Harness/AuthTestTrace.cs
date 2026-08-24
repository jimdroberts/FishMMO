// Nullable annotations without null-state analysis. These harness types use `T?` to document
// which references are legitimately absent — a paired core that has not been attached, a captured
// payload that never arrived — and Unity compiles this assembly with the nullable context off, so
// every one of those annotations raised CS8632. Enabling `annotations` alone turns the annotations
// on without switching on flow analysis, which would bury the real warnings in this assembly under
// hundreds of new ones for code that was never written against it.
#nullable enable annotations

using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using FishMMO.Logging;
using UnityEngine;

namespace FishMMO.UnitTests.Harness
{
	/// <summary>
	/// Trace gateway used by the test harness components. When <see cref="Verbose"/> is true,
	/// every routed handshake / SRP / broadcast event is logged with a monotonically increasing
	/// step counter so the full server↔client conversation can be reconstructed from the console.
	/// </summary>
	internal static class AuthTestTrace
	{
		/// <summary>When true, harness components emit per-event Debug.Log lines.</summary>
		public static bool Verbose;

		private static int step;

		public static void Reset() => step = 0;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static async Task Log(string component, string evt, string? detail = null)
		{
			if (!Verbose) return;
			int s = System.Threading.Interlocked.Increment(ref step);
			string msg = detail is null
				? $"[AuthTrace {s:000}] {component} :: {evt}"
				: $"[AuthTrace {s:000}] {component} :: {evt}  | {detail}";
			await FishMMO.Logging.Log.WritePartsToConsole(
				LogLevel.Debug,
				component,
				0,
				("grey", msg)
			);
		}

		/// <summary>
		/// Emits a colorized, descriptive log at the START of a test.
		/// </summary>
		public static async Task LogTestStart(string testName, string description)
		{
			await FishMMO.Logging.Log.WritePartsToConsole(
				LogLevel.Info,
				"UnitTest",
				0,
				("cyan", "========== "),
				("yellow", $"START TEST: "),
				("white", testName),
				("cyan", " =========="),
				("grey", $"\n{description}")
			);
		}

		/// <summary>
		/// Emits a colorized, descriptive log at the END of a test.
		/// </summary>
		public static async Task LogTestEnd(string testName)
		{
			await FishMMO.Logging.Log.WritePartsToConsole(
				LogLevel.Info,
				"UnitTest",
				0,
				("cyan", "========== "),
				("green", $"END TEST: "),
				("white", testName),
				("cyan", " ==========\n")
			);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static string Hex(byte[]? bytes, int max = 8)
		{
			if (bytes is null) return "<null>";
			if (bytes.Length == 0) return "<empty>";
			int n = Math.Min(bytes.Length, max);
			char[] c = new char[n * 2];
			const string h = "0123456789abcdef";
			for (int i = 0; i < n; i++)
			{
				c[i * 2] = h[bytes[i] >> 4];
				c[i * 2 + 1] = h[bytes[i] & 0xF];
			}
			return bytes.Length > max
				? $"{new string(c)}…({bytes.Length}B)"
				: $"{new string(c)}({bytes.Length}B)";
		}
	}
}