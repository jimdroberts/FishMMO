#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FishMMO.Shared.CustomBuildTool.Config;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace FishMMO.Shared
{
    public partial class FishMMODashboard
    {
        private readonly Dictionary<string, ServerCfgDocument> serverCfgDocuments = new Dictionary<string, ServerCfgDocument>();
        private string serverCfgBuildFolder;
        private static readonly string[] ServerCfgNames = { "LoginServer.cfg", "WorldServer.cfg", "SceneServer.cfg" };

        private string ServerCfgSetupFolder => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..",
            WorkingEnvironmentOptions.AppendEnvironmentToPath(Constants.Configuration.SetupDirectory)));

        private ServerCfgDocument GetServerCfg(string name)
        {
            string path = Path.Combine(ServerCfgSetupFolder, name);
            if (!serverCfgDocuments.TryGetValue(path, out var doc)) serverCfgDocuments[path] = doc = new ServerCfgDocument(path);
            return doc;
        }

        private bool SaveDashboardServerConfigs()
        {
            try
            {
                bool production = WorkingEnvironmentOptions.GetWorkingEnvironmentState() == WorkingEnvironmentState.Production;
                foreach (string name in ServerCfgNames) GetServerCfg(name).Validate(production);
                foreach (string name in ServerCfgNames) GetServerCfg(name).Save(production);
                return true;
            }
            catch (Exception ex) { SetStatus("Server CFG settings not saved: " + ex.Message); return false; }
        }

        private void AddServerConfigurationInspector()
        {
            bool production = WorkingEnvironmentOptions.GetWorkingEnvironmentState() == WorkingEnvironmentState.Production;
            var section = CreateConstantsSection("Server CFG Setup — " + WorkingEnvironmentOptions.GetWorkingEnvironmentState());
            section.Add(new Label("Edit all Login, World and Scene CFG settings here. Valid changes save automatically to the selected environment's setup templates.")
                { style = { whiteSpace = WhiteSpace.Normal } });
            foreach (string name in ServerCfgNames)
            {
                try
                {
                    var doc = GetServerCfg(name);
                    var view = ServerCfgEditorView.Create(doc, production, SetStatus, () =>
                    {
                        serverCfgDocuments.Remove(doc.PathName);
                        ShowBuildInspector();
                    });
                    var fast = view.Q<Toggle>("server-cfg-AutoVerifyAccounts");
                    if (fast != null)
                    {
                        fast.RemoveFromHierarchy();
                        section.Add(fast);
                        section.Add(new Label("Skips verification for registrations in development builds. Login does not create accounts.")
                            { style = { whiteSpace = WhiteSpace.Normal } });
                    }
                    var foldout = new Foldout { text = name, value = false };
                    foldout.Add(new Label(doc.PathName) { style = { whiteSpace = WhiteSpace.Normal } });
                    foldout.Add(view);
                    section.Add(foldout);
                }
                catch (Exception ex) { section.Add(new HelpBox("Cannot open " + name + ": " + ex.Message, HelpBoxMessageType.Error)); }
            }

            if (string.IsNullOrEmpty(serverCfgBuildFolder))
                serverCfgBuildFolder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Builds", "Server", "Linux Server", "FishMMO GameServer Linux"));
            var destination = new TextField("Built server folder") { value = serverCfgBuildFolder, isDelayed = true };
            destination.RegisterValueChangedCallback(evt => serverCfgBuildFolder = evt.newValue);
            section.Add(destination);
            section.Add(new Button(() =>
            {
                string folder = EditorUtility.OpenFolderPanel("Choose built server folder containing CFG files", serverCfgBuildFolder, "");
                if (!string.IsNullOrEmpty(folder)) { serverCfgBuildFolder = folder; destination.SetValueWithoutNotify(folder); }
            }) { text = "Choose built server folder" });
            section.Add(new Button(() =>
            {
                try
                {
                    if (!SaveDashboardServerConfigs()) return;
                    ServerCfgDocument.CopyToBuild(ServerCfgNames.Select(GetServerCfg), serverCfgBuildFolder, production);
                    SetStatus("CFG files applied. Restart the game servers to load changes; no player rebuild is needed.");
                }
                catch (Exception ex) { SetStatus("CFG files not applied: " + ex.Message); }
            }) { text = "Apply CFGs to existing server build" });
            section.Add(new Label("Applying copies all three CFGs from this environment. Restart servers afterward. No player rebuild is needed for CFG-only edits.")
                { style = { whiteSpace = WhiteSpace.Normal } });
            inspectorContent.Add(section);
        }
    }
}
#endif
