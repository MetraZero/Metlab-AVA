// v0.1.0
// 衣装トグル設定の保存アセット（別プロジェクト・別アバターへ流用するためのテンプレート）。
// 構成（カテゴリ/アイテム/コーディネイト/フォルダ/アイコン/オプション）を保持する。
// ※シーン上の GameObject 参照（対象オブジェクトや素体）はアセットには保存できないため、
//   読込後に各アイテムで割り当て直す必要がある。アイコン等のアセット参照は保持される。

using System.Collections.Generic;
using UnityEngine;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace Metlab.Toggle
{
    [CreateAssetMenu(menuName = "Metlab/トグルをつくるやつ テンプレート", fileName = "CostumeToggleTemplate")]
    public class CostumeTogglePreset : ScriptableObject
    {
        // CostumeToggleConfig と同名フィールド（JsonUtility で相互コピーするため名前を一致させる）
        public List<CostumeGroup> groups = new List<CostumeGroup>();
        public List<CostumePreset> presets = new List<CostumePreset>();
        public List<SectionIcon> sectionIcons = new List<SectionIcon>();
        public string menuName = "衣装";
        public bool wrapInFolder = true;
        public string coordinateMenuName = "コーディネイト";
        public VRCExpressionsMenu installTargetMenu;
    }
}
