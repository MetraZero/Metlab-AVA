// MET_FolderColorTag
// v1.3.0
// 実際のAssetフォルダ構造には一切変更を加えず、Projectウィンドウ上でのみ
// フォルダに色タグを付ける機能 + 色選択用の共通ポップアップ(MET_ColorPickerPopup)。
// データは ProjectSettings/MET_FolderColors.json に保存される(Assets非汚染)。

using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Metlab.Shortcut
{
    /// <summary>
    /// 実際のAssetフォルダ構造には一切変更を加えず、
    /// Projectウィンドウ上でのみフォルダに色タグを付ける機能。
    /// データは ProjectSettings/MET_FolderColors.json に保存される(Assets非汚染)。
    /// </summary>
    [InitializeOnLoad]
    public static class MET_FolderColorTag
    {
        private const string DataPath = "ProjectSettings/MET_FolderColors.json";
        private static readonly Dictionary<string, Color> colorMap = new Dictionary<string, Color>();

        [Serializable]
        private class Entry { public string guid; public float r, g, b, a; }
        [Serializable]
        private class Data { public List<Entry> entries = new List<Entry>(); }

        static MET_FolderColorTag()
        {
            Load();
            EditorApplication.projectWindowItemOnGUI += OnGUI;
        }

        private static void Load()
        {
            colorMap.Clear();
            if (!File.Exists(DataPath)) return;
            try
            {
                var data = JsonUtility.FromJson<Data>(File.ReadAllText(DataPath));
                if (data?.entries == null) return;
                foreach (var e in data.entries)
                    colorMap[e.guid] = new Color(e.r, e.g, e.b, e.a);
            }
            catch
            {
                // 破損データは無視して初期化状態のまま進む
            }
        }

        private static void Save()
        {
            var data = new Data();
            foreach (var kv in colorMap)
                data.entries.Add(new Entry { guid = kv.Key, r = kv.Value.r, g = kv.Value.g, b = kv.Value.b, a = kv.Value.a });
            File.WriteAllText(DataPath, JsonUtility.ToJson(data, true));
        }

        private static void OnGUI(string guid, Rect selectionRect)
        {
            if (!colorMap.TryGetValue(guid, out var color)) return;

            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path) || !AssetDatabase.IsValidFolder(path)) return;

            bool isListView = selectionRect.height <= 20f; // 一覧(ツリー)表示かグリッド表示かの簡易判定
            var prevColor = GUI.color;
            GUI.color = new Color(color.r, color.g, color.b, 0.35f);

            if (isListView)
            {
                var bg = new Rect(selectionRect.x + 14, selectionRect.y, selectionRect.width - 14, selectionRect.height);
                GUI.DrawTexture(bg, EditorGUIUtility.whiteTexture);
            }
            else
            {
                var bar = new Rect(selectionRect.x, selectionRect.yMax - 4, selectionRect.width, 4);
                GUI.DrawTexture(bar, EditorGUIUtility.whiteTexture);
            }

            GUI.color = prevColor;
        }

        public static void SetColor(string guid, Color color)
        {
            colorMap[guid] = color;
            Save();
            EditorApplication.RepaintProjectWindow();
        }

        public static void ClearColor(string guid)
        {
            if (colorMap.Remove(guid))
            {
                Save();
                EditorApplication.RepaintProjectWindow();
            }
        }

        // --- コンテキストメニュー ---

        [MenuItem("Assets/Metlab/フォルダ色/赤", false, 100)]
        private static void SetRed() => ApplyToSelection(new Color(0.85f, 0.30f, 0.30f));
        [MenuItem("Assets/Metlab/フォルダ色/黄", false, 101)]
        private static void SetYellow() => ApplyToSelection(new Color(0.90f, 0.80f, 0.20f));
        [MenuItem("Assets/Metlab/フォルダ色/緑", false, 102)]
        private static void SetGreen() => ApplyToSelection(new Color(0.30f, 0.80f, 0.40f));
        [MenuItem("Assets/Metlab/フォルダ色/青", false, 103)]
        private static void SetBlue() => ApplyToSelection(new Color(0.30f, 0.60f, 0.90f));
        [MenuItem("Assets/Metlab/フォルダ色/紫", false, 104)]
        private static void SetPurple() => ApplyToSelection(new Color(0.70f, 0.40f, 0.90f));

        [MenuItem("Assets/Metlab/フォルダ色/カスタム...", false, 105)]
        private static void SetCustom() => MET_ColorPickerPopup.Show(ApplyToSelection, Color.white);

        [MenuItem("Assets/Metlab/フォルダ色/解除", false, 120)]
        private static void Clear()
        {
            foreach (var obj in Selection.objects)
            {
                string path = AssetDatabase.GetAssetPath(obj);
                if (!AssetDatabase.IsValidFolder(path)) continue;
                ClearColor(AssetDatabase.AssetPathToGUID(path));
            }
        }

        [MenuItem("Assets/Metlab/フォルダ色/赤", true)]
        [MenuItem("Assets/Metlab/フォルダ色/黄", true)]
        [MenuItem("Assets/Metlab/フォルダ色/緑", true)]
        [MenuItem("Assets/Metlab/フォルダ色/青", true)]
        [MenuItem("Assets/Metlab/フォルダ色/紫", true)]
        [MenuItem("Assets/Metlab/フォルダ色/カスタム...", true)]
        [MenuItem("Assets/Metlab/フォルダ色/解除", true)]
        private static bool ValidateFolderSelected()
            => Selection.objects.Any(o => AssetDatabase.IsValidFolder(AssetDatabase.GetAssetPath(o)));

        private static void ApplyToSelection(Color color)
        {
            foreach (var obj in Selection.objects)
            {
                string path = AssetDatabase.GetAssetPath(obj);
                if (!AssetDatabase.IsValidFolder(path)) continue;
                SetColor(AssetDatabase.AssetPathToGUID(path), color);
            }
        }
    }

    /// <summary>フォルダ色のカスタム選択用の簡易ポップアップ</summary>
    public class MET_ColorPickerPopup : EditorWindow
    {
        private Color color = Color.white;
        private Action<Color> onApply;

        public static void Show(Action<Color> onApply, Color initialColor)
        {
            var win = CreateInstance<MET_ColorPickerPopup>();
            win.onApply = onApply;
            win.color = initialColor;
            win.titleContent = new GUIContent("色を選択");
            var mainPos = EditorGUIUtility.GetMainWindowPosition();
            float x = mainPos.x + (mainPos.width - 260f) * 0.5f;
            float y = mainPos.y + (mainPos.height - 96f) * 0.5f;
            win.position = new Rect(x, y, 260, 96);
            win.ShowUtility();
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(4);
            color = EditorGUILayout.ColorField("色", color);
            EditorGUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("適用"))
            {
                onApply?.Invoke(color);
                Close();
            }
            if (GUILayout.Button("キャンセル"))
            {
                Close();
            }
            EditorGUILayout.EndHorizontal();
        }
    }

    /// <summary>
    /// カスタムアイコン画像の選択用の簡易ポップアップ。
    /// ObjectField を使うことで ShowObjectPicker のコマンド取りこぼしを避け、
    /// 選択結果を確実にコールバックへ返す。
    /// </summary>
    public class MET_IconPickerPopup : EditorWindow
    {
        private Texture tex;
        private Action<Texture> onApply;

        private const float WinW = 300f;
        private const float WinH = 250f;

        public static void Show(Action<Texture> onApply, Texture initial)
        {
            var win = CreateInstance<MET_IconPickerPopup>();
            win.onApply = onApply;
            win.tex = initial;
            win.titleContent = new GUIContent("アイコン画像");
            win.minSize = win.maxSize = new Vector2(WinW, WinH);
            var mainPos = EditorGUIUtility.GetMainWindowPosition();
            float x = mainPos.x + (mainPos.width - WinW) * 0.5f;
            float y = mainPos.y + (mainPos.height - WinH) * 0.5f;
            win.position = new Rect(x, y, WinW, WinH);
            win.ShowUtility();
        }

        private void OnGUI()
        {
            // 全体に余白をとる
            GUILayout.Space(12);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(14);
                using (new EditorGUILayout.VerticalScope())
                {
                    GUILayout.Label("フォルダ/ショートカットに表示する画像", EditorStyles.boldLabel);
                    GUILayout.Space(8);

                    // プレビュー枠(中央に正方形)
                    var line = GUILayoutUtility.GetRect(0, 120, GUILayout.ExpandWidth(true));
                    float sz = 120f;
                    var box = new Rect(line.x + (line.width - sz) * 0.5f, line.y, sz, sz);
                    DrawPreview(box);

                    GUILayout.Space(10);

                    // 選択欄(通常の高さ)
                    tex = (Texture)EditorGUILayout.ObjectField(tex, typeof(Texture), false);

                    GUILayout.Space(12);

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("適用", GUILayout.Height(24)))
                        {
                            onApply?.Invoke(tex);
                            Close();
                        }
                        if (GUILayout.Button("解除", GUILayout.Height(24)))
                        {
                            onApply?.Invoke(null);
                            Close();
                        }
                        if (GUILayout.Button("キャンセル", GUILayout.Height(24)))
                        {
                            Close();
                        }
                    }
                }
                GUILayout.Space(14);
            }
        }

        private void DrawPreview(Rect box)
        {
            // 背景
            EditorGUI.DrawRect(box, new Color(0.16f, 0.16f, 0.16f, 1f));
            // 枠線
            var border = new Color(0f, 0f, 0f, 0.6f);
            EditorGUI.DrawRect(new Rect(box.x, box.y, box.width, 1), border);
            EditorGUI.DrawRect(new Rect(box.x, box.yMax - 1, box.width, 1), border);
            EditorGUI.DrawRect(new Rect(box.x, box.y, 1, box.height), border);
            EditorGUI.DrawRect(new Rect(box.xMax - 1, box.y, 1, box.height), border);

            if (tex != null)
            {
                var inner = new Rect(box.x + 6, box.y + 6, box.width - 12, box.height - 12);
                GUI.DrawTexture(inner, tex, ScaleMode.ScaleToFit);
            }
            else
            {
                var style = new GUIStyle(EditorStyles.centeredGreyMiniLabel) { alignment = TextAnchor.MiddleCenter };
                GUI.Label(box, "画像なし", style);
            }
        }
    }
}