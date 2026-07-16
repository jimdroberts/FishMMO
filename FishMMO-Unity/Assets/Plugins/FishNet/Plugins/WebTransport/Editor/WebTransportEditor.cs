using FishNet.Transporting.WebTransport;
using UnityEditor;
using UnityEngine;

namespace FishNet.Transporting.WebTransport.Editor
{
    /// <summary>
    /// Custom Unity Inspector for the WebTransport component.
    /// Groups serialized fields into logical sections, mirroring BayouEditor.
    /// </summary>
    [CustomEditor(typeof(WebTransport), true)]
    [CanEditMultipleObjects]
    public class WebTransportEditor : UnityEditor.Editor
    {
        private SerializedProperty _useTls;
        private SerializedProperty _mtu;
        private SerializedProperty _serverBindAddress;
        private SerializedProperty _port;
        private SerializedProperty _maximumClients;
        private SerializedProperty _clientAddress;
        private SerializedProperty _certificatePath;
        private SerializedProperty _privateKeyPath;

        private void OnEnable()
        {
            _useTls = serializedObject.FindProperty("_useTls");
            _mtu = serializedObject.FindProperty("_mtu");
            _serverBindAddress = serializedObject.FindProperty("_serverBindAddress");
            _port = serializedObject.FindProperty("_port");
            _maximumClients = serializedObject.FindProperty("_maximumClients");
            _clientAddress = serializedObject.FindProperty("_clientAddress");
            _certificatePath = serializedObject.FindProperty("_certificatePath");
            _privateKeyPath = serializedObject.FindProperty("_privateKeyPath");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            // ── TLS / Security ─────────────────────────────
            EditorGUILayout.LabelField("Security", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_useTls, new GUIContent("Use TLS",
                "Enable TLS 1.3 for QUIC connections. Required when running " +
                "without a TLS-terminating proxy in front."));
            if (_useTls.boolValue)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_certificatePath, new GUIContent("Certificate Path",
                    "Path to TLS certificate in PEM format."));
                EditorGUILayout.PropertyField(_privateKeyPath, new GUIContent("Private Key Path",
                    "Path to TLS private key in PEM format."));
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space();

            // ── Channels ────────────────────────────────────
            EditorGUILayout.LabelField("Channels", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_mtu, new GUIContent("MTU",
                "Maximum transmission unit for unreliable datagrams. " +
                "Affects QUIC datagram frame size."));
            EditorGUILayout.HelpBox(
                "Channel 0 (Reliable)   → WebTransport bidirectional streams\n" +
                "Channel 1 (Unreliable) → QUIC DATAGRAM frames (RFC 9221)",
                MessageType.Info);

            EditorGUILayout.Space();

            // ── Server ──────────────────────────────────────
            EditorGUILayout.LabelField("Server", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_serverBindAddress, new GUIContent("Bind Address",
                "Address the server binds to. 'localhost' for co-located proxy, " +
                "'0.0.0.0' for direct external access."));
            EditorGUILayout.PropertyField(_port, new GUIContent("Port",
                "UDP port for QUIC/WebTransport connections."));
            EditorGUILayout.PropertyField(_maximumClients, new GUIContent("Maximum Clients",
                "Maximum number of concurrent client connections."));

            EditorGUILayout.Space();

            // ── Client ──────────────────────────────────────
            EditorGUILayout.LabelField("Client", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_clientAddress, new GUIContent("Client Address",
                "Address the client connects to. Can include a path for NGINX routing, " +
                "e.g. 'game.fishmmo.com/wt/7770'."));

            serializedObject.ApplyModifiedProperties();
        }
    }
}