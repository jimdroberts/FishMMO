using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using FishNet.Transporting.WebTransport;
using FishNet.Transporting.WebTransport.Native;
using NUnit.Framework;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Pins the contracts of the transport's traffic counters that no behavioural test can reach
	/// without a live connection: the managed/native struct layouts and ABI pair, where the sockets
	/// count, that a server hop never resets the totals, and that the per-packet logging stays out
	/// of release builds.
	/// </summary>
	/// <remarks>
	/// Source scans, like their neighbours: they see what was written, not what runs. The native
	/// source lives in the sibling FishMMO-WebTransport checkout; the pins that read it are ignored
	/// when it is absent.
	/// </remarks>
	[TestFixture]
	public class TransportTrafficPinsTests
	{
		private const string PluginRoot = "Assets/Plugins/FishNet/Plugins/WebTransport/";
		private const string ClientSocketPath = PluginRoot + "Core/ClientSocket.cs";
		private const string ServerSocketPath = PluginRoot + "Core/ServerSocket.cs";
		private const string NativeApiHeader = "../FishMMO-WebTransport/src/webtransport_api.h";
		private const string NativeApiSource = "../FishMMO-WebTransport/src/webtransport_api.cpp";

		private static string ReadSource(string relativePath)
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), relativePath);
			LogAssert.IsTrue(File.Exists(path), $"{relativePath} not found at {path}.");
			return File.ReadAllText(path).Replace("\r\n", "\n");
		}

		private static string ReadNativeOrIgnore(string relativePath)
		{
			string path = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), relativePath));
			if (!File.Exists(path))
				Assert.Ignore($"The native source is not checked out beside this project ({path}).");
			return File.ReadAllText(path).Replace("\r\n", "\n");
		}

		/// <summary>The source with comment lines removed, so prose about a construct does not trip a scan for it.</summary>
		private static string CodeOnly(string source)
		{
			var code = new StringBuilder(source.Length);
			foreach (string line in source.Split('\n'))
			{
				string trimmed = line.TrimStart();
				if (trimmed.StartsWith("//", StringComparison.Ordinal) ||
					trimmed.StartsWith("/*", StringComparison.Ordinal) ||
					trimmed.StartsWith("*", StringComparison.Ordinal))
				{
					continue;
				}
				code.Append(line).Append('\n');
			}
			return code.ToString();
		}

		/// <summary>The body of the method declared by <paramref name="signature"/>, brace-matched.</summary>
		private static string MethodBody(string source, string signature)
		{
			int at = source.IndexOf(signature, StringComparison.Ordinal);
			LogAssert.IsTrue(at >= 0, $"the source must still declare {signature}");

			int open = source.IndexOf('{', at);
			LogAssert.IsTrue(open > at, $"the body of {signature} must be locatable");

			int depth = 0;
			for (int i = open; i < source.Length; ++i)
			{
				if (source[i] == '{')
				{
					++depth;
				}
				else if (source[i] == '}' && --depth == 0)
				{
					return source.Substring(open, i - open + 1);
				}
			}
			LogAssert.Fail($"the body of {signature} never closes");
			return string.Empty;
		}

		// ── Managed/native contract ─────────────────────────────────────

		[Test]
		public void StatisticsStructs_HaveTheNativeLayout()
		{
			/* A field added on one side only would shift every field after it: RTT read as bytes,
			 * bytes as packets. The native side static_asserts the same sizes. */
			LogAssert.AreEqual(WebTransportNative.WtGlobalCounters.NativeSize, Marshal.SizeOf<WebTransportNative.WtGlobalCounters>());
			LogAssert.AreEqual(160, WebTransportNative.WtGlobalCounters.NativeSize);
			LogAssert.AreEqual(WebTransportNative.WtConnectionStats.NativeSize, Marshal.SizeOf<WebTransportNative.WtConnectionStats>());
			LogAssert.AreEqual(152, WebTransportNative.WtConnectionStats.NativeSize);
			LogAssert.AreEqual(8L, (long)Marshal.OffsetOf<WebTransportNative.WtGlobalCounters>(nameof(WebTransportNative.WtGlobalCounters.UdpSendDatagrams)));
			LogAssert.AreEqual(48L, (long)Marshal.OffsetOf<WebTransportNative.WtConnectionStats>(nameof(WebTransportNative.WtConnectionStats.SendPackets)),
				"the uint64 block starts after twelve uint32 fields, with no padding");
		}

		[Test]
		public void NativeSource_AssertsTheSameSizes()
		{
			string source = ReadNativeOrIgnore(NativeApiSource);
			LogAssert.IsTrue(Regex.IsMatch(source, @"static_assert\(sizeof\(wt_global_counters_t\)\s*==\s*" + WebTransportNative.WtGlobalCounters.NativeSize + @"\b"),
				"webtransport_api.cpp must static_assert wt_global_counters_t at the managed NativeSize");
			LogAssert.IsTrue(Regex.IsMatch(source, @"static_assert\(sizeof\(wt_connection_stats_t\)\s*==\s*" + WebTransportNative.WtConnectionStats.NativeSize + @"\b"),
				"webtransport_api.cpp must static_assert wt_connection_stats_t at the managed NativeSize");
		}

		[Test]
		public void AbiVersion_MatchesTheNativeHeader()
		{
			/* The same pair tests/check_abi_version.sh checks, run with the rest of the suite. */
			string header = ReadNativeOrIgnore(NativeApiHeader);
			Match m = Regex.Match(header, @"#define\s+WT_ABI_VERSION\s+(\d+)");
			LogAssert.IsTrue(m.Success, "webtransport_api.h must define WT_ABI_VERSION");
			LogAssert.AreEqual(int.Parse(m.Groups[1].Value), WebTransportNative.ExpectedAbiVersion);
		}

		// ── Where the sockets count ─────────────────────────────────────

		[Test]
		public void ClientHop_NeverResetsTheTrafficTotals()
		{
			/* StartConnection runs on every login → world → scene hop and resets the per-session
			 * handshake diagnostics. The traffic statistics must survive it. */
			string body = MethodBody(CodeOnly(ReadSource(ClientSocketPath)), "internal bool StartConnection(string address, ushort port, bool useTls)");
			LogAssert.IsFalse(body.Contains("TransportTraffic"), "StartConnection must not touch the process-lifetime traffic totals");
		}

		[Test]
		public void ClientCountsEverySuccessfulSend_OnBothBackends()
		{
			string body = MethodBody(CodeOnly(ReadSource(ClientSocketPath)), "private void DequeueOutgoing()");
			LogAssert.AreEqual(2, Regex.Matches(body, @"TransportTrafficCounters\.CountSent\(pkt\.Channel, pkt\.Length\)").Count,
				"the browser and the native send paths each count what they hand to the transport");
		}

		[Test]
		public void ClientCountsEveryReceive_OnBothBackends()
		{
			string source = CodeOnly(ReadSource(ClientSocketPath));
			foreach (string signature in new[]
			{
				"private void HandleNativeStreamData(",
				"private void HandleNativeDatagram(",
				"private static void WebGlOnStream(",
				"private static void WebGlOnDatagram(",
			})
			{
				string body = MethodBody(source, signature);
				LogAssert.IsTrue(body.Contains("TransportTrafficCounters.CountReceived("), $"{signature} must count what it receives");
				LogAssert.IsTrue(body.IndexOf("TransportTrafficCounters.CountReceived(", StringComparison.Ordinal) <
					body.IndexOf("incomingEventCount", StringComparison.Ordinal),
					$"{signature} counts on arrival, before the event queue can drop the message");
			}
		}

		[Test]
		public void ServerCountsInboundBeforeTheRateLimiter()
		{
			/* A flooder's refused messages still crossed the wire and still cost bandwidth. */
			string source = CodeOnly(ReadSource(ServerSocketPath));
			foreach (string signature in new[] { "private void HandleNativeStreamData(", "private void HandleNativeDatagram(" })
			{
				string body = MethodBody(source, signature);
				int count = body.IndexOf("TransportTrafficCounters.CountReceived(", StringComparison.Ordinal);
				int admit = body.IndexOf("TryAdmitInbound(", StringComparison.Ordinal);
				LogAssert.IsTrue(count >= 0 && admit > count, $"{signature} must count before TryAdmitInbound");
			}
		}

		[Test]
		public void ServerCountsEachRecipientOfASuccessfulSend()
		{
			string body = MethodBody(CodeOnly(ReadSource(ServerSocketPath)), "private void SendPacketToClient(Packet packet, int connectionId)");
			LogAssert.IsTrue(body.Contains("TransportTrafficCounters.CountSent(packet.Channel, packet.Length)"),
				"a broadcast is paid for once per recipient, so it is counted per recipient");
		}

		[Test]
		public void HotPathCounters_HaveNoTypeInitializer()
		{
			/* The first received message can reach an msquic worker thread before the main thread
			 * has touched the counters, and whatever type initializer runs then runs on that
			 * thread. The sockets copy into unmanaged memory precisely to keep managed allocations
			 * off those threads (IL2CPP), so the classes the receive path touches must have no
			 * initializer to run: value-type statics only, no field initializers. */
			Type counters = typeof(TransportTraffic).Assembly.GetType("FishNet.Transporting.WebTransport.TransportTrafficCounters");
			LogAssert.IsNotNull(counters, "TransportTrafficCounters must still exist in the WebTransport assembly");
			foreach (Type type in new[] { counters, typeof(TransportTrafficMath) })
			{
				LogAssert.IsNull(type.TypeInitializer, $"{type.Name} must have no static constructor or static field initializer");
				foreach (FieldInfo field in type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
				{
					LogAssert.IsTrue(field.IsLiteral || field.FieldType.IsValueType,
						$"{type.Name}.{field.Name} must be a constant or a value type");
				}
			}
		}

		// ── Release-build logging ───────────────────────────────────────

		[Test]
		public void PerPacketLogging_IsCompiledOutOfReleaseBuilds()
		{
			/* The client used to Debug.Log every 50th packet for as long as it stayed connected, in
			 * every build. The handshake trace that replaced it is [Conditional] on development and
			 * editor builds and stops after the first few packets of a session. */
			string raw = ReadSource(ClientSocketPath);
			string code = CodeOnly(raw);
			LogAssert.IsFalse(Regex.IsMatch(code, @"%\s*50\b"), "no periodic per-packet log");
			LogAssert.IsTrue(Regex.IsMatch(raw,
				@"\[System\.Diagnostics\.Conditional\(""DEVELOPMENT_BUILD""\),\s*System\.Diagnostics\.Conditional\(""UNITY_EDITOR""\)\]\s*\n\s*private void TraceHandshakePacket\("),
				"TraceHandshakePacket must stay [Conditional] on DEVELOPMENT_BUILD and UNITY_EDITOR");
			foreach (string signature in new[] { "internal void SendToServer(", "private void DequeueOutgoing()" })
			{
				string body = MethodBody(code, signature);
				LogAssert.IsFalse(Regex.IsMatch(body, @"UnityEngine\.Debug\.Log\(\s*\$""\[FishWT\] (SendToServer QUEUED|WIRE SEND OK)"),
					$"{signature} must trace sends through TraceHandshakePacket, not Debug.Log");
			}
		}
	}
}
