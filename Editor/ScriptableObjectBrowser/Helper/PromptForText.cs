using System;
using UnityEditor;
using UnityEngine;

namespace IndiGamesEditor.Tools.Editor.ScriptableObjectBrowser
{
    public class PromptForText : PopupWindowContent
    {
        public static void Show(string prompt, Action<string> callback)
        {
            PopupWindow.Show(GUILayoutUtility.GetLastRect(), new PromptForText(prompt, callback));
        }

        private string prompt;
        private Action<string> callback;
        private string content = "";

        public PromptForText(string prompt, Action<string> callback)
        {
            this.prompt = prompt;
            this.callback = callback;
        }

        public override Vector2 GetWindowSize()
        {
            return new Vector2(480, 40);
        }

        public override void OnGUI(Rect rect)
        {
            GUILayout.BeginArea(rect);
            EditorGUILayout.LabelField(prompt);

            if (Event.current.type == EventType.KeyDown)
            {
                if (Event.current.keyCode == KeyCode.Escape) editorWindow.Close();
                if (Event.current.keyCode == KeyCode.Return)
                {
                    callback(content);
                    editorWindow.Close();
                }
            }

            GUI.SetNextControlName(GetHashCode().ToString());
            content = EditorGUILayout.TextField(content);
            GUI.FocusControl(GetHashCode().ToString());


            GUILayout.EndArea();
        }
    }
}