using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace IndiGames.Tools.ScriptableObjectBrowser
{
    public abstract class ScriptableObjectBrowserEditor
    {
        public ScriptableObjectBrowser Browser;

        /// <summary>
        /// The <c>DefaultStoragePath</c> property is
        /// used to set the default path for the Data.
        /// <example>
        /// <code>
        /// public YourClassEditor()
        /// {
        ///    this.DefaultStoragePath = "Assets/ScriptableObjects/Data/FolderSampleName";
        /// }
        /// </code>
        /// </example>
        /// </summary>
        public string DefaultStoragePath { get; protected set; } = null;

        /// <summary>
        /// The <c>CreateDataFolder</c> property is
        /// used to know if the <see cref="ScriptableObjectBrowser"/>
        /// should create a folder for the Data. 
        /// <example>
        /// <code>
        /// public YourClassEditor()
        /// {
        ///     this.CreateDataFolder = true;
        /// }
        /// </code>
        /// </example>
        /// </summary>

        public bool CreateDataFolder { get; protected set; } = false;

        public GenericMenu ContextMenu { get; protected set; } = null;

        protected UnityEditor.Editor CachedEditor = null;

        public virtual void SetTargetObjects(Object[] objs) { }
        public virtual void RenderInspector() { }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="directory">Directory of input file</param>
        /// <param name="callback">callback for adding new ScriptableObject to ListView</param>
        public virtual void ImportBatchData(string directory, Action<ScriptableObject> callback) { }

        private T CreateAsset<T>(string path) where T : Object
        {
            if (path.EndsWith(".asset") == false) path += ".asset";
            if (new FileInfo(path).Exists) return null;

            var result = Activator.CreateInstance<T>();
            AssetDatabase.CreateAsset(result, path);
            AssetDatabase.ImportAsset(path);

            return result;
        }

        protected T CreateLocalAsset<T>(ScriptableObject obj, string path) where T : Object
        {
            var folderPath = GetAssetContainingFolder(obj);
            if (string.IsNullOrEmpty(path)) return null;

            path = Path.Combine(folderPath, path);
            return CreateAsset<T>(path);
        }

        protected T CreateSubAsset<T>(ScriptableObject obj, string name) where T : Object
        {
            var path = AssetDatabase.GetAssetPath(obj);
            if (string.IsNullOrEmpty(path)) return null;

            var asset = Activator.CreateInstance<T>();
            asset.name = name;
            AssetDatabase.AddObjectToAsset(asset, path);
            AssetDatabase.ImportAsset(path);

            return asset;
        }

        protected void RemoveAllSubAsset<T>(ScriptableObject obj) where T : Object
        {
            var path = AssetDatabase.GetAssetPath(obj);
            if (AssetDatabase.LoadAssetAtPath<ScriptableObject>(path) != obj) return;

            var assets = AssetDatabase.LoadAllAssetsAtPath(path);
            foreach (var asset in assets)
            {
                if (asset == obj) continue;
                if (!(asset is T)) continue;
                AssetDatabase.RemoveObjectFromAsset(asset);
            }

            AssetDatabase.ImportAsset(path);
        }

        protected void ReimportAsset(ScriptableObject o)
        {
            var path = AssetDatabase.GetAssetPath(o);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        }

        protected void Ping(Object o)
        {
            EditorGUIUtility.PingObject(o);
        }


        private GameObject CreatePrefab(string path, Action<GameObject> onPrefabCreated = null)
        {
            if (path.EndsWith(".prefab") == false) path += ".prefab";

            var go = new GameObject();
            go.hideFlags = HideFlags.HideInHierarchy;

            if (onPrefabCreated != null) onPrefabCreated(go);

            var newGO = PrefabUtility.SaveAsPrefabAssetAndConnect(go, path, InteractionMode.UserAction);

            Object.DestroyImmediate(go);
            newGO.hideFlags = HideFlags.None;
            AssetDatabase.ImportAsset(path);
            return newGO;
        }

        protected GameObject CreateLocalPrefab(Object asset, string relPath,
            Action<GameObject> onPrefabCreated)
        {
            var path = GetAssetContainingFolder(asset);
            if (string.IsNullOrEmpty(path)) return null;

            path = Path.Combine(path, relPath);
            if (path.EndsWith(".prefab") == false) path += ".prefab";

            return CreatePrefab(path, onPrefabCreated);
        }

        protected string CreateLocalFolder(Object asset, string replacePath)
        {
            var path = GetAssetContainingFolder(asset);
            AssetDatabase.CreateFolder(path, replacePath);
            return Path.Combine(path, replacePath);
        }

        private string GetAssetContainingFolder(Object asset)
        {
            string path = AssetDatabase.GetAssetPath(asset);
            if (string.IsNullOrEmpty(path)) return null;

            var dir = new FileInfo(path).Directory?.FullName;
            var i = dir!.IndexOf("Assets", StringComparison.Ordinal);
            dir = dir.Substring(i);

            return dir;
        }

        protected List<T> FindAllAssets<T>() where T : Object
        {
            List<T> results = new List<T>();
            HashSet<string> assetPaths = new HashSet<string>();

            foreach (var objUid in AssetDatabase.FindAssets($"t:{typeof(T).Name}"))
                assetPaths.Add(AssetDatabase.GUIDToAssetPath(objUid));

            foreach (var assetPath in assetPaths)
            foreach (var loadedAsset in AssetDatabase.LoadAllAssetsAtPath(assetPath))
                if (loadedAsset is T)
                    results.Add((T)loadedAsset);

            return results;
        }

        protected List<T> FindAllLocalAssets<T>(Object asset) where T : Object
        {
            List<T> results = new List<T>();
            HashSet<string> assetPaths = new HashSet<string>();
            string path = GetAssetContainingFolder(asset);

            foreach (var objUid in AssetDatabase.FindAssets($"t:{typeof(T).Name}", new[] { path }))
                assetPaths.Add(AssetDatabase.GUIDToAssetPath(objUid));

            foreach (var assetPath in assetPaths)
            foreach (var loadedAsset in AssetDatabase.LoadAllAssetsAtPath(assetPath))
                if (loadedAsset is T)
                    results.Add((T)loadedAsset);

            return results;
        }

        protected void PromptTextInput(string prompt, Action<string> callback)
        {
            PromptForText.Show(prompt, callback);
        }
    }

    public abstract class ScriptableObjectBrowserEditor<T> : ScriptableObjectBrowserEditor where T : Object
    {
        private T _targetObject;

        private T Target => (T)CachedEditor.target;

        private IEnumerable<T> Targets
        {
            get
            {
                foreach (Object t in CachedEditor.targets) yield return (T)t;
            }
        }

        public override void SetTargetObjects(Object[] objs)
        {
            if (objs == null || objs.Length <= 0) _targetObject = null;
            else _targetObject = (T)objs[0];

            UnityEditor.Editor.CreateCachedEditor(objs, null, ref CachedEditor);
            if (CachedEditor != null) CachedEditor.ResetTarget();
        }

        public override void RenderInspector()
        {
            if (_targetObject == null) return;
            CustomInspector(CachedEditor.serializedObject);
        }

        private void DrawDefaultInspector()
        {
            this.CachedEditor.OnInspectorGUI();
        }

        protected virtual void CustomInspector(SerializedObject obj)
        {
            DrawDefaultInspector();
        }

        private void ButtonRun(string label, Action<T> action)
        {
            if (Targets.Count() > 1) return;
            if (GUILayout.Button(label, EditorStyles.miniButton))
            {
                action(Target);
                SetDirty(Target);
            }
        }

        protected void ButtonRunPrompt(string label, string prompt, Action<T, string> action)
        {
            ButtonRun(label, (t) => PromptForText.Show(prompt, (str) =>
            {
                action(t, str);
                SetDirty(t);
            }));
        }

        private void ButtonRunForEach(string label, Action<T> action)
        {
            if (GUILayout.Button(label, EditorStyles.miniButton)) RunForEach(action);
        }

        protected void ButtonRunForEachPrompt(string label, string prompt, Action<T, string> action)
        {
            ButtonRunForEach(label, (t) => PromptForText.Show(prompt, (str) =>
            {
                action(t, str);
                SetDirty(t);
            }));
        }

        private void RunForEach(Action<T> action)
        {
            foreach (var target in this.Targets)
            {
                action(target);
                SetDirty(target);
            }
        }

        private void SetDirty(Object target)
        {
            EditorUtility.SetDirty(target);
        }

        protected void Space()
        {
            GUILayout.Space(32);
        }

        protected A GetAssetFromName<A>(string assetName) where A : Object
        {
            string[] paths = AssetDatabase.FindAssets($"t:{typeof(A).Name} " + assetName);

            if (paths.Length <= 0 || assetName == "") return null;

            var assetPath = AssetDatabase.GUIDToAssetPath(paths[0]);
            var asset = (A)AssetDatabase.LoadAssetAtPath(assetPath, typeof(A));
            return asset;
        }

        protected A[] GetAssetsFromType<A>() where A : Object
        {
            string targetType = typeof(A).Name;
            string[] assetGUIDs = AssetDatabase.FindAssets($"t:{targetType}");

            List<A> assetsList = new List<A>();

            foreach (string guid in assetGUIDs)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                Object[] assetsAtPath = AssetDatabase.LoadAllAssetsAtPath(assetPath);

                foreach (Object asset in assetsAtPath)
                {
                    if (asset is A a)
                    {
                        assetsList.Add(a);
                    }
                }
            }

            A[] assets = assetsList.ToArray();
            return assets;
        }

        public override void ImportBatchData(string directory, Action<ScriptableObject> callback)
        {
            throw new NotImplementedException();
        }
    }
}