// v1.0.0
// 髪ふさカラー編集のバックアップ情報。
// 編集開始時に複製フォルダへ保存し、元のメッシュ・マテリアルへ戻せるようにする。
// （元のアセットファイルは変更しないため、これは「元へ戻すための参照」を保持するだけ）

using UnityEngine;

namespace Metlab.HairColor
{
    public class HairColorBackup : ScriptableObject
    {
        [Tooltip("編集前の元メッシュ（複製元）")]
        public Mesh OriginalMesh;

        [Tooltip("編集前の元マテリアル一式")]
        public Material[] OriginalMaterials;

        [Tooltip("対象だったオブジェクトのヒエラルキー上の名前（参照用）")]
        public string RendererName;

        [Tooltip("作成日時")]
        public string CreatedAt;
    }
}
