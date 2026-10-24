// v1.2.1
// 房を選んで髪色をかえるやつ 本体ウィンドウ。
// メニュー「Tools/Metlab/房を選んで髪色をかえるやつ」から開く。
// v1.2.1: メニューを「Tools/Metlab/」に統一（MetLab/Metlabo の表記ゆれ解消）。
// 使い方:
//   1. 対象の髪レンダラー（SkinnedMeshRenderer / MeshRenderer）を指定
//   2. 「編集開始（プレビュー）」で作業開始（この時点ではファイルは作られない）
//   3. 色と塗る方式を選ぶ
//   4. シーンビューで髪をクリック → そのふさを即座に塗る。同じ色でもう一度クリックすると解除
//   5. 「確定して保存」で複製メッシュ・マテリアル・バックアップをアセット化（アップロード可能に）
//   6. 「元データに戻す」でいつでも編集前のメッシュ・マテリアルへ戻せる
//      （元メッシュ/元マテリアルのファイルは一切変更しない）
// v1.2.0 変更点:
//   ・アセット生成を「確定して保存」時のみに変更（編集中はメモリ上プレビュー）
//   ・スキンメッシュのワールド座標を手動スキニングで直接計算し、クリック位置ズレを解消

using System;
using UnityEditor;
using UnityEngine;

namespace Metlab.HairColor
{
    public class HairColorToolWindow : EditorWindow
    {
        private const string RootFolder = "Assets/MetlabGenerated/房を選んで髪色をかえるやつ";

        private Renderer _target;
        private Color _paintColor = Color.white;
        private ColorBlendMode _blendMode = ColorBlendMode.Normal;

        // 復元セクション用
        private HairColorBackup _restoreBackup;
        private Renderer _restoreTarget;

        [NonSerialized] private HairColorSession _session;
        [NonSerialized] private int _hoveredIsland = -1;
        [NonSerialized] private Material _glMaterial;

        private Vector2 _scroll;

        private static readonly Color FillHover = new Color(0.25f, 0.8f, 1f, 0.35f);
        private static readonly Color FillPaintedPreview = new Color(1f, 0.82f, 0.15f, 0.12f);

        [MenuItem("Tools/Metlab/房を選んで髪色をかえるやつ")]
        public static void Open()
        {
            var win = GetWindow<HairColorToolWindow>();
            win.titleContent = new GUIContent("房を選んで髪色をかえるやつ");
            win.minSize = new Vector2(320, 460);
            win.Show();
        }

        private void OnEnable()
        {
            SceneView.duringSceneGui += OnSceneGUI;
            if (_target == null && Selection.activeGameObject != null)
                _target = Selection.activeGameObject.GetComponent<Renderer>();
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            if (_glMaterial != null) DestroyImmediate(_glMaterial);
        }

        // =============================================================
        //  ウィンドウGUI
        // =============================================================

        private void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            EditorGUILayout.LabelField("房を選んで髪色をかえるやつ", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "髪をクリックして『ひとふさ（連結した塊）』単位で色を変えるツールです。\n" +
                "元のメッシュ・マテリアルのファイルは変更しません（複製に対して編集します）。",
                MessageType.Info);

            EditorGUILayout.Space();

            using (new EditorGUI.DisabledScope(_session != null && _session.IsActive))
            {
                _target = (Renderer)EditorGUILayout.ObjectField(
                    "対象レンダラー", _target, typeof(Renderer), true);

                if (GUILayout.Button("選択中のオブジェクトから取得"))
                {
                    if (Selection.activeGameObject != null)
                        _target = Selection.activeGameObject.GetComponent<Renderer>();
                }
            }

            EditorGUILayout.Space();

            if (_session == null || !_session.IsActive)
                DrawBeginSection();
            else
                DrawEditSection();

            EditorGUILayout.Space(16);
            DrawRestoreSection();

            EditorGUILayout.EndScrollView();
        }

        private void DrawBeginSection()
        {
            using (new EditorGUI.DisabledScope(_target == null || HairColorSession.GetSharedMesh(_target) == null))
            {
                if (GUILayout.Button("編集開始（プレビュー・ファイルは作りません）", GUILayout.Height(32)))
                    BeginSession();
            }
            if (_target != null && HairColorSession.GetSharedMesh(_target) == null)
                EditorGUILayout.HelpBox("このレンダラーにはメッシュが設定されていません。", MessageType.Warning);
        }

        private void DrawEditSection()
        {
            EditorGUILayout.LabelField($"ふさ数: {_session.Islands.IslandCount}　/　着色済み: {_session.IslandColors.Count}");
            if (_session.Committed)
                EditorGUILayout.LabelField("保存先:", _session.OutputFolder, EditorStyles.miniLabel);
            else
                EditorGUILayout.LabelField("状態:", "プレビュー中（未保存）", EditorStyles.miniLabel);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("塗る設定", EditorStyles.boldLabel);
            _paintColor = EditorGUILayout.ColorField("色", _paintColor);

            using (var check = new EditorGUI.ChangeCheckScope())
            {
                _blendMode = (ColorBlendMode)EditorGUILayout.Popup(
                    "塗る方式",
                    (int)_blendMode,
                    new[] { "通常（置き換え）", "乗算（元の色に重ねる）" });
                if (check.changed)
                {
                    _session.BlendMode = _blendMode;
                    _session.Apply(); // 既に塗った箇所も新方式で塗り直す
                    SceneView.RepaintAll();
                }
            }

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "シーンビューの操作:\n" +
                "・クリック … そのふさを今の色で即座に塗る\n" +
                "・同じ色のふさをもう一度クリック … 塗りを解除\n" +
                "・違う色で塗られたふさをクリック … 今の色で塗り直し",
                MessageType.None);

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(_session.WorldVertices == null))
            {
                if (GUILayout.Button("クリック判定用の座標を再取得（ポーズを変えた時）"))
                {
                    _session.RecomputeWorldVertices();
                    SceneView.RepaintAll();
                }
            }

            EditorGUILayout.Space(12);
            using (new EditorGUI.DisabledScope(_session.IslandColors.Count == 0))
            {
                var prev = GUI.backgroundColor;
                GUI.backgroundColor = new Color(0.6f, 0.9f, 0.6f);
                if (GUILayout.Button("確定して保存（アセット化・アップロードに使える状態に）", GUILayout.Height(32)))
                {
                    if (_session.Commit())
                        EditorUtility.DisplayDialog("保存完了",
                            "複製を保存しました。\n" + _session.OutputFolder, "OK");
                }
                GUI.backgroundColor = prev;
            }
            EditorGUILayout.HelpBox("保存するまでファイルは作られません（メモリ上のプレビューです）。アップロード前に必ず『確定して保存』してください。", MessageType.None);

            EditorGUILayout.Space(8);
            if (GUILayout.Button("すべての色変えを解除（初期状態へ）"))
            {
                if (EditorUtility.DisplayDialog("確認", "すべての色変えを解除して初期状態に戻します。よろしいですか？", "戻す", "キャンセル"))
                {
                    _session.ResetAll();
                    SceneView.RepaintAll();
                }
            }

            if (GUILayout.Button("元データに戻す（オブジェクトを編集前のメッシュ・色へ）"))
            {
                if (EditorUtility.DisplayDialog("確認",
                    "対象オブジェクトを編集前の元メッシュ・元マテリアルに戻します。\nよろしいですか？", "元に戻す", "キャンセル"))
                {
                    _session.RestoreOriginal();
                    SceneView.RepaintAll();
                }
            }

            EditorGUILayout.Space();
            if (GUILayout.Button("編集を終了（セッションを閉じる）"))
                EndSession();
        }

        private void EndSession()
        {
            // 未保存の変更がある場合は、元データへ戻してから閉じる（メモリ上の複製が残らないように）
            if (_session.IslandColors.Count > 0 && !_session.Committed)
            {
                int choice = EditorUtility.DisplayDialogComplex("未保存の変更があります",
                    "まだ『確定して保存』していません。どうしますか？",
                    "保存して終了", "キャンセル", "破棄して元に戻す");
                if (choice == 1) return;                 // キャンセル
                if (choice == 0) { if (!_session.Commit()) return; }   // 保存して終了
                else _session.RestoreOriginal();          // 破棄して元に戻す
            }

            _session = null;
            _hoveredIsland = -1;
            SceneView.RepaintAll();
        }

        private void DrawRestoreSection()
        {
            EditorGUILayout.LabelField("あとから元データに戻す", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "編集を終了した後でも、複製フォルダ内の「_backup」を指定すれば元に戻せます。", MessageType.None);

            _restoreBackup = (HairColorBackup)EditorGUILayout.ObjectField(
                "バックアップ(_backup)", _restoreBackup, typeof(HairColorBackup), false);
            _restoreTarget = (Renderer)EditorGUILayout.ObjectField(
                "戻す対象レンダラー", _restoreTarget, typeof(Renderer), true);

            using (new EditorGUI.DisabledScope(_restoreBackup == null || _restoreTarget == null))
            {
                if (GUILayout.Button("このバックアップで元に戻す"))
                {
                    if (HairColorSession.RestoreFromBackup(_restoreTarget, _restoreBackup))
                    {
                        EditorUtility.DisplayDialog("完了", "元のメッシュ・マテリアルに戻しました。", "OK");
                        SceneView.RepaintAll();
                    }
                }
            }
        }

        // =============================================================
        //  セッション操作
        // =============================================================

        private void BeginSession()
        {
            _session = HairColorSession.Begin(_target, RootFolder);
            if (_session != null) _session.BlendMode = _blendMode;
            _hoveredIsland = -1;
            SceneView.RepaintAll();
            Repaint();
        }

        /// <summary>クリックされたふさを塗る／解除する（トグル）。</summary>
        private void PaintOrToggle(int island)
        {
            if (_session == null || !_session.IsActive) return;

            if (_session.TryGetIslandColor(island, out Color current))
            {
                // 既に塗られている：同じ色なら解除、違う色なら塗り直し
                if (ApproximatelyEqual(current, _paintColor))
                    _session.ClearIslandColor(island);
                else
                    _session.SetIslandColor(island, _paintColor);
            }
            else
            {
                _session.SetIslandColor(island, _paintColor);
            }

            _session.Apply();
            SceneView.RepaintAll();
            Repaint();
        }

        private static bool ApproximatelyEqual(Color a, Color b)
        {
            const float e = 0.004f; // 8bit 1段分程度
            return Mathf.Abs(a.r - b.r) < e && Mathf.Abs(a.g - b.g) < e
                && Mathf.Abs(a.b - b.b) < e && Mathf.Abs(a.a - b.a) < e;
        }

        // =============================================================
        //  シーンビュー操作（ピッキング＋ハイライト）
        // =============================================================

        private void OnSceneGUI(SceneView sv)
        {
            if (_session == null || !_session.IsActive) return;
            if (_session.WorldVertices == null) return;

            Event e = Event.current;

            // クリックを受け取るためのデフォルトコントロールを確保（他オブジェクトの選択を防ぐ）
            int controlId = GUIUtility.GetControlID(FocusType.Passive);
            if (e.type == EventType.Layout)
                HandleUtility.AddDefaultControl(controlId);

            if (e.type == EventType.MouseMove)
            {
                int hit = PickIsland(e.mousePosition);
                if (hit != _hoveredIsland)
                {
                    _hoveredIsland = hit;
                    sv.Repaint();
                }
            }
            else if (e.type == EventType.MouseDown && e.button == 0 && !e.alt)
            {
                int island = PickIsland(e.mousePosition);
                if (island >= 0)
                    PaintOrToggle(island);
                e.Use();
            }

            if (e.type == EventType.Repaint)
                DrawHighlights();
        }

        private int PickIsland(Vector2 guiPos)
        {
            Ray ray = HandleUtility.GUIPointToWorldRay(guiPos);
            int tri = _session.Islands.Raycast(_session.WorldVertices, ray);
            return tri >= 0 ? _session.Islands.TriIslandId[tri] : -1;
        }

        private void DrawHighlights()
        {
            EnsureGLMaterial();
            var islands = _session.Islands;
            Vector3[] wv = _session.WorldVertices;

            _glMaterial.SetPass(0);
            GL.PushMatrix();
            GL.Begin(GL.TRIANGLES);

            // 着色済みのふさをうっすら表示（どこを塗ったか把握しやすくする）
            GL.Color(FillPaintedPreview);
            foreach (var kv in _session.IslandColors)
            {
                if (kv.Key == _hoveredIsland) continue;
                if (islands.IslandToTriangles.TryGetValue(kv.Key, out var tris))
                    DrawTriangles(islands, wv, tris);
            }

            // ホバー中のふさ（これから塗る対象）をはっきり表示
            if (_hoveredIsland >= 0
                && islands.IslandToTriangles.TryGetValue(_hoveredIsland, out var hoverTris))
            {
                GL.Color(FillHover);
                DrawTriangles(islands, wv, hoverTris);
            }

            GL.End();
            GL.PopMatrix();
        }

        private static void DrawTriangles(HairMeshIslands islands, Vector3[] wv, System.Collections.Generic.List<int> triangleList)
        {
            foreach (int t in triangleList)
            {
                GL.Vertex(wv[islands.TriV0[t]]);
                GL.Vertex(wv[islands.TriV1[t]]);
                GL.Vertex(wv[islands.TriV2[t]]);
            }
        }

        private void EnsureGLMaterial()
        {
            if (_glMaterial != null) return;
            Shader shader = Shader.Find("Hidden/Internal-Colored");
            _glMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            // 半透明オーバーレイ（面に沿って表示・深度は書き込まない）
            _glMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _glMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            _glMaterial.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            _glMaterial.SetInt("_ZWrite", 0);
            _glMaterial.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.LessEqual);
        }
    }
}
