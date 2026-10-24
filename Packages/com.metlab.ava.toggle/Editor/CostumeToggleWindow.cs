// v0.12.0
// トグルをつくるやつ 本体ウィンドウ（横長・左右2ペイン・タブUI）。
// v0.12.0: 「保存・読込」を独立タブに分離（読込をオプションから移動）。保存は手動＝任意で、生成時に自動バックアップされる旨を明記。
//          不要になった「対象を全部OFF（トグル基準リセット）」ボタンを削除（初期ONのフォールバック安全性は MA の isDefault が担保）。
// v0.11.0: 拡張名を「トグルメニューをつくるやつ」に変更。メニューを「Tools/メトラボ/」に統一（MetLab/Metlabo の表記ゆれ解消）。
// メニュー「Tools/Metlab/トグルをつくるやつ」から開く。
// タブ:
//   [トグル設定]   左=カテゴリ/アイテムのツリー（アイテムはドラッグで他カテゴリへ移動可）、右=詳細
//   [コーディネイト] 左=コーディネイト一覧、右=各カテゴリの組合せ指定
//   [オプション]   メニュー名・起点（設置先）・中身の構成（単一フォルダ化など）・検証/パラメータ使用量
//   [保存・読込]   設定テンプレートの手動保存(.asset)と読込（別アバター流用・バックアップ用）
// v0.5.0:
//   ・ディゾルブ機能を廃止（lilToon 不透明マテリアルで動作しないため）。トグルは表示/非表示の瞬時切替に統一
// v0.6.0:
//   ・カテゴリに「フォルダ（所属フォルダ名）」を追加。着替え内で身体/服/アクセサリ等の中間フォルダにまとめられる
// v0.7.0:
//   ・自動で隠す条件に「カテゴリ参照（どれかON）」を追加。靴などをまとめて1参照で指定可能に
//   ・アイテムに「競合が全OFFなら自動表示（手動トグルなし）」を追加（下地レイヤー向け）
// v0.8.0:
//   ・メニューアイコン設定（アイテム/カテゴリ/フォルダ/コーディネイト）
//   ・アイテム名の自動補完（最初に割り当てたGameObject名を採用）
//   ・自動表示を条件なしでも可（常時表示）に
// v0.9.0:
//   ・着用時に素体ブレンドシェイプを変更/削除（ShapeChanger、クリッピング対策）
//   ・検証＆Expressionパラメータ使用量の表示（オプションタブ）
//   ・設定テンプレートの保存/読込（ScriptableObject、別アバター流用）
//   ・コーディネイトのシーンプレビュー
// v0.10.0:
//   ・名称を [MET]MA Menu Manager に変更
//   ・アイテムに「すべてのコーディネイトでON」を追加
//   ・カテゴリ構成を折りたたみ可能に（全部畳む/開く）
//   ・アイコン生成機能は廃止（アイコン設定欄は存続）

using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace Metlab.Toggle
{
    public class CostumeToggleWindow : EditorWindow
    {
        private const string ConfigObjectName = "【衣装トグル設定】";
        private const string DragKey = "MACT_ItemDrag";

        private VRCAvatarDescriptor _avatar;
        private CostumeToggleConfig _config;

        private int _tab = 0;
        private int _selGroup = -1, _selItem = -1;
        private int _selPreset = -1;

        private Vector2 _leftScroll, _rightScroll;

        private bool _dragCandidate;
        private Vector2Int _dragFrom;
        // 構造変更はイテレーション後に遅延適用（IMGUIレイアウト不整合を避ける）
        private (int gi, int ii) _pendingDeleteItem = (-1, -1);
        private int _pendingDeleteCat = -1;
        private (int from, int to) _pendingMoveCat = (-1, -1);
        private (int fromGi, int fromIi, int toGi, int toIndex) _pendingMoveItem = (-1, -1, -1, -1);

        private GUIStyle _rowStyle;
        private static readonly Color SelColor = new Color(0.40f, 0.62f, 1f);
        // カテゴリの折りたたみ状態（セッション内・カテゴリ参照で保持）
        private readonly HashSet<CostumeGroup> _collapsed = new HashSet<CostumeGroup>();

        [MenuItem("Tools/Metlab/トグルをつくるやつ")]
        public static void Open()
        {
            var win = GetWindow<CostumeToggleWindow>();
            win.titleContent = new GUIContent("トグルをつくるやつ");
            win.minSize = new Vector2(820, 520);
            win.Show();
        }

        private void OnEnable()
        {
            if (_avatar == null && Selection.activeGameObject != null)
                _avatar = Selection.activeGameObject.GetComponentInParent<VRCAvatarDescriptor>();
            TryLoadConfig();
        }

        private void TryLoadConfig()
        {
            if (_avatar != null && _config == null)
                _config = _avatar.GetComponentInChildren<CostumeToggleConfig>(true);
        }

        private GUIStyle RowStyle => _rowStyle ?? (_rowStyle = new GUIStyle(EditorStyles.label));

        // =============================================================
        //  GUI ルート
        // =============================================================

        private void OnGUI()
        {
            DrawTopBar();
            if (_avatar == null)
            {
                EditorGUILayout.HelpBox("対象のアバター(VRCAvatarDescriptor)を指定してください。", MessageType.Warning);
                return;
            }
            if (_config == null)
            {
                EditorGUILayout.Space();
                if (GUILayout.Button("この avatar に衣装トグル設定を作成", GUILayout.Height(30)))
                    CreateConfig();
                return;
            }

            _tab = GUILayout.Toolbar(_tab, new[] { "トグル設定", "コーディネイト", "オプション", "保存・読込" });
            EditorGUILayout.Space(2);

            if (_tab == 2)
            {
                DrawOptionsTab();
                return;
            }
            if (_tab == 3)
            {
                DrawTemplatePage();
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (_tab == 0) DrawToggleTab();
                else DrawPresetTab();
            }
        }

        private void DrawTopBar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                using (var check = new EditorGUI.ChangeCheckScope())
                {
                    _avatar = (VRCAvatarDescriptor)EditorGUILayout.ObjectField(_avatar, typeof(VRCAvatarDescriptor), true, GUILayout.Width(220));
                    if (check.changed) { _config = null; _selGroup = _selItem = _selPreset = -1; TryLoadConfig(); }
                }
                GUILayout.FlexibleSpace();

                if (_config != null)
                {
                    bool generated = _config.generatedRoot != null;
                    GUILayout.Label(generated ? "生成済み" : "未生成", EditorStyles.miniLabel);

                    var prev = GUI.backgroundColor;
                    GUI.backgroundColor = new Color(0.6f, 0.9f, 0.6f);
                    if (GUILayout.Button("生成 / 作り直す", EditorStyles.toolbarButton, GUILayout.Width(110)))
                        DoGenerate();
                    GUI.backgroundColor = prev;

                    using (new EditorGUI.DisabledScope(!generated))
                    {
                        if (GUILayout.Button("アンインストール", EditorStyles.toolbarButton, GUILayout.Width(110)))
                        {
                            if (EditorUtility.DisplayDialog("確認", "生成した MA 部品を削除します。設定は残ります。", "削除", "キャンセル"))
                                CostumeToggleGenerator.RemoveGenerated(_config);
                        }
                    }
                }
            }
        }

        private void DoGenerate()
        {
            if (CostumeToggleGenerator.Generate(_config, out string msg))
                ShowNotification(new GUIContent("生成しました"));
            else
                EditorUtility.DisplayDialog("生成できません", msg, "OK");
        }

        // =============================================================
        //  トグル設定タブ（左ツリー＋右詳細）
        // =============================================================

        private void DrawToggleTab()
        {
            ClampToggleSelection();
            _pendingDeleteItem = (-1, -1);
            _pendingDeleteCat = -1;
            _pendingMoveCat = (-1, -1);
            _pendingMoveItem = (-1, -1, -1, -1);

            // 左ペイン：ツリー
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(290)))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("構成（ドラッグで他カテゴリへ移動）", EditorStyles.miniBoldLabel);
                    if (GUILayout.Button("全部畳む", EditorStyles.miniButtonLeft, GUILayout.Width(56)))
                        foreach (var g in _config.groups) if (g != null) _collapsed.Add(g);
                    if (GUILayout.Button("全部開く", EditorStyles.miniButtonRight, GUILayout.Width(56)))
                        _collapsed.Clear();
                }
                _leftScroll = EditorGUILayout.BeginScrollView(_leftScroll);
                for (int gi = 0; gi < _config.groups.Count; gi++)
                    DrawCategoryNode(gi);
                EditorGUILayout.EndScrollView();

                if (GUILayout.Button("＋ カテゴリを追加", GUILayout.Height(24)))
                {
                    RecordConfig("カテゴリ追加");
                    _config.groups.Add(new CostumeGroup());
                    _selGroup = _config.groups.Count - 1; _selItem = -1;
                }
            }

            ApplyPendingStructureChanges();

            // 右ペイン：詳細
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                _rightScroll = EditorGUILayout.BeginScrollView(_rightScroll);
                if (_selGroup < 0)
                    EditorGUILayout.HelpBox("左でカテゴリかアイテムを選ぶと、ここで編集できます。", MessageType.Info);
                else if (_selItem < 0)
                    DrawGroupDetail(_config.groups[_selGroup]);
                else
                    DrawItemDetail(_config.groups[_selGroup].items[_selItem]);
                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawCategoryNode(int gi)
        {
            var group = _config.groups[gi];
            bool collapsed = _collapsed.Contains(group);

            using (new EditorGUILayout.HorizontalScope())
            {
                // 折りたたみトグル
                if (GUILayout.Button(collapsed ? "▶" : "▼", EditorStyles.miniButton, GUILayout.Width(22)))
                {
                    if (collapsed) _collapsed.Remove(group); else _collapsed.Add(group);
                }
                using (new EditorGUI.DisabledScope(gi == 0))
                    if (GUILayout.Button("▲", EditorStyles.miniButtonLeft, GUILayout.Width(20))) _pendingMoveCat = (gi, gi - 1);
                using (new EditorGUI.DisabledScope(gi == _config.groups.Count - 1))
                    if (GUILayout.Button("▼", EditorStyles.miniButtonRight, GUILayout.Width(20))) _pendingMoveCat = (gi, gi + 1);

                Rect r = GUILayoutUtility.GetRect(GUIContent.none, RowStyle, GUILayout.Height(18), GUILayout.ExpandWidth(true));
                string secTag = string.IsNullOrEmpty(group.section) ? "" : $"  ［{group.section}］";
                string countTag = collapsed ? $"  ({group.items.Count})" : "";
                DrawRowLabel(r, (group.exclusive ? "◉ " : "▸ ") + DisplayName(group.name, $"カテゴリ{gi + 1}") + secTag + countTag, _selGroup == gi && _selItem < 0, 0);
                HandleCategoryHeaderEvents(r, gi);

                if (GUILayout.Button("×", GUILayout.Width(22))) _pendingDeleteCat = gi;
            }

            if (!collapsed)
            {
                for (int ii = 0; ii < group.items.Count; ii++)
                {
                    var item = group.items[ii];
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Space(16);
                        Rect r = GUILayoutUtility.GetRect(GUIContent.none, RowStyle, GUILayout.Height(18), GUILayout.ExpandWidth(true));
                        bool hasCond = item.hideWhen != null && item.hideWhen.Count > 0;
                        string tag = (item.autoShowWhenUncovered && hasCond ? "  [自動]" : (item.defaultOn ? "  (初期ON)" : "")) + (hasCond ? "  [条件]" : "") + (item.onInAllPresets ? "  [全コーデ]" : "");
                        DrawRowLabel(r, "・" + DisplayName(item.name, $"アイテム{ii + 1}") + tag, _selGroup == gi && _selItem == ii, 0);
                        HandleItemRowEvents(r, gi, ii);
                        if (GUILayout.Button("×", GUILayout.Width(22))) _pendingDeleteItem = (gi, ii);
                    }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Space(16);
                    if (GUILayout.Button("＋ アイテム", EditorStyles.miniButton))
                    {
                        RecordConfig("アイテム追加");
                        group.items.Add(new CostumeItem());
                        _selGroup = gi; _selItem = group.items.Count - 1;
                    }
                }
            }
            EditorGUILayout.Space(4);
        }

        private void DrawRowLabel(Rect r, string text, bool selected, int indent)
        {
            if (Event.current.type == EventType.Repaint && selected)
                EditorGUI.DrawRect(r, new Color(SelColor.r, SelColor.g, SelColor.b, 0.35f));
            var lr = r; lr.xMin += 4 + indent;
            GUI.Label(lr, text, RowStyle);
        }

        // --- ドラッグ＆ドロップ ---

        private void HandleItemRowEvents(Rect r, int gi, int ii)
        {
            Event e = Event.current;
            switch (e.type)
            {
                case EventType.MouseDown:
                    if (e.button == 0 && r.Contains(e.mousePosition))
                    {
                        _selGroup = gi; _selItem = ii;
                        _dragCandidate = true; _dragFrom = new Vector2Int(gi, ii);
                        e.Use();
                    }
                    break;
                case EventType.MouseDrag:
                    if (_dragCandidate && _dragFrom == new Vector2Int(gi, ii))
                    {
                        DragAndDrop.PrepareStartDrag();
                        DragAndDrop.SetGenericData(DragKey, new Vector2Int(gi, ii));
                        DragAndDrop.objectReferences = new UnityEngine.Object[0];
                        DragAndDrop.StartDrag("アイテム移動");
                        _dragCandidate = false;
                        e.Use();
                    }
                    break;
                case EventType.DragUpdated:
                    if (r.Contains(e.mousePosition) && DragAndDrop.GetGenericData(DragKey) is Vector2Int)
                    {
                        DragAndDrop.visualMode = DragAndDropVisualMode.Move;
                        e.Use();
                    }
                    break;
                case EventType.DragPerform:
                    if (r.Contains(e.mousePosition) && DragAndDrop.GetGenericData(DragKey) is Vector2Int from)
                    {
                        DragAndDrop.AcceptDrag();
                        _pendingMoveItem = (from.x, from.y, gi, ii); // このアイテムの前に挿入
                        e.Use();
                    }
                    break;
                case EventType.MouseUp:
                    _dragCandidate = false;
                    break;
            }
        }

        private void HandleCategoryHeaderEvents(Rect r, int gi)
        {
            Event e = Event.current;
            switch (e.type)
            {
                case EventType.MouseDown:
                    if (e.button == 0 && r.Contains(e.mousePosition))
                    {
                        _selGroup = gi; _selItem = -1; _dragCandidate = false;
                        e.Use();
                    }
                    break;
                case EventType.DragUpdated:
                    if (r.Contains(e.mousePosition) && DragAndDrop.GetGenericData(DragKey) is Vector2Int)
                    {
                        DragAndDrop.visualMode = DragAndDropVisualMode.Move;
                        e.Use();
                    }
                    break;
                case EventType.DragPerform:
                    if (r.Contains(e.mousePosition) && DragAndDrop.GetGenericData(DragKey) is Vector2Int from)
                    {
                        DragAndDrop.AcceptDrag();
                        _pendingMoveItem = (from.x, from.y, gi, _config.groups[gi].items.Count); // カテゴリ末尾へ
                        e.Use();
                    }
                    break;
            }
        }

        private void ApplyPendingStructureChanges()
        {
            if (_pendingMoveItem.fromGi >= 0)
            {
                MoveItem(_pendingMoveItem.fromGi, _pendingMoveItem.fromIi, _pendingMoveItem.toGi, _pendingMoveItem.toIndex);
                _pendingMoveItem = (-1, -1, -1, -1);
            }
            if (_pendingDeleteItem.gi >= 0)
            {
                var (gi, ii) = _pendingDeleteItem;
                if (gi < _config.groups.Count && ii < _config.groups[gi].items.Count)
                {
                    RecordConfig("アイテム削除");
                    _config.groups[gi].items.RemoveAt(ii);
                    if (_selGroup == gi && _selItem == ii) _selItem = -1;
                }
                _pendingDeleteItem = (-1, -1);
            }
            if (_pendingDeleteCat >= 0)
            {
                if (_pendingDeleteCat < _config.groups.Count)
                {
                    RecordConfig("カテゴリ削除");
                    _config.groups.RemoveAt(_pendingDeleteCat);
                    _selGroup = -1; _selItem = -1;
                }
                _pendingDeleteCat = -1;
            }
            if (_pendingMoveCat.from >= 0)
            {
                MoveCategory(_pendingMoveCat.from, _pendingMoveCat.to);
                _pendingMoveCat = (-1, -1);
            }
        }

        private void MoveItem(int fromGi, int fromIi, int toGi, int toIndex)
        {
            if (fromGi < 0 || fromGi >= _config.groups.Count) return;
            if (fromIi < 0 || fromIi >= _config.groups[fromGi].items.Count) return;
            if (toGi < 0 || toGi >= _config.groups.Count) return;

            RecordConfig("アイテム移動");
            var item = _config.groups[fromGi].items[fromIi];
            _config.groups[fromGi].items.RemoveAt(fromIi);
            if (fromGi == toGi && fromIi < toIndex) toIndex--;
            var dst = _config.groups[toGi].items;
            toIndex = Mathf.Clamp(toIndex, 0, dst.Count);
            dst.Insert(toIndex, item);

            _selGroup = toGi; _selItem = toIndex;
        }

        private void MoveCategory(int from, int to)
        {
            if (from < 0 || from >= _config.groups.Count) return;
            if (to < 0 || to >= _config.groups.Count) return;
            RecordConfig("カテゴリ移動");
            var g = _config.groups[from];
            _config.groups.RemoveAt(from);
            _config.groups.Insert(to, g);
            _selGroup = to; _selItem = -1;
        }

        private void DrawGroupDetail(CostumeGroup group)
        {
            EditorGUILayout.LabelField("カテゴリ設定", EditorStyles.boldLabel);
            using (var c = new EditorGUI.ChangeCheckScope())
            {
                string name = EditorGUILayout.TextField("カテゴリ名", group.name);
                var icon = (Texture2D)EditorGUILayout.ObjectField("アイコン", group.icon, typeof(Texture2D), false);
                bool exclusive = EditorGUILayout.ToggleLeft("排他（このカテゴリ内では同時に1つだけ）", group.exclusive);
                if (c.changed) { RecordConfig("カテゴリ編集"); group.name = name; group.icon = icon; group.exclusive = exclusive; }
            }

            // 所属フォルダ（中間フォルダ）
            using (var c = new EditorGUI.ChangeCheckScope())
            {
                string section = EditorGUILayout.TextField(
                    new GUIContent("フォルダ（任意）", "着替え内でカテゴリをまとめる中間フォルダ名。同じ名前のカテゴリが1つのフォルダに入る。空なら着替え直下"),
                    group.section);
                if (c.changed) { RecordConfig("フォルダ変更"); group.section = section; }
            }
            var sections = _config.groups
                .Where(g => g != null && !string.IsNullOrEmpty(g.section))
                .Select(g => g.section).Distinct().ToList();
            if (sections.Count > 0)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label("既存から:", GUILayout.Width(56));
                    foreach (var s in sections)
                        if (GUILayout.Button(s, EditorStyles.miniButton)) { RecordConfig("フォルダ選択"); group.section = s; }
                    if (GUILayout.Button("フォルダなし", EditorStyles.miniButton)) { RecordConfig("フォルダ解除"); group.section = ""; }
                }
            }

            EditorGUILayout.Space();
            if (GUILayout.Button("＋ アイテムを追加"))
            {
                RecordConfig("アイテム追加");
                group.items.Add(new CostumeItem());
                _selItem = group.items.Count - 1;
            }
        }

        private void DrawItemDetail(CostumeItem item)
        {
            EditorGUILayout.LabelField("アイテム設定", EditorStyles.boldLabel);
            using (var c = new EditorGUI.ChangeCheckScope())
            {
                string name = EditorGUILayout.TextField("アイテム名", item.name);
                var icon = (Texture2D)EditorGUILayout.ObjectField("アイコン", item.icon, typeof(Texture2D), false);
                bool defOn = EditorGUILayout.ToggleLeft("初期状態でONにする", item.defaultOn);
                bool onAll = EditorGUILayout.ToggleLeft(
                    new GUIContent("すべてのコーディネイトでONにする", "各コーデで毎回チェックしなくても、全コーデで自動的にONになります"),
                    item.onInAllPresets);
                if (c.changed) { RecordConfig("アイテム編集"); item.name = name; item.icon = icon; item.defaultOn = defOn; item.onInAllPresets = onAll; }
            }

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("対象オブジェクト", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("すべて表示", EditorStyles.miniButtonLeft, GUILayout.Width(70)))
                    SetObjectsActive(item, true);
                if (GUILayout.Button("すべて非表示", EditorStyles.miniButtonRight, GUILayout.Width(80)))
                    SetObjectsActive(item, false);
            }

            int remove = -1;
            for (int oi = 0; oi < item.objects.Count; oi++)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    var cur = item.objects[oi];
                    using (new EditorGUI.DisabledScope(cur == null))
                    {
                        bool shown = cur != null && cur.activeSelf;
                        var prev = GUI.backgroundColor;
                        if (shown) GUI.backgroundColor = new Color(0.5f, 0.85f, 0.5f);
                        if (GUILayout.Button(shown ? "表示" : "非表示", GUILayout.Width(52)) && cur != null)
                            ToggleObjectActive(cur);
                        GUI.backgroundColor = prev;
                    }
                    using (var c = new EditorGUI.ChangeCheckScope())
                    {
                        var go = (GameObject)EditorGUILayout.ObjectField(cur, typeof(GameObject), true);
                        if (c.changed) { RecordConfig("オブジェクト変更"); item.objects[oi] = go; AutoNameFromObject(item); }
                    }
                    if (GUILayout.Button("×", GUILayout.Width(24))) remove = oi;
                }
            }
            if (remove >= 0) { RecordConfig("オブジェクト削除"); item.objects.RemoveAt(remove); }

            var drop = GUILayoutUtility.GetRect(0, 34, GUILayout.ExpandWidth(true));
            GUI.Box(drop, "ここにオブジェクトをドラッグ＆ドロップ");
            HandleObjectDrop(drop, item);
            if (GUILayout.Button("＋ 空の枠を追加", GUILayout.Width(120)))
            {
                RecordConfig("枠追加"); item.objects.Add(null);
            }

            EditorGUILayout.Space();
            DrawHideWhen(item);

            EditorGUILayout.Space();
            DrawBodyShapes(item);

            EditorGUILayout.Space(16);
            if (GUILayout.Button("このアイテムを削除"))
            {
                RecordConfig("アイテム削除");
                _config.groups[_selGroup].items.RemoveAt(_selItem);
                _selItem = -1;
            }
        }

        private void DrawBodyShapes(CostumeItem item)
        {
            EditorGUILayout.LabelField("着用時に素体を変更（ブレンドシェイプ／クリッピング対策）", EditorStyles.boldLabel);
            if (item.autoShowWhenUncovered)
            {
                EditorGUILayout.HelpBox("自動表示アイテムでは無効です（手動トグルが無いため）。", MessageType.Info);
                return;
            }

            int remove = -1;
            for (int k = 0; k < item.bodyShapes.Count; k++)
            {
                var bs = item.bodyShapes[k];
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (var c = new EditorGUI.ChangeCheckScope())
                    {
                        var rend = (GameObject)EditorGUILayout.ObjectField("素体(Renderer)", bs.renderer, typeof(GameObject), true);
                        // ブレンドシェイプ名：メッシュがあればドロップダウン、無ければテキスト
                        var names = GetBlendShapeNames(rend);
                        string shapeName = bs.shapeName;
                        if (names != null && names.Length > 0)
                        {
                            int cur = System.Array.IndexOf(names, bs.shapeName);
                            if (cur < 0) cur = 0;
                            int sel = EditorGUILayout.Popup("シェイプ", cur, names);
                            shapeName = names[sel];
                        }
                        else
                        {
                            shapeName = EditorGUILayout.TextField("シェイプ名", bs.shapeName);
                        }
                        bool del = EditorGUILayout.ToggleLeft("メッシュを削除する（値ではなく範囲を消す）", bs.delete);
                        float val = bs.value;
                        using (new EditorGUI.DisabledScope(del))
                            val = EditorGUILayout.Slider("値", bs.value, 0f, 100f);
                        if (c.changed)
                        {
                            RecordConfig("素体変更編集");
                            bs.renderer = rend; bs.shapeName = shapeName; bs.delete = del; bs.value = val;
                        }
                    }
                    if (GUILayout.Button("× この行を削除", GUILayout.Width(120))) remove = k;
                }
            }
            if (remove >= 0) { RecordConfig("素体変更削除"); item.bodyShapes.RemoveAt(remove); }

            if (GUILayout.Button("＋ 素体変更を追加", GUILayout.Width(150)))
            {
                RecordConfig("素体変更追加");
                item.bodyShapes.Add(new BodyShape());
            }
        }

        private static string[] GetBlendShapeNames(GameObject go)
        {
            if (go == null) return null;
            var smr = go.GetComponent<SkinnedMeshRenderer>();
            if (smr == null || smr.sharedMesh == null) return null;
            var mesh = smr.sharedMesh;
            var names = new string[mesh.blendShapeCount];
            for (int i = 0; i < names.Length; i++) names[i] = mesh.GetBlendShapeName(i);
            return names;
        }

        private void DrawHideWhen(CostumeItem item)
        {
            EditorGUILayout.LabelField("自動で隠す条件（指定アイテム/カテゴリがONのとき隠す／複数=OR）", EditorStyles.boldLabel);

            // 選択肢：カテゴリ参照（どれかON）＋ 個別アイテム参照（自分は除外）
            var refGroups = new List<string>();
            var refItems = new List<string>();
            var refIsCat = new List<bool>();
            var labels = new List<string>();
            for (int gi = 0; gi < _config.groups.Count; gi++)
            {
                var g = _config.groups[gi];
                refGroups.Add(g.name); refItems.Add(""); refIsCat.Add(true);
                labels.Add("［カテゴリ］" + DisplayName(g.name, $"カテゴリ{gi + 1}") + "（どれかON）");
                for (int ii = 0; ii < g.items.Count; ii++)
                {
                    var it = g.items[ii];
                    if (gi == _selGroup && ReferenceEquals(it, item)) continue;
                    refGroups.Add(g.name); refItems.Add(it.name); refIsCat.Add(false);
                    labels.Add(DisplayName(g.name, $"カテゴリ{gi + 1}") + " / " + DisplayName(it.name, $"アイテム{ii + 1}"));
                }
            }

            if (refGroups.Count == 0)
            {
                EditorGUILayout.HelpBox("他にアイテム/カテゴリがありません。先に作ってください。", MessageType.None);
                return;
            }

            var labelArr = labels.ToArray();
            int removeIdx = -1;
            for (int k = 0; k < item.hideWhen.Count; k++)
            {
                var entry = item.hideWhen[k];
                int cur = -1;
                for (int j = 0; j < refGroups.Count; j++)
                    if (refIsCat[j] == entry.category && refGroups[j] == entry.groupName &&
                        (entry.category || refItems[j] == entry.itemName)) { cur = j; break; }
                if (cur < 0) cur = 0;

                using (new EditorGUILayout.HorizontalScope())
                {
                    using (var c = new EditorGUI.ChangeCheckScope())
                    {
                        int sel = EditorGUILayout.Popup(cur, labelArr);
                        if (c.changed)
                        {
                            RecordConfig("隠す条件変更");
                            entry.groupName = refGroups[sel];
                            entry.itemName = refItems[sel];
                            entry.category = refIsCat[sel];
                        }
                    }
                    if (GUILayout.Button("×", GUILayout.Width(24))) removeIdx = k;
                }
            }
            if (removeIdx >= 0) { RecordConfig("隠す条件削除"); item.hideWhen.RemoveAt(removeIdx); }

            if (GUILayout.Button("＋ 条件を追加", GUILayout.Width(120)))
            {
                RecordConfig("隠す条件追加");
                item.hideWhen.Add(new ItemRef { groupName = refGroups[0], itemName = refItems[0], category = refIsCat[0] });
            }

            // 自動表示（下地レイヤー）
            EditorGUILayout.Space(4);
            using (var c = new EditorGUI.ChangeCheckScope())
            {
                bool auto = EditorGUILayout.ToggleLeft(
                    new GUIContent("競合がどれもOFFのとき自動で表示（手動トグルを作らない）",
                        "ONにすると、上の条件がすべてOFFのとき自動表示される下地レイヤーになります（メニューのトグルは作られません）。条件が1つ以上必要です。"),
                    item.autoShowWhenUncovered);
                if (c.changed) { RecordConfig("自動表示変更"); item.autoShowWhenUncovered = auto; }
            }
            if (item.autoShowWhenUncovered && item.hideWhen.Count == 0)
                EditorGUILayout.HelpBox("条件が無いため、このアイテムは常時表示になります（手動トグルは作られません）。", MessageType.Info);
        }

        private void HandleObjectDrop(Rect area, CostumeItem item)
        {
            Event e = Event.current;
            if (!area.Contains(e.mousePosition)) return;
            if (e.type == EventType.DragUpdated)
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                e.Use();
            }
            else if (e.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                RecordConfig("オブジェクトD&D追加");
                foreach (var obj in DragAndDrop.objectReferences)
                    if (obj is GameObject go && !item.objects.Contains(go))
                        item.objects.Add(go);
                AutoNameFromObject(item);
                e.Use();
            }
        }

        // =============================================================
        //  コーディネイトタブ
        // =============================================================

        private void DrawPresetTab()
        {
            if (_selPreset >= _config.presets.Count) _selPreset = -1;

            using (new EditorGUILayout.VerticalScope(GUILayout.Width(240)))
            {
                EditorGUILayout.LabelField("コーディネイト", EditorStyles.boldLabel);
                _leftScroll = EditorGUILayout.BeginScrollView(_leftScroll);
                for (int pi = 0; pi < _config.presets.Count; pi++)
                {
                    int piLocal = pi;
                    DrawSelectable("★ " + DisplayName(_config.presets[pi].name, $"コーディネイト{pi + 1}"),
                        _selPreset == pi, () => _selPreset = piLocal, 0);
                }
                EditorGUILayout.EndScrollView();

                if (GUILayout.Button("＋ コーディネイトを追加", GUILayout.Height(24)))
                {
                    RecordConfig("コーディネイト追加");
                    _config.presets.Add(new CostumePreset());
                    _selPreset = _config.presets.Count - 1;
                }
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                _rightScroll = EditorGUILayout.BeginScrollView(_rightScroll);
                if (_selPreset < 0)
                    EditorGUILayout.HelpBox("左でコーディネイトを選ぶと、各カテゴリの組合せを指定できます。\n1ボタンでまとめて呼び出せます（呼び出し後の個別調整も可能）。", MessageType.Info);
                else
                    DrawPresetDetail(_config.presets[_selPreset]);
                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawPresetDetail(CostumePreset preset)
        {
            using (var c = new EditorGUI.ChangeCheckScope())
            {
                string name = EditorGUILayout.TextField("コーディネイト名", preset.name);
                var icon = (Texture2D)EditorGUILayout.ObjectField("アイコン", preset.icon, typeof(Texture2D), false);
                if (c.changed) { RecordConfig("コーディネイト名編集"); preset.name = name; preset.icon = icon; }
            }

            if (GUILayout.Button("この組合せをシーンでプレビュー", GUILayout.Height(22)))
                PreviewPreset(preset);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("各カテゴリの状態", EditorStyles.boldLabel);

            foreach (var group in _config.groups)
            {
                if (group == null) continue;
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField(DisplayName(group.name, "カテゴリ") + (group.exclusive ? "（1つ選択）" : "（複数可）"), EditorStyles.miniBoldLabel);

                    if (group.exclusive)
                    {
                        var names = new List<string> { "(なし)" };
                        names.AddRange(group.items.Select((it, idx) => DisplayName(it.name, $"アイテム{idx + 1}")));
                        int cur = 0;
                        for (int i = 0; i < group.items.Count; i++)
                            if (IsItemOn(preset, group.name, group.items[i].name)) { cur = i + 1; break; }

                        using (var c = new EditorGUI.ChangeCheckScope())
                        {
                            int sel = EditorGUILayout.Popup("選択", cur, names.ToArray());
                            if (c.changed)
                            {
                                RecordConfig("コーディネイト選択変更");
                                foreach (var it in group.items) SetItemOn(preset, group.name, it.name, false);
                                if (sel >= 1) SetItemOn(preset, group.name, group.items[sel - 1].name, true);
                            }
                        }
                    }
                    else
                    {
                        foreach (var item in group.items)
                        {
                            bool on = IsItemOn(preset, group.name, item.name);
                            using (var c = new EditorGUI.ChangeCheckScope())
                            {
                                bool v = EditorGUILayout.ToggleLeft(DisplayName(item.name, "アイテム"), on);
                                if (c.changed) { RecordConfig("コーディネイト項目変更"); SetItemOn(preset, group.name, item.name, v); }
                            }
                        }
                    }
                }
            }

            EditorGUILayout.Space(16);
            if (GUILayout.Button("このコーディネイトを削除"))
            {
                RecordConfig("コーディネイト削除");
                _config.presets.RemoveAt(_selPreset);
                _selPreset = -1;
            }
        }

        // =============================================================
        //  オプションタブ
        // =============================================================

        private CostumeTogglePreset _loadAsset;

        // 検証・パラメータ使用量（すべてエディタ専用＝実行時コストなし）
        private void DrawValidation()
        {
            int bits = EstimateParamBits(out int paramCount);
            string budget = $"Expressionパラメータ概算: {bits} / 256 bit（{paramCount}個）";
            if (bits > 256)
                EditorGUILayout.HelpBox(budget + "\n超過しています。排他カテゴリ化やアイテム削減を検討してください。", MessageType.Error);
            else
                EditorGUILayout.HelpBox(budget, MessageType.None);

            var issues = Validate();
            if (issues.Count > 0)
                EditorGUILayout.HelpBox("確認事項:\n・" + string.Join("\n・", issues), MessageType.Warning);
        }

        // 設定テンプレートの保存・読込ページ（別アバターへの流用／バックアップ用。エディタ専用）
        private void DrawTemplatePage()
        {
            EditorGUILayout.LabelField("設定の保存・読込", EditorStyles.boldLabel);
            EditorGUILayout.Space(2);

            EditorGUILayout.LabelField("手動保存", EditorStyles.miniBoldLabel);
            if (GUILayout.Button("設定をテンプレートとして保存(.asset)")) SaveTemplate();
            EditorGUILayout.HelpBox(
                "「設定を保存」は手動です（任意）。別アバターへの流用やバックアップ用に、現在の設定をテンプレート(.asset)として書き出します。\n" +
                "※この手動保存とは別に、生成（作り直し）のたびに設定は自動バックアップされます（アバターごと最大" +
                CostumeToggleGenerator.MaxBackups + "件）。普段は押さなくても設定は失われません。",
                MessageType.Info);

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("読込", EditorStyles.miniBoldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                _loadAsset = (CostumeTogglePreset)EditorGUILayout.ObjectField(_loadAsset, typeof(CostumeTogglePreset), false);
                using (new EditorGUI.DisabledScope(_loadAsset == null))
                    if (GUILayout.Button("読込", GUILayout.Width(80))) LoadTemplate();
            }
            EditorGUILayout.HelpBox("読み込むと現在の設定を上書きします。対象オブジェクト等のシーン参照は解除され、割り当て直しが必要です（アイコン等のアセット参照は保持）。", MessageType.None);
        }

        private int EstimateParamBits(out int paramCount)
        {
            int bits = 0; paramCount = 0;
            foreach (var g in _config.groups)
            {
                if (g == null) continue;
                int nonAuto = g.items.Count(it => it != null && !it.autoShowWhenUncovered);
                if (nonAuto == 0) continue;
                if (g.exclusive) { bits += (nonAuto >= 2 ? 8 : 1); paramCount += 1; } // 2件以上=Int(8bit)/1件=Bool(1bit)
                else { bits += nonAuto; paramCount += nonAuto; }
            }
            if (_config.presets != null && _config.presets.Count(p => p != null) > 0) { bits += 8; paramCount += 1; }
            return bits;
        }

        private List<string> Validate()
        {
            var issues = new List<string>();
            var seenGroups = new HashSet<string>();
            for (int gi = 0; gi < _config.groups.Count; gi++)
            {
                var g = _config.groups[gi];
                if (g == null) continue;
                string gname = DisplayName(g.name, $"カテゴリ{gi + 1}");
                if (!string.IsNullOrEmpty(g.name) && !seenGroups.Add(g.name))
                    issues.Add($"カテゴリ名が重複: {gname}");
                if (g.items.Count(it => it != null) == 0)
                    issues.Add($"空のカテゴリ: {gname}");
                var seenItems = new HashSet<string>();
                for (int ii = 0; ii < g.items.Count; ii++)
                {
                    var it = g.items[ii];
                    if (it == null) continue;
                    string iname = DisplayName(it.name, $"アイテム{ii + 1}");
                    if (!string.IsNullOrEmpty(it.name) && !seenItems.Add(it.name))
                        issues.Add($"アイテム名が重複（同一カテゴリ内）: {gname}/{iname}");
                    int objs = it.objects.Count(o => o != null);
                    bool hasShapes = it.bodyShapes != null && it.bodyShapes.Count(s => s != null && s.renderer != null) > 0;
                    if (objs == 0 && !hasShapes)
                        issues.Add($"対象オブジェクト未設定: {gname}/{iname}");
                    if (it.hideWhen != null)
                        foreach (var r in it.hideWhen)
                        {
                            if (r == null) continue;
                            if (r.category) { if (!GroupExists(r.groupName)) issues.Add($"存在しないカテゴリを参照: {gname}/{iname} → {r.groupName}"); }
                            else if (!ItemExists(r.groupName, r.itemName)) issues.Add($"存在しないアイテムを参照: {gname}/{iname} → {r.groupName}/{r.itemName}");
                        }
                }
            }
            return issues;
        }

        private bool GroupExists(string name) => _config.groups.Any(g => g != null && g.name == name);
        private bool ItemExists(string gname, string iname)
            => _config.groups.Any(g => g != null && g.name == gname && g.items.Any(it => it != null && it.name == iname));

        private void SaveTemplate()
        {
            string path = EditorUtility.SaveFilePanelInProject("設定テンプレートを保存", "CostumeToggleTemplate", "asset", "保存先を選択");
            if (string.IsNullOrEmpty(path)) return;
            var asset = ScriptableObject.CreateInstance<CostumeTogglePreset>();
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(_config), asset);
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            EditorUtility.DisplayDialog("衣装トグル",
                "テンプレートを保存しました。\n※対象オブジェクト等のシーン参照は保存されません（アイコン等のアセット参照は保持）。\n" + path, "OK");
        }

        private void LoadTemplate()
        {
            if (_loadAsset == null) return;
            if (!EditorUtility.DisplayDialog("確認",
                "現在の設定をテンプレートで上書きします。\n対象オブジェクト等のシーン参照は解除され、割り当て直しが必要です。", "上書き", "キャンセル")) return;
            RecordConfig("テンプレート読込");
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(_loadAsset), _config);
            _selGroup = -1; _selItem = -1; _selPreset = -1;
        }

        private void DrawOptionsTab()
        {
            DrawValidation();
            EditorGUILayout.Space();

            EditorGUILayout.LabelField("メニュー構成オプション", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            using (var c = new EditorGUI.ChangeCheckScope())
            {
                var target = (VRCExpressionsMenu)EditorGUILayout.ObjectField(
                    new GUIContent("起点（設置先メニュー）", "未指定ならアバターのメインメニューへ追加。指定すると、そのメニューの中へ追加されます。"),
                    _config.installTargetMenu, typeof(VRCExpressionsMenu), false);
                bool wrap = EditorGUILayout.ToggleLeft("単一フォルダにまとめる（メインメニューの消費枠を1つに）", _config.wrapInFolder);
                string menuName = EditorGUILayout.TextField("親フォルダ名", _config.menuName);
                string coordName = EditorGUILayout.TextField("コーディネイトのフォルダ名", _config.coordinateMenuName);
                if (c.changed)
                {
                    RecordConfig("オプション編集");
                    _config.installTargetMenu = target;
                    _config.wrapInFolder = wrap;
                    _config.menuName = menuName;
                    _config.coordinateMenuName = coordName;
                }
            }

            // フォルダ（中間フォルダ）のアイコン
            var sectionNames = _config.groups
                .Where(g => g != null && !string.IsNullOrEmpty(g.section))
                .Select(g => g.section).Distinct().ToList();
            if (sectionNames.Count > 0)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("フォルダのアイコン", EditorStyles.boldLabel);
                foreach (var s in sectionNames)
                {
                    var si = _config.sectionIcons.FirstOrDefault(x => x != null && x.sectionName == s);
                    using (var c = new EditorGUI.ChangeCheckScope())
                    {
                        var icon = (Texture2D)EditorGUILayout.ObjectField(s, si != null ? si.icon : null, typeof(Texture2D), false);
                        if (c.changed)
                        {
                            RecordConfig("フォルダアイコン");
                            if (si == null) { si = new SectionIcon { sectionName = s }; _config.sectionIcons.Add(si); }
                            si.icon = icon;
                        }
                    }
                }
            }

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox("生成されるメニュー構成:\n" + BuildMenuPreview(), MessageType.None);
            EditorGUILayout.HelpBox("「単一フォルダにまとめる」をOFFにすると、フォルダ/カテゴリとコーディネイトが起点へ直接並びます（枠を多く消費します）。\nカテゴリに「フォルダ」を設定すると、着替え内の中間フォルダ（身体/服/アクセサリ等）にまとまります。", MessageType.None);
        }

        // メニュー構成のプレビュー文字列（フォルダ階層を反映）
        private string BuildMenuPreview()
        {
            string root = _config.installTargetMenu != null ? _config.installTargetMenu.name : "メインメニュー";
            string indent = _config.wrapInFolder ? "   " : "";
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(root);
            if (_config.wrapInFolder)
                sb.AppendLine("└ " + DisplayName(_config.menuName, "衣装"));

            // フォルダ（初出順）→ 所属カテゴリ、の順に表示。フォルダなしカテゴリは直下。
            var sections = _config.groups.Where(g => g != null && !string.IsNullOrEmpty(g.section))
                .Select(g => g.section).Distinct().ToList();
            foreach (var s in sections)
            {
                sb.AppendLine(indent + "├ " + s + "（フォルダ）");
                foreach (var g in _config.groups.Where(g => g != null && g.section == s))
                    sb.AppendLine(indent + "│  ├ " + DisplayName(g.name, "カテゴリ") + " → トグル");
            }
            foreach (var g in _config.groups.Where(g => g != null && string.IsNullOrEmpty(g.section)))
                sb.AppendLine(indent + "├ " + DisplayName(g.name, "カテゴリ") + " → トグル");

            sb.Append(indent + "└ " + DisplayName(_config.coordinateMenuName, "コーディネイト") + " → 呼び出しボタン");
            return sb.ToString();
        }

        // =============================================================
        //  コーディネイト entries 操作
        // =============================================================

        // コーデの組合せをシーンのオブジェクト表示へ適用（エディタ上の見た目確認用・簡易）
        private void PreviewPreset(CostumePreset preset)
        {
            foreach (var g in _config.groups)
            {
                if (g == null) continue;
                foreach (var it in g.items)
                {
                    if (it == null) continue;
                    bool on = IsItemOn(preset, g.name, it.name);
                    foreach (var go in it.objects)
                    {
                        if (go == null) continue;
                        Undo.RecordObject(go, "コーデプレビュー");
                        go.SetActive(on);
                        EditorUtility.SetDirty(go);
                    }
                }
            }
            ShowNotification(new GUIContent("プレビューを適用（見た目確認用）"));
        }

        private bool IsItemOn(CostumePreset preset, string groupName, string itemName)
            => preset.entries.Any(e => e.on && e.groupName == groupName && e.itemName == itemName);

        private void SetItemOn(CostumePreset preset, string groupName, string itemName, bool on)
        {
            preset.entries.RemoveAll(e => e.groupName == groupName && e.itemName == itemName);
            if (on)
                preset.entries.Add(new PresetEntry { groupName = groupName, itemName = itemName, on = true });
        }

        // =============================================================
        //  共通
        // =============================================================

        private void DrawSelectable(string label, bool selected, System.Action onClick, int indent)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (indent > 0) GUILayout.Space(indent);
                var style = new GUIStyle(EditorStyles.miniButton) { alignment = TextAnchor.MiddleLeft };
                var prev = GUI.backgroundColor;
                if (selected) GUI.backgroundColor = SelColor;
                if (GUILayout.Button(label, style)) onClick();
                GUI.backgroundColor = prev;
            }
        }

        private static string DisplayName(string s, string fallback) => string.IsNullOrEmpty(s) ? fallback : s;

        // アイテム名が未設定（空 or 既定"アイテム"）なら、最初に割り当てたGameObject名を採用
        private static void AutoNameFromObject(CostumeItem item)
        {
            if (!(string.IsNullOrEmpty(item.name) || item.name == "アイテム")) return;
            var first = item.objects.FirstOrDefault(o => o != null);
            if (first != null) item.name = first.name;
        }

        private static void ToggleObjectActive(GameObject go)
        {
            Undo.RecordObject(go, "表示切替（確認用）");
            go.SetActive(!go.activeSelf);
            EditorUtility.SetDirty(go);
        }

        private static void SetObjectsActive(CostumeItem item, bool active)
        {
            foreach (var go in item.objects)
            {
                if (go == null) continue;
                Undo.RecordObject(go, "表示一括切替（確認用）");
                go.SetActive(active);
                EditorUtility.SetDirty(go);
            }
        }

        private void ClampToggleSelection()
        {
            if (_selGroup >= _config.groups.Count) { _selGroup = -1; _selItem = -1; }
            if (_selGroup >= 0 && _selItem >= _config.groups[_selGroup].items.Count) _selItem = -1;
        }

        private void CreateConfig()
        {
            var go = new GameObject(ConfigObjectName);
            Undo.RegisterCreatedObjectUndo(go, "衣装トグル設定を作成");
            go.transform.SetParent(_avatar.transform, false);
            go.tag = "EditorOnly";
            _config = Undo.AddComponent<CostumeToggleConfig>(go);
            Selection.activeGameObject = go;
        }

        private void RecordConfig(string label)
        {
            if (_config != null)
            {
                Undo.RegisterCompleteObjectUndo(_config, label);
                EditorUtility.SetDirty(_config);
            }
        }
    }
}
