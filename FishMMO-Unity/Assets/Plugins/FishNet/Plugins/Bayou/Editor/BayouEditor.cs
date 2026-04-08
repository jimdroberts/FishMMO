#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace FishNet.Transporting.Bayou
{


    [CustomEditor(typeof(Bayou), true)]
    [CanEditMultipleObjects]
    public class BayouEditor : Editor
    {
        private SerializedProperty _useWss;
        private SerializedProperty _sslConfiguration;
        private SerializedProperty _mtu;
        private SerializedProperty _useProxyProtocol;
        private SerializedProperty _port;
        private SerializedProperty _maximumClients;
        private SerializedProperty _clientAddress;
        protected virtual void OnEnable()
        {
            _useWss = serializedObject.FindProperty(nameof(_useWss));
            _sslConfiguration = serializedObject.FindProperty(nameof(_sslConfiguration));

            _mtu = serializedObject.FindProperty(nameof(_mtu));

            _useProxyProtocol = serializedObject.FindProperty(nameof(_useProxyProtocol));
            _port = serializedObject.FindProperty(nameof(_port));
            _maximumClients = serializedObject.FindProperty(nameof(_maximumClients));

            _clientAddress = serializedObject.FindProperty(nameof(_clientAddress));
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            GUI.enabled = false;
            EditorGUILayout.ObjectField("Script:", MonoScript.FromMonoBehaviour((Bayou)target), typeof(Bayou), false);
            GUI.enabled = true;
            
            EditorGUILayout.LabelField("Security", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(_useWss);
            if (_useWss.boolValue == true)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_sslConfiguration, new GUIContent("SSL Configuration"));
                EditorGUI.indentLevel--;
            }
            EditorGUI.indentLevel--;
            EditorGUILayout.Space();

            EditorGUILayout.LabelField("Channels", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(_mtu);
            EditorGUI.indentLevel--;
            EditorGUILayout.Space();

            EditorGUILayout.LabelField("Server", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(_useProxyProtocol, new GUIContent("Use Proxy Protocol", "When true, expects a PROXY protocol header from the reverse proxy before the WebSocket handshake."));
            EditorGUILayout.PropertyField(_port);
            EditorGUILayout.PropertyField(_maximumClients);
            EditorGUI.indentLevel--;
            EditorGUILayout.Space();

            EditorGUILayout.LabelField("Client", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(_clientAddress);
            EditorGUI.indentLevel--;



            serializedObject.ApplyModifiedProperties();
        }

    }
}
#endif