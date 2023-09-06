using System;
using Object = UnityEngine.Object;

namespace IndiGamesEditor.Tools.Editor.ScriptableObjectBrowser
{
    [Serializable]
    public class AssetEntry
    {
        public string Path = String.Empty;
        public string RPath = String.Empty;
        public string Name = String.Empty;
        public Object Asset;
        public int MatchAmount;
        public bool Visible = true;
        public bool Selected = false;

        public AssetEntry()
        {
        }

        public AssetEntry(string path, string name, Object asset)
        {
            Path = path;
            RPath = ReverseString(path);
            Name = name;
            Asset = asset;
        }

        private static string ReverseString(string s)
        {
            char[] charArray = s.ToCharArray();

            Array.Reverse(charArray);

            return new string(charArray);
        }
    }
}