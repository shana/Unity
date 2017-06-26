using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace GitHub.Unity
{
    [Serializable]
    class BranchesView : Subview
    {
        private const string ConfirmSwitchTitle = "Confirm branch switch";
        private const string ConfirmSwitchMessage = "Switch branch to {0}?";
        private const string ConfirmSwitchOK = "Switch";
        private const string ConfirmSwitchCancel = "Cancel";
        private const string NewBranchCancelButton = "x";
        private const string NewBranchConfirmButton = "Create";
        private const string FavoritesSetting = "Favorites";
        private const string FavoritesTitle = "Favorites";
        private const string LocalTitle = "LOCAL BRANCHES";
        private const string RemoteTitle = "REMOTE BRANCHES";
        private const string CreateBranchButton = "New Branch";

        [NonSerialized] private int listID = -1;
        [NonSerialized] private BranchesMode targetMode;
        [SerializeField] private Tree treeLocals = new Tree();
        [SerializeField] private Tree treeRemotes = new Tree();

        [SerializeField] private BranchesMode mode = BranchesMode.Default;
        [SerializeField] private string newBranchName;
        [SerializeField] private Vector2 scroll;

        public override void InitializeView(IView parent)
        {
            base.InitializeView(parent);
            targetMode = mode;
            Manager.CacheManager.SetupCache(BranchCache.Instance, Environment.Repository);
        }


        public override void OnShow()
        {
            base.OnShow();

            if (treeLocals == null || !treeLocals.IsInitialized)
            {
                BuildTree(BranchCache.Instance.LocalBranches, BranchCache.Instance.RemoteBranches);
            }

            if (Repository != null)
            {
                Repository.OnLocalBranchListChanged += RunRefreshOnMainThread;
                Repository.OnActiveBranchChanged += HandleRepositoryBranchChangeEvent;
                Repository.OnActiveRemoteChanged += HandleRepositoryBranchChangeEvent;
            }
        }

        public override void OnHide()
        {
            base.OnHide();
            if (Repository != null)
            {
                Repository.OnLocalBranchListChanged -= RunRefreshOnMainThread;
                Repository.OnActiveBranchChanged -= HandleRepositoryBranchChangeEvent;
                Repository.OnActiveRemoteChanged -= HandleRepositoryBranchChangeEvent;
            }
        }

        private void RunRefreshOnMainThread()
        {
            new ActionTask(TaskManager.Token, RefreshBranchList) { Affinity = TaskAffinity.UI }.Start();
        }

        private void HandleRepositoryBranchChangeEvent(string obj)
        {
            RunRefreshOnMainThread();
        }

        public override void Refresh()
        {
            var historyView = ((Window)Parent).HistoryTab;

#if ENABLE_BROADMODE
            if (historyView.BroadMode)
                historyView.Refresh();
            else
#endif
                RefreshBranchList();
        }

        private void RefreshBranchList()
        {
            var localBranches = BranchCache.Instance.LocalBranches;
            localBranches.Sort(CompareBranches);
            var remoteBranches = BranchCache.Instance.RemoteBranches;
            remoteBranches.Sort(CompareBranches);
            BuildTree(localBranches, remoteBranches);
        }

        public override void OnGUI()
        {
            var historyView = ((Window)Parent).HistoryTab;

#if ENABLE_BROADMODE
            if (historyView.BroadMode)
                historyView.OnGUI();
            else
#endif
            {
                OnEmbeddedGUI();

#if ENABLE_BROADMODE
                if (Event.current.type == EventType.Repaint && historyView.EvaluateBroadMode())
                {
                    Refresh();
                }
#endif
            }
        }

        public void OnEmbeddedGUI()
        {
            scroll = GUILayout.BeginScrollView(scroll, false, true);
            {
                listID = GUIUtility.GetControlID(FocusType.Keyboard);

                GUILayout.BeginHorizontal();
                {
                    OnCreateGUI();
                }
                GUILayout.EndHorizontal();

                var rect = GUILayoutUtility.GetLastRect();
                OnTreeGUI(new Rect(0f, rect.height + Styles.CommitAreaPadding, Position.width, Position.height - rect.height + Styles.CommitAreaPadding));
            }

            GUILayout.EndScrollView();
        }

        private void BuildTree(List<GitBranch> localBranches, List<GitBranch> remoteBranches)
        {
            localBranches.Sort(CompareBranches);
            remoteBranches.Sort(CompareBranches);
            treeLocals = new Tree();
            treeLocals.ActiveNodeIcon = Styles.ActiveBranchIcon;
            treeLocals.NodeIcon = Styles.BranchIcon;
            treeLocals.RootFolderIcon = Styles.RootFolderIcon;
            treeLocals.FolderIcon = Styles.FolderIcon;

            treeRemotes = new Tree();
            treeRemotes.ActiveNodeIcon = Styles.ActiveBranchIcon;
            treeRemotes.NodeIcon = Styles.BranchIcon;
            treeRemotes.RootFolderIcon = Styles.RootFolderIcon;
            treeRemotes.FolderIcon = Styles.FolderIcon;

            treeLocals.Load(localBranches.Cast<ITreeData>(), LocalTitle);
            treeRemotes.Load(remoteBranches.Cast<ITreeData>(), RemoteTitle);
            Redraw();
        }

        private void OnCreateGUI()
        {
            // Create button
            if (mode == BranchesMode.Default)
            {
                // If the current branch is selected, then do not enable the Delete button
                var disableDelete = treeLocals.SelectedNode == null || treeLocals.ActiveNode == treeLocals.SelectedNode && !treeLocals.SelectedNode.IsFolder;
                EditorGUI.BeginDisabledGroup(disableDelete);
                {
                    if (GUILayout.Button("Delete", EditorStyles.miniButton, GUILayout.ExpandWidth(false)))
                    {
                        var selectedBranchName = treeLocals.SelectedNode.Name;
                        var dialogTitle = "Delete Branch: " + selectedBranchName;
                        var dialogMessage = "Are you sure you want to delete the branch: " + selectedBranchName + "?";
                        if (EditorUtility.DisplayDialog("Delete Branch?", dialogMessage, "Delete", "Cancel"))
                        {
                            GitClient.DeleteBranch(selectedBranchName, true).Start();
                        }
                    }
                }
                EditorGUI.EndDisabledGroup();

                GUILayout.FlexibleSpace();
                if (GUILayout.Button(CreateBranchButton, EditorStyles.miniButton, GUILayout.ExpandWidth(false)))
                {
                    targetMode = BranchesMode.Create;
                }
            }
            // Branch name + cancel + create
            else if (mode == BranchesMode.Create)
            {
                GUILayout.BeginHorizontal();
                {
                    var createBranch = false;
                    var cancelCreate = false;
                    var cannotCreate = treeLocals.SelectedNode == null ||
                                       newBranchName == null ||
                                       !Utility.BranchNameRegex.IsMatch(newBranchName);

                    // Create on return/enter or cancel on escape
                    var offsetID = GUIUtility.GetControlID(FocusType.Passive);
                    if (Event.current.isKey && GUIUtility.keyboardControl == offsetID + 1)
                    {
                        if (Event.current.keyCode == KeyCode.Escape)
                        {
                            cancelCreate = true;
                            Event.current.Use();
                        }
                        else if (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter)
                        {
                            if (cannotCreate)
                            {
                                EditorApplication.Beep();
                            }
                            else
                            {
                                createBranch = true;
                            }
                            Event.current.Use();
                        }
                    }
                    newBranchName = EditorGUILayout.TextField(newBranchName);

                    // Create
                    EditorGUI.BeginDisabledGroup(cannotCreate);
                    {
                        if (GUILayout.Button(NewBranchConfirmButton, EditorStyles.miniButtonLeft, GUILayout.ExpandWidth(false)))
                        {
                            createBranch = true;
                        }
                    }
                    EditorGUI.EndDisabledGroup();

                    // Cancel create
                    if (GUILayout.Button(NewBranchCancelButton, EditorStyles.miniButtonRight, GUILayout.ExpandWidth(false)))
                    {
                        cancelCreate = true;
                    }

                    // Effectuate create
                    if (createBranch)
                    {
                        GitClient.CreateBranch(newBranchName, treeLocals.SelectedNode.Name)
                            .FinallyInUI((success, e) => { if (success) Refresh(); })
                            .Start();
                    }

                    // Cleanup
                    if (createBranch || cancelCreate)
                    {
                        newBranchName = "";
                        GUIUtility.keyboardControl = -1;
                        targetMode = BranchesMode.Default;
                    }
                }
                GUILayout.EndHorizontal();
            }
        }

        private void OnTreeGUI(Rect rect)
        {
            if (!treeLocals.IsInitialized)
                RefreshBranchList();

            if (treeLocals.FolderStyle == null)
            {
                treeLocals.FolderStyle = Styles.Foldout;
                treeLocals.TreeNodeStyle = Styles.TreeNode;
                treeLocals.ActiveTreeNodeStyle = Styles.TreeNodeActive;
                treeRemotes.FolderStyle = Styles.Foldout;
                treeRemotes.TreeNodeStyle = Styles.TreeNode;
                treeRemotes.ActiveTreeNodeStyle = Styles.TreeNodeActive;
            }

            var treeHadFocus = treeLocals.SelectedNode != null;

            rect = treeLocals.Render(rect, _ => { }, node =>
                {
                    if (EditorUtility.DisplayDialog(ConfirmSwitchTitle, String.Format(ConfirmSwitchMessage, node.Name), ConfirmSwitchOK,
                            ConfirmSwitchCancel))
                    {
                        GitClient.SwitchBranch(node.Name)
                            .FinallyInUI((success, e) =>
                            {
                                if (success)
                                    Refresh();
                                else
                                {
                                    EditorUtility.DisplayDialog(Localization.SwitchBranchTitle,
                                        String.Format(Localization.SwitchBranchFailedDescription, node.Name),
                                    Localization.Cancel);
                                }
                            }).Start();
                    }
                });

            if (treeHadFocus && treeLocals.SelectedNode == null)
                treeRemotes.Focus();
            else if (!treeHadFocus && treeLocals.SelectedNode != null)
                treeRemotes.Blur();

            if (treeLocals.RequiresRepaint)
                Redraw();

            treeHadFocus = treeRemotes.SelectedNode != null;

            rect.y += Styles.TreePadding;

            treeRemotes.Render(rect, _ => {}, node =>
            {
                GitClient.CreateBranch(node.Name.Substring(node.Name.IndexOf('/') + 1), node.Name)
                    .FinallyInUI((success, e) =>
                    {
                        if (success)
                            Refresh();
                        else
                        {
                            EditorUtility.DisplayDialog(Localization.SwitchBranchTitle,
                                String.Format(Localization.SwitchBranchFailedDescription, node.Name),
                            Localization.Cancel);
                        }
                    }).Start();
            });

            if (treeHadFocus && treeRemotes.SelectedNode == null)
            {
                treeLocals.Focus();
            }
            else if (!treeHadFocus && treeRemotes.SelectedNode != null)
            {
                treeLocals.Blur();
            }

            if (treeRemotes.RequiresRepaint)
                Redraw();
        }
        private int CompareBranches(GitBranch a, GitBranch b)
        {
            if (Favourites.Instance.IsFavourite(a.Name))
            {
                return -1;
            }

            if (Favourites.Instance.IsFavourite(b.Name))
            {
                return 1;
            }

            if (a.Name.Equals("master"))
            {
                return -1;
            }

            if (b.Name.Equals("master"))
            {
                return 1;
            }

            return 0;
        }

        [Serializable]
        public class Tree
        {
            [SerializeField] private List<TreeNode> nodes = new List<TreeNode>();
            [SerializeField] private TreeNode selectedNode = null;
            [SerializeField] private TreeNode activeNode = null;
            [SerializeField] public float ItemHeight = EditorGUIUtility.singleLineHeight;
            [SerializeField] public float ItemSpacing = EditorGUIUtility.standardVerticalSpacing;
            [SerializeField] public float Indentation = 12f;
            [SerializeField] public Rect Margin = new Rect();
            [SerializeField] public Rect Padding = new Rect();
            [SerializeField] private List<string> foldersKeys = new List<string>();
            [SerializeField] public Texture2D ActiveNodeIcon;
            [SerializeField] public Texture2D NodeIcon;
            [SerializeField] public Texture2D FolderIcon;
            [SerializeField] public Texture2D RootFolderIcon;
            [SerializeField] public GUIStyle FolderStyle;
            [SerializeField] public GUIStyle TreeNodeStyle;
            [SerializeField] public GUIStyle ActiveTreeNodeStyle;

            [NonSerialized]
            private Stack<bool> indents = new Stack<bool>();
            [NonSerialized]
            private Hashtable folders;

            public bool IsInitialized { get { return nodes != null && nodes.Count > 0 && !String.IsNullOrEmpty(nodes[0].Name); } }
            public bool RequiresRepaint { get; private set; }

            public TreeNode SelectedNode
            {
                get
                {
                    if (selectedNode != null && String.IsNullOrEmpty(selectedNode.Name))
                        selectedNode = null;
                    return selectedNode;
                }
                private set
                {
                    selectedNode = value;
                }
            }

            public TreeNode ActiveNode { get { return activeNode; } }

            private Hashtable Folders
            {
                get
                {
                    if (folders == null)
                    {
                        folders = new Hashtable();
                        for (int i = 0; i < foldersKeys.Count; i++)
                        {
                            folders.Add(foldersKeys[i], null);
                        }
                    }
                    return folders;
                }
            }

            public void Load(IEnumerable<ITreeData> data, string title)
            {
                foldersKeys.Clear();
                Folders.Clear();
                nodes.Clear();

                var titleNode = new TreeNode()
                {
                    Name = title,
                    Label = title,
                    Level = 0,
                    IsFolder = true
                };
                titleNode.Load();
                nodes.Add(titleNode);

                foreach (var d in data)
                {
                    var parts = d.Name.Split('/');
                    for (int i = 0; i < parts.Length; i++)
                    {
                        var label = parts[i];
                        var name = String.Join("/", parts, 0, i + 1);
                        var isFolder = i < parts.Length - 1;
                        var alreadyExists = Folders.ContainsKey(name);
                        if (!alreadyExists)
                        {
                            var node = new TreeNode()
                            {
                                Name = name,
                                IsActive = d.IsActive,
                                Label = label,
                                Level = i + 1,
                                IsFolder = isFolder
                            };

                            if (node.IsActive)
                            {
                                activeNode = node;
                                node.Icon = ActiveNodeIcon;
                            }
                            else if (node.IsFolder)
                            {
                                if (node.Level == 1)
                                    node.Icon = RootFolderIcon;
                                else
                                    node.Icon = FolderIcon;
                            }
                            else
                            {
                                node.Icon = NodeIcon;
                            }

                            node.Load();

                            nodes.Add(node);
                            if (isFolder)
                            {
                                Folders.Add(name, null);
                            }
                        }
                    }
                }
                foldersKeys = Folders.Keys.Cast<string>().ToList();
            }

            public Rect Render(Rect rect, Action<TreeNode> singleClick = null, Action<TreeNode> doubleClick = null)
            {
                RequiresRepaint = false;
                rect = new Rect(0f, rect.y, rect.width, ItemHeight);

                var titleNode = nodes[0];
                bool selectionChanged = titleNode.Render(rect, 0f, selectedNode == titleNode, FolderStyle, TreeNodeStyle, ActiveTreeNodeStyle);

                if (selectionChanged)
                {
                    ToggleNodeVisibility(0, titleNode);
                }

                RequiresRepaint = HandleInput(rect, titleNode, 0);
                rect.y += ItemHeight + ItemSpacing;

                Indent();

                int level = 1;
                for (int i = 1; i < nodes.Count; i++)
                {
                    var node = nodes[i];

                    if (node.Level > level && !node.IsHidden)
                    {
                        Indent();
                    }

                    var changed = node.Render(rect, Indentation, selectedNode == node, FolderStyle, TreeNodeStyle, ActiveTreeNodeStyle);

                    if (node.IsFolder && changed)
                    {
                        // toggle visibility for all the nodes under this one
                        ToggleNodeVisibility(i, node);
                    }

                    if (node.Level < level)
                    {
                        for (; node.Level > level && indents.Count > 1; level--)
                        {
                            Unindent();
                        }
                    }
                    level = node.Level;

                    if (!node.IsHidden)
                    {
                        RequiresRepaint = HandleInput(rect, node, i, singleClick, doubleClick);
                        rect.y += ItemHeight + ItemSpacing;
                    }
                }

                Unindent();

                foldersKeys = Folders.Keys.Cast<string>().ToList();
                return rect;
            }

            public void Focus()
            {
                bool selectionChanged = false;
                if (Event.current.type == EventType.KeyDown)
                {
                    int directionY = Event.current.keyCode == KeyCode.UpArrow ? -1 : Event.current.keyCode == KeyCode.DownArrow ? 1 : 0;
                    int directionX = Event.current.keyCode == KeyCode.LeftArrow ? -1 : Event.current.keyCode == KeyCode.RightArrow ? 1 : 0;
                    if (directionY != 0 || directionX != 0)
                    {
                        if (directionY < 0 || directionY < 0)
                        {
                            SelectedNode = nodes[nodes.Count - 1];
                            selectionChanged = true;
                            Event.current.Use();
                        }
                        else if (directionY > 0 || directionX > 0)
                        {
                            SelectedNode = nodes[0];
                            selectionChanged = true;
                            Event.current.Use();
                        }
                    }
                }
                RequiresRepaint = selectionChanged;
            }

            public void Blur()
            {
                SelectedNode = null;
                RequiresRepaint = true;
            }

            private int ToggleNodeVisibility(int idx, TreeNode rootNode)
            {
                var rootNodeLevel = rootNode.Level;
                rootNode.IsCollapsed = !rootNode.IsCollapsed;
                idx++;
                for (; idx < nodes.Count && nodes[idx].Level > rootNodeLevel; idx++)
                {
                    nodes[idx].IsHidden = rootNode.IsCollapsed;
                    if (nodes[idx].IsFolder && !rootNode.IsCollapsed && nodes[idx].IsCollapsed)
                    {
                        var level = nodes[idx].Level;
                        for (idx++; idx < nodes.Count && nodes[idx].Level > level; idx++) { }
                        idx--;
                    }
                }
                if (SelectedNode != null && SelectedNode.IsHidden)
                {
                    SelectedNode = rootNode;
                }
                return idx;
            }

            private bool HandleInput(Rect rect, TreeNode currentNode, int index, Action<TreeNode> singleClick = null, Action<TreeNode> doubleClick = null)
            {
                bool selectionChanged = false;
                var clickRect = new Rect(0f, rect.y, rect.width, rect.height);
                if (Event.current.type == EventType.MouseDown && clickRect.Contains(Event.current.mousePosition))
                {
                    Event.current.Use();
                    SelectedNode = currentNode;
                    selectionChanged = true;
                    var clickCount = Event.current.clickCount;
                    if (clickCount == 1 && singleClick != null)
                    {
                        singleClick(currentNode);
                    }
                    if (clickCount > 1 && doubleClick != null)
                    {
                        doubleClick(currentNode);
                    }
                }

                // Keyboard navigation if this child is the current selection
                if (currentNode == selectedNode && Event.current.type == EventType.KeyDown)
                {
                    int directionY = Event.current.keyCode == KeyCode.UpArrow ? -1 : Event.current.keyCode == KeyCode.DownArrow ? 1 : 0;
                    int directionX = Event.current.keyCode == KeyCode.LeftArrow ? -1 : Event.current.keyCode == KeyCode.RightArrow ? 1 : 0;
                    if (directionY != 0 || directionX != 0)
                    {
                        if (directionY > 0)
                        {
                            selectionChanged = SelectNext(index, false) != index;
                        }
                        else if (directionY < 0)
                        {
                            selectionChanged = SelectPrevious(index, false) != index;
                        }
                        else if (directionX > 0)
                        {
                            if (currentNode.IsFolder && currentNode.IsCollapsed)
                            {
                                ToggleNodeVisibility(index, currentNode);
                                Event.current.Use();
                            }
                            else
                            {
                                selectionChanged = SelectNext(index, true) != index;
                            }
                        }
                        else if (directionX < 0)
                        {
                            if (currentNode.IsFolder && !currentNode.IsCollapsed)
                            {
                                ToggleNodeVisibility(index, currentNode);
                                Event.current.Use();
                            }
                            else
                            {
                                selectionChanged = SelectPrevious(index, true) != index;
                            }
                        }
                    }
                }
                return selectionChanged;
            }

            private int SelectNext(int index, bool foldersOnly)
            {
                for (index++; index < nodes.Count; index++)
                {
                    if (nodes[index].IsHidden)
                        continue;
                    if (!nodes[index].IsFolder && foldersOnly)
                        continue;
                    break;
                }

                if (index < nodes.Count)
                {
                    SelectedNode = nodes[index];
                    Event.current.Use();
                }
                else
                {
                    SelectedNode = null;
                }
                return index;
            }

            private int SelectPrevious(int index, bool foldersOnly)
            {
                for (index--; index >= 0; index--)
                {
                    if (nodes[index].IsHidden)
                        continue;
                    if (!nodes[index].IsFolder && foldersOnly)
                        continue;
                    break;
                }

                if (index >= 0)
                {
                    SelectedNode = nodes[index];
                    Event.current.Use();
                }
                else
                {
                    SelectedNode = null;
                }
                return index;
            }

            private void Indent()
            {
                indents.Push(true);
            }

            private void Unindent()
            {
                indents.Pop();
            }
        }

        [Serializable]
        public class TreeNode
        {
            public string Name;
            public string Label;
            public int Level;
            public bool IsFolder;
            public bool IsCollapsed;
            public bool IsHidden;
            public bool IsActive;
            public GUIContent content;
            public Texture2D Icon;

            public void Load()
            {
                content = new GUIContent(Label, Icon);
            }

            public bool Render(Rect rect, float indentation, bool isSelected, GUIStyle folderStyle, GUIStyle nodeStyle, GUIStyle activeNodeStyle)
            {
                if (IsHidden)
                    return false;

                GUIStyle style;
                if (IsFolder)
                {
                    style = folderStyle;
                }
                else
                {
                    style = IsActive ? activeNodeStyle : nodeStyle;
                }

                bool changed = false;
                var fillRect = rect;
                var nodeRect = new Rect(Level * indentation, rect.y, rect.width, rect.height);

                if (Event.current.type == EventType.repaint)
                {
                    nodeStyle.Draw(fillRect, "", false, false, false, isSelected);
                    if (IsFolder)
                        style.Draw(nodeRect, content, false, false, !IsCollapsed, isSelected);
                    else
                    {
                        style.Draw(nodeRect, content, false, false, false, isSelected);
                    }
                }

                if (IsFolder)
                {
                    EditorGUI.BeginChangeCheck();
                    GUI.Toggle(nodeRect, !IsCollapsed, "", GUIStyle.none);
                    changed = EditorGUI.EndChangeCheck();
                }

                return changed;
            }

            public override string ToString()
            {
                return String.Format("name:{0} label:{1} level:{2} isFolder:{3} isCollapsed:{4} isHidden:{5} isActive:{6}",
                    Name, Label, Level, IsFolder, IsCollapsed, IsHidden, IsActive);
            }
        }

        private enum NodeType
        {
            Folder,
            LocalBranch,
            RemoteBranch
        }

        private enum BranchesMode
        {
            Default,
            Create
        }
    }
}
