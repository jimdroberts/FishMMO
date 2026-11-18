#if UNITY_EDITOR
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace FishMMO.Shared.CustomBuildTool.Config
{
    /// <summary>All active CFG keys are rendered; comments remain the source of tooltips.</summary>
    public static class ServerCfgEditorView
    {
        public static VisualElement Create(ServerCfgDocument document, bool production, Action<string> report, Action reload)
        {
            var root = new VisualElement { name = "server-cfg-editor" };
            var status = new HelpBox("Changes save automatically to this environment's setup CFG. Restart servers after applying CFGs to a build.", HelpBoxMessageType.Info);
            root.Add(status);
            void Persist()
            {
                try
                {
                    document.Save(production);
                    status.text = "Saved " + Path.GetFileName(document.PathName) + ". Next builds use these settings.";
                    status.messageType = HelpBoxMessageType.Info;
                    report(status.text);
                }
                catch (Exception ex)
                {
                    status.text = "Not saved: " + ex.Message;
                    status.messageType = HelpBoxMessageType.Error;
                    report(status.text);
                }
            }
            void Update(string key, string value)
            {
                try { document.Set(key, value); Persist(); }
                catch (Exception ex) { status.text = "Not saved: " + ex.Message; status.messageType = HelpBoxMessageType.Error; report(status.text); }
            }

            if (Path.GetFileName(document.PathName).Equals("LoginServer.cfg", StringComparison.OrdinalIgnoreCase))
            {
                bool.TryParse(document.Get("AutoVerifyAccounts", "false"), out bool enabled);
                var fast = new Toggle("Fast registration: auto-verify accounts") { name = "server-cfg-AutoVerifyAccounts" };
                fast.SetValueWithoutNotify(enabled);
                fast.tooltip = "Uses AutoVerifyAccounts. Skips registration verification in editor/development builds. It does not create accounts when Login is clicked.";
                fast.SetEnabled(!production || enabled); // An invalid production value can still be switched off.
                fast.RegisterValueChangedCallback(evt =>
                {
                    if (production && evt.newValue) { fast.SetValueWithoutNotify(false); return; }
                    Update("AutoVerifyAccounts", evt.newValue ? "true" : "false");
                    if (production && !evt.newValue) fast.SetEnabled(false);
                });
                root.Add(fast);
                root.Add(new Label(production ? "Auto-verification is unavailable in Production builds." :
                    "AutoVerifyAccounts skips verification for registrations; account creation still uses Register.") { style = { whiteSpace = WhiteSpace.Normal } });
            }

            var search = new ToolbarSearchField { name = "server-cfg-search" };
            root.Add(search);
            var settings = new VisualElement();
            root.Add(settings);
            foreach (string key in document.Keys.ToArray())
            {
                if (key.Equals("AutoVerifyAccounts", StringComparison.OrdinalIgnoreCase) && Path.GetFileName(document.PathName).Equals("LoginServer.cfg", StringComparison.OrdinalIgnoreCase)) continue;
                var row = new VisualElement { name = "server-cfg-row-" + key };
                row.style.flexDirection = FlexDirection.Row;
                string value = document.Get(key);
                VisualElement input;
                if (!IsText(key) && bool.TryParse(value, out bool boolean))
                {
                    var field = new Toggle(key);
                    field.SetValueWithoutNotify(boolean);
                    field.RegisterValueChangedCallback(evt => Update(key, evt.newValue ? "true" : "false"));
                    input = field;
                }
                else if (!IsText(key) && long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long integer))
                {
                    var field = new LongField(key) { isDelayed = true };
                    field.SetValueWithoutNotify(integer);
                    field.RegisterValueChangedCallback(evt => Update(key, evt.newValue.ToString(CultureInfo.InvariantCulture)));
                    input = field;
                }
                else if (!IsText(key) && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
                {
                    var field = new DoubleField(key) { isDelayed = true };
                    field.SetValueWithoutNotify(number);
                    field.RegisterValueChangedCallback(evt => Update(key, evt.newValue.ToString("R", CultureInfo.InvariantCulture)));
                    input = field;
                }
                else
                {
                    var field = new TextField(key) { isDelayed = true, isPasswordField = IsSecret(key) };
                    field.SetValueWithoutNotify(value);
                    field.RegisterValueChangedCallback(evt => Update(key, evt.newValue));
                    input = field;
                }
                input.name = "server-cfg-" + key;
                input.tooltip = document.Description(key);
                input.style.flexGrow = 1;
                row.Add(input);
                var remove = new Button(() => { document.Remove(key); Persist(); reload(); }) { text = "Remove" };
                remove.SetEnabled(!key.Equals("ServerName", StringComparison.OrdinalIgnoreCase) && !key.Equals("Port", StringComparison.OrdinalIgnoreCase));
                row.Add(remove);
                settings.Add(row);
            }
            search.RegisterValueChangedCallback(evt =>
            {
                foreach (var row in settings.Children())
                    row.style.display = row.name.IndexOf(evt.newValue ?? "", StringComparison.OrdinalIgnoreCase) >= 0 ? DisplayStyle.Flex : DisplayStyle.None;
            });

            var advanced = new Foldout { text = "Add setting", value = false };
            var newKey = new TextField("Key");
            var newValue = new TextField("Value");
            newKey.RegisterValueChangedCallback(evt => newValue.isPasswordField = IsSecret(evt.newValue));
            advanced.Add(newKey); advanced.Add(newValue);
            advanced.Add(new Button(() =>
            {
                try
                {
                    if (document.Keys.Contains(newKey.value, StringComparer.OrdinalIgnoreCase)) throw new InvalidOperationException("This setting already exists.");
                    document.Set(newKey.value, newValue.value ?? ""); document.Save(production); reload();
                }
                catch (Exception ex) { status.text = "Not saved: " + ex.Message; status.messageType = HelpBoxMessageType.Error; }
            }) { text = "Add and save" });
            root.Add(advanced);
            var actions = new VisualElement(); actions.style.flexDirection = FlexDirection.Row;
            actions.Add(new Button(Persist) { text = "Save changes" });
            actions.Add(new Button(reload) { text = "Reload from disk" });
            root.Add(actions);
            return root;
        }

        private static bool IsText(string key) => IsSecret(key) || new[] { "Name", "Host", "Address", "Path", "Programs", "Origins" }.Any(part => key.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0);

        private static bool IsSecret(string key) => !string.IsNullOrEmpty(key) &&
            (key.IndexOf("Password", StringComparison.OrdinalIgnoreCase) >= 0 || key.IndexOf("Secret", StringComparison.OrdinalIgnoreCase) >= 0 || key.IndexOf("HmacKey", StringComparison.OrdinalIgnoreCase) >= 0);
    }
}
#endif
