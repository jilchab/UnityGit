using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using MS.Shell.Editor;

public class Git : EditorWindow
{
    private int toolbarSelected = 0;
    private string[] toolbarLabels = {"Changes", "Branches"};
    private Vector2 scrollPosition;

    private bool isClean = true;    
    private string commitMsg = "";

    private bool showStaged = true;
    private bool showModified = true;
    public string[] staged = new string[0];
    public string[] modified = new string[0];

    private string[] localBranches = new string[0];

    [MenuItem("Window/Git")]
    public static void ShowWindow()
    {
        GetWindow(typeof(Git));
    }

    public void displayChanges() 
    {
        if (staged.Length > 0) {
            showStaged = EditorGUILayout.Foldout(showStaged, "Staged Changes");
            if (showStaged) {
                foreach (string b in staged) {
                    displayChange(b, true);
                }
            }
        }
        if (modified.Length > 0) {
            showModified = EditorGUILayout.Foldout(showModified, "Changes");
            if (showModified) {
                foreach (string b in modified) {
                    displayChange(b, false);
                }
            }
        }
    }

    private void displayChange(string file, bool staged)
    {
        GUILayout.BeginHorizontal();
        if (GUILayout.Button(staged ? "-" : "+", GUILayout.Width(20))) {
            if (staged) {
                EditorShell.Execute($"git reset HEAD {file}");
            } else {
                EditorShell.Execute($"git add {file}");
            }
            update();
        }
        GUILayout.Label(file);
        GUILayout.EndHorizontal();
    }

    private void update()
    {
        // localBranches = new string[0];

        updateChanges();
        updateLocalBranches();
        Repaint();
    }

    private void OnEnable()
    {
        scrollPosition = Vector2.zero;
        update();
    }

    
    private void OnGUI()
    {
        if (GUILayout.Button("Update")) {
            update();
        }
        GUILayout.Space(10);
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        toolbarSelected = GUILayout.Toolbar(toolbarSelected, toolbarLabels, GUILayout.ExpandWidth(false));
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();


        scrollPosition = GUILayout.BeginScrollView(scrollPosition);

        if (toolbarSelected == 0) {
            if (isClean) {
                GUILayout.Label("No changes");
            } else {
                commitMsg = GUILayout.TextArea(commitMsg);
                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Commit", GUILayout.ExpandWidth(false))) {
                    commit();
                }
                GUILayout.EndHorizontal();
                displayChanges();
            }
            
        } else {
            foreach (string b in localBranches) {
                displayBranch(b.Substring(2), b.StartsWith("* "));
            }
        }
        GUILayout.EndScrollView();
    }

    private void displayBranch(string name, bool current)
    {
        GUILayout.BeginHorizontal();
        if (current) {
            Color c = GUI.color;
            GUI.color = Color.green;
            GUILayout.Label(name);
            GUI.color = c;
            GUI.enabled = false;
            GUILayout.Button(">", GUILayout.Width(20));
            GUI.enabled = true;
        } else {
            GUILayout.Label(name);
            if (GUILayout.Button(">", GUILayout.Width(20))) {
                checkOutBranch(name);
            }
        }
        GUILayout.EndHorizontal();
    }

    private void checkOutBranch(string name)
    {
        var operation = EditorShell.Execute($"git checkout {name}");
        operation.onExit += (int exitCode) => {
            update();
        };
    }

    private async void commit()
    {
        if (staged.Length > 0 && commitMsg != "") {
            string msg = commitMsg.Replace("\"", " ");
            await EditorShell.Execute($"git commit -m \"{msg}\"");
            commitMsg = "";
        }
        update(); // dsf
    }

    private void updateLocalBranches()
    {
        List<string> temp = new List<string>();
    
        var operation = EditorShell.Execute("git branch");
        operation.onLog += (EditorShell.LogType LogType, string log) => {
            temp.Add(log);
        };
        operation.onExit += (int exitCode) => {
            localBranches = temp.ToArray();
        };
    }

    private async void updateChanges()
    {
        List<string> tempModified = new List<string>();
        List<string> tempStaged = new List<string>();
    
        var isCleanReq = EditorShell.Execute("git diff-index --quiet HEAD --");
        isClean = await isCleanReq == 0;

        if (isClean) {
            return;
        }

        var getStaged = EditorShell.Execute("git diff --name-only --cached");
        getStaged.onLog += (EditorShell.LogType LogType, string log) => {
            tempStaged.Add(log);
        };
        getStaged.onExit += (int exitCode) => {
            staged = tempStaged.ToArray();
        };

        var getUntracked = EditorShell.Execute("git ls-files --others --exclude-standard");
        getUntracked.onLog += (EditorShell.LogType LogType, string log) => {
            tempModified.Add(log);
        };
        getStaged.onExit += (int exitCode) => {
            var getModified = EditorShell.Execute("git diff --name-only");
            getModified.onLog += (EditorShell.LogType LogType, string log) => {
                tempModified.Add(log);
            };
            getModified.onExit += (int exitCode) => {
                modified = tempModified.ToArray();
            };
        };
    }
}
