using System;
using UnityEditor;
using UnityEngine;

namespace IndiGames.Tools.ScriptableObjectBrowser
{
    public class CreateNewEntryPopup : PopupWindowContent
    {
        private Rect _position;
        private bool isAutoFocus = true;
        private string _entryValue = String.Empty;

        private readonly bool _isSameName = false;
        private readonly Action<string> _callback;

        public CreateNewEntryPopup(Rect rect, string currentEntryValue, Action<string> callback)
        {
            _entryValue = currentEntryValue;
            _position = rect;
            _callback = callback;
        }

        public override Vector2 GetWindowSize()
        {
            Vector2 size = _position.size;

            if (_isSameName) size.y += size.y + 4;
            return size;
        }


        public override void OnGUI(Rect rect)
        {
            editorWindow.position = _position;
            if (Event.current.keyCode == KeyCode.Escape) editorWindow.Close();

            rect.x = rect.y = 0;
            rect.height = 18;

            GUI.SetNextControlName("Name");

            _entryValue = EditorGUI.TextField(rect, _entryValue);

            GUI.FocusControl("Name");

            if (isAutoFocus)
            {
                if (_entryValue.Length > 0) EditorGUI.FocusTextInControl("Name");
                isAutoFocus = false;
            }

            if (_isSameName)
            {
                rect.y += rect.height + 2;
                rect.x += 2;
                rect.width -= 4;
                EditorGUI.HelpBox(rect, "Object with the same name already exist", MessageType.Error);
            }

            if (Event.current.keyCode == KeyCode.Return) ConfirmNewEntry();
        }

        private void ConfirmNewEntry()
        {
            if (_isSameName) return;

            editorWindow.Close();

            _callback?.Invoke(_entryValue);
        }
    }
}