// v0.7.0
// 衣装トグル・ウィザードの設定データ（アバターに付く設定コンポーネント）。
// このコンポーネント自体はビルドに含めない想定（置かれるオブジェクトは EditorOnly タグ）。
// ここには「どんなカテゴリ・アイテム・コーディネイトがあるか」だけを保持し、
// 実際の Modular Avatar 部品は Editor 側のジェネレーターが生成する（非破壊）。

using System;
using System.Collections.Generic;
using UnityEngine;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace Metlab.Toggle
{
    /// <summary>1つのトグル項目（1ボタン）。対象オブジェクトをまとめてON/OFFする。</summary>
    [Serializable]
    public class CostumeItem
    {
        public string name = "アイテム";
        [Tooltip("メニューに表示するアイコン（任意）")]
        public Texture2D icon;
        public List<GameObject> objects = new List<GameObject>();
        [Tooltip("初期状態でONにするか")]
        public bool defaultOn = false;

        [Tooltip("ここに指定したアイテム/カテゴリがONのとき、このアイテムを自動で隠す（複数指定=ORで、いずれかONなら隠す）")]
        public List<ItemRef> hideWhen = new List<ItemRef>();

        [Tooltip("自動で表示（手動トグルを作らない下地レイヤー向け）。隠す条件があれば条件OFF時のみ、条件が無ければ常時表示")]
        public bool autoShowWhenUncovered = false;

        [Tooltip("すべてのコーディネイトで自動的にONにする（各コーデで毎回チェックしなくて済む）")]
        public bool onInAllPresets = false;

        [Tooltip("着用時に素体などのブレンドシェイプを変更/削除（クリッピング対策）。自動表示アイテムでは無効")]
        public List<BodyShape> bodyShapes = new List<BodyShape>();
    }

    /// <summary>着用時に変更する素体ブレンドシェイプ1件（MA ShapeChanger に生成）。</summary>
    [Serializable]
    public class BodyShape
    {
        [Tooltip("対象の SkinnedMeshRenderer を持つ GameObject（素体など）")]
        public GameObject renderer;
        [Tooltip("ブレンドシェイプ名")]
        public string shapeName = "";
        [Tooltip("設定する値(0-100)。削除ONのときは無視")]
        [Range(0f, 100f)] public float value = 100f;
        [Tooltip("ONにするとメッシュを削除（ブレンドシェイプで動く範囲を消す）。OFFは値をセット")]
        public bool delete = false;
    }

    /// <summary>別アイテム（またはカテゴリ）を指し示す参照。</summary>
    [Serializable]
    public class ItemRef
    {
        public string groupName;
        public string itemName;
        [Tooltip("ONならカテゴリ全体を参照（そのカテゴリのどれかがONで条件成立）。itemName は無視")]
        public bool category = false;
    }

    /// <summary>トグル項目のまとまり（サブメニュー1つ分）。</summary>
    [Serializable]
    public class CostumeGroup
    {
        public string name = "カテゴリ";
        [Tooltip("メニューに表示するアイコン（任意）")]
        public Texture2D icon;
        [Tooltip("所属フォルダ名（任意）。同じ名前のカテゴリが着替え内の1つの中間フォルダにまとまる。空なら着替え直下に置く")]
        public string section = "";
        [Tooltip("ONにすると、このカテゴリ内では同時に1つしか選べない（排他／ラジオ）")]
        public bool exclusive = false;
        public List<CostumeItem> items = new List<CostumeItem>();
    }

    /// <summary>コーディネイトが各アイテムをどの状態にするかの1件。※次フェーズで生成対応</summary>
    [Serializable]
    public class PresetEntry
    {
        public string groupName;
        public string itemName;
        public bool on;
    }

    /// <summary>複数アイテムの組合せを1ボタンで呼び出すコーディネイト。※次フェーズで生成対応</summary>
    [Serializable]
    public class CostumePreset
    {
        public string name = "コーディネイト";
        [Tooltip("メニューに表示するアイコン（任意）")]
        public Texture2D icon;
        public List<PresetEntry> entries = new List<PresetEntry>();
    }

    /// <summary>中間フォルダ（section）のアイコン指定。</summary>
    [Serializable]
    public class SectionIcon
    {
        public string sectionName;
        public Texture2D icon;
    }

    [AddComponentMenu("")] // 手動追加はさせず、ツールからのみ付与する
    [DisallowMultipleComponent]
    public class CostumeToggleConfig : MonoBehaviour
    {
        public List<CostumeGroup> groups = new List<CostumeGroup>();
        public List<CostumePreset> presets = new List<CostumePreset>();
        [Tooltip("中間フォルダ（身体/服など）のアイコン指定")]
        public List<SectionIcon> sectionIcons = new List<SectionIcon>();

        // --- オプション（メニュー構成） ---
        [Tooltip("親フォルダ名（単一フォルダにまとめる場合、この中にカテゴリとコーディネイトが入る）")]
        public string menuName = "衣装";

        [Tooltip("ONの場合、カテゴリとコーディネイトを1つの親フォルダ配下にまとめる")]
        public bool wrapInFolder = true;

        [Tooltip("コーディネイトのフォルダ名")]
        public string coordinateMenuName = "コーディネイト";

        [Tooltip("エクスプレッションメニューの起点。未指定ならアバターのメインメニューへ追加。指定するとそのメニューへ追加")]
        public VRCExpressionsMenu installTargetMenu;

        [Tooltip("直近に生成した MA 部品のルート（再生成時に作り直す）")]
        public GameObject generatedRoot;
    }
}
