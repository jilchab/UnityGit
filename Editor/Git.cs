using MS.Shell.Editor;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public class Git : EditorWindow
{
    public enum ChangeState {
        Added,
        Modified,
        Deleted,
        Renamed,
        Conflicted,
    };

    class Change {

        public ChangeState state;
        public string name, file;
        public bool isStaged, isSelected;

        // For Renamed changes:
        public string renamedFile;
        public int percentage;

        public static List<Change> Sort(List<Change> changes) {
           return changes.OrderBy(o=>o.file).ToList();
        }

        public Change(string str, bool tracked, bool staged)
        {
            if (!tracked) {
                isStaged = false;
                state = ChangeState.Added;
                file = str;
            } else {
                string[] subs = str.Split('\t');
            
                isStaged = staged;
                file = subs[1];

                switch (subs[0][0]) {
                    case 'D':
                        state = ChangeState.Deleted;
                        break;
                    case 'M':
                        state = ChangeState.Modified;
                        break;
                    case 'A':
                        state = ChangeState.Added;
                        break;
                    case 'R':
                        state = ChangeState.Renamed;
                        renamedFile = file;
                        file = subs[2];
                        percentage = int.Parse(subs[0].Substring(1));
                        break;
                    default:
                        throw new NotImplementedException();
                }
            }
            name = Path.GetFileName(file);
        }

        public async Task Add()
        {
            if (!isStaged) {
                await EditorShell.Execute($"git add {file}");
            }
        }

        public async Task Reset()
        {
            if (isStaged) {
                if (state == ChangeState.Renamed) {
                    await EditorShell.Execute($"git reset HEAD {renamedFile}");
                }
                await EditorShell.Execute($"git reset HEAD {file}");
            }
        }

        public async Task Revert(bool deleteIfUntracked)
        {
            if (state != ChangeState.Added) {
                await EditorShell.Execute($"git checkout HEAD -- {file}");
            } else if (deleteIfUntracked) {
                await EditorShell.Execute($"rm {file}");
            }
        }
        /**
         * TODO:
         *
         * public void Diff()
         * public void Open()
         */
       
    }

    private int toolbarSelected = 0;
    private string[] toolbarLabels = {"Changes", "Branches", "Settings"};
    private Vector2 scrollPosition;
  
    private string commitMsg = "";

    private bool showStaged = true;
    private bool showModified = true;

    private string newBrName = "";
    private string[] localBranches = new string[0];
    private string[] logsList = new string[0];
    

    private bool s_saveOnUpdate = false;
    private bool s_deleteIfUntracked = true;
    private int s_maxLogsDepth = 5;

    Color addedColor = Color.green;
    Color renamedColor = new Color(0.5f, 1f, 0.5f);
    Color modifiedColor = Color.yellow;
    Color deletedColor = new Color(1f, .4f, .4f);

    private List<Change> staged, modified;

    [MenuItem("Window/Git")]
    public static void ShowWindow()
    {
        GetWindow(typeof(Git));
    }

    private void update()
    {
        if (s_saveOnUpdate) {
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        }

        updateChanges();
        updateLocalBranches();
        updateLogs(s_maxLogsDepth);
        Repaint();

    }

    private void OnEnable()
    {
        scrollPosition = Vector2.zero;
        staged = new List<Change>();
        modified = new List<Change>();
        update();
    }

    
    private void OnGUI()
    {
        GUILayout.BeginHorizontal();
        GUILayout.Space(10);
        GUILayout.BeginVertical();
        
        if (GUILayout.Button("Update")) {
            update();
        }

        GUILayout.Space(10);
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        toolbarSelected = GUILayout.Toolbar(toolbarSelected, toolbarLabels, GUILayout.ExpandWidth(false));
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
        GUILayout.Space(20);

        scrollPosition = GUILayout.BeginScrollView(scrollPosition);
        
        if (toolbarSelected == 0) {

            if (staged.Count == 0 && modified.Count == 0) {
                GUILayout.Label("No changes");
            } else {
                GUILayout.Label("Commit Message:", EditorStyles.boldLabel);
                GUILayout.BeginHorizontal();
                commitMsg = GUILayout.TextArea(commitMsg);
                GUILayout.Space(4);
                if (GUILayout.Button("Commit", GUILayout.ExpandWidth(false))) {
                    commit();
                }
                GUILayout.EndHorizontal();
                GUILayout.Space(4);
                displayChanges();
            }
            
        } else if (toolbarSelected == 1) {
            GUILayout.Label("Checkout:", EditorStyles.boldLabel);
            displayNewBranch();
            foreach (string b in localBranches) {
                displayBranch(b.Substring(2), b.StartsWith("* "));
            }
            GUILayout.Space(10);
            GUILayout.Label("Logs:", EditorStyles.boldLabel);
            foreach (string log in logsList) {
                GUILayout.Label(log);
            }
        } else {
            string maxLogsStr = s_maxLogsDepth.ToString();
            GUILayout.BeginHorizontal();
            GUILayout.Label("Auto Save Scene During Update",  GUILayout.Width(Screen.width / 2));
            s_saveOnUpdate = GUILayout.Toggle(s_saveOnUpdate, "", GUILayout.ExpandWidth(false));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Delete on revert when untracked",  GUILayout.Width(Screen.width / 2));
            s_deleteIfUntracked = GUILayout.Toggle(s_deleteIfUntracked, "", GUILayout.ExpandWidth(false));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Max Log Depth", GUILayout.Width(Screen.width / 2));
            maxLogsStr = GUILayout.TextField(maxLogsStr, GUILayout.Width(50));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            maxLogsStr = Regex.Replace(maxLogsStr, "[^0-9]", "");
            if (maxLogsStr != "") {
                s_maxLogsDepth = int.Parse(maxLogsStr);
            } else {
                maxLogsStr = "0";
                s_maxLogsDepth = 0;
            }
            
        }
        GUILayout.EndScrollView();
        GUILayout.EndVertical();
        GUILayout.Space(10);
        GUILayout.EndHorizontal();
    }

    public void displayChanges() 
    {
        if (staged.Count > 0) {
            showStaged = EditorGUILayout.Foldout(showStaged, "Staged Changes");
            if (showStaged) {
                foreach (Change c in staged) {
                    displayChange(c);
                }
            }
        }
        if (modified.Count > 0) {
            showModified = EditorGUILayout.Foldout(showModified, "Changes");
            if (showModified) {
                foreach (Change c in modified) {
                    displayChange(c);
                }
            }
        }
    }

    private async void displayChange(Change c)
    {
        int task = 0;

        GUILayout.BeginHorizontal();
        c.isSelected = GUILayout.Toggle(c.isSelected, "", GUILayout.ExpandWidth(false));
        if (GUILayout.Button(c.isStaged ? "-" : "+", GUILayout.Width(20))) {
            if (c.isStaged) {
                task = 1;
            } else {
                task = 2;
            }
        }
        if (GUILayout.Button("X", GUILayout.Width(20))) {
            task = 3;
        }
        Color oldColor = GUI.color;
        switch (c.state) {
            case ChangeState.Added:
                GUI.color = addedColor;
                GUILayout.Label("A", EditorStyles.boldLabel, GUILayout.ExpandWidth(false));
                break;
            case ChangeState.Renamed:
                GUI.color = renamedColor;
                GUILayout.Label("R", EditorStyles.boldLabel, GUILayout.ExpandWidth(false));
                break;
            case ChangeState.Modified:
                GUI.color = modifiedColor;
                GUILayout.Label("M", EditorStyles.boldLabel, GUILayout.ExpandWidth(false));
                break;
            case ChangeState.Deleted:
                GUI.color = deletedColor;
                GUILayout.Label("D", EditorStyles.boldLabel, GUILayout.ExpandWidth(false));
                break;
            default:
                GUI.color = Color.blue;
                GUILayout.Label("C", GUILayout.ExpandWidth(false));
                break;
        }
        GUI.color = oldColor;
        GUILayout.Label(c.name);
        GUILayout.EndHorizontal();

        if (task == 1) {
            if (c.isSelected) {
                foreach (Change change in c.isStaged ? staged : modified) {
                    if (change.isSelected) {
                        await change.Reset();
                    }
                }
            } else {
                await c.Reset();
            }
        } else if (task == 2) {
            if (c.isSelected) {
                foreach (Change change in c.isStaged ? staged : modified) {
                    if (change.isSelected) {
                        await change.Add();
                    }
                }
            } else {
                await c.Add();
            }
        } else if (task == 3) {
            if (c.isSelected) {
                foreach (Change change in c.isStaged ? staged : modified) {
                    if (change.isSelected) {
                        await change.Revert(s_deleteIfUntracked);
                    }
                }
            } else {
                await c.Revert(s_deleteIfUntracked);
            }
        }

        if (task > 0) {
            update();
        }
    }

    private void displayBranch(string name, bool current)
    {
        Color c = GUI.color;
    
        GUILayout.BeginHorizontal();
        
        if (current) {
            GUILayout.Button(">", GUILayout.Width(20));
            GUI.color = Color.green;
        } else {
            if (GUILayout.Button(">", GUILayout.Width(20))) {
                checkOutBranch(name, false);
            }
        }
        GUILayout.Label(name);
        GUI.color = c;
        GUILayout.EndHorizontal();
    }

    private void displayNewBranch()
    {
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("+", GUILayout.Width(20))) {
            if (newBrName != "") {
               checkOutBranch(newBrName, true);
            }
        }
        GUILayout.Space(5);
        newBrName = GUILayout.TextField(newBrName);
        GUILayout.EndHorizontal();
        GUILayout.Space(10);
    }

    private async void updateChanges()
    {
        List<Change> tempStaged = new List<Change>();
        List<Change> tempModified = new List<Change>();

        var getStaged = EditorShell.Execute("git diff --name-status --cached");
        getStaged.onLog += (EditorShell.LogType LogType, string log) => {
            tempStaged.Add(new Change(log, true, true));
        };
        await getStaged;

        var getUntracked = EditorShell.Execute("git ls-files --others --exclude-standard");
        getUntracked.onLog += (EditorShell.LogType LogType, string log) => {
            if (!log.Contains(" ")) {
                tempModified.Add(new Change(log, false, false));
            }
        };
        await getUntracked;
    
        var getModified = EditorShell.Execute("git diff --name-status");
        getModified.onLog += (EditorShell.LogType LogType, string log) => {
            if (!log.Contains(" ")) {
                tempModified.Add(new Change(log, true, false));
            }
        };
        await getModified;
    
        while (!getStaged.isDone || !getUntracked.isDone || !getModified.isDone);

        staged = Change.Sort(tempStaged);
        modified = Change.Sort(tempModified);
    }

    private void commit()
    {
        if (staged.Count > 0 && commitMsg != "") {
            string msg = commitMsg.Replace("\"", " ");
            var ci =  EditorShell.Execute($"git commit -m \"{msg}\"");
            ci.onExit += (int exitCode) => {
                commitMsg = "";
                update();
            };
        }


    }

    private void checkOutBranch(string name, bool newBranch)
    {
        string cmd = $"git checkout ";

        if (newBranch) {
            cmd += $"-b {name}";
        } else {
            cmd += $"{name}";
        }
        var operation = EditorShell.Execute(cmd);
    }

    private async void updateLocalBranches()
    {
        List<string> temp = new List<string>();
    
        var getLocalBranch = EditorShell.Execute("git branch");
        getLocalBranch.onLog += (EditorShell.LogType LogType, string log) => {
            temp.Add(log);
        };
        getLocalBranch.onExit += (int exitCode) => {
            localBranches = temp.ToArray();
        };
        await getLocalBranch;
    }

    private async void updateLogs(int maxDepth)
    {
        List<string> tempLogs = new List<string>();
        var logs = EditorShell.Execute("git log --pretty=oneline --abbrev-commit");
        logs.onLog += (EditorShell.LogType LogType, string log) => {
            if (tempLogs.Count < maxDepth) {
                tempLogs.Add(log);
            }
        };
        logs.onExit += (int exitCode) => {
            logsList = tempLogs.ToArray();
        };
        await logs;
    }
}
