// v0.10.2
// 衣装トグル設定(CostumeToggleConfig)から Modular Avatar 部品を生成する（非破壊）。
// v0.10.2: 対象オブジェクトも素体シェイプも無い（未設定／参照がMissing）アイテムはメニュー項目を作らない（死にボタン防止）。
//          有効アイテムが1つも無くなったカテゴリも、カテゴリボタン自体を作らない（空サブメニュー防止）。
// v0.10.1: フォールバック（全裸）対策は MA 任せにする方針を確認。単純トグルは全て MA Object Toggle で生成する。
//          MenuItem.isDefault（= item.defaultOn）を設定すれば、MA の Reactive Object が初期状態＝表示を焼き
//          （initial-state クリップ）、m_IsActive を常時アニメするため、同期前・アニメ非評価・インポスター生成でも
//          表示が保たれる。自前FXレイヤーは不要（v0.10.0 の初期ON→FXロジック層化を取り消し）。
//          条件付き（自動で隠す/自動表示）は MA Object Toggle では表現できないため従来どおりFXロジック層。
// v0.9.1: FX制御アイテム（自動表示/条件付き）は編集中の表示を初期状態に合わせる。
//         常時ONの自動表示（ケモミミ等）は生成してもONのまま残る。
// 生成構造: アバター直下に空の「MA Menu Manager」を作り、その下に
//   ・設定オブジェクト「【衣装トグル設定】(EditorOnlyにする)」（EditorOnly：アップロード時に自動除去）
//   ・「MA 衣装トグル (生成物)」（機能本体。EditorOnlyにしない）
// を配置する。再生成時は生成物だけ作り直す。生成時に設定を自動バックアップ（アバターごと最大10件）。
// 対応範囲:
//   カテゴリ=サブメニュー / アイテム=トグル / 排他(カテゴリ内ラジオ) / デフォルトON
//   中間フォルダ（カテゴリに所属フォルダ名を付けると、着替え内でフォルダにまとまる）
//   自動で隠す条件（アイテム／カテゴリ参照）/ 自動表示（条件が全OFFなら下地を自動表示。条件なしなら常時表示）
//   メニューアイコン（アイテム/カテゴリ/フォルダ/コーディネイトに任意設定）
//   着用時の素体ブレンドシェイプ変更/削除（MA ShapeChanger。既存トグルに反応＝追加パラメータなし）
//   コーディネイト（1ボタンで複数トグルの組合せを呼び出し。Parameter Driver + Merge Animator）
//   自動で隠す条件（指定アイテムがONのとき自分を隠す。FXロジック層で m_IsActive を制御）
//
// 仕組み:
//   ・通常アイテム（隠す条件なし）: 「MA Menu Item(Toggle) + MA Object Toggle」で対象をON/OFF。元の表示はOFFに揃える（初期ONは isDefault で MA が初期表示を焼く）
//   ・条件付きアイテム（自動で隠す条件あり）: Object Toggle は使わず、FXに合流するアニメーターのレイヤーで
//       「自分ON かつ 指定アイテムが全部OFF のときだけ表示」になるよう m_IsActive をアニメ（瞬時切替）
//   ・排他カテゴリは同じ Int パラメータ＋明示値(1,2,3…)でラジオ化
//   ・コーディネイトは Button メニュー→Int パラメータ→Parameter Driver で各トグルをまとめてセット

using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using nadena.dev.modular_avatar.core;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC_AvatarParameterDriver = VRC.SDKBase.VRC_AvatarParameterDriver;

namespace Metlab.Toggle
{
    public static class CostumeToggleGenerator
    {
        private const string ManagerName = "MA Menu Manager";
        private const string ConfigObjectName = "【衣装トグル設定】(EditorOnlyにする)";
        private const string GeneratedRootName = "MA 衣装トグル (生成物)";
        private const string ParamPrefix = "MACT_";
        private const string PresetParam = ParamPrefix + "Preset";
        private const string BaseFolder = "Assets/MetlabGenerated/トグルをつくるやつ";
        private const string BackupFolder = "Assets/MetlabGenerated/トグルをつくるやつ/_Backups";
        public const int MaxBackups = 10;

        /// <summary>1アイテムのパラメータ情報（他アイテムから条件参照するため）。</summary>
        private struct ParamInfo
        {
            public string param;
            public bool isInt;
            public int onValue;
            public bool defaultOn;
        }

        /// <summary>1カテゴリのパラメータ情報（カテゴリ参照の条件展開に使う）。</summary>
        private class GroupInfo
        {
            public bool exclusive;
            public string groupParam;              // 排他カテゴリの共有Intパラメータ
            public List<ParamInfo> items = new List<ParamInfo>();
            public bool anyDefaultOn;
        }

        /// <summary>メニュー生成後に集めたアイテム情報。</summary>
        private class ItemInfo
        {
            public GameObject iGo;
            public CostumeItem item;
            public CostumeGroup group;
            public string param;
            public bool isInt;
            public int onValue;
            public string label;
        }

        /// <summary>自動表示アイテム（手動トグルなし。条件が全OFFのとき表示）。</summary>
        private class AutoItem
        {
            public CostumeItem item;
            public List<GameObject> objects;
            public string label;
        }

        /// <summary>隠す条件の最小単位（covered = これらのいずれかが"ON"）。</summary>
        private struct CoverCond
        {
            public string param;
            public bool isInt;
            public int onValue;       // 単体/非排他アイテムの値
            public bool nonZero;      // 排他カテゴリ参照：param != 0 のとき covered
            public bool defaultCovered;
        }

        /// <summary>FXアニメーターで可視性を制御するアイテム。</summary>
        private class LogicItem
        {
            public bool hasOwnToggle;              // 自動表示アイテムは false（自分のトグルを持たない）
            public string param;
            public bool isInt;
            public int onValue;
            public bool defaultOn;
            public List<GameObject> objects;
            public string label;
            public List<CoverCond> covers = new List<CoverCond>();
        }

        private static string Key(string groupName, string itemName) => (groupName ?? "") + "|" + (itemName ?? "");

        public static bool Generate(CostumeToggleConfig config, out string message)
        {
            message = "";
            if (config == null) { message = "設定がありません。"; return false; }

            var descriptor = config.GetComponentInParent<VRCAvatarDescriptor>();
            if (descriptor == null)
            {
                message = "アバター(VRCAvatarDescriptor)が見つかりません。設定コンポーネントをアバターの下に置いてください。";
                return false;
            }
            Transform avatarRoot = descriptor.transform;

            RemoveGenerated(config);

            // アバター直下に「MA Menu Manager」を用意し、その下に設定と生成物を配置
            var manager = EnsureManager(avatarRoot);
            ReparentConfig(config, manager);

            var rootGo = new GameObject(GeneratedRootName);
            Undo.RegisterCreatedObjectUndo(rootGo, "衣装トグル生成");
            rootGo.transform.SetParent(manager.transform, false);
            // 起点：未指定ならメインメニュー、指定があればそのメニューへ設置
            var installer = rootGo.AddComponent<ModularAvatarMenuInstaller>();
            installer.installTargetMenu = config.installTargetMenu;
            // MA 1.18 では Installer は「同じオブジェクト上の MenuSource」を設置対象にする。
            // rootGo 直下の MenuItem 群（単一フォルダON=「衣装」1個 / OFF=各カテゴリ）をまとめて設置。
            rootGo.AddComponent<ModularAvatarMenuGroup>();

            // 中身の構成：単一フォルダにまとめるか、起点へ直接並べるか
            GameObject menuParentGo;
            if (config.wrapInFolder)
            {
                menuParentGo = new GameObject(string.IsNullOrEmpty(config.menuName) ? "衣装" : config.menuName);
                menuParentGo.transform.SetParent(rootGo.transform, false);
                var menuRootItem = menuParentGo.AddComponent<ModularAvatarMenuItem>();
                menuRootItem.Control = new VRCExpressionsMenu.Control { type = VRCExpressionsMenu.Control.ControlType.SubMenu };
                menuRootItem.MenuSource = SubmenuSource.Children;
                menuRootItem.label = menuParentGo.name;
            }
            else
            {
                menuParentGo = rootGo;
            }

            // --- パス1：メニュー生成＋アイテム情報の収集 ---
            var itemInfos = new List<ItemInfo>();
            var paramLookup = new Dictionary<string, ParamInfo>();
            var groupLookup = new Dictionary<string, GroupInfo>();   // カテゴリ参照の条件展開用
            var autoItems = new List<AutoItem>();                    // 自動表示アイテム（手動トグルなし）
            // 中間フォルダ（所属フォルダ名ごとに1つ。初出順で着替え直下に作成）
            var sectionFolders = new Dictionary<string, GameObject>();
            // フォルダ名 → アイコン
            var sectionIcons = new Dictionary<string, Texture2D>();
            if (config.sectionIcons != null)
                foreach (var si in config.sectionIcons)
                    if (si != null && !string.IsNullOrEmpty(si.sectionName)) sectionIcons[si.sectionName] = si.icon;

            int groupIndex = 0;
            foreach (var group in config.groups)
            {
                if (group == null) continue;
                groupIndex++;

                // 有効なアイテム（対象オブジェクト or 素体シェイプを持つ）が1つも無いカテゴリは、
                // 開いても空のサブメニューなので、カテゴリボタン自体を作らない。
                // （groupIndex は進めておき、他カテゴリのパラメータ名を安定させる）
                if (group.items == null || !group.items.Any(it => it != null && ItemHasEffect(it)))
                    continue;

                // カテゴリの親：所属フォルダ指定があれば中間フォルダ、なければ着替え直下
                Transform catParent = menuParentGo.transform;
                if (!string.IsNullOrEmpty(group.section))
                {
                    if (!sectionFolders.TryGetValue(group.section, out var folderGo))
                    {
                        folderGo = new GameObject(group.section);
                        folderGo.transform.SetParent(menuParentGo.transform, false);
                        var fItem = folderGo.AddComponent<ModularAvatarMenuItem>();
                        fItem.Control = new VRCExpressionsMenu.Control { type = VRCExpressionsMenu.Control.ControlType.SubMenu };
                        fItem.MenuSource = SubmenuSource.Children;
                        fItem.label = folderGo.name;
                        if (sectionIcons.TryGetValue(group.section, out var ficon)) fItem.Control.icon = ficon;
                        sectionFolders[group.section] = folderGo;
                    }
                    catParent = folderGo.transform;
                }

                var gGo = new GameObject(string.IsNullOrEmpty(group.name) ? $"カテゴリ{groupIndex}" : group.name);
                gGo.transform.SetParent(catParent, false);
                var gItem = gGo.AddComponent<ModularAvatarMenuItem>();
                gItem.Control = new VRCExpressionsMenu.Control { type = VRCExpressionsMenu.Control.ControlType.SubMenu, icon = group.icon };
                gItem.MenuSource = SubmenuSource.Children;
                gItem.label = gGo.name;

                string groupParam = GroupParam(group, groupIndex);
                var ginfo = new GroupInfo { exclusive = group.exclusive, groupParam = groupParam };
                groupLookup[group.name ?? ""] = ginfo;

                int itemIndex = 0;
                foreach (var item in group.items)
                {
                    if (item == null) continue;
                    itemIndex++;
                    // 対象オブジェクトも素体シェイプも無い（未設定／参照がMissing）アイテムは、
                    // 押しても何も起きない死にボタンになるため、メニュー項目自体を作らない。
                    // itemIndex は進めておき、隣のスキップで既存アイテムのパラメータ名（index由来）が変わらないようにする。
                    if (!ItemHasEffect(item)) continue;

                    string label = Sanitize(group.name) + "_" + Sanitize(item.name) + "_" + itemIndex;

                    // 自動表示アイテム：手動トグル・パラメータを作らず、条件だけで表示する
                    // （隠す条件があれば条件OFF時のみ表示、条件が無ければ常時表示）
                    bool auto = item.autoShowWhenUncovered;
                    if (auto)
                    {
                        autoItems.Add(new AutoItem
                        {
                            item = item,
                            objects = item.objects.Where(o => o != null).ToList(),
                            label = label,
                        });
                        continue;
                    }

                    var iGo = new GameObject(string.IsNullOrEmpty(item.name) ? $"アイテム{itemIndex}" : item.name);
                    iGo.transform.SetParent(gGo.transform, false);

                    string param = ItemParam(group, groupParam, item, itemIndex);
                    int onValue = ItemOnValue(group, itemIndex);

                    var mi = iGo.AddComponent<ModularAvatarMenuItem>();
                    mi.Control = new VRCExpressionsMenu.Control
                    {
                        type = VRCExpressionsMenu.Control.ControlType.Toggle,
                        parameter = new VRCExpressionsMenu.Control.Parameter { name = param },
                        value = onValue,
                        icon = item.icon,
                    };
                    mi.automaticValue = false;
                    mi.isSaved = true;
                    mi.isSynced = true;
                    mi.isDefault = item.defaultOn;
                    mi.label = iGo.name;

                    var pinfo = new ParamInfo
                    {
                        param = param,
                        isInt = group.exclusive,
                        onValue = onValue,
                        defaultOn = item.defaultOn,
                    };
                    paramLookup[Key(group.name, item.name)] = pinfo;
                    ginfo.items.Add(pinfo);
                    if (item.defaultOn) ginfo.anyDefaultOn = true;

                    itemInfos.Add(new ItemInfo
                    {
                        iGo = iGo,
                        item = item,
                        group = group,
                        param = param,
                        isInt = group.exclusive,
                        onValue = onValue,
                        label = label,
                    });
                }
            }

            // --- パス2：可視性の決定（単純トグル or ロジック層） ---
            var logicItems = new List<LogicItem>();
            foreach (var info in itemInfos)
            {
                bool hasHide = info.item.hideWhen != null && info.item.hideWhen.Count > 0;
                var objects = info.item.objects.Where(o => o != null).ToList();

                // 着用時の素体ブレンドシェイプ変更（既存トグルに反応＝追加パラメータなし）
                AddShapeChanger(info.iGo, info.item.bodyShapes);

                if (!hasHide)
                {
                    // 単純トグル：MA Object Toggle。表示/非表示が成立するよう元の表示はOFFへ。
                    // 初期ONアイテムのフォールバック安全性は MA が担保する：MenuItem.isDefault（下で設定済み）により
                    // MA の Reactive Object が初期状態＝表示を焼き（initial-state クリップ）、m_IsActive を常時アニメするため、
                    // 同期前・アニメ非評価・インポスター生成でも表示が保たれる。自前FXレイヤーは不要。
                    var ot = info.iGo.AddComponent<ModularAvatarObjectToggle>();
                    var list = new List<ToggledObject>();
                    foreach (var go in objects)
                    {
                        var r = new AvatarObjectReference();
                        r.Set(go);
                        list.Add(new ToggledObject { Object = r, Active = true });
                        if (go.activeSelf)
                        {
                            Undo.RecordObject(go, "衣装トグル 初期表示をOFFに");
                            go.SetActive(false);
                        }
                    }
                    ot.Objects = list;
                    continue;
                }

                // ロジック層：自分ON かつ 条件が全部"非covered" のときだけ表示（m_IsActive の瞬時切替）
                var li = new LogicItem
                {
                    hasOwnToggle = true,
                    param = info.param,
                    isInt = info.isInt,
                    onValue = info.onValue,
                    defaultOn = info.item.defaultOn,
                    objects = objects,
                    label = info.label,
                    covers = ResolveCovers(info.item.hideWhen, paramLookup, groupLookup),
                };
                // 編集中の表示を初期状態に合わせる（実機はFXが m_IsActive を制御するのでシーン状態には依存しない）
                SetSceneStateToDefault(li);
                logicItems.Add(li);
            }

            // 自動表示アイテム：手動トグルなし。条件が全部"非covered" なら表示
            foreach (var ai in autoItems)
            {
                var li = new LogicItem
                {
                    hasOwnToggle = false,
                    objects = ai.objects,
                    label = ai.label,
                    covers = ResolveCovers(ai.item.hideWhen, paramLookup, groupLookup),
                };
                // 自動表示（常時ON or 条件OFF時のみ表示）の初期状態に合わせる。ケモミミ等の常時ONは表示のまま残る
                SetSceneStateToDefault(li);
                logicItems.Add(li);
            }

            var validPresets = config.presets?.Where(p => p != null).ToList() ?? new List<CostumePreset>();

            // --- アニメーター（ロジック or コーディネイトがある時だけ） ---
            if (logicItems.Count > 0 || validPresets.Count > 0)
            {
                string avatarFolder = BaseFolder + "/" + Sanitize(descriptor.name);
                if (AssetDatabase.IsValidFolder(avatarFolder)) AssetDatabase.DeleteAsset(avatarFolder);
                avatarFolder = EnsureFolder(BaseFolder, Sanitize(descriptor.name));

                string controllerPath = avatarFolder + "/CostumeToggles.controller";
                var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);

                if (logicItems.Count > 0)
                {
                    string clipsFolder = EnsureFolder(avatarFolder, "Clips");
                    foreach (var li in logicItems)
                        AddLogicLayer(controller, clipsFolder, li, avatarRoot);
                }

                if (validPresets.Count > 0)
                {
                    AddPresetLayer(controller, config, validPresets);
                    BuildPresetMenu(menuParentGo, validPresets, config.coordinateMenuName);
                }

                EditorUtility.SetDirty(controller);

                var merge = rootGo.AddComponent<ModularAvatarMergeAnimator>();
                merge.animator = controller;
                merge.layerType = VRCAvatarDescriptor.AnimLayerType.FX;
                merge.pathMode = MergeAnimatorPathMode.Absolute;
                merge.matchAvatarWriteDefaults = true;
                merge.deleteAttachedAnimator = false;
            }

            Undo.RecordObject(config, "衣装トグル生成物を記録");
            config.generatedRoot = rootGo;
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();

            // 設定の自動バックアップ（アバターごと最大 MaxBackups 件・軽量な小アセット）
            SaveBackup(config, descriptor);

            message = $"生成しました。カテゴリ{groupIndex}個 / コーディネイト{validPresets.Count}個 / 条件付き{logicItems.Count}個。";
            return true;
        }

        // =============================================================
        //  構造（MA Menu Manager）
        // =============================================================

        private static GameObject EnsureManager(Transform avatarRoot)
        {
            var t = avatarRoot.Find(ManagerName);
            if (t != null) return t.gameObject;
            var go = new GameObject(ManagerName);
            Undo.RegisterCreatedObjectUndo(go, "MA Menu Manager 生成");
            go.transform.SetParent(avatarRoot, false);
            return go;
        }

        // 設定オブジェクトを Manager 配下へ移動＋改名＋EditorOnly 化（生成物は EditorOnly にしない）
        private static void ReparentConfig(CostumeToggleConfig config, GameObject manager)
        {
            var cgo = config.gameObject;
            if (cgo == manager) return;
            Undo.RegisterCompleteObjectUndo(cgo, "設定を整理");
            if (cgo.transform.parent != manager.transform)
                Undo.SetTransformParent(cgo.transform, manager.transform, "設定を移動");
            cgo.name = ConfigObjectName;
            if (!cgo.CompareTag("EditorOnly")) cgo.tag = "EditorOnly";
            EditorUtility.SetDirty(cgo);
        }

        // =============================================================
        //  自動バックアップ
        // =============================================================

        private static void SaveBackup(CostumeToggleConfig config, VRCAvatarDescriptor descriptor)
        {
            string dir = EnsureFolder(BackupFolder, Sanitize(descriptor.name));
            var asset = ScriptableObject.CreateInstance<CostumeTogglePreset>();
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(config), asset);
            string stamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string path = AssetDatabase.GenerateUniqueAssetPath(dir + "/backup_" + stamp + ".asset");
            AssetDatabase.CreateAsset(asset, path);

            // 古いものを MaxBackups 件まで削減
            var paths = AssetDatabase.FindAssets("t:CostumeTogglePreset", new[] { dir })
                .Select(AssetDatabase.GUIDToAssetPath)
                .OrderBy(p => p) // backup_ タイムスタンプ順
                .ToList();
            for (int i = 0; i < paths.Count - MaxBackups; i++)
                AssetDatabase.DeleteAsset(paths[i]);

            AssetDatabase.SaveAssets();
        }

        /// <summary>対象オブジェクト（非null）か有効な素体シェイプのどちらかを持つか。どちらも無い＝死にボタンなので生成対象外。</summary>
        private static bool ItemHasEffect(CostumeItem item)
        {
            bool hasObj = item.objects != null && item.objects.Any(o => o != null);
            bool hasShapes = item.bodyShapes != null &&
                             item.bodyShapes.Any(s => s != null && s.renderer != null && !string.IsNullOrEmpty(s.shapeName));
            return hasObj || hasShapes;
        }

        /// <summary>着用時に素体ブレンドシェイプを変更する MA ShapeChanger を付与（既存メニュートグルに反応）。</summary>
        private static void AddShapeChanger(GameObject iGo, List<BodyShape> shapes)
        {
            if (iGo == null || shapes == null) return;
            var valid = shapes.Where(s => s != null && s.renderer != null && !string.IsNullOrEmpty(s.shapeName)).ToList();
            if (valid.Count == 0) return;

            var sc = iGo.AddComponent<ModularAvatarShapeChanger>();
            var list = new List<ChangedShape>();
            foreach (var s in valid)
            {
                var r = new AvatarObjectReference();
                r.Set(s.renderer);
                list.Add(new ChangedShape
                {
                    Object = r,
                    ShapeName = s.shapeName,
                    ChangeType = s.delete ? ShapeChangeType.Delete : ShapeChangeType.Set,
                    Value = s.delete ? 100f : s.value,
                });
            }
            sc.Shapes = list;
        }

        // 編集中の表示を初期状態に合わせる（FX制御アイテム用。実機はFXが m_IsActive を決めるので安全）
        private static void SetSceneStateToDefault(LogicItem li)
        {
            bool coveredDefault = li.covers.Any(c => c.defaultCovered);
            bool visible = (!li.hasOwnToggle || li.defaultOn) && !coveredDefault;
            foreach (var go in li.objects)
            {
                if (go == null || go.activeSelf == visible) continue;
                Undo.RecordObject(go, "衣装トグル 初期表示を設定");
                go.SetActive(visible);
            }
        }

        // =============================================================
        //  ロジック・レイヤー（自動で隠す条件）
        // =============================================================

        private static void AddLogicLayer(AnimatorController controller, string clipsFolder,
            LogicItem li, Transform avatarRoot)
        {
            const float seconds = 0f; // 瞬時切替

            if (li.hasOwnToggle)
                EnsureParam(controller, li.param, li.isInt ? AnimatorControllerParameterType.Int : AnimatorControllerParameterType.Bool);
            foreach (var c in li.covers)
                EnsureParam(controller, c.param, c.isInt ? AnimatorControllerParameterType.Int : AnimatorControllerParameterType.Bool);

            AnimationClip visible = MakeActiveClip(li, avatarRoot, true);
            AnimationClip hidden = MakeActiveClip(li, avatarRoot, false);
            visible.name = li.label + "_Visible";
            hidden.name = li.label + "_Hidden";
            AssetDatabase.CreateAsset(visible, AssetDatabase.GenerateUniqueAssetPath(clipsFolder + "/" + visible.name + ".anim"));
            AssetDatabase.CreateAsset(hidden, AssetDatabase.GenerateUniqueAssetPath(clipsFolder + "/" + hidden.name + ".anim"));

            var sm = new AnimatorStateMachine { name = li.label, hideFlags = HideFlags.HideInHierarchy };
            AssetDatabase.AddObjectToAsset(sm, controller);

            var stVis = sm.AddState("Visible");
            stVis.motion = visible;
            var stHid = sm.AddState("Hidden");
            stHid.motion = hidden;

            // 初期状態：(自分ON) かつ 条件が全部"非covered" のときだけ表示
            bool coveredDefault = li.covers.Any(c => c.defaultCovered);
            bool visibleDefault = (!li.hasOwnToggle || li.defaultOn) && !coveredDefault;
            sm.defaultState = visibleDefault ? stVis : stHid;

            // Hidden→Visible：(自分ON) かつ 条件が全部"非covered"（1遷移にAND）
            var toVis = stHid.AddTransition(stVis);
            SetupTransition(toVis, seconds);
            if (li.hasOwnToggle) AddOn(toVis, li.param, li.isInt, li.onValue);
            foreach (var c in li.covers) AddCoverOff(toVis, c);
            // 条件が1つも無い退化ケース（自動表示で参照が解決できない等）は無条件遷移になるため、ExitTimeで有効化
            if (toVis.conditions == null || toVis.conditions.Length == 0)
            { toVis.hasExitTime = true; toVis.exitTime = 0f; }

            // Visible→Hidden：自分OFF、または いずれかが covered（それぞれ別遷移＝OR）
            if (li.hasOwnToggle)
            {
                var tOff = stVis.AddTransition(stHid);
                SetupTransition(tOff, seconds);
                AddOff(tOff, li.param, li.isInt, li.onValue);
            }
            foreach (var c in li.covers)
            {
                var t = stVis.AddTransition(stHid);
                SetupTransition(t, seconds);
                AddCoverOn(t, c);
            }

            controller.AddLayer(new AnimatorControllerLayer
            {
                name = UniqueLayerName(controller, li.label),
                defaultWeight = 1f,
                stateMachine = sm,
            });
        }

        /// <summary>隠す条件(hideWhen)を、条件の最小単位(CoverCond)のリストへ展開する。</summary>
        private static List<CoverCond> ResolveCovers(List<ItemRef> hideWhen,
            Dictionary<string, ParamInfo> paramLookup, Dictionary<string, GroupInfo> groupLookup)
        {
            var result = new List<CoverCond>();
            if (hideWhen == null) return result;
            foreach (var refItem in hideWhen)
            {
                if (refItem == null) continue;
                if (refItem.category)
                {
                    if (!groupLookup.TryGetValue(refItem.groupName ?? "", out var gi)) continue;
                    if (gi.exclusive && !string.IsNullOrEmpty(gi.groupParam))
                    {
                        // 排他カテゴリ：param != 0（どれか選択中）で covered
                        result.Add(new CoverCond { param = gi.groupParam, isInt = true, nonZero = true, defaultCovered = gi.anyDefaultOn });
                    }
                    else
                    {
                        // 非排他カテゴリ：各アイテムのORで covered
                        foreach (var pi in gi.items)
                            result.Add(new CoverCond { param = pi.param, isInt = pi.isInt, onValue = pi.onValue, defaultCovered = pi.defaultOn });
                    }
                }
                else if (paramLookup.TryGetValue(Key(refItem.groupName, refItem.itemName), out var pc))
                {
                    result.Add(new CoverCond { param = pc.param, isInt = pc.isInt, onValue = pc.onValue, defaultCovered = pc.defaultOn });
                }
            }
            return result;
        }

        // covered（その条件がONの状態）になる遷移条件
        private static void AddCoverOn(AnimatorStateTransition t, CoverCond c)
        {
            if (c.nonZero) t.AddCondition(AnimatorConditionMode.NotEqual, 0, c.param);
            else if (c.isInt) t.AddCondition(AnimatorConditionMode.Equals, c.onValue, c.param);
            else t.AddCondition(AnimatorConditionMode.If, 0, c.param);
        }

        // 非covered（その条件がOFFの状態）の遷移条件
        private static void AddCoverOff(AnimatorStateTransition t, CoverCond c)
        {
            if (c.nonZero) t.AddCondition(AnimatorConditionMode.Equals, 0, c.param);
            else if (c.isInt) t.AddCondition(AnimatorConditionMode.NotEqual, c.onValue, c.param);
            else t.AddCondition(AnimatorConditionMode.IfNot, 0, c.param);
        }

        private static AnimationClip MakeActiveClip(LogicItem li, Transform avatarRoot, bool active)
        {
            var clip = new AnimationClip();
            foreach (var go in li.objects)
            {
                if (go == null) continue;
                string path = AnimationUtility.CalculateTransformPath(go.transform, avatarRoot);
                SetConst(clip, path, typeof(GameObject), "m_IsActive", active ? 1f : 0f);
            }
            return clip;
        }

        private static void SetConst(AnimationClip clip, string path, System.Type type, string prop, float value)
        {
            var binding = EditorCurveBinding.FloatCurve(path, type, prop);
            AnimationUtility.SetEditorCurve(clip, binding, AnimationCurve.Constant(0f, 1f / 60f, value));
        }

        private static void AddOn(AnimatorStateTransition t, string param, bool isInt, int onValue)
        {
            t.AddCondition(isInt ? AnimatorConditionMode.Equals : AnimatorConditionMode.If, isInt ? onValue : 0, param);
        }

        private static void AddOff(AnimatorStateTransition t, string param, bool isInt, int onValue)
        {
            t.AddCondition(isInt ? AnimatorConditionMode.NotEqual : AnimatorConditionMode.IfNot, isInt ? onValue : 0, param);
        }

        private static void SetupTransition(AnimatorStateTransition t, float seconds)
        {
            t.hasExitTime = false;
            t.exitTime = 0f;
            t.hasFixedDuration = true;
            t.duration = seconds;
            t.canTransitionToSelf = false;
        }

        // =============================================================
        //  コーディネイト（Parameter Driver レイヤー + メニュー）
        // =============================================================

        private static void AddPresetLayer(AnimatorController controller, CostumeToggleConfig config, List<CostumePreset> presets)
        {
            EnsureParam(controller, PresetParam, AnimatorControllerParameterType.Int);

            var sm = new AnimatorStateMachine { name = "Presets", hideFlags = HideFlags.HideInHierarchy };
            AssetDatabase.AddObjectToAsset(sm, controller);

            var idle = sm.AddState("Idle");
            sm.defaultState = idle;

            for (int pi = 0; pi < presets.Count; pi++)
            {
                var preset = presets[pi];
                int value = pi + 1;

                var st = sm.AddState(string.IsNullOrEmpty(preset.name) ? $"Coord{value}" : preset.name);
                var driver = st.AddStateMachineBehaviour<VRCAvatarParameterDriver>();
                driver.localOnly = false;
                driver.parameters = BuildDriverParameters(config, preset);
                driver.parameters.Add(SetParam(PresetParam, 0)); // セレクタを戻す

                var toState = sm.AddAnyStateTransition(st);
                toState.duration = 0;
                toState.hasExitTime = false;
                toState.hasFixedDuration = true;
                toState.canTransitionToSelf = false;
                toState.AddCondition(AnimatorConditionMode.Equals, value, PresetParam);

                var back = st.AddTransition(idle);
                back.hasExitTime = true;
                back.exitTime = 0f;
                back.duration = 0f;
                back.hasFixedDuration = true;
            }

            controller.AddLayer(new AnimatorControllerLayer
            {
                name = UniqueLayerName(controller, "Presets"),
                defaultWeight = 1f,
                stateMachine = sm,
            });
        }

        private static void BuildPresetMenu(GameObject parentGo, List<CostumePreset> presets, string folderName)
        {
            string coordName = string.IsNullOrEmpty(folderName) ? "コーディネイト" : folderName;
            var pGo = new GameObject(coordName);
            pGo.transform.SetParent(parentGo.transform, false);
            var pItem = pGo.AddComponent<ModularAvatarMenuItem>();
            pItem.Control = new VRCExpressionsMenu.Control { type = VRCExpressionsMenu.Control.ControlType.SubMenu };
            pItem.MenuSource = SubmenuSource.Children;
            pItem.label = coordName;

            for (int pi = 0; pi < presets.Count; pi++)
            {
                var preset = presets[pi];
                var go = new GameObject(string.IsNullOrEmpty(preset.name) ? $"コーディネイト{pi + 1}" : preset.name);
                go.transform.SetParent(pGo.transform, false);
                var mi = go.AddComponent<ModularAvatarMenuItem>();
                mi.Control = new VRCExpressionsMenu.Control
                {
                    type = VRCExpressionsMenu.Control.ControlType.Button,
                    parameter = new VRCExpressionsMenu.Control.Parameter { name = PresetParam },
                    value = pi + 1,
                    icon = preset.icon,
                };
                mi.automaticValue = false;
                mi.isSaved = false;
                mi.isSynced = true;
                mi.isDefault = false;
                mi.label = go.name;
            }
        }

        private static List<VRC_AvatarParameterDriver.Parameter> BuildDriverParameters(CostumeToggleConfig config, CostumePreset preset)
        {
            var result = new List<VRC_AvatarParameterDriver.Parameter>();
            int gi = 0;
            foreach (var group in config.groups)
            {
                if (group == null) continue;
                gi++;
                string groupParam = GroupParam(group, gi);

                if (group.exclusive)
                {
                    // 選択中アイテム優先。無ければ「全コーデでON」指定のアイテムを採用
                    int val = 0, ii = 0, forced = 0;
                    foreach (var item in group.items)
                    {
                        if (item == null) continue;
                        ii++;
                        if (IsOn(preset, group.name, item.name)) { val = ItemOnValue(group, ii); break; }
                        if (forced == 0 && item.onInAllPresets) forced = ItemOnValue(group, ii);
                    }
                    if (val == 0) val = forced;
                    result.Add(SetParam(groupParam, val));
                }
                else
                {
                    int ii = 0;
                    foreach (var item in group.items)
                    {
                        if (item == null) continue;
                        ii++;
                        string param = ItemParam(group, groupParam, item, ii);
                        bool on = item.onInAllPresets || IsOn(preset, group.name, item.name);
                        result.Add(SetParam(param, on ? 1 : 0));
                    }
                }
            }
            return result;
        }

        private static VRC_AvatarParameterDriver.Parameter SetParam(string name, float value)
            => new VRC_AvatarParameterDriver.Parameter
            {
                name = name,
                type = VRC_AvatarParameterDriver.ChangeType.Set,
                value = value,
            };

        // =============================================================
        //  アンインストール
        // =============================================================

        public static void RemoveGenerated(CostumeToggleConfig config)
        {
            if (config == null) return;

            GameObject target = config.generatedRoot;
            if (target == null)
            {
                var descriptor = config.GetComponentInParent<VRCAvatarDescriptor>();
                if (descriptor != null)
                {
                    // Manager 配下など階層が深くても名前で探す
                    var hit = descriptor.GetComponentsInChildren<Transform>(true)
                        .FirstOrDefault(tr => tr.name == GeneratedRootName);
                    if (hit != null) target = hit.gameObject;
                }
            }

            if (target != null)
                Undo.DestroyObjectImmediate(target);

            if (config.generatedRoot != null)
            {
                Undo.RecordObject(config, "衣装トグル生成物の参照をクリア");
                config.generatedRoot = null;
                EditorUtility.SetDirty(config);
            }
        }

        // =============================================================
        //  共通
        // =============================================================

        private static void EnsureParam(AnimatorController c, string name, AnimatorControllerParameterType t)
        {
            if (c.parameters.Any(p => p.name == name)) return;
            c.AddParameter(name, t);
        }

        private static string UniqueLayerName(AnimatorController c, string baseName)
        {
            string name = baseName;
            int n = 1;
            while (c.layers.Any(l => l.name == name))
                name = baseName + "_" + (++n);
            return name;
        }

        private static string GroupParam(CostumeGroup g, int groupIndex1) => ParamPrefix + Sanitize(g.name) + "_" + groupIndex1;

        private static string ItemParam(CostumeGroup g, string groupParam, CostumeItem it, int itemIndex1)
            => g.exclusive ? groupParam : groupParam + "_" + Sanitize(it.name) + "_" + itemIndex1;

        private static int ItemOnValue(CostumeGroup g, int itemIndex1) => g.exclusive ? itemIndex1 : 1;

        private static bool IsOn(CostumePreset preset, string groupName, string itemName)
            => preset.entries != null && preset.entries.Any(e => e.on && e.groupName == groupName && e.itemName == itemName);

        private static string EnsureFolder(string parent, string child)
        {
            if (!AssetDatabase.IsValidFolder(parent))
            {
                string[] parts = parent.Split('/');
                string cur = parts[0];
                for (int i = 1; i < parts.Length; i++)
                {
                    string next = cur + "/" + parts[i];
                    if (!AssetDatabase.IsValidFolder(next))
                        AssetDatabase.CreateFolder(cur, parts[i]);
                    cur = next;
                }
            }
            string full = parent + "/" + child;
            if (!AssetDatabase.IsValidFolder(full))
                AssetDatabase.CreateFolder(parent, child);
            return full;
        }

        private static string Sanitize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "x";
            var sb = new System.Text.StringBuilder();
            foreach (char c in s)
                sb.Append(char.IsLetterOrDigit(c) ? c : '_');
            return sb.ToString();
        }
    }
}
