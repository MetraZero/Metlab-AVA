// MET_ShortcutWindow
// v0.3.19 beta (2026-09-21)
// 実Assetフォルダとは独立した「仮想ショートカットツリー」を、
// Projectウィンドウのアイコングリッド表示に近い形で表示・管理するタブ。
// ・仮想フォルダ / アセットショートカットをドラッグで登録・並び替え・格納
// ・クリックで選択、もう一度クリックで「開く」(フォルダは中に入る)
// ・アセットフォルダのショートカットを開くと、その中の"本物のアセット"を辿れる
//   (中身はショートカットではなく実アセットとしてブラウズする)
// ・色付けはフォルダのみ(フォルダアイコンを着色) / カスタム画像があれば画像を優先
// ・↗バッジは実体(実在フォルダ/ファイル)を指すショートカットのみ / 仮想フォルダは無印
// ・各項目に優先度(数値)を設定可能。値が大きいほど上位に表示(優先度はソートより優先)
// ・トップバーのソートメニューで並び順(名前順/追加順)を切替
// ・下部の表示切替ボタンでグリッド表示 / リスト表示を切替
// ・トップバーの「+」ボタン / 右クリックメニューでフォルダ作成(選択+名前変更待ち+その場へスクロール)
// ・実フォルダをダブルクリックしたときの開き方は設定で複数チェック可
//   (ショートカット内 / Projectタブで見つける / Projectタブで開く をまとめて実行)
// ・トップバーの設定ボタンからコンフィグ(背景色/フォルダの開き方/バッジ表示など)を変更
// データは ProjectSettings/MET_ShortcutData.json に保存(Assets非汚染)。

using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Metlab.Shortcut
{
    [Serializable]
    public class MET_ShortcutNode
    {
        public string id = Guid.NewGuid().ToString();
        public string name;
        public bool isFolder;
        public string assetGuid;       // isFolder = false の場合のみ使用(参照アセット)

        public bool hasColor;
        public float colorR, colorG, colorB;

        public string customIconGuid;  // 設定されていればデフォルトアイコンより優先

        public int priority;           // 大きいほど上位に表示(既定0 / 同値は登録順)

        public List<MET_ShortcutNode> children = new List<MET_ShortcutNode>();
    }

    // 本物(実アセット)のフォルダをダブルクリックで開いたときの挙動
    public enum MET_RealFolderOpenMode
    {
        BrowseInShortcut = 0, // ショートカットウィンドウ内で開く(中に入る)
        RevealInProject  = 1, // Projectウィンドウで見つける(選択して表示)
        OpenInProject    = 2, // Projectウィンドウで開く(中に入る)
    }

    // 一覧の並び順(優先度は常にこのソートより優先される)
    public enum MET_SortMode
    {
        NameAsc    = 0, // 名前順(A→Z) ※既定
        NameDesc   = 1, // 名前順(Z→A)
        AddedOrder = 2, // 追加順(登録した順)
    }

    // 一覧の表示形式
    public enum MET_ViewLayout
    {
        Grid = 0, // アイコンを並べて表示(グリッド)
        List = 1, // 1行ずつのリスト表示
    }

    [Serializable]
    public class MET_ShortcutData
    {
        public List<MET_ShortcutNode> roots = new List<MET_ShortcutNode>();

        // ウィンドウ背景色(Projectウィンドウ風・全体で1つの既定)
        // 既定は #31605F(深いティール)を有効状態で用いる
        public bool hasBgColor = true;
        public float bgR = 0.1922f, bgG = 0.3765f, bgB = 0.3725f;

        // アイコン表示倍率(下部スライダー)
        public float iconScale = 1f;

        // 一覧の表示形式(グリッド / リスト)
        public MET_ViewLayout viewLayout = MET_ViewLayout.Grid;

        // --- コンフィグ設定 ---
        // 本物(実アセット)のフォルダをダブルクリックで開いたときの挙動
        // 旧バージョン(単一選択)の設定。互換のため保持し、初回ロード時に下の複数フラグへ移行する。
        public MET_RealFolderOpenMode realFolderOpenMode = MET_RealFolderOpenMode.BrowseInShortcut;
        public bool realOpenConfigMigrated = false;

        // 本物フォルダのダブルクリック動作(複数指定可・独立チェック)
        public bool realOpenBrowseInShortcut = true;  // ショートカット内で開く(中に入る)
        public bool realOpenRevealInProject  = false; // Projectタブで見つける(選択)
        public bool realOpenEnterInProject   = false; // Projectタブで開く(中に入る)

        // 右クリックメニューに「アセットを開く」を表示するか(既定は非表示)
        public bool showOpenAssetInMenu = false;

        // 一覧の並び順(既定は名前順)。優先度は常にこのソートより優先される。
        public MET_SortMode sortMode = MET_SortMode.NameAsc;

        // 優先度バッジ(アイコン右上の数値)を表示するか(既定は表示)
        public bool showPriorityBadge = true;

        // 優先度★バッジ(アイコン右下の星・優先度1以上で付く)を表示するか(既定は表示)
        public bool showPriorityStar = true;
    }

    public class MET_ShortcutWindow : EditorWindow
    {
        // バージョン表記(左下・設定画面に表示)。更新のたびに Version と VersionDate を更新すること。
        // ベータ期間中はメジャー番号を 0 とする(例: 0.3.15 beta)。
        public const string Version = "0.3.19";
        public const string VersionStage = "beta";     // 正式版になったら空文字に
        public const string VersionDate = "2026-09-21"; // 最終更新日
        public static string VersionLabel =>
            string.IsNullOrEmpty(VersionStage) ? $"v{Version}" : $"v{Version} {VersionStage}";

        private const string DataPath = "ProjectSettings/MET_ShortcutData.json";
        private MET_ShortcutData data;
        private Vector2 scroll;

        // ------------------------------------------------------------
        // ナビゲーション(仮想フォルダ or 実アセットフォルダ)
        // ------------------------------------------------------------
        private class NavItem
        {
            public MET_ShortcutNode virtualFolder; // 仮想フォルダ。null の場合は実フォルダ
            public string realFolderPath;          // 実アセットフォルダのパス
            public bool IsReal => virtualFolder == null;
            public string Label => virtualFolder != null
                ? virtualFolder.name
                : Path.GetFileName(realFolderPath);
        }
        private readonly List<NavItem> navStack = new List<NavItem>();

        private bool InRealMode => navStack.Count > 0 && navStack[navStack.Count - 1].IsReal;

        // 現在の仮想リスト(実フォルダ閲覧中は null)
        private List<MET_ShortcutNode> CurrentVirtualList
        {
            get
            {
                if (navStack.Count == 0) return data.roots;
                var top = navStack[navStack.Count - 1];
                return top.IsReal ? null : top.virtualFolder.children;
            }
        }

        // ------------------------------------------------------------
        // 表示用タイル(仮想ノード / 実アセットの両対応)
        // ------------------------------------------------------------
        private class Tile
        {
            public MET_ShortcutNode node;   // 仮想ノード(null = 実アセット)
            public string assetPath;        // アイコン・参照用のアセットパス
            public bool isFolderTile;       // フォルダとして振る舞う
            public bool isRealAsset;        // 実アセット(ツリー操作の対象外)
            public string name;
            public string key;              // 選択キー
            public bool hasColor;
            public Color color;
            public string customIconGuid;
        }
        private List<Tile> tilesCache;
        private readonly HashSet<string> selectedKeys = new HashSet<string>();
        private string selectionAnchor;      // Shift範囲選択の基点

        // 新規作成した項目をビューに映すためのスクロール予約
        private string scrollToKey;
        private int gridColumns = 1;         // 直近の列数(スクロール位置計算に使用。リストは常に1)
        private float lastRowStride = 1f;    // 直近の1行あたりの高さ(スクロール位置計算に使用)

        // ダブルクリック判定(clickCountはFocus等でリセットされ得るため時間ベースで判定)
        private double lastClickTime;
        private string lastClickKey;
        private const double DoubleClickSeconds = 0.3;

        private MET_ShortcutNode renamingNode;
        private string renameBuffer;
        private bool renameFocusPending;      // 名前編集開始直後、テキストフィールドへフォーカスを移すまでの猶予
        private const string RenameControlName = "MET_rename";

        // --- 内部ドラッグ(並び替え)/ クリック判定用 ---
        private string pressedKey;
        private Vector2 mouseDownPos;
        private bool dragInitiated;
        private const float DragThreshold = 6f;
        private const string DragGenericKey = "MET_ShortcutDragNode";

        private const float BaseTileWidth = 72f;
        private const float BaseTileHeight = 76f;
        private const float BaseIconSize = 48f;
        private const float MinScale = 0.5f;
        private const float MaxScale = 2.0f;

        private float Scale => Mathf.Clamp(data != null && data.iconScale > 0f ? data.iconScale : 1f, MinScale, MaxScale);
        private float TileWidth => BaseTileWidth * Scale;
        private float TileHeight => BaseTileHeight * Scale;
        private float IconSize => BaseIconSize * Scale;
        private float LabelHeight => (BaseTileHeight - BaseIconSize) * Scale;

        private bool scaleDirty;

        [MenuItem("Tools/Metlab/ショートカットをつくるやつ")]
        public static void Open()
        {
            var win = GetWindow<MET_ShortcutWindow>();
            win.titleContent = new GUIContent("ショートカットをつくるやつ");
            win.Show();
        }

        private void OnEnable() => Load();

        private void Load()
        {
            data = null;
            if (File.Exists(DataPath))
            {
                try { data = JsonUtility.FromJson<MET_ShortcutData>(File.ReadAllText(DataPath)); }
                catch { data = null; }
            }
            if (data == null) data = new MET_ShortcutData();
            MigrateConfig();
        }

        // 旧バージョンの単一選択設定(realFolderOpenMode)を、新しい複数チェックへ一度だけ移行する。
        private void MigrateConfig()
        {
            if (data.realOpenConfigMigrated) return;
            switch (data.realFolderOpenMode)
            {
                case MET_RealFolderOpenMode.RevealInProject:
                    data.realOpenBrowseInShortcut = false;
                    data.realOpenRevealInProject  = true;
                    data.realOpenEnterInProject   = false;
                    break;
                case MET_RealFolderOpenMode.OpenInProject:
                    data.realOpenBrowseInShortcut = false;
                    data.realOpenRevealInProject  = false;
                    data.realOpenEnterInProject   = true;
                    break;
                default: // BrowseInShortcut
                    data.realOpenBrowseInShortcut = true;
                    data.realOpenRevealInProject  = false;
                    data.realOpenEnterInProject   = false;
                    break;
            }
            data.realOpenConfigMigrated = true;
            Save();
        }

        private void Save() => File.WriteAllText(DataPath, JsonUtility.ToJson(data, true));

        // ============================================================
        // OnGUI
        // ============================================================
        private void OnGUI()
        {
            if (Event.current.type == EventType.DragExited)
            {
                pressedKey = null;
                dragInitiated = false;
            }

            // GUILayoutの整合性のため、タイルはLayoutパスで構築して1フレーム内で固定
            if (Event.current.type == EventType.Layout || tilesCache == null)
                tilesCache = BuildCurrentTiles();

            DrawBackground();
            HandleKeyboard();
            DrawTopBar();
            DrawBreadcrumb();

            scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.ExpandHeight(true));
            DrawContent();
            EditorGUILayout.EndScrollView();

            HandleRenameCommitOnBlur();

            var gridAreaRect = GUILayoutUtility.GetLastRect();
            ApplyPendingScroll(gridAreaRect);
            HandleEmptyAreaClick(gridAreaRect);
            HandleGridBackgroundDrop(gridAreaRect);
            HandleEmptyAreaContextMenu(gridAreaRect);

            DrawBottomBar();
        }

        private void DrawBackground()
        {
            // 実フォルダ閲覧中はショートカットを作れないため、背景色は適用せずデフォルトに戻す
            if (!data.hasBgColor || InRealMode || Event.current.type != EventType.Repaint) return;
            var prev = GUI.color;
            GUI.color = new Color(data.bgR, data.bgG, data.bgB);
            GUI.DrawTexture(new Rect(0, 0, position.width, position.height), EditorGUIUtility.whiteTexture);
            GUI.color = prev;
        }

        // Projectウィンドウと体裁を合わせる上部バー(パンくずの上)。右端に設定ボタンを置く。
        private void DrawTopBar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.FlexibleSpace();

            // 並び替えメニュー(名前順/追加順)。優先度は常にこのソートより優先される。
            if (GUILayout.Button(new GUIContent(" " + SortModeLabel(data.sortMode), "並び替え"),
                    EditorStyles.toolbarDropDown, GUILayout.Width(96)))
            {
                var sortRect = GUILayoutUtility.GetLastRect();
                var sortMenu = new GenericMenu();
                AddSortItem(sortMenu, MET_SortMode.NameAsc, "名前順 (A→Z)");
                AddSortItem(sortMenu, MET_SortMode.NameDesc, "名前順 (Z→A)");
                AddSortItem(sortMenu, MET_SortMode.AddedOrder, "追加順");
                sortMenu.DropDown(sortRect);
            }

            // フォルダ追加ボタン。開いている場所(現在の仮想リスト)に作成する。
            // 実フォルダ閲覧中は作成できないため無効化。
            var plusIcon = EditorGUIUtility.IconContent("Toolbar Plus");
            var plusContent = (plusIcon != null && plusIcon.image != null)
                ? new GUIContent(plusIcon.image, "フォルダを追加")
                : new GUIContent("＋", "フォルダを追加");
            using (new EditorGUI.DisabledScope(InRealMode))
            {
                if (GUILayout.Button(plusContent, EditorStyles.toolbarButton, GUILayout.Width(30)))
                    CreateFolderInCurrent();
            }

            // 歯車アイコンが取得できればアイコン、無ければテキストでフォールバック
            var gearIcon = EditorGUIUtility.IconContent("SettingsIcon");
            var settingsContent = (gearIcon != null && gearIcon.image != null)
                ? new GUIContent(gearIcon.image, "設定")
                : new GUIContent("設定", "設定");
            if (GUILayout.Button(settingsContent, EditorStyles.toolbarButton, GUILayout.Width(30)))
                MET_ShortcutConfigPopup.Show(data, () => { Save(); Repaint(); });

            EditorGUILayout.EndHorizontal();
        }

        // 並び替えメニューのラベル(トップバーのドロップダウン表示用)
        private static string SortModeLabel(MET_SortMode mode) => mode switch
        {
            MET_SortMode.NameDesc   => "名前 Z→A",
            MET_SortMode.AddedOrder => "追加順",
            _                       => "名前 A→Z",
        };

        private void AddSortItem(GenericMenu menu, MET_SortMode mode, string label)
        {
            menu.AddItem(new GUIContent(label), data.sortMode == mode, () =>
            {
                if (data.sortMode == mode) return;
                data.sortMode = mode;
                Save();
                Repaint(); // タイルは次のLayoutパスで再構築
            });
        }

        private void DrawBreadcrumb()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            if (GUILayout.Button("ルート", EditorStyles.toolbarButton, GUILayout.Width(50)))
                ClearNav();

            for (int i = 0; i < navStack.Count; i++)
            {
                GUILayout.Label(">", GUILayout.Width(12));
                int captured = i;
                string label = navStack[i].Label ?? "";
                float w = Mathf.Clamp(20 + label.Length * 8, 40, 160);
                if (GUILayout.Button(label, EditorStyles.toolbarButton, GUILayout.Width(w)))
                    TruncateNav(captured + 1);
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        // 下部のアイコンサイズ調整バー(Projectウィンドウ下部のスライダー風)
        private void DrawBottomBar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            // バージョン表記(左下・控えめに小さく)
            var verStyle = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleLeft };
            verStyle.normal.textColor = new Color(1f, 1f, 1f, 0.45f);
            GUILayout.Label(VersionLabel, verStyle, GUILayout.Width(84));

            GUILayout.FlexibleSpace();

            // アイコンサイズ調整スライダー(短め)
            EditorGUI.BeginChangeCheck();
            float newScale = GUILayout.HorizontalSlider(Scale, MinScale, MaxScale, GUILayout.Width(80));
            if (EditorGUI.EndChangeCheck())
            {
                data.iconScale = Mathf.Clamp(newScale, MinScale, MaxScale);
                scaleDirty = true;
                Repaint();
            }

            GUILayout.Space(6);
            GUILayout.Label($"{Mathf.RoundToInt(Scale * 100f)}%", EditorStyles.miniLabel, GUILayout.Width(34));

            // 表示切替(並べて表示 / リスト表示)
            GUILayout.Space(8);
            DrawViewLayoutButtons();

            EditorGUILayout.EndHorizontal();

            // ドラッグ終了時にだけ保存(書き込み頻度を抑える)
            if (scaleDirty && Event.current.rawType == EventType.MouseUp)
            {
                Save();
                scaleDirty = false;
            }
        }

        // 表示形式の切替ボタン(並べて表示 / リスト表示)。トグル風に現在の表示を強調する。
        private void DrawViewLayoutButtons()
        {
            var gridIcon = EditorGUIUtility.IconContent("d_GridLayoutGroup Icon");
            var gridContent = (gridIcon != null && gridIcon.image != null)
                ? new GUIContent(gridIcon.image, "並べて表示")
                : new GUIContent("▦", "並べて表示");
            var listIcon = EditorGUIUtility.IconContent("d_VerticalLayoutGroup Icon");
            var listContent = (listIcon != null && listIcon.image != null)
                ? new GUIContent(listIcon.image, "リスト表示")
                : new GUIContent("≡", "リスト表示");

            bool isGrid = data.viewLayout == MET_ViewLayout.Grid;
            if (GUILayout.Toggle(isGrid, gridContent, EditorStyles.toolbarButton, GUILayout.Width(30)) && !isGrid)
                SetViewLayout(MET_ViewLayout.Grid);

            bool isList = data.viewLayout == MET_ViewLayout.List;
            if (GUILayout.Toggle(isList, listContent, EditorStyles.toolbarButton, GUILayout.Width(30)) && !isList)
                SetViewLayout(MET_ViewLayout.List);
        }

        private void SetViewLayout(MET_ViewLayout layout)
        {
            if (data.viewLayout == layout) return;
            data.viewLayout = layout;
            scroll = Vector2.zero;
            Save();
            Repaint(); // タイルは次のLayoutパスで再構築
        }

        // ============================================================
        // タイル構築
        // ============================================================
        private List<Tile> BuildCurrentTiles()
        {
            if (navStack.Count == 0)
                return BuildVirtualTiles(data.roots);

            var top = navStack[navStack.Count - 1];
            if (top.IsReal)
                return BuildRealTiles(top.realFolderPath);

            return BuildVirtualTiles(top.virtualFolder.children);
        }

        private List<Tile> BuildVirtualTiles(List<MET_ShortcutNode> nodes)
        {
            var list = new List<Tile>(nodes.Count);

            // まず選択中の並び順で整列(OrderByは安定ソート)
            IEnumerable<MET_ShortcutNode> ordered = data.sortMode switch
            {
                MET_SortMode.NameDesc   => nodes.OrderByDescending(n => n.name, StringComparer.OrdinalIgnoreCase),
                MET_SortMode.AddedOrder => nodes,                 // 登録順(リスト順)をそのまま
                _                       => nodes.OrderBy(n => n.name, StringComparer.OrdinalIgnoreCase),
            };
            // 優先度を最優先(安定ソートのため、同値内では上のソート結果を維持)
            ordered = ordered.OrderByDescending(n => n.priority);

            foreach (var n in ordered)
            {
                string path = n.isFolder ? null : AssetDatabase.GUIDToAssetPath(n.assetGuid);
                bool folderTile = n.isFolder || (!string.IsNullOrEmpty(path) && AssetDatabase.IsValidFolder(path));
                list.Add(new Tile
                {
                    node = n,
                    assetPath = path,
                    isFolderTile = folderTile,
                    isRealAsset = false,
                    name = n.name,
                    key = "v:" + n.id,
                    hasColor = n.hasColor,
                    color = new Color(n.colorR, n.colorG, n.colorB),
                    customIconGuid = n.customIconGuid,
                });
            }
            return list;
        }

        private List<Tile> BuildRealTiles(string folderPath)
        {
            var list = new List<Tile>();
            if (string.IsNullOrEmpty(folderPath) || !AssetDatabase.IsValidFolder(folderPath))
                return list;

            // サブフォルダ(実アセット)
            var subs = AssetDatabase.GetSubFolders(folderPath);
            Array.Sort(subs, (a, b) => string.Compare(Path.GetFileName(a), Path.GetFileName(b), StringComparison.OrdinalIgnoreCase));
            foreach (var sub in subs)
            {
                list.Add(new Tile
                {
                    node = null,
                    assetPath = sub,
                    isFolderTile = true,
                    isRealAsset = true,
                    name = Path.GetFileName(sub),
                    key = "r:" + sub,
                });
            }

            // 直下のファイル(実アセット)
            string[] files;
            try { files = Directory.GetFiles(folderPath); }
            catch { files = Array.Empty<string>(); }
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            foreach (var f in files)
            {
                if (f.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;
                string p = f.Replace('\\', '/');
                string fileName = Path.GetFileName(p);
                if (fileName.StartsWith(".")) continue;
                list.Add(new Tile
                {
                    node = null,
                    assetPath = p,
                    isFolderTile = false,
                    isRealAsset = true,
                    name = Path.GetFileNameWithoutExtension(p),
                    key = "r:" + p,
                });
            }
            return list;
        }

        // ============================================================
        // コンテンツ描画(グリッド / リストを切替)
        // ============================================================
        private void DrawContent()
        {
            var items = tilesCache;

            if (items.Count == 0)
            {
                EditorGUILayout.Space(24);
                string msg = InRealMode ? "(空のフォルダ)" : "ここにフォルダ・アセットをドラッグ";
                EditorGUILayout.LabelField(msg, EditorStyles.centeredGreyMiniLabel);
                return;
            }

            if (data.viewLayout == MET_ViewLayout.List)
                DrawListLayout(items);
            else
                DrawGridLayout(items);
        }

        // アイコンを並べて表示(グリッド)
        private void DrawGridLayout(List<Tile> items)
        {
            float availWidth = Mathf.Max(TileWidth, position.width - 20f);
            int columns = Mathf.Max(1, Mathf.FloorToInt(availWidth / TileWidth));
            gridColumns = columns;             // スクロール位置計算のため保持
            lastRowStride = TileHeight + 2f;   // 行間スペース込みの概算
            int rows = Mathf.CeilToInt(items.Count / (float)columns);

            for (int r = 0; r < rows; r++)
            {
                EditorGUILayout.BeginHorizontal();
                for (int c = 0; c < columns; c++)
                {
                    int idx = r * columns + c;
                    if (idx >= items.Count) { GUILayout.Space(TileWidth); continue; }
                    DrawTile(items[idx]);
                }
                EditorGUILayout.EndHorizontal();
            }
        }

        // 1行ずつのリスト表示
        private void DrawListLayout(List<Tile> items)
        {
            gridColumns = 1;                       // リストは常に1列
            lastRowStride = ListRowHeight + 2f;    // 行間スペース込みの概算
            foreach (var t in items) DrawListRow(t);
        }

        // 予約された項目(新規作成したフォルダ等)が見えるようにスクロール位置を調整する。
        // viewport はスクロールビューの表示領域(画面上の矩形)。
        private void ApplyPendingScroll(Rect viewport)
        {
            if (string.IsNullOrEmpty(scrollToKey)) return;
            if (Event.current.type != EventType.Repaint) return; // レイアウト確定後にのみ計算
            if (tilesCache == null) { scrollToKey = null; return; }

            int idx = tilesCache.FindIndex(t => t.key == scrollToKey);
            if (idx < 0) { scrollToKey = null; return; }

            int cols = Mathf.Max(1, gridColumns);
            int row = idx / cols;
            float rowStride = lastRowStride > 1f ? lastRowStride : (TileHeight + 2f);
            float rowTop = row * rowStride;
            float rowBottom = rowTop + rowStride;

            float vh = viewport.height;
            float newY = scroll.y;
            if (rowTop < newY) newY = rowTop;                       // 上に隠れている
            else if (rowBottom > newY + vh) newY = rowBottom - vh;  // 下に隠れている

            newY = Mathf.Max(0f, newY);
            if (!Mathf.Approximately(newY, scroll.y))
            {
                scroll.y = newY;
                Repaint();
            }
            scrollToKey = null;
        }

        // アイコン決定(カスタム画像 > フォルダアイコン > アセットアイコン)。
        // isCustom はカスタム画像かどうか(着色可否の判定に使う)。
        private static Texture ResolveTileIcon(Tile tile, out bool isCustom)
        {
            Texture customIcon = null;
            if (!string.IsNullOrEmpty(tile.customIconGuid))
            {
                string iconPath = AssetDatabase.GUIDToAssetPath(tile.customIconGuid);
                if (!string.IsNullOrEmpty(iconPath))
                {
                    // 基底型 Texture で広く対応し、直接読めない場合はサムネイルにフォールバック
                    customIcon = AssetDatabase.LoadAssetAtPath<Texture>(iconPath);
                    if (customIcon == null) customIcon = AssetDatabase.GetCachedIcon(iconPath);
                }
            }

            isCustom = customIcon != null;
            if (customIcon != null) return customIcon;
            if (tile.isFolderTile) return EditorGUIUtility.IconContent("Folder Icon").image;
            return string.IsNullOrEmpty(tile.assetPath) ? null : AssetDatabase.GetCachedIcon(tile.assetPath);
        }

        // アイコン枠内の実際の画像描画矩形。カスタム画像は縁いっぱいに広がり大きく見えるため、
        // Unityの内蔵アイコン(内側に約2割の余白がある)と footprint を合わせるべく、
        // 均等な余白を取って中央に収める(上下左右そろえることで中央に見える)。
        private static Rect ImageRectInBox(Rect box, bool isCustom)
        {
            if (!isCustom) return box;
            float inset = Mathf.Round(box.width * 0.12f);
            return new Rect(box.x + inset, box.y + inset, box.width - inset * 2f, box.height - inset * 2f);
        }

        // 名前編集フィールドの開始直後フォーカス処理(グリッド/リスト共通)
        private void ApplyRenameFocus()
        {
            if (!renameFocusPending) return;
            EditorGUI.FocusTextInControl(RenameControlName);
            if (Event.current.type == EventType.Repaint &&
                GUI.GetNameOfFocusedControl() == RenameControlName)
                renameFocusPending = false;
        }

        // 名前編集中のキー処理(Enter=確定 / Esc=中止)(グリッド/リスト共通)
        private void HandleRenameKeys()
        {
            var e = Event.current;
            if (e.type != EventType.KeyDown) return;
            if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) { CommitRename(); e.Use(); }
            else if (e.keyCode == KeyCode.Escape) { renamingNode = null; renameFocusPending = false; e.Use(); Repaint(); }
        }

        private void DrawTile(Tile tile)
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(TileWidth));

            Texture icon = ResolveTileIcon(tile, out bool isCustom);

            var iconRect = GUILayoutUtility.GetRect(TileWidth, IconSize);
            var fullTileRect = new Rect(iconRect.x, iconRect.y, TileWidth, TileHeight);

            // 選択ハイライト(背景)
            if (selectedKeys.Contains(tile.key) && Event.current.type == EventType.Repaint)
            {
                var prevC = GUI.color;
                GUI.color = new Color(0.24f, 0.48f, 0.90f, 0.35f);
                GUI.DrawTexture(fullTileRect, EditorGUIUtility.whiteTexture);
                GUI.color = prevC;
            }

            // アイコン帯の中央に置く基準の正方形(バッジ配置の基準。ピクセル整合のため整数丸め)
            float boxX = Mathf.Round(iconRect.x + (iconRect.width - IconSize) / 2f);
            float boxY = Mathf.Round(iconRect.y);
            var iconBoxRect = new Rect(boxX, boxY, IconSize, IconSize);

            if (icon != null)
            {
                // 着色はフォルダのみ。カスタム画像がある場合は画像優先(着色しない)。
                bool tint = tile.isFolderTile && tile.hasColor && !isCustom;
                var prevColor = GUI.color;
                if (tint) GUI.color = tile.color;
                // カスタム画像は縁いっぱいに広がり大きく見えるため、フォルダアイコンと体裁を
                // 合わせて少し内側に収める(上下左右に均等な余白 = 中央寄せに見える)。
                GUI.DrawTexture(ImageRectInBox(iconBoxRect, isCustom), icon, ScaleMode.ScaleToFit);
                GUI.color = prevColor;
            }

            // ↗バッジは「実体(実在フォルダ/ファイル)を指すショートカット」のみに付ける。
            // 仮想フォルダ(node.isFolder)は無印にして、実在フォルダのショートカットと区別する。
            if (!tile.isRealAsset && tile.node != null && !tile.node.isFolder)
                DrawShortcutBadge(iconBoxRect);

            // 優先度バッジ(設定でON かつ 0以外のときのみ、右上に数値表示)
            if (data.showPriorityBadge && !tile.isRealAsset && tile.node != null && tile.node.priority != 0)
                DrawPriorityBadge(iconBoxRect, tile.node.priority);

            // 優先度★バッジ(設定でON かつ 優先度1以上のときのみ、右下に表示。数値と併用可)
            if (data.showPriorityStar && !tile.isRealAsset && tile.node != null && tile.node.priority >= 1)
                DrawPriorityStar(iconBoxRect);

            if (!tile.isRealAsset && renamingNode == tile.node)
            {
                GUI.SetNextControlName(RenameControlName);
                renameBuffer = EditorGUILayout.TextField(renameBuffer, GUILayout.Width(TileWidth));
                ApplyRenameFocus();  // 開始直後のみフォーカスを移す
                HandleRenameKeys();
            }
            else
            {
                var style = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.UpperCenter, wordWrap = true };
                EditorGUILayout.LabelField(tile.name, style, GUILayout.Width(TileWidth), GUILayout.Height(LabelHeight));
            }

            EditorGUILayout.EndVertical();

            HandleTileInteraction(tile, fullTileRect);
            HandleTileContextMenu(tile, fullTileRect);
            HandleTileDragTarget(tile, fullTileRect);
        }

        // リスト表示の1行あたり高さ(スケールに応じて緩やかに変化)
        private float ListRowHeight
        {
            get
            {
                float t = (Scale - MinScale) / (MaxScale - MinScale);
                return Mathf.Round(Mathf.Lerp(18f, 30f, Mathf.Clamp01(t)));
            }
        }

        // リスト表示の1行(アイコン + 名前、必要なら優先度)。
        private void DrawListRow(Tile tile)
        {
            float rowH = ListRowHeight;
            Rect rowRect = EditorGUILayout.GetControlRect(false, rowH, GUILayout.ExpandWidth(true));

            // 選択ハイライト(行全体)
            if (selectedKeys.Contains(tile.key) && Event.current.type == EventType.Repaint)
            {
                var prevC = GUI.color;
                GUI.color = new Color(0.24f, 0.48f, 0.90f, 0.35f);
                GUI.DrawTexture(rowRect, EditorGUIUtility.whiteTexture);
                GUI.color = prevC;
            }

            float iconSize = rowH - 3f;
            var iconRect = new Rect(Mathf.Round(rowRect.x + 4f), Mathf.Round(rowRect.y + 1.5f), iconSize, iconSize);

            Texture icon = ResolveTileIcon(tile, out bool isCustom);
            if (icon != null && Event.current.type == EventType.Repaint)
            {
                bool tint = tile.isFolderTile && tile.hasColor && !isCustom;
                var prevColor = GUI.color;
                if (tint) GUI.color = tile.color;
                GUI.DrawTexture(ImageRectInBox(iconRect, isCustom), icon, ScaleMode.ScaleToFit);
                GUI.color = prevColor;
            }
            // ↗バッジは実体を指すショートカットのみ(仮想フォルダは無印で区別)
            if (!tile.isRealAsset && tile.node != null && !tile.node.isFolder)
                DrawShortcutBadge(iconRect);

            // 優先度★バッジ(設定でON かつ 優先度1以上のときのみ、アイコン右下に表示)
            if (data.showPriorityStar && !tile.isRealAsset && tile.node != null && tile.node.priority >= 1)
                DrawPriorityStar(iconRect);

            float textX = iconRect.xMax + 5f;
            var labelRect = new Rect(textX, rowRect.y, Mathf.Max(10f, rowRect.xMax - textX - 6f), rowRect.height);

            if (!tile.isRealAsset && renamingNode == tile.node)
            {
                GUI.SetNextControlName(RenameControlName);
                float fh = EditorGUIUtility.singleLineHeight;
                var fieldRect = new Rect(labelRect.x, rowRect.y + (rowRect.height - fh) * 0.5f, labelRect.width, fh);
                renameBuffer = EditorGUI.TextField(fieldRect, renameBuffer);
                ApplyRenameFocus();
                HandleRenameKeys();
            }
            else
            {
                // 優先度バッジ表示ON かつ 0以外なら、行の右端に数値を表示し名前領域を狭める
                bool showP = data.showPriorityBadge && !tile.isRealAsset && tile.node != null && tile.node.priority != 0;
                float priorityW = showP ? 52f : 0f;
                var nameRect = new Rect(labelRect.x, labelRect.y, labelRect.width - priorityW, labelRect.height);

                var style = new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleLeft };
                GUI.Label(nameRect, tile.name, style);

                if (showP)
                {
                    var pRect = new Rect(nameRect.xMax, labelRect.y, priorityW, labelRect.height);
                    var pStyle = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleRight };
                    var prev = pStyle.normal.textColor;
                    pStyle.normal.textColor = new Color(0.95f, 0.72f, 0.3f);
                    GUI.Label(pRect, "優先度 " + tile.node.priority, pStyle);
                    pStyle.normal.textColor = prev;
                }
            }

            HandleTileInteraction(tile, rowRect);
            HandleTileContextMenu(tile, rowRect);
            HandleTileDragTarget(tile, rowRect);
        }

        // ============================================================
        // クリック(選択→開く) / 内部ドラッグ開始
        // ============================================================
        private void HandleTileInteraction(Tile tile, Rect rect)
        {
            var evt = Event.current;
            switch (evt.type)
            {
                case EventType.MouseDown:
                    if (evt.button != 0 || !rect.Contains(evt.mousePosition)) break;
                    if (tile.node != null && renamingNode == tile.node) break; // 編集中タイルはTextFieldに任せる
                    if (renamingNode != null) CommitRename(); // 別タイルをクリックしたら編集を確定
                    pressedKey = tile.key;
                    mouseDownPos = evt.mousePosition;
                    dragInitiated = false;
                    // キーボードショートカット(F2/Del)を受け取れるよう、
                    // ウィンドウにフォーカスを移し、他コントロールの入力フォーカスを解除する
                    Focus();
                    GUIUtility.keyboardControl = 0;
                    evt.Use();
                    break;

                case EventType.MouseDrag:
                    // 実アセットは内部ドラッグ対象外(仮想ノードのみ並び替え可能)
                    if (evt.button == 0 && pressedKey == tile.key && !dragInitiated && !tile.isRealAsset &&
                        Vector2.Distance(evt.mousePosition, mouseDownPos) > DragThreshold)
                    {
                        dragInitiated = true;

                        // 掴んだ項目が選択に含まれていれば選択中の仮想ノードをまとめて、
                        // そうでなければ掴んだ1件だけを運ぶ
                        List<MET_ShortcutNode> dragNodes;
                        if (selectedKeys.Contains(tile.key))
                        {
                            dragNodes = SelectedVirtualNodes();
                            if (!dragNodes.Contains(tile.node)) dragNodes.Add(tile.node);
                        }
                        else
                        {
                            selectedKeys.Clear();
                            selectedKeys.Add(tile.key);
                            selectionAnchor = tile.key;
                            dragNodes = new List<MET_ShortcutNode> { tile.node };
                        }

                        DragAndDrop.PrepareStartDrag();
                        DragAndDrop.SetGenericData(DragGenericKey, dragNodes);
                        DragAndDrop.objectReferences = Array.Empty<UnityEngine.Object>();
                        DragAndDrop.StartDrag(dragNodes.Count > 1 ? $"{dragNodes.Count} 項目" :
                            (string.IsNullOrEmpty(tile.name) ? "Shortcut" : tile.name));
                        evt.Use();
                    }
                    break;

                case EventType.MouseUp:
                    if (evt.button == 0 && pressedKey == tile.key)
                    {
                        if (!dragInitiated && rect.Contains(evt.mousePosition))
                            PerformClick(tile);
                        pressedKey = null;
                        dragInitiated = false;
                        evt.Use();
                    }
                    break;
            }
        }

        // 選択中の項目に対する一般的なキーボード操作(F2=リネーム / Del=削除)
        private void HandleKeyboard()
        {
            var evt = Event.current;
            if (evt.type != EventType.KeyDown) return;
            if (renamingNode != null) return;            // 名前編集中は無効
            if (selectedKeys.Count == 0) return;

            if (evt.keyCode == KeyCode.Delete)
            {
                DeleteSelected();
                evt.Use();
            }
            else if (evt.keyCode == KeyCode.F2)
            {
                // 名前変更は単一選択のときのみ
                var nodes = SelectedVirtualNodes();
                if (nodes.Count == 1) { BeginRename(nodes[0]); evt.Use(); }
            }
        }

        // 選択中の仮想ノード(実アセットは除く)
        private List<MET_ShortcutNode> SelectedVirtualNodes()
        {
            var list = new List<MET_ShortcutNode>();
            if (tilesCache == null) return list;
            foreach (var t in tilesCache)
                if (selectedKeys.Contains(t.key) && !t.isRealAsset && t.node != null)
                    list.Add(t.node);
            return list;
        }

        // 名前編集の確定(Enter / フォーカスが外れたとき / 他項目クリック時)
        private void CommitRename()
        {
            if (renamingNode == null) return;
            if (!string.IsNullOrWhiteSpace(renameBuffer))
                renamingNode.name = renameBuffer.Trim();
            renamingNode = null;
            renameFocusPending = false;
            Save();
            Repaint();
        }

        // テキストフィールドからフォーカスが外れたら確定(Projectウィンドウ同様、その辺クリックで確定)
        private void HandleRenameCommitOnBlur()
        {
            if (renamingNode == null || renameFocusPending) return;
            if (Event.current.type != EventType.Repaint) return;
            if (GUI.GetNameOfFocusedControl() != RenameControlName)
                CommitRename();
        }

        private void BeginRename(MET_ShortcutNode node)
        {
            renamingNode = node;
            renameBuffer = node.name;
            renameFocusPending = true;
        }

        // 現在開いている場所に新規フォルダを作成し、選択+名前変更待ち状態で開始する。
        // (+ボタン / 空エリアの右クリックメニューから呼ぶ)
        private void CreateFolderInCurrent()
        {
            var list = CurrentVirtualList;
            if (list == null) return; // 実フォルダ閲覧中は作成しない

            var node = new MET_ShortcutNode { name = "New Folder", isFolder = true };
            list.Add(node);
            Save();

            SelectNodes(new List<MET_ShortcutNode> { node });
            BeginRename(node);
            scrollToKey = "v:" + node.id; // 作成した項目が見えるようスクロール
            Repaint(); // タイルは次のLayoutパスで再構築され、名前編集フィールドにフォーカスが移る
        }

        // 指定フォルダの中に入ってからサブフォルダを作成し、選択+名前変更待ち状態で開始する。
        // (作成したフォルダがその場で見えるよう、対象フォルダへナビゲートする)
        private void CreateSubFolder(MET_ShortcutNode parent)
        {
            if (parent == null || !parent.isFolder) return;

            // 対象フォルダを開いていなければ中に入る(既に開いていれば二重pushしない)
            var top = navStack.Count > 0 ? navStack[navStack.Count - 1] : null;
            if (top == null || top.IsReal || top.virtualFolder != parent)
                PushNav(new NavItem { virtualFolder = parent });

            var node = new MET_ShortcutNode { name = "New Folder", isFolder = true };
            parent.children.Add(node);
            Save();

            SelectNodes(new List<MET_ShortcutNode> { node });
            BeginRename(node);
            scrollToKey = "v:" + node.id; // 作成した項目が見えるようスクロール
            Repaint();
        }

        // 選択中のショートカットをまとめて削除(中身のあるフォルダ・複数選択時は確認ダイアログ)
        private void DeleteSelected()
        {
            var nodes = SelectedVirtualNodes();
            if (nodes.Count == 0) return;

            int folderWithChildren = nodes.Count(n => n.isFolder && n.children != null && n.children.Count > 0);
            if (nodes.Count > 1 || folderWithChildren > 0)
            {
                string msg = nodes.Count == 1
                    ? $"「{nodes[0].name}」を削除しますか?"
                    : $"{nodes.Count} 個の項目を削除しますか?";
                if (folderWithChildren > 0) msg += $"\n※ 中身のあるフォルダが {folderWithChildren} 個含まれます。";
                msg += "\n(実際のアセットは削除されません)";
                if (!EditorUtility.DisplayDialog("ショートカットの削除", msg, "削除", "キャンセル")) return;
            }

            foreach (var n in nodes) RemoveFromTree(data.roots, n);
            selectedKeys.Clear();
            selectionAnchor = null;
            Save();
            Repaint(); // タイルは次のLayoutパスで再構築
        }

        // クリック: 通常=単一選択 / Ctrl(Cmd)=トグル / Shift=範囲 / 無修飾のダブルクリック=開く
        private void PerformClick(Tile tile)
        {
            var evt = Event.current;
            bool additive = evt.control || evt.command;
            bool range = evt.shift;

            if (additive)
            {
                if (!selectedKeys.Remove(tile.key)) selectedKeys.Add(tile.key);
                selectionAnchor = tile.key;
                lastClickKey = null; // ダブルクリック無効化
                Repaint();
                return;
            }

            if (range && !string.IsNullOrEmpty(selectionAnchor))
            {
                SelectRange(selectionAnchor, tile.key);
                lastClickKey = null;
                Repaint();
                return;
            }

            // 通常クリック(単一選択)
            double now = EditorApplication.timeSinceStartup;
            bool isDouble = tile.key == lastClickKey && (now - lastClickTime) <= DoubleClickSeconds;

            selectedKeys.Clear();
            selectedKeys.Add(tile.key);
            selectionAnchor = tile.key;

            if (isDouble)
            {
                lastClickKey = null; // 連続オープン防止(次は改めて2回押しが必要)
                OpenTile(tile);
            }
            else
            {
                lastClickKey = tile.key;
                lastClickTime = now;
            }
            Repaint();
        }

        // 現在のタイル並び順で anchor～target の範囲を選択
        private void SelectRange(string anchorKey, string targetKey)
        {
            if (tilesCache == null) return;
            int ai = tilesCache.FindIndex(t => t.key == anchorKey);
            int bi = tilesCache.FindIndex(t => t.key == targetKey);
            selectedKeys.Clear();
            if (ai < 0 || bi < 0)
            {
                selectedKeys.Add(targetKey);
                return;
            }
            if (ai > bi) { int tmp = ai; ai = bi; bi = tmp; }
            for (int i = ai; i <= bi; i++) selectedKeys.Add(tilesCache[i].key);
            // anchor は維持(以後のShiftクリックで同じ基点から選べるように)
        }

        // グリッドの空き領域をクリックしたら選択解除 + 名前編集を確定
        private void HandleEmptyAreaClick(Rect areaRect)
        {
            var evt = Event.current;
            if (evt.type != EventType.MouseDown || evt.button != 0) return;
            if (!areaRect.Contains(evt.mousePosition)) return;

            if (renamingNode != null) CommitRename();
            selectedKeys.Clear();
            selectionAnchor = null;
            Focus();
            GUIUtility.keyboardControl = 0;
            evt.Use();
            Repaint();
        }

        private void OpenTile(Tile tile)
        {
            if (tile.isFolderTile)
            {
                if (tile.node != null && tile.node.isFolder)
                {
                    PushNav(new NavItem { virtualFolder = tile.node });
                }
                else if (!string.IsNullOrEmpty(tile.assetPath) && AssetDatabase.IsValidFolder(tile.assetPath))
                {
                    OpenRealFolder(tile.assetPath);
                }
            }
            else if (!string.IsNullOrEmpty(tile.assetPath))
            {
                var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(tile.assetPath);
                if (obj != null) AssetDatabase.OpenAsset(obj);
            }
        }

        // 実在フォルダを、設定でチェックされた動作すべてで1回のダブルクリックで開く。
        // (各チェックは独立。どれもOFFのときはショートカット内ブラウズにフォールバック)
        private void OpenRealFolder(string folderPath)
        {
            bool acted = false;

            // Projectタブで見つける(選択・フレーム表示)
            if (data.realOpenRevealInProject)
            {
                RevealFolderInProjectWindow(folderPath);
                acted = true;
            }
            // Projectタブで開く(中に入る)。1回のダブルクリックで確実に中身表示まで行う。
            if (data.realOpenEnterInProject)
            {
                OpenFolderInProjectWindow(folderPath);
                acted = true;
            }
            // ショートカット内で開く(中に入る)
            if (data.realOpenBrowseInShortcut)
            {
                PushNav(new NavItem { realFolderPath = folderPath });
                acted = true;
            }
            // どれも未チェックなら、最低限ショートカット内で開く
            if (!acted)
                PushNav(new NavItem { realFolderPath = folderPath });
        }

        private void PushNav(NavItem item)
        {
            if (renamingNode != null) CommitRename();
            navStack.Add(item);
            ClearSelection();
            scroll = Vector2.zero;
            Repaint(); // タイルは次のLayoutパスで再構築(フレーム途中でnull化しない)
        }

        private void ClearNav()
        {
            if (renamingNode != null) CommitRename();
            navStack.Clear();
            ClearSelection();
            scroll = Vector2.zero;
            Repaint(); // タイルは次のLayoutパスで再構築(フレーム途中でnull化しない)
        }

        private void TruncateNav(int keepCount)
        {
            if (renamingNode != null) CommitRename();
            if (keepCount < navStack.Count)
                navStack.RemoveRange(keepCount, navStack.Count - keepCount);
            ClearSelection();
            scroll = Vector2.zero;
            Repaint(); // タイルは次のLayoutパスで再構築(フレーム途中でnull化しない)
        }

        private void ClearSelection()
        {
            selectedKeys.Clear();
            selectionAnchor = null;
        }

        // ============================================================
        // 右クリックメニュー
        // ============================================================
        private void HandleTileContextMenu(Tile tile, Rect rect)
        {
            if (Event.current.type != EventType.ContextClick || !rect.Contains(Event.current.mousePosition)) return;

            // 選択外の項目を右クリックしたら、その項目だけを選択対象にする
            if (!selectedKeys.Contains(tile.key))
            {
                selectedKeys.Clear();
                selectedKeys.Add(tile.key);
                selectionAnchor = tile.key;
            }

            var menu = new GenericMenu();

            if (tile.isRealAsset)
            {
                // 実アセット: プロジェクトを壊さないよう最小限の操作のみ
                if (tile.isFolderTile)
                    menu.AddItem(new GUIContent("開く"), false, () => OpenTile(tile));
                else if (data.showOpenAssetInMenu) // 「アセットを開く」はコンフィグでONのときのみ表示
                    menu.AddItem(new GUIContent("アセットを開く"), false, () => OpenTile(tile));
                menu.AddItem(new GUIContent("Projectウィンドウで表示"), false, () => PingAsset(tile.assetPath));
            }
            else
            {
                var node = tile.node;
                menu.AddItem(new GUIContent("名前を変更"), false, () => BeginRename(node));
                if (node.isFolder)
                {
                    menu.AddItem(new GUIContent("開く"), false, () => PushNav(new NavItem { virtualFolder = node }));
                    menu.AddItem(new GUIContent("サブフォルダを追加"), false, () => CreateSubFolder(node));
                }
                else
                {
                    // 「アセットを開く」はコンフィグでONのときのみ表示(ダブルクリックでは従来通り開ける)
                    if (data.showOpenAssetInMenu)
                        menu.AddItem(new GUIContent("アセットを開く"), false, () => OpenTile(tile));
                    menu.AddItem(new GUIContent("Projectウィンドウで表示"), false, () => PingAsset(tile.assetPath));
                }

                menu.AddSeparator("");

                // 色はフォルダのみ
                if (tile.isFolderTile)
                {
                    menu.AddItem(new GUIContent("色を設定..."), false, () =>
                    {
                        var initial = node.hasColor ? new Color(node.colorR, node.colorG, node.colorB) : Color.white;
                        MET_ColorPickerPopup.Show(c =>
                        {
                            node.hasColor = true;
                            node.colorR = c.r; node.colorG = c.g; node.colorB = c.b;
                            Save();
                            Repaint();
                        }, initial);
                    });
                    if (node.hasColor)
                        menu.AddItem(new GUIContent("色を解除"), false, () => { node.hasColor = false; Save(); Repaint(); });
                }

                menu.AddItem(new GUIContent("アイコン画像を設定..."), false, () =>
                {
                    Texture current = null;
                    if (!string.IsNullOrEmpty(node.customIconGuid))
                        current = AssetDatabase.LoadAssetAtPath<Texture>(AssetDatabase.GUIDToAssetPath(node.customIconGuid));
                    MET_IconPickerPopup.Show(tex =>
                    {
                        string path = tex != null ? AssetDatabase.GetAssetPath(tex) : null;
                        node.customIconGuid = string.IsNullOrEmpty(path) ? null : AssetDatabase.AssetPathToGUID(path);
                        Save();
                        Repaint();
                    }, current);
                });
                if (!string.IsNullOrEmpty(node.customIconGuid))
                    menu.AddItem(new GUIContent("アイコン画像を解除"), false, () => { node.customIconGuid = null; Save(); Repaint(); });

                menu.AddSeparator("");

                // 優先度(数値が大きいほど上位表示)。複数選択時はまとめて設定。
                var priorityTargets = SelectedVirtualNodes();
                if (priorityTargets.Count == 0) priorityTargets = new List<MET_ShortcutNode> { node };
                string priorityLabel = priorityTargets.Count > 1
                    ? $"優先度を設定... ({priorityTargets.Count})"
                    : "優先度を設定...";
                menu.AddItem(new GUIContent(priorityLabel), false, () =>
                {
                    MET_PriorityPopup.Show(node.priority, p =>
                    {
                        foreach (var t in priorityTargets) t.priority = p;
                        Save();
                        Repaint();
                    });
                });

                menu.AddSeparator("");
                int selCount = SelectedVirtualNodes().Count;
                string delLabel = selCount > 1 ? $"ショートカットから削除 ({selCount})" : "ショートカットから削除";
                menu.AddItem(new GUIContent(delLabel), false, DeleteSelected);
            }

            menu.ShowAsContext();
            Event.current.Use();
        }

        private static void PingAsset(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
            if (obj != null) EditorGUIUtility.PingObject(obj);
        }

        // 実フォルダを Project ウィンドウ上で「見つける」(選択してハイライト表示)。
        private static void RevealFolderInProjectWindow(string folderPath)
        {
            var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(folderPath);
            if (obj == null) return;
            Selection.activeObject = obj;
            EditorGUIUtility.PingObject(obj);
        }

        // 実フォルダを Project ウィンドウで開く(中身を展開して中に入る)。
        // 内部APIを reflection で呼び、取得できない場合は選択+Pingにフォールバックする。
        // ※ 以前は Selection を先に変更していたため初回は「選択(reveal)止まり」になり、
        //   もう一度ダブルクリックしないと中に入れなかった。フォーカスを先に移し、
        //   1カラム表示のときは2カラムに切り替えてから ShowFolderContents を呼ぶことで、
        //   1回のダブルクリックで確実に中へ入るようにする。
        private static void OpenFolderInProjectWindow(string folderPath)
        {
            var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(folderPath);
            if (obj == null) return;

            try
            {
                var browserType = typeof(EditorWindow).Assembly.GetType("UnityEditor.ProjectBrowser");
                if (browserType != null)
                {
                    var browser = EditorWindow.GetWindow(browserType);
                    if (browser != null)
                    {
                        // 先にフォーカスを移さないと初回呼び出しが選択止まりになりやすい
                        browser.Focus();

                        // 1カラム表示だと「中身の展開」が働かないため、2カラムへ切り替える
                        EnsureTwoColumns(browserType, browser);

                        var showContents = browserType.GetMethod("ShowFolderContents",
                            BindingFlags.Instance | BindingFlags.NonPublic);
                        if (showContents != null)
                        {
                            showContents.Invoke(browser, new object[] { obj.GetInstanceID(), true });
                            return;
                        }
                    }
                }
            }
            catch
            {
                // 内部APIが変わっていても壊れないよう、フォールバックに任せる
            }

            // フォールバック: 少なくとも Project ウィンドウ上でフォルダを指し示す
            Selection.activeObject = obj;
            EditorGUIUtility.PingObject(obj);
        }

        // ProjectBrowser が1カラム表示なら2カラムへ切り替える(内部APIが無ければ何もしない)。
        private static void EnsureTwoColumns(Type browserType, EditorWindow browser)
        {
            try
            {
                var viewModeField = browserType.GetField("m_ViewMode", BindingFlags.Instance | BindingFlags.NonPublic);
                var setTwoColumns = browserType.GetMethod("SetTwoColumns", BindingFlags.Instance | BindingFlags.NonPublic);
                if (viewModeField != null && setTwoColumns != null)
                {
                    // m_ViewMode: 0 = OneColumn, 1 = TwoColumns
                    int mode = (int)viewModeField.GetValue(browser);
                    if (mode == 0) setTwoColumns.Invoke(browser, null);
                }
            }
            catch
            {
                // 取得できない場合はそのままの表示モードで開く
            }
        }


        // ============================================================
        // ドラッグ&ドロップ(登録 + 内部並び替え。実フォルダ閲覧中は無効)
        // ============================================================
        private void HandleTileDragTarget(Tile tile, Rect rect)
        {
            var evt = Event.current;
            if (evt.type != EventType.DragUpdated && evt.type != EventType.DragPerform) return;
            if (!rect.Contains(evt.mousePosition)) return;
            if (InRealMode) { DragAndDrop.visualMode = DragAndDropVisualMode.Rejected; return; }

            var parentList = CurrentVirtualList;
            var draggedNodes = GetDraggedNodes();

            if (draggedNodes != null)
            {
                if (!CanDropOn(draggedNodes, tile.node))
                {
                    DragAndDrop.visualMode = DragAndDropVisualMode.Rejected;
                    return;
                }

                DragAndDrop.visualMode = DragAndDropVisualMode.Move;
                DrawDropHighlight(rect);

                if (evt.type == EventType.DragPerform)
                {
                    DragAndDrop.AcceptDrag();
                    if (tile.node.isFolder)
                    {
                        foreach (var d in draggedNodes) RemoveFromTree(data.roots, d);
                        tile.node.children.AddRange(draggedNodes);
                    }
                    else
                    {
                        bool before = evt.mousePosition.x < rect.x + rect.width / 2f;
                        foreach (var d in draggedNodes) RemoveFromTree(data.roots, d);
                        int insertAt = parentList.IndexOf(tile.node);
                        if (insertAt < 0) insertAt = parentList.Count;
                        if (!before) insertAt += 1;
                        parentList.InsertRange(Mathf.Clamp(insertAt, 0, parentList.Count), draggedNodes);
                    }
                    SelectNodes(draggedNodes);
                    Save();
                    Repaint(); // タイルは次のLayoutパスで再構築(フレーム途中でnull化しない)
                    evt.Use();
                }
                else evt.Use();
            }
            else if (DragAndDrop.objectReferences != null && DragAndDrop.objectReferences.Length > 0)
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                DrawDropHighlight(rect);

                if (evt.type == EventType.DragPerform)
                {
                    DragAndDrop.AcceptDrag();
                    var target = (tile.node != null && tile.node.isFolder) ? tile.node.children : parentList;
                    foreach (var obj in DragAndDrop.objectReferences)
                        AddShortcut(target, obj);
                    Save();
                    Repaint(); // タイルは次のLayoutパスで再構築(フレーム途中でnull化しない)
                    evt.Use();
                }
                else evt.Use();
            }
        }

        // ショートカットである事を示す隅バッジ(OSのショートカット矢印風)
        private static GUIStyle badgeStyle;
        private void DrawShortcutBadge(Rect iconDrawRect)
        {
            if (Event.current.type != EventType.Repaint) return;

            // アイコンの大きさに合わせてバッジも拡縮(左下の重さを抑えるため控えめサイズ)
            float s = Mathf.Clamp(iconDrawRect.width * 0.30f, 11f, 20f);
            float radius = s * 0.25f;
            var badge = new Rect(iconDrawRect.x - 1f, iconDrawRect.yMax - s + 1f, s, s);

            // 角丸の暗い下地
            GUI.DrawTexture(badge, EditorGUIUtility.whiteTexture, ScaleMode.StretchToFill, true, 0f,
                new Color(0.10f, 0.10f, 0.10f, 0.92f), 0f, radius);
            // 明るい縁取り
            GUI.DrawTexture(badge, EditorGUIUtility.whiteTexture, ScaleMode.StretchToFill, true, 0f,
                new Color(1f, 1f, 1f, 0.85f), 1f, radius);

            if (badgeStyle == null)
                badgeStyle = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter };
            badgeStyle.fontSize = Mathf.RoundToInt(s * 0.7f);

            var prev = badgeStyle.normal.textColor;
            badgeStyle.normal.textColor = Color.white;
            GUI.Label(badge, "↗", badgeStyle);
            badgeStyle.normal.textColor = prev;
        }

        // 優先度を示す右上バッジ(数値が大きいほど上位表示)
        private static GUIStyle priorityBadgeStyle;
        private void DrawPriorityBadge(Rect iconDrawRect, int priority)
        {
            if (Event.current.type != EventType.Repaint) return;

            string text = priority.ToString();
            float h = Mathf.Clamp(iconDrawRect.width * 0.30f, 12f, 22f);
            float w = Mathf.Max(h, 6f + text.Length * h * 0.5f);
            float radius = h * 0.25f;
            var badge = new Rect(iconDrawRect.xMax - w + 1f, iconDrawRect.y - 1f, w, h);

            // オレンジ系の下地(色付けフォルダと混同しない配色)
            GUI.DrawTexture(badge, EditorGUIUtility.whiteTexture, ScaleMode.StretchToFill, true, 0f,
                new Color(0.88f, 0.55f, 0.12f, 0.95f), 0f, radius);

            if (priorityBadgeStyle == null)
                priorityBadgeStyle = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter };
            priorityBadgeStyle.fontSize = Mathf.RoundToInt(h * 0.62f);

            var prev = priorityBadgeStyle.normal.textColor;
            priorityBadgeStyle.normal.textColor = Color.white;
            GUI.Label(badge, text, priorityBadgeStyle);
            priorityBadgeStyle.normal.textColor = prev;
        }

        // 優先度が1以上であることを示す右下の星バッジ(数値バッジと併用可)
        private static GUIStyle priorityStarStyle;
        private void DrawPriorityStar(Rect iconDrawRect)
        {
            if (Event.current.type != EventType.Repaint) return;

            float s = Mathf.Clamp(iconDrawRect.width * 0.38f, 12f, 26f);
            var rect = new Rect(iconDrawRect.xMax - s + 2f, iconDrawRect.yMax - s + 2f, s, s);

            if (priorityStarStyle == null)
                priorityStarStyle = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter };
            priorityStarStyle.fontSize = Mathf.RoundToInt(s * 0.9f);

            var prev = priorityStarStyle.normal.textColor;
            // 影(視認性確保) → 金色の星
            priorityStarStyle.normal.textColor = new Color(0f, 0f, 0f, 0.6f);
            GUI.Label(new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height), "★", priorityStarStyle);
            priorityStarStyle.normal.textColor = new Color(1f, 0.82f, 0.2f);
            GUI.Label(rect, "★", priorityStarStyle);
            priorityStarStyle.normal.textColor = prev;
        }

        private void DrawDropHighlight(Rect rect)
        {
            if (Event.current.type != EventType.Repaint) return;
            var prev = GUI.color;
            GUI.color = new Color(0.2f, 0.6f, 1f, 0.6f);
            GUI.DrawTexture(rect, EditorGUIUtility.whiteTexture, ScaleMode.StretchToFill, true, 0,
                new Color(0.2f, 0.6f, 1f, 0.15f), 2, 3);
            GUI.color = prev;
        }

        private void HandleGridBackgroundDrop(Rect areaRect)
        {
            var evt = Event.current;
            if (evt.type != EventType.DragUpdated && evt.type != EventType.DragPerform) return;
            if (!areaRect.Contains(evt.mousePosition)) return;
            if (InRealMode) return; // 実フォルダ閲覧中は登録しない

            var draggedNodes = GetDraggedNodes();
            bool external = draggedNodes == null && DragAndDrop.objectReferences != null && DragAndDrop.objectReferences.Length > 0;
            if (draggedNodes == null && !external) return;

            DragAndDrop.visualMode = draggedNodes != null ? DragAndDropVisualMode.Move : DragAndDropVisualMode.Copy;
            if (evt.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                var list = CurrentVirtualList;
                if (draggedNodes != null)
                {
                    foreach (var d in draggedNodes) RemoveFromTree(data.roots, d);
                    list.AddRange(draggedNodes);
                    SelectNodes(draggedNodes);
                }
                else
                {
                    foreach (var obj in DragAndDrop.objectReferences)
                        AddShortcut(list, obj);
                }
                Save();
                Repaint(); // タイルは次のLayoutパスで再構築(フレーム途中でnull化しない)
                evt.Use();
            }
        }

        private static List<MET_ShortcutNode> GetDraggedNodes()
        {
            var nodes = DragAndDrop.GetGenericData(DragGenericKey) as List<MET_ShortcutNode>;
            return (nodes != null && nodes.Count > 0) ? nodes : null;
        }

        // ドロップ可否: 対象が仮想ノードで、運ぶノード自身やその子孫でないこと
        private static bool CanDropOn(List<MET_ShortcutNode> dragged, MET_ShortcutNode target)
        {
            if (target == null) return false;
            foreach (var d in dragged)
            {
                if (d == target) return false;                 // 自分自身へは不可
                if (IsDescendant(d, target)) return false;     // 自分の子孫(=掴んだフォルダの中)へは不可
            }
            return true;
        }

        // 指定ノード群を選択状態にする
        private void SelectNodes(List<MET_ShortcutNode> nodes)
        {
            selectedKeys.Clear();
            foreach (var n in nodes) selectedKeys.Add("v:" + n.id);
            selectionAnchor = nodes.Count > 0 ? "v:" + nodes[nodes.Count - 1].id : null;
        }

        private void AddShortcut(List<MET_ShortcutNode> list, UnityEngine.Object obj)
        {
            string path = AssetDatabase.GetAssetPath(obj);
            if (string.IsNullOrEmpty(path)) return;
            string guid = AssetDatabase.AssetPathToGUID(path);
            if (list.Any(n => !n.isFolder && n.assetGuid == guid)) return; // 重複登録防止
            list.Add(new MET_ShortcutNode { name = obj.name, isFolder = false, assetGuid = guid });
        }

        // ============================================================
        // 何もない場所の右クリック
        // ============================================================
        private void HandleEmptyAreaContextMenu(Rect areaRect)
        {
            var evt = Event.current;
            if (evt.type != EventType.ContextClick || !areaRect.Contains(evt.mousePosition)) return;
            // 実フォルダ閲覧中は新規作成も背景色も対象外
            if (InRealMode) { evt.Use(); return; }

            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("新規フォルダ"), false, CreateFolderInCurrent);
            menu.AddSeparator("");
            // 背景色はコンフィグ(上部の設定ボタン)へ一本化
            menu.AddItem(new GUIContent("設定を開く..."), false,
                () => MET_ShortcutConfigPopup.Show(data, () => { Save(); Repaint(); }));

            menu.ShowAsContext();
            evt.Use();
        }

        // ============================================================
        // ツリー操作ヘルパー
        // ============================================================
        private static bool RemoveFromTree(List<MET_ShortcutNode> list, MET_ShortcutNode target)
        {
            if (list.Remove(target)) return true;
            foreach (var n in list)
            {
                if (n.isFolder && RemoveFromTree(n.children, target)) return true;
            }
            return false;
        }

        private static bool IsDescendant(MET_ShortcutNode possibleAncestor, MET_ShortcutNode node)
        {
            if (!possibleAncestor.isFolder) return false;
            foreach (var c in possibleAncestor.children)
            {
                if (c == node || IsDescendant(c, node)) return true;
            }
            return false;
        }
    }

    /// <summary>
    /// ショートカットウィンドウのコンフィグ(設定)ポップアップ。
    /// 対象の MET_ShortcutData を直接編集し、変更のたびに onChanged(保存+再描画)を呼ぶ。
    /// </summary>
    public class MET_ShortcutConfigPopup : EditorWindow
    {
        private MET_ShortcutData data;
        private Action onChanged;

        private const float WinW = 360f;
        private const float WinH = 344f;

        public static void Show(MET_ShortcutData data, Action onChanged)
        {
            if (data == null) return;
            var win = CreateInstance<MET_ShortcutConfigPopup>();
            win.data = data;
            win.onChanged = onChanged;
            win.titleContent = new GUIContent("ショートカット設定");
            win.minSize = win.maxSize = new Vector2(WinW, WinH);
            var mainPos = EditorGUIUtility.GetMainWindowPosition();
            float x = mainPos.x + (mainPos.width - WinW) * 0.5f;
            float y = mainPos.y + (mainPos.height - WinH) * 0.5f;
            win.position = new Rect(x, y, WinW, WinH);
            win.ShowUtility();
        }

        private void OnGUI()
        {
            if (data == null) { Close(); return; }

            GUILayout.Space(10);

            // --- 背景色(全体で1つの既定) ---
            GUILayout.Label("背景色(ショートカット全体の既定)", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(6);
                EditorGUI.BeginChangeCheck();
                bool useBg = EditorGUILayout.ToggleLeft("背景色を使用", data.hasBgColor, GUILayout.Width(120));
                if (EditorGUI.EndChangeCheck())
                {
                    data.hasBgColor = useBg;
                    onChanged?.Invoke();
                }

                using (new EditorGUI.DisabledScope(!data.hasBgColor))
                {
                    EditorGUI.BeginChangeCheck();
                    Color c = EditorGUILayout.ColorField(new Color(data.bgR, data.bgG, data.bgB));
                    if (EditorGUI.EndChangeCheck())
                    {
                        data.bgR = c.r; data.bgG = c.g; data.bgB = c.b;
                        onChanged?.Invoke();
                    }
                }
                GUILayout.Space(6);
            }

            GUILayout.Space(12);

            // --- 本物フォルダの開き方(複数チェック可) ---
            GUILayout.Label("本物のフォルダをダブルクリックで開いたとき", EditorStyles.boldLabel);
            GUILayout.Label("(チェックした動作をまとめて実行。複数選択可)", EditorStyles.miniLabel);
            DrawRealOpenToggle("ショートカット内で開く(中に入る)",
                data.realOpenBrowseInShortcut, v => data.realOpenBrowseInShortcut = v);
            DrawRealOpenToggle("Projectタブで見つける",
                data.realOpenRevealInProject, v => data.realOpenRevealInProject = v);
            DrawRealOpenToggle("Projectタブで開く(中に入る)",
                data.realOpenEnterInProject, v => data.realOpenEnterInProject = v);

            GUILayout.Space(12);

            // --- 動作設定 ---
            GUILayout.Label("動作", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            bool showOpenAsset = EditorGUILayout.ToggleLeft(
                "右クリックに「アセットを開く」を表示",
                data.showOpenAssetInMenu);
            if (EditorGUI.EndChangeCheck())
            {
                data.showOpenAssetInMenu = showOpenAsset;
                onChanged?.Invoke();
            }

            EditorGUI.BeginChangeCheck();
            bool showBadge = EditorGUILayout.ToggleLeft(
                "優先度バッジ(右上の数値)を表示",
                data.showPriorityBadge);
            if (EditorGUI.EndChangeCheck())
            {
                data.showPriorityBadge = showBadge;
                onChanged?.Invoke();
            }

            EditorGUI.BeginChangeCheck();
            bool showStar = EditorGUILayout.ToggleLeft(
                "優先度★バッジ(右下の星・優先度1以上)を表示",
                data.showPriorityStar);
            if (EditorGUI.EndChangeCheck())
            {
                data.showPriorityStar = showStar;
                onChanged?.Invoke();
            }

            GUILayout.FlexibleSpace();

            using (new EditorGUILayout.HorizontalScope())
            {
                // バージョン + 最終更新日(左下)
                var verStyle = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleLeft };
                verStyle.normal.textColor = new Color(1f, 1f, 1f, 0.5f);
                GUILayout.Label($"{MET_ShortcutWindow.VersionLabel}  (最終更新: {MET_ShortcutWindow.VersionDate})", verStyle);

                GUILayout.FlexibleSpace();
                if (GUILayout.Button("閉じる", GUILayout.Width(80), GUILayout.Height(24)))
                    Close();
            }
            GUILayout.Space(8);
        }

        // 独立チェックボックスの1項目(複数選択可)。変更時に onChanged を呼ぶ。
        private void DrawRealOpenToggle(string label, bool current, Action<bool> apply)
        {
            EditorGUI.BeginChangeCheck();
            bool now = EditorGUILayout.ToggleLeft(label, current);
            if (EditorGUI.EndChangeCheck())
            {
                apply(now);
                onChanged?.Invoke();
            }
        }
    }

    /// <summary>
    /// 優先度(整数)入力用の簡易ポップアップ。
    /// 数値が大きいほどショートカット一覧の上位に表示される。
    /// </summary>
    public class MET_PriorityPopup : EditorWindow
    {
        private int value;
        private Action<int> onApply;
        private bool focusPending = true;

        private const float WinW = 260f;
        private const float WinH = 118f;
        private const string FieldName = "MET_priority_field";

        public static void Show(int initial, Action<int> onApply)
        {
            var win = CreateInstance<MET_PriorityPopup>();
            win.value = initial;
            win.onApply = onApply;
            win.titleContent = new GUIContent("優先度");
            win.minSize = win.maxSize = new Vector2(WinW, WinH);
            var mainPos = EditorGUIUtility.GetMainWindowPosition();
            float x = mainPos.x + (mainPos.width - WinW) * 0.5f;
            float y = mainPos.y + (mainPos.height - WinH) * 0.5f;
            win.position = new Rect(x, y, WinW, WinH);
            win.ShowUtility();
        }

        private void OnGUI()
        {
            GUILayout.Space(8);
            GUILayout.Label("数値が大きいほど上位に表示されます", EditorStyles.miniLabel);
            GUILayout.Space(6);

            GUI.SetNextControlName(FieldName);
            value = EditorGUILayout.IntField("優先度", value);

            // 開いた直後は入力欄にフォーカスを当てる
            if (focusPending)
            {
                EditorGUI.FocusTextInControl(FieldName);
                if (Event.current.type == EventType.Repaint) focusPending = false;
            }

            var e = Event.current;
            if (e.type == EventType.KeyDown)
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) { Apply(); e.Use(); }
                else if (e.keyCode == KeyCode.Escape) { Close(); e.Use(); }
            }

            GUILayout.FlexibleSpace();
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("適用", GUILayout.Width(80), GUILayout.Height(24))) Apply();
                if (GUILayout.Button("キャンセル", GUILayout.Width(80), GUILayout.Height(24))) Close();
            }
            GUILayout.Space(6);
        }

        private void Apply()
        {
            onApply?.Invoke(value);
            Close();
        }
    }
}
