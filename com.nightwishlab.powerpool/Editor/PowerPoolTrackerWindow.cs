using System;
using System.Collections.Generic;
using PowerPool.API;
using PowerPoolFacade = PowerPool.API.PowerPool;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace PowerPool.Editor
{
    public sealed class PowerPoolTrackerWindow : EditorWindow
    {
        private const string WindowTitle = "PowerPool Tracker";

        [MenuItem("Window/PowerPool/Pool Tracker")]
        private static void Open()
        {
            var win = GetWindow<PowerPoolTrackerWindow>();
            win.titleContent = new GUIContent(WindowTitle);
            win.Show();
        }

        private readonly List<PowerPoolStats> _buffer = new List<PowerPoolStats>(64);

        private TreeViewState _treeState;
        private MultiColumnHeader _header;
        private PowerPoolTreeView _tree;

        private bool _autoRefresh = true;
        private float _refreshIntervalSec = 0.25f;
        private double _lastRefreshTime;
        private bool _destroyOnClear = true;
        private string _search = string.Empty;

        private void OnEnable()
        {
            _treeState ??= new TreeViewState();

            var headerState = PowerPoolTreeView.CreateDefaultHeaderState();
            _header = new MultiColumnHeader(headerState)
            {
                canSort = true,
                height = 22f
            };
            _header.sortingChanged += OnSortingChanged;

            _tree = new PowerPoolTreeView(_treeState, _header);
            RefreshNow(force: true);

            EditorApplication.update += Tick;
        }

        private void OnDisable()
        {
            EditorApplication.update -= Tick;
            if (_header != null) _header.sortingChanged -= OnSortingChanged;
        }

        private void Tick()
        {
            if (!_autoRefresh) return;
            if (_refreshIntervalSec <= 0f) return;

            double now = EditorApplication.timeSinceStartup;
            if (now - _lastRefreshTime < _refreshIntervalSec) return;

            if (RefreshNow(force: false))
            {
                Repaint();
            }
        }

        private void OnSortingChanged(MultiColumnHeader _)
        {
            _tree.RebuildVisible(forceReload: true);
            Repaint();
        }

        private bool RefreshNow(bool force)
        {
            double now = EditorApplication.timeSinceStartup;
            if (!force && now - _lastRefreshTime < _refreshIntervalSec) return false;

            PowerPoolFacade.GetAllStats(_buffer);
            bool changed = _tree.UpdateSnapshot(_buffer);
            // 자동 갱신에서는 "값만 업데이트"하고, 검색/정렬 변경이 없으면 Reload를 하지 않습니다.
            // (신규/삭제된 풀 등 Visible set이 바뀐 경우에만 Reload)
            if (changed)
            {
                _tree.RebuildVisible(forceReload: true);
            }
            _lastRefreshTime = now;
            return true;
        }

        private void OnGUI()
        {
            DrawToolbar();

            var rect = GUILayoutUtility.GetRect(0, 100000, 0, 100000);
            _tree.OnGUI(rect);
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(60)))
                {
                    RefreshNow(force: true);
                    Repaint();
                }

                _autoRefresh = GUILayout.Toggle(_autoRefresh, "Auto", EditorStyles.toolbarButton, GUILayout.Width(50));
                _refreshIntervalSec = EditorGUILayout.Slider(_refreshIntervalSec, 0.1f, 2.0f, GUILayout.Width(220));

                GUILayout.Space(10);

                _destroyOnClear = GUILayout.Toggle(_destroyOnClear, "Destroy", EditorStyles.toolbarButton, GUILayout.Width(70));

                if (GUILayout.Button("ClearAll", EditorStyles.toolbarButton, GUILayout.Width(70)))
                {
                    PowerPoolFacade.ClearAll(destroyPooled: _destroyOnClear);
                    RefreshNow(force: true);
                    Repaint();
                }

                if (GUILayout.Button("DisposeAll", EditorStyles.toolbarButton, GUILayout.Width(80)))
                {
                    PowerPoolFacade.DisposeAll(destroyPooled: _destroyOnClear);
                    RefreshNow(force: true);
                    Repaint();
                }

                GUILayout.FlexibleSpace();

                GUIStyle searchStyle = GUI.skin.FindStyle("ToolbarSearchTextField") ?? EditorStyles.toolbarTextField;
                GUIStyle cancelStyle = GUI.skin.FindStyle("ToolbarSearchCancelButton") ?? EditorStyles.toolbarButton;

                string nextSearch = GUILayout.TextField(_search, searchStyle, GUILayout.Width(260));
                if (!string.Equals(nextSearch, _search, StringComparison.Ordinal))
                {
                    _search = nextSearch ?? string.Empty;
                    _tree.SetSearch(_search);
                    _tree.RebuildVisible(forceReload: true);
                }

                if (GUILayout.Button(string.Empty, cancelStyle))
                {
                    _search = string.Empty;
                    GUI.FocusControl(null);
                    _tree.SetSearch(_search);
                    _tree.RebuildVisible(forceReload: true);
                }

                GUILayout.Space(8);
                GUILayout.Label($"Pools: {PowerPoolFacade.RegisteredPoolCount}", EditorStyles.miniLabel);
            }
        }

        private sealed class PowerPoolTreeView : TreeView
        {
            private enum Col
            {
                Prefab = 0,
                Type = 1,
                InPool = 2,
                Active = 3,
                Created = 4,
                Capacity = 5,
                Max = 6,
                Overflow = 7,
                Disposed = 8,
            }

            private readonly List<PowerPoolStats> _stats = new List<PowerPoolStats>(64);
            private readonly List<int> _visibleKeys = new List<int>(64);
            private readonly Dictionary<int, PowerPoolStats> _map = new Dictionary<int, PowerPoolStats>(64);
            private string _search = string.Empty;

            private GUIStyle _right;
            private GUIStyle _center;

            public PowerPoolTreeView(TreeViewState state, MultiColumnHeader header) : base(state, header)
            {
                showBorder = true;
                showAlternatingRowBackgrounds = true;
                rowHeight = 20f;
                Reload();
            }

            private void EnsureStyles()
            {
                // If you modify EditorStyles.* in static initialization, NRE may occur at the domain reload timing,
                // so create styles based on GUI.skin only at the actual OnGUI timing (after skin preparation).
                if (_right == null)
                {
                    _right = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleRight };
                }

                if (_center == null)
                {
                    _center = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter };
                }
            }

            public static MultiColumnHeaderState CreateDefaultHeaderState()
            {
                var cols = new[]
                {
                    new MultiColumnHeaderState.Column { headerContent = new GUIContent("Prefab"), width = 200, minWidth = 120, autoResize = true, canSort = true },
                    new MultiColumnHeaderState.Column { headerContent = new GUIContent("Type"), width = 120, minWidth = 90, autoResize = true, canSort = true },
                    new MultiColumnHeaderState.Column { headerContent = new GUIContent("InPool"), width = 60, minWidth = 50, autoResize = false, canSort = true },
                    new MultiColumnHeaderState.Column { headerContent = new GUIContent("Active"), width = 60, minWidth = 50, autoResize = false, canSort = true },
                    new MultiColumnHeaderState.Column { headerContent = new GUIContent("Created"), width = 70, minWidth = 60, autoResize = false, canSort = true },
                    new MultiColumnHeaderState.Column { headerContent = new GUIContent("Cap"), width = 55, minWidth = 45, autoResize = false, canSort = true },
                    new MultiColumnHeaderState.Column { headerContent = new GUIContent("Max"), width = 55, minWidth = 45, autoResize = false, canSort = true },
                    new MultiColumnHeaderState.Column { headerContent = new GUIContent("Overflow"), width = 95, minWidth = 80, autoResize = true, canSort = true },
                    new MultiColumnHeaderState.Column { headerContent = new GUIContent("Disposed"), width = 65, minWidth = 55, autoResize = false, canSort = true },
                };

                return new MultiColumnHeaderState(cols);
            }

            public void SetSearch(string search)
            {
                _search = search ?? string.Empty;
            }

            /// <summary>
            /// Update only the snapshot (value). Returns true if it affects the Visible set.
            /// </summary>
            public bool UpdateSnapshot(List<PowerPoolStats> snapshot)
            {
                bool keysChanged = false;

                // 1) Compare the existing key set (primary determination based on size)
                if (_map.Count != snapshot.Count)
                {
                    keysChanged = true;
                }

                _stats.Clear();
                if (_stats.Capacity < snapshot.Count) _stats.Capacity = snapshot.Count;

                // 2) map update
                // Process O(n) under the assumption that snapshot is smaller (limited number of pools)
                // - The value is always replaced with the latest
                // - Key changes are detected separately
                // - Removal detection is handled by Count mismatch
                for (int i = 0; i < snapshot.Count; i++)
                {
                    var st = snapshot[i];
                    _stats.Add(st);
                    if (!_map.ContainsKey(st.PrefabKey)) keysChanged = true;
                    _map[st.PrefabKey] = st;
                }

                if (!keysChanged)
                {
                    // Check if there was a removal (even if Count is the same, PrefabKey may have changed)
                    // If there are only keys remaining in the map after all snapshot keys are contained, it means a removal occurred
                    // (Linear search assuming a small scale)
                    for (int i = 0; i < _visibleKeys.Count; i++)
                    {
                        int k = _visibleKeys[i];
                        bool exists = false;
                        for (int j = 0; j < snapshot.Count; j++)
                        {
                            if (snapshot[j].PrefabKey == k)
                            {
                                exists = true;
                                break;
                            }
                        }
                        if (!exists)
                        {
                            keysChanged = true;
                            break;
                        }
                    }
                }

                return keysChanged;
            }

            public void RebuildVisible(bool forceReload)
            {
                BuildVisibleKeyList(_search);
                // As requested: Reload only when sorting/searching/key changes
                if (forceReload) Reload();
            }

            private void BuildVisibleKeyList(string search)
            {
                _visibleKeys.Clear();

                if (string.IsNullOrEmpty(search))
                {
                    if (_visibleKeys.Capacity < _stats.Count) _visibleKeys.Capacity = _stats.Count;
                    for (int i = 0; i < _stats.Count; i++) _visibleKeys.Add(_stats[i].PrefabKey);
                    ApplySortKeys();
                    return;
                }

                string s = search.Trim();
                if (s.Length == 0)
                {
                    if (_visibleKeys.Capacity < _stats.Count) _visibleKeys.Capacity = _stats.Count;
                    for (int i = 0; i < _stats.Count; i++) _visibleKeys.Add(_stats[i].PrefabKey);
                    ApplySortKeys();
                    return;
                }

                for (int i = 0; i < _stats.Count; i++)
                {
                    var st = _stats[i];
                    if (st.PrefabName != null && st.PrefabName.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        _visibleKeys.Add(st.PrefabKey);
                        continue;
                    }

                    if (st.ItemTypeName != null && st.ItemTypeName.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        _visibleKeys.Add(st.PrefabKey);
                    }
                }

                ApplySortKeys();
            }

            private void ApplySortKeys()
            {
                if (_visibleKeys.Count <= 1) return;

                var sorted = multiColumnHeader.state.sortedColumns;
                if (sorted == null || sorted.Length == 0) return;

                int col = sorted[0];
                bool asc = multiColumnHeader.IsSortedAscending(col);

                _visibleKeys.Sort((ka, kb) =>
                {
                    if (!_map.TryGetValue(ka, out var a)) a = default;
                    if (!_map.TryGetValue(kb, out var b)) b = default;
                    int c = CompareByColumn(a, b, (Col)col);
                    return asc ? c : -c;
                });
            }

            protected override TreeViewItem BuildRoot()
            {
                var root = new TreeViewItem { id = 0, depth = -1, displayName = "root" };
                var children = new List<TreeViewItem>(_visibleKeys.Count);

                for (int i = 0; i < _visibleKeys.Count; i++)
                {
                    int key = _visibleKeys[i];
                    if (!_map.TryGetValue(key, out var st)) st = default;
                    children.Add(new StatItem(key, st.PrefabName));
                }

                root.children = children;
                SetupDepthsFromParentsAndChildren(root);
                return root;
            }

            protected override void RowGUI(RowGUIArgs args)
            {
                var item = (StatItem)args.item;
                _map.TryGetValue(item.PrefabKey, out var st);
                EnsureStyles();

                for (int i = 0; i < args.GetNumVisibleColumns(); i++)
                {
                    int col = args.GetColumn(i);
                    Rect cell = args.GetCellRect(i);
                    CenterRectUsingSingleLineHeight(ref cell);

                    DrawCell(cell, st, (Col)col);
                }
            }

            private void DrawCell(Rect rect, PowerPoolStats st, Col col)
            {
                switch (col)
                {
                    case Col.Prefab:
                        EditorGUI.LabelField(rect, st.PrefabName ?? "<null>");
                        break;
                    case Col.Type:
                        EditorGUI.LabelField(rect, st.ItemTypeName ?? "<unknown>");
                        break;
                    case Col.InPool:
                        EditorGUI.LabelField(rect, st.InPool.ToString(), _right);
                        break;
                    case Col.Active:
                        EditorGUI.LabelField(rect, st.ActiveEstimated.ToString(), _right);
                        break;
                    case Col.Created:
                        EditorGUI.LabelField(rect, st.TotalCreated.ToString(), _right);
                        break;
                    case Col.Capacity:
                        EditorGUI.LabelField(rect, st.Capacity.ToString(), _right);
                        break;
                    case Col.Max:
                        EditorGUI.LabelField(rect, st.MaxPoolSize <= 0 ? "-" : st.MaxPoolSize.ToString(), _right);
                        break;
                    case Col.Overflow:
                        EditorGUI.LabelField(rect, st.OverflowPolicy.ToString(), _center);
                        break;
                    case Col.Disposed:
                        EditorGUI.LabelField(rect, st.IsDisposed ? "Yes" : "No", _center);
                        break;
                }
            }

            private static int CompareByColumn(PowerPoolStats a, PowerPoolStats b, Col col)
            {
                switch (col)
                {
                    case Col.Prefab: return string.Compare(a.PrefabName, b.PrefabName, StringComparison.OrdinalIgnoreCase);
                    case Col.Type: return string.Compare(a.ItemTypeName, b.ItemTypeName, StringComparison.OrdinalIgnoreCase);
                    case Col.InPool: return a.InPool.CompareTo(b.InPool);
                    case Col.Active: return a.ActiveEstimated.CompareTo(b.ActiveEstimated);
                    case Col.Created: return a.TotalCreated.CompareTo(b.TotalCreated);
                    case Col.Capacity: return a.Capacity.CompareTo(b.Capacity);
                    case Col.Max: return a.MaxPoolSize.CompareTo(b.MaxPoolSize);
                    case Col.Overflow: return a.OverflowPolicy.CompareTo(b.OverflowPolicy);
                    case Col.Disposed: return a.IsDisposed.CompareTo(b.IsDisposed);
                    default: return 0;
                }
            }

            protected override void ContextClickedItem(int id)
            {
                var st = FindById(id);
                if (st.PrefabKey == 0) return;

                var menu = new GenericMenu();
                menu.AddItem(new GUIContent("Clear (Destroy Pooled)"), false, () => PowerPoolFacade.TryClearByKey(st.PrefabKey, destroyPooled: true));
                menu.AddItem(new GUIContent("Clear (Keep Objects)"), false, () => PowerPoolFacade.TryClearByKey(st.PrefabKey, destroyPooled: false));
                menu.AddSeparator(string.Empty);
                menu.AddItem(new GUIContent("Dispose (Destroy Pooled)"), false, () => PowerPoolFacade.TryDisposeByKey(st.PrefabKey, destroyPooled: true));
                menu.AddItem(new GUIContent("Dispose (Keep Objects)"), false, () => PowerPoolFacade.TryDisposeByKey(st.PrefabKey, destroyPooled: false));
                menu.AddSeparator(string.Empty);
                menu.AddItem(new GUIContent("Ping Prefab (best effort)"), false, () =>
                {
                    var obj = EditorUtility.InstanceIDToObject(st.PrefabKey);
                    if (obj != null) EditorGUIUtility.PingObject(obj);
                });
                menu.ShowAsContext();
            }

            private PowerPoolStats FindById(int id)
            {
                return _map.TryGetValue(id, out var st) ? st : default;
            }

            private sealed class StatItem : TreeViewItem
            {
                public readonly int PrefabKey;
                public StatItem(int id, string prefabName) : base(id, 0)
                {
                    PrefabKey = id;
                    displayName = prefabName ?? "<null>";
                }
            }
        }
    }
}

