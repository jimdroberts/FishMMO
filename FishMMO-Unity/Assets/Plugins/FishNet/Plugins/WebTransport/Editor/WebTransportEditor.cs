using FishNet.Transporting.WebTransport;
using UnityEditor;
using UnityEngine;

namespace FishNet.Transporting.WebTransport.Editor
{
	/// <summary>
	/// Info panel for the WebTransport component.
	/// Most configuration comes from server .cfg files (bind address, port,
	/// max clients, certificate paths) or Constants.GameHost (client address).
	/// The <c>serverTimeout</c> and <c>clientTimeout</c> fields ARE configurable
	/// through the Inspector.
	///
	/// Dual-stack transport:
	///   <see cref="Native.WebTransportNative"/> on Windows/Linux/macOS (standalone + editor).
	///   <see cref="WebGL.WebTransportJSLib"/> on WebGL (browser) builds.
	///   Editor always uses the native library for testing; the WebGL path
	///   is only active in actual browser builds.
	/// </summary>
	[CustomEditor(typeof(WebTransport), true)]
	[CanEditMultipleObjects]
	public class WebTransportEditor : UnityEditor.Editor
	{
		public override void OnInspectorGUI()
		{
			EditorGUILayout.HelpBox(
				"WebTransport (QUIC/HTTP3) — dual-stack transport for FishMMO.\n\n" +
				"Platform backends:\n" +
				"  • Windows/Linux/macOS → Native C library (msquic, P/Invoke)\n" +
				"  • WebGL (browser)      → Browser WebTransport API (JS interop)\n" +
				"  • Unity Editor         → Native library (same as standalone)\n\n" +
				"Server configuration is loaded from .cfg files:\n" +
				"  • Address / Port / MaximumClients\n" +
				"  • CertificatePath / PrivateKeyPath (PEM)\n\n" +
				"Inspector-configurable fields:\n" +
				"  • ServerTimeout / ClientTimeout (idle timeout in seconds)\n\n" +
				"Client address is set from Constants.GameHost.\n\n" +
				"TLS 1.3 is mandatory. MTU is 1200 (RFC 9000).",
				MessageType.Info);

			serializedObject.Update();
			DrawPropertiesExcluding(serializedObject, "m_Script");
			serializedObject.ApplyModifiedProperties();
		}
	}
}