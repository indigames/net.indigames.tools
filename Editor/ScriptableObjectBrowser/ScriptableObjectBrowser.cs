using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;
using Object = UnityEngine.Object;

namespace IndiGamesEditor.Tools.Editor.ScriptableObjectBrowser
{
    public class ScriptableObjectBrowser : EditorWindow
    {
        private const int BROWSE_AREA_WIDTH = 320;
        private const int ENTRY_LINE_HEIGHT = 22;
        private const int EDITOR_HISTORY_MAX = 60;

        private const string SUFFIX_NAME = ".asset";
        private const string TOOLBAR_PLUS_NAME = "Toolbar Plus More";
        private const string CONSOLE_WINDOW_EDITOR_NAME = "UnityEditor.ConsoleWindow";

        private const string FILTER_DUMMY = "FILTER_DUMMY";
        private const string FILTER = "FILTER";
        private const string BROWSE_FOCUS_ID = "browse_focus_id";

        private static readonly LinkedList<ScriptableObject> EditorHistory = new();
        private static readonly List<Type> BrowsableTypes = new();
        private readonly HashSet<Object> _selections = new();

        private static Dictionary<Type, ScriptableObjectBrowserEditor> _editors;
        private static string[] _browsableTypeNames = Array.Empty<string>();
        private static GUIStyle _selectedStyle;
        private static Texture2D _textSO;

        private ScriptableObjectBrowserEditor _currentEditor;
        private Vector2 _inspectScroll = Vector2.zero;
        private Vector2 _browseScroll = Vector2.zero;
        private List<AssetEntry> _assetList = new();
        private List<AssetEntry> _sortedAssetList = new();
        private AssetEntry _currentSelectionEntry;
        private AssetEntry _startSelectionEntry;
        private Object _currentObject;
        private Type _currentType;

        private int _currentTypeIndex;

        private string _filterText = String.Empty;

        private bool _isSelectedPrevious;
        private bool _isControlFocused;

        protected void OnEnable()
        {
            ReloadBrowserEditors();
            SetupEditorAssets();

            if (_currentEditor == null && _currentObject != null)
            {
                OpenObject(_currentObject);
            }
            else if (BrowsableTypes.Count > 0)
            {
                GetType(BrowsableTypes[0]);
            }
        }

        [MenuItem("IndiGames Tools/SO Browser %#o")]
        protected static ScriptableObjectBrowser ShowWindow()
        {
            ReloadBrowserEditors();

            ScriptableObjectBrowser[] windows = Resources.FindObjectsOfTypeAll<ScriptableObjectBrowser>();
            if (windows is { Length: > 0 }) return windows[0];

            ScriptableObjectBrowser window = GetWindow<ScriptableObjectBrowser>();

            window.ShowTab();
            return window;
        }


        private static void ReloadBrowserEditors()
        {
            if (_editors != null) return;

            _editors = new();
            List<string> browsableTypeNames = new();

            Assembly[] allAssemblies = AppDomain.CurrentDomain.GetAssemblies();

            IEnumerable<Type> types = allAssemblies.SelectMany(assembly => assembly.GetTypes())
                .Where(t =>
                    t.BaseType != null &&
                    t.BaseType.IsGenericType &&
                    t.BaseType.GetGenericTypeDefinition() == typeof(ScriptableObjectBrowserEditor<>));

            foreach (Type type in types)
            {
                if (type.BaseType == null && _editors != null) return;

                Type genericArgument = type.BaseType?.GetGenericArguments()[0];
                _editors[genericArgument!] = (ScriptableObjectBrowserEditor)Activator.CreateInstance(type);

                BrowsableTypes.Add(genericArgument);
                browsableTypeNames.Add(genericArgument.Name);
            }

            _browsableTypeNames = browsableTypeNames.ToArray();
        }

        protected static void OpenObject(Object obj)
        {
            ScriptableObjectBrowser window = ShowWindow();
            Type type = obj.GetType();

            window.GetType(type);
            ScriptableObjectBrowserEditor editor = window._currentEditor;

            editor.SetTargetObjects(new[] { obj });
            window.SelectionSingle(obj);
        }

        [OnOpenAsset(1)]
        public static bool OnOpenAsset(int instanceID, int line)
        {
            if (_editors == null || Selection.activeObject == null) return false;
            ReloadBrowserEditors();

            if (_editors.ContainsKey(Selection.activeObject.GetType()))
            {
                OpenObject(EditorUtility.InstanceIDToObject(instanceID));
                return true;
            }

            return false; // let unity open the file
        }

        protected void GetType(Type type)
        {
            RecordCurrentSelection();
            _currentSelectionEntry = _startSelectionEntry = null;
            _selections.Clear();

            while (type != null && _editors.ContainsKey(type) == false) type = type.BaseType;
            if (type == null) return;

            _currentTypeIndex = BrowsableTypes.IndexOf(type);
            _currentEditor = _editors[type];
            _currentEditor.Browser = this;
            _currentType = type;
            ResetAssetList(type);
            SelectionChanged();
        }


        protected void ResetAssetList(Type type)
        {
            string[] assets = AssetDatabase.FindAssets($"t:{type.Name}");

            HashSet<Object> foundAssets = new HashSet<Object>();

            _filterText = String.Empty;

            List<AssetEntry> assetList = new List<AssetEntry>();

            string lastPath = null;
            foreach (string guid in assets)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);

                if (path == lastPath) continue;
                lastPath = path;

                Object[] loadedAssets = AssetDatabase.LoadAllAssetsAtPath(path);

                foreach (Object asset in loadedAssets)
                {
                    if (type.IsInstanceOfType(asset))
                    {
                        foundAssets.Add(asset);
                    }
                }
            }

            foreach (Object asset in foundAssets)
            {
                assetList.Add(CreateAssetEntry(asset));
            }

            _assetList = assetList;
            _sortedAssetList = new List<AssetEntry>(assetList);
        }


        protected AssetEntry CreateAssetEntry(Object asset)
        {
            string name = asset.name;

            string path = $"{AssetDatabase.GetAssetPath(asset)}.{name}";

            AssetEntry entry = new AssetEntry()
            {
                Path = path,
                RPath = ReverseString(path),
                Name = name,
                Asset = asset
            };

            return entry;
        }

        protected void SyncAssetEntry(AssetEntry entry)
        {
            Object asset = entry.Asset;
            string name = asset.name;
            string path = $"{AssetDatabase.GetAssetPath(asset)}.{name}";

            entry.Path = path;
            entry.RPath = ReverseString(path);
            entry.Name = name;
        }

        protected void AddAssetEntry(Object asset)
        {
            AssetEntry entry = CreateAssetEntry(asset);

            _assetList.Add(entry);

            ResortEntries(_filterText);
        }

        protected void SetupEditorAssets()
        {
            _textSO = EditorGUIUtility.FindTexture(CONSOLE_WINDOW_EDITOR_NAME);
            _selectedStyle = new GUIStyle();
            Texture2D texture2D = new Texture2D(1, 1);

            texture2D.SetPixel(0, 0, new Color(62 / 255f, 125 / 255f, 231 / 255f));
            texture2D.Apply();

            _selectedStyle.normal.background = texture2D;
            _selectedStyle.normal.textColor = Color.white;
            _selectedStyle.fixedHeight = 18;
        }

        protected void ResortEntries(string textResort)
        {
            string filterText = ReverseString(textResort);

            _filterText = textResort;
            textResort = textResort.ToLower();

            foreach (AssetEntry entry in _assetList)
            {
                if (textResort.Length == 0)
                {
                    entry.Visible = true;
                    continue;
                }

                if (textResort.Length > entry.Path.Length) continue;

                int lastIndex = 0;
                string path = entry.Path.ToLower();

                foreach (char t in textResort)
                {
                    lastIndex = path.IndexOf(t, lastIndex);
                    if (lastIndex < 0) break;
                    lastIndex++;
                }

                entry.Visible = lastIndex >= 0;

                if (!entry.Visible) continue;

                FindHelper.Match(entry.RPath, filterText, out int matchAmount);
                FindHelper.Match(entry.Path, textResort, out int amount);

                entry.MatchAmount = Mathf.Max(matchAmount, amount);
            }

            _sortedAssetList = new List<AssetEntry>(_assetList);
            _sortedAssetList.RemoveAll((a) => a.Visible == false);

            if (textResort.Length > 0) _sortedAssetList.Sort((e2, e1) => e1.MatchAmount.CompareTo(e2.MatchAmount));
        }

        protected void RenderAssetEntry(AssetEntry asset, ref Rect rectEntry)
        {
            if (!asset.Visible) return;

            EditorGUILayout.BeginHorizontal(GUILayout.MinWidth(rectEntry.width));

            var selected_color = _isControlFocused
                ? new Color(62 / 255f, 125 / 255f, 231 / 255f)
                : new Color(0.6f, 0.6f, 0.6f);

            if (_selections.Contains(asset.Asset)) EditorGUI.DrawRect(rectEntry, selected_color);

            var content = new GUIContent(asset.Name, _textSO);

            EditorGUILayout.LabelField(content, EditorStyles.boldLabel,
                GUILayout.Width(EditorStyles.boldLabel.CalcSize(content).x));

            EditorGUILayout.LabelField(asset.Path, EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();

            rectEntry.y += rectEntry.height;

            Rect r = GUILayoutUtility.GetLastRect();

            Event currentEvent = Event.current;

            if (currentEvent.type != EventType.MouseDown || currentEvent.button != 0 ||
                !r.Contains(currentEvent.mousePosition)) return;

            switch (currentEvent.control)
            {
                case true:
                    SelectionSingleToggle(asset);
                    break;

                default:
                {
                    if (currentEvent.shift && _currentSelectionEntry != null)
                        SelectionToRange(asset);
                    else
                        SelectionSingle(asset);
                    break;
                }
            }

            Repaint();
        }

        protected void SelectionSingle(Object obj)
        {
            if (_sortedAssetList.Find((a) => a.Asset == obj) is { } asset) SelectionSingle(asset);
        }

        protected void SelectionSingle(AssetEntry asset)
        {
            RecordCurrentSelection(asset.Asset);

            bool isSameObject = _currentSelectionEntry == asset;

            _currentSelectionEntry = _startSelectionEntry = asset;

            _selections.Clear();
            _selections.Add(asset.Asset);

            SelectionChanged();

            if (isSameObject) EditorGUIUtility.PingObject(asset.Asset);
        }

        protected void SelectionSingleToggle(AssetEntry asset)
        {
            if (_selections.Contains(asset.Asset) && _selections.Count <= 1) return;

            _currentSelectionEntry = _startSelectionEntry = asset;

            if (_selections.Contains(asset.Asset)) _selections.Remove(asset.Asset);
            else _selections.Add(asset.Asset);

            SelectionChanged();
        }

        protected void SelectionSetAll()
        {
            _selections.Clear();

            foreach (AssetEntry asset in _sortedAssetList)
            {
                _selections.Add(asset.Asset);
            }

            SelectionChanged();
        }

        protected void SelectionToRange(AssetEntry asset)
        {
            if (_startSelectionEntry == null)
            {
                SelectionSingle(asset);
                return;
            }

            _selections.Clear();

            int index = _sortedAssetList.IndexOf(_startSelectionEntry);

            int target_index = _sortedAssetList.IndexOf(asset);

            for (; index != target_index; index += index > target_index ? -1 : 1)
            {
                _selections.Add(_sortedAssetList[index].Asset);
            }

            _selections.Add(asset.Asset);

            _currentSelectionEntry = asset;
            SelectionChanged();
        }

        protected void SelectionChanged()
        {
            _currentObject = null;
            foreach (Object selection in _selections)
            {
                _currentObject = selection;
                break;
            }

            _currentEditor.SetTargetObjects(_selections.ToArray());
            Repaint();
        }

        protected void RecordCurrentSelection(Object nextSelection = null)
        {
            if (_currentSelectionEntry != null && _currentSelectionEntry.Asset == nextSelection) return;

            if (_isSelectedPrevious)
            {
                _isSelectedPrevious = false;
                return;
            }

            if (_currentSelectionEntry != null)
            {
                ScriptableObject entry = (ScriptableObject)_currentSelectionEntry.Asset;

                if (EditorHistory.Count > 0 && entry == EditorHistory.Last()) return;

                EditorHistory.AddLast(entry);
                while (EditorHistory.Count > EDITOR_HISTORY_MAX) EditorHistory.RemoveFirst();
            }
        }


        protected void SelectPrevious()
        {
            while (EditorHistory.Count > 0 && EditorHistory.Last() == null) EditorHistory.RemoveLast();

            if (EditorHistory.Count <= 0) return;

            ScriptableObject lastObject = EditorHistory.Last();

            EditorHistory.RemoveLast();
            _isSelectedPrevious = true;

            OpenObject(lastObject);
        }


        protected void OnInspect(Rect area)
        {
            area.x = area.y = 0;

            if (_currentEditor == null) return;

            _inspectScroll = EditorGUILayout.BeginScrollView(_inspectScroll);
            _currentEditor.RenderInspector();

            EditorGUILayout.EndScrollView();
        }

        protected static string ReverseString(string s)
        {
            char[] arr = s.ToCharArray();

            Array.Reverse(arr);

            return new string(arr);
        }

        protected void RenameCurrentEntry()
        {
            if (_currentObject == null) return;

            Rect rect = new()
            {
                position = position.position
            };

            rect.y += 42;
            rect.x += 32;

            rect.width = BROWSE_AREA_WIDTH - 34;
            rect.height = 18;

            PopupWindow.Show(rect, new CreateNewEntryPopup(rect, _currentObject.name, FinishRenameCurrentEntry));
        }

        protected void FinishRenameCurrentEntry(string newName)
        {
            if (_currentObject == null) return;

            string path = AssetDatabase.GetAssetPath(_currentObject);

            if (AssetDatabase.LoadAssetAtPath<Object>(path) != _currentObject) return;

            string folderPath = path.Substring(0, path.LastIndexOf('/') + 1);


            string newPath = $"{folderPath}{newName}{SUFFIX_NAME}";

            if (AssetDatabase.LoadAssetAtPath<Object>(newPath) != null) return;

            _currentObject.name = newName;

            EditorUtility.SetDirty(_currentObject);
            AssetDatabase.RenameAsset(path, $"{newName}{SUFFIX_NAME}");
            AssetDatabase.SaveAssets();

            SyncAssetEntry(_currentSelectionEntry);
        }

        protected void ImportEntries()
        {
            var r = new Rect
            {
                position = position.position
            };

            r.y += 42;
            r.x += 32;
            r.width = BROWSE_AREA_WIDTH - 34;
            r.height = 18;


            string path = EditorUtility.OpenFilePanel(TOOLBAR_PLUS_NAME, "", "tsv");
            if (path.Length != 0)
            {
                FinishImportEntries(path);
            }
        }

        protected void CreateNewEntry()
        {
            var rect = new Rect
            {
                position = position.position
            };

            rect.y += 42;
            rect.x += 32;

            rect.width = BROWSE_AREA_WIDTH - 34;
            rect.height = 18;

            PopupWindow.Show(rect, new CreateNewEntryPopup(rect, "", FinishCreateNewEntry));
        }

        protected void FinishCreateNewEntry(string name)
        {
            CreateNewEntry(name);
            Repaint();
        }

        protected void FinishImportEntries(string directory)
        {
            Repaint();

            _currentEditor.ImportBatchData(directory, AddAssetEntry);
        }


        protected void CreateNewEntry(string name)
        {
            string path;

            if (_currentEditor.CreateDataFolder)
            {
                AssetDatabase.CreateFolder(_currentEditor.DefaultStoragePath, name);
                path = $"{_currentEditor.DefaultStoragePath}/{name}/{name}.asset";
            }
            else
                path = $"{_currentEditor.DefaultStoragePath}/{name}.asset";

            ScriptableObject instance = CreateInstance(_currentType);
            instance.name = name;
            AssetDatabase.CreateAsset(instance, path);
            AddAssetEntry(instance);
        }

        #region Context Menu

        protected void OnGUI()
        {
            Rect pos = position;
            pos.x -= pos.xMin;
            pos.y -= pos.yMin;

            Rect rectBrowse = pos;
            Rect rectInspect = pos;
            rectBrowse.width = BROWSE_AREA_WIDTH;
            rectInspect.x += BROWSE_AREA_WIDTH;

            rectInspect.width -= BROWSE_AREA_WIDTH;

            GUILayout.BeginArea(rectBrowse, EditorStyles.helpBox);
            OnBrowse(rectBrowse);
            GUILayout.EndArea();

            GUILayout.BeginArea(rectInspect, EditorStyles.helpBox);
            OnInspect(rectInspect);
            GUILayout.EndArea();
        }

        private void OnBrowse(Rect area)
        {
            area.x = area.y = 0;

            string dummyControlName = $"{GetHashCode()}{FILTER_DUMMY}";
            string filterControlName = $"{GetHashCode()}{FILTER}";
            string focusControlName = $"{GetHashCode()}{BROWSE_FOCUS_ID}";

            GUI.color = Color.clear;

            Rect dummyControlRect = EditorGUILayout.GetControlRect(false, 0);

            dummyControlRect.width = 0;
            GUI.SetNextControlName(dummyControlName);
            EditorGUI.TextField(dummyControlRect, "");
            GUI.color = Color.white;

            int newEditorTypeIndex = EditorGUILayout.Popup(_currentTypeIndex, _browsableTypeNames);

            if (newEditorTypeIndex != _currentTypeIndex)
                GetType(BrowsableTypes[newEditorTypeIndex]);

            EditorGUILayout.BeginHorizontal();

            GUI.enabled = _currentEditor?.DefaultStoragePath != null;
            if (GUILayout.Button(EditorGUIUtility.IconContent("Toolbar Plus"), EditorStyles.miniButton,
                    GUILayout.Width(24), GUILayout.Height(18)))
                CreateNewEntry();
            GUI.enabled = true;

            GUI.enabled = _currentEditor?.DefaultStoragePath != null;
            if (GUILayout.Button(EditorGUIUtility.IconContent("Toolbar Plus More"), EditorStyles.miniButton,
                    GUILayout.Width(24), GUILayout.Height(18)))
                ImportEntries();
            GUI.enabled = true;

            GUI.enabled = _currentEditor?.ContextMenu != null;
            if (GUILayout.Button(EditorGUIUtility.IconContent("SettingsIcon"), EditorStyles.miniButton,
                    GUILayout.Width(24), GUILayout.Height(18)))
                _currentEditor?.ContextMenu.ShowAsContext();
            GUI.enabled = true;

            GUI.SetNextControlName(filterControlName);
            var filter_text = EditorGUILayout.TextField(_filterText, (GUIStyle)"SearchTextField");
            if (filter_text.Length != _filterText.Length) ResortEntries(filter_text);

            if (GUILayout.Button(" ", (GUIStyle)"SearchCancelButton"))
            {
                ResortEntries("");
                GUIUtility.keyboardControl = 0;
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();

            var editing_text = EditorGUIUtility.editingTextField;
            if (!editing_text && Event.current.type == EventType.KeyDown &&
                Event.current.keyCode == KeyCode.LeftArrow && (Event.current.control || Event.current.command))
            {
                Event.current.Use();
                SelectPrevious();
            }

            if (GUI.GetNameOfFocusedControl() == dummyControlName)
                GUI.FocusControl(filterControlName);

            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.F && Event.current.control)
            {
                GUI.FocusControl(dummyControlName);
                Event.current.Use();
            }

            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.N &&
                (Event.current.control || Event.current.command))
            {
                Event.current.Use();
                CreateNewEntry();
            }

            if (Event.current.type == EventType.KeyDown && (Event.current.keyCode == KeyCode.F2 ||
                                                            ((Event.current.control || Event.current.command) &&
                                                             Event.current.keyCode == KeyCode.R)))
            {
                Event.current.Use();
                RenameCurrentEntry();
            }

            if (GUI.GetNameOfFocusedControl() == filterControlName && Event.current.type == EventType.Layout)
            {
                if (Event.current.keyCode == KeyCode.Escape)
                {
                    ResortEntries("");
                    GUI.FocusControl(dummyControlName);
                }

                if (Event.current.keyCode == KeyCode.UpArrow || Event.current.keyCode == KeyCode.DownArrow ||
                    Event.current.keyCode == KeyCode.PageUp || Event.current.keyCode == KeyCode.PageDown)
                {
                    GUI.FocusControl(focusControlName);
                }
            }


            _browseScroll =
                GUILayout.BeginScrollView(_browseScroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar);
            var rect_entry = new Rect(0, 0, area.width, ENTRY_LINE_HEIGHT);
            foreach (var asset in _sortedAssetList)
                RenderAssetEntry(asset, ref rect_entry);
            EditorGUILayout.EndScrollView();

            var scroll_rect = GUILayoutUtility.GetLastRect();

            GUI.SetNextControlName(focusControlName);
            GUI.color = Color.clear;
            EditorGUI.Toggle(scroll_rect, true);
            GUI.color = Color.white;
            _isControlFocused = GUI.GetNameOfFocusedControl() == focusControlName;

            if (_isControlFocused && _sortedAssetList.Count > 0 && Event.current.type == EventType.KeyDown &&
                Event.current.keyCode == KeyCode.Escape)
            {
                ResortEntries("");
                GUI.FocusControl(filterControlName);
            }

            if (_isControlFocused && _sortedAssetList.Count > 0 && Event.current.type == EventType.KeyDown)
            {
                var start_selection_index = _sortedAssetList.IndexOf(_startSelectionEntry);
                var current_selection_index = _sortedAssetList.IndexOf(_currentSelectionEntry);
                var min_index = 0;
                var max_index = _sortedAssetList.Count - 1;
                var page_index_count = Mathf.Max(1, Mathf.FloorToInt(scroll_rect.height / ENTRY_LINE_HEIGHT) - 1);

                if (Event.current.keyCode == KeyCode.UpArrow)
                {
                    var target_asset = current_selection_index < 0
                        ? _currentSelectionEntry = _sortedAssetList[max_index]
                        : _sortedAssetList[Mathf.Max(min_index, current_selection_index - 1)];
                    if (Event.current.shift) SelectionToRange(target_asset);
                    else SelectionSingle(target_asset);
                }
                else if (Event.current.keyCode == KeyCode.DownArrow)
                {
                    var target_asset = current_selection_index < 0
                        ? _currentSelectionEntry = _sortedAssetList[min_index]
                        : _sortedAssetList[Mathf.Min(max_index, current_selection_index + 1)];
                    if (Event.current.shift) SelectionToRange(target_asset);
                    else SelectionSingle(target_asset);
                }
                else if (Event.current.keyCode == KeyCode.PageUp)
                {
                    var target_asset = current_selection_index < 0
                        ? _currentSelectionEntry = _sortedAssetList[max_index]
                        : _sortedAssetList[Mathf.Max(min_index, current_selection_index - page_index_count)];
                    if (Event.current.shift) SelectionToRange(target_asset);
                    else SelectionSingle(target_asset);
                }
                else if (Event.current.keyCode == KeyCode.PageDown)
                {
                    var target_asset = current_selection_index < 0
                        ? _currentSelectionEntry = _sortedAssetList[min_index]
                        : _sortedAssetList[Mathf.Min(max_index, current_selection_index + page_index_count)];
                    if (Event.current.shift) SelectionToRange(target_asset);
                    else SelectionSingle(target_asset);
                }
                else if (Event.current.keyCode == KeyCode.Home)
                {
                    var target_asset = _sortedAssetList[min_index];
                    if (Event.current.shift) SelectionToRange(target_asset);
                    else SelectionSingle(target_asset);
                }
                else if (Event.current.keyCode == KeyCode.End)
                {
                    var target_asset = _sortedAssetList[max_index];
                    if (Event.current.shift) SelectionToRange(target_asset);
                    else SelectionSingle(target_asset);
                }
                else if ((Event.current.command || Event.current.control) && Event.current.keyCode == KeyCode.A)
                    SelectionSetAll();

                current_selection_index = _sortedAssetList.IndexOf(_currentSelectionEntry);
                var selection_y = current_selection_index * ENTRY_LINE_HEIGHT;
                if (selection_y < _browseScroll.y) _browseScroll.y = Mathf.Max(0, selection_y - ENTRY_LINE_HEIGHT / 2);
                if (selection_y > _browseScroll.y + scroll_rect.height - ENTRY_LINE_HEIGHT * 2)
                {
                    var max = ENTRY_LINE_HEIGHT * _sortedAssetList.Count - scroll_rect.height + 14;
                    _browseScroll.y = Mathf.Min(max, selection_y - scroll_rect.height + 50);
                }
            }

            #region SHIFT MOVE SELECTION WHILE EDITING

            if (!_isControlFocused && _sortedAssetList.Count > 0 &&
                Event.current.type == EventType.KeyDown && (Event.current.control || Event.current.command))
            {
                var start_selection_index = _sortedAssetList.IndexOf(_startSelectionEntry);
                var current_selection_index = _sortedAssetList.IndexOf(_currentSelectionEntry);
                var min_index = 0;
                var max_index = _sortedAssetList.Count - 1;
                var page_index_count = Mathf.Max(1, Mathf.FloorToInt(scroll_rect.height / ENTRY_LINE_HEIGHT) - 1);

                if (Event.current.keyCode == KeyCode.UpArrow)
                {
                    var target_asset = current_selection_index < 0
                        ? _currentSelectionEntry = _sortedAssetList[max_index]
                        : _sortedAssetList[Mathf.Max(min_index, current_selection_index - 1)];
                    SelectionSingle(target_asset);
                    GUIUtility.keyboardControl = 0;
                    ;
                }
                else if (Event.current.keyCode == KeyCode.DownArrow)
                {
                    var target_asset = current_selection_index < 0
                        ? _currentSelectionEntry = _sortedAssetList[min_index]
                        : _sortedAssetList[Mathf.Min(max_index, current_selection_index + 1)];
                    SelectionSingle(target_asset);
                    GUIUtility.keyboardControl = 0;
                }
                else if (Event.current.keyCode == KeyCode.PageUp)
                {
                    var target_asset = current_selection_index < 0
                        ? _currentSelectionEntry = _sortedAssetList[max_index]
                        : _sortedAssetList[Mathf.Max(min_index, current_selection_index - page_index_count)];
                    SelectionSingle(target_asset);
                    GUIUtility.keyboardControl = 0;
                }
                else if (Event.current.keyCode == KeyCode.PageDown)
                {
                    var target_asset = current_selection_index < 0
                        ? _currentSelectionEntry = _sortedAssetList[min_index]
                        : _sortedAssetList[Mathf.Min(max_index, current_selection_index + page_index_count)];
                    SelectionSingle(target_asset);
                    GUIUtility.keyboardControl = 0;
                }
            }

            #endregion
        }

        #endregion
    }
}