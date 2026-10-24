// v1.2.0
// 房を選んで髪色をかえるやつのデータ管理・変更処理のコア。
// ・編集中はメッシュ／マテリアルをメモリ上の複製として扱い、ファイル（アセット）は生成しない＝プレビュー
// ・「確定（Commit）」したときに初めて、複製フォルダへメッシュ・色変えマテリアル・バックアップを保存する
// ・選んだふさ（アイランド）を「元のサブメッシュ×色×方式」単位の新サブメッシュへ振り分け、色変え複製マテリアルを割り当てる
// ・同じ色のふさは1つにまとめ、マテリアル数の増加を最小限にする
// ・いつでも初期状態／元データへ戻せる。元メッシュ/元マテリアルのファイルは一切変更しない
// v1.2.0 変更点:
//   ・アセット生成を Commit 時のみに変更（編集中はメモリ上プレビュー）
//   ・スキンメッシュのワールド座標をボーン＋バインドポーズから手動計算（クリック位置ズレを解消）

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Metlab.HairColor
{
    /// <summary>色の適用方式。</summary>
    public enum ColorBlendMode
    {
        Normal,    // 通常（元の色を選んだ色で置き換え）
        Multiply,  // 乗算（元の色に選んだ色を掛け合わせる）
    }

    /// <summary>
    /// 1回の編集セッション（対象レンダラー＋複製データ＋ふさ情報＋色割り当て）を保持する。
    /// </summary>
    public class HairColorSession
    {
        public Renderer Renderer { get; private set; }
        public SkinnedMeshRenderer Smr { get; private set; }
        public MeshFilter MeshFilter { get; private set; }

        public Mesh OriginalMesh { get; private set; }
        public Material[] OriginalMaterials { get; private set; }

        public Mesh WorkingMesh { get; private set; }   // メモリ上の作業用複製（確定時にアセット化）
        public HairMeshIslands Islands { get; private set; }

        /// <summary>島ID -> 適用色（登録のない島は未着色）</summary>
        public Dictionary<int, Color> IslandColors { get; } = new Dictionary<int, Color>();

        /// <summary>色の適用方式（全ふさ共通）。変更後は Apply で反映される。</summary>
        public ColorBlendMode BlendMode { get; set; } = ColorBlendMode.Normal;

        public bool Committed { get; private set; }
        public string OutputFolder { get; private set; }  // 確定後に設定される
        public HairColorBackup Backup { get; private set; }

        private readonly string _rootFolder;

        // 生成済みの色変えマテリアル（メモリ上）。キー: "元サブメッシュ_方式_色HEX"
        private readonly Dictionary<string, Material> _generatedMaterials = new Dictionary<string, Material>();

        // ワールド座標頂点キャッシュ（クリック判定とハイライト描画に使用）
        private Vector3[] _worldVertices;

        public bool IsActive => WorkingMesh != null && Islands != null;
        public Vector3[] WorldVertices => _worldVertices;

        private HairColorSession(string rootFolder) { _rootFolder = rootFolder; }

        // =============================================================
        //  編集開始（アセットは作らずメモリ上でプレビュー）
        // =============================================================

        public static HairColorSession Begin(Renderer renderer, string rootFolder)
        {
            if (renderer == null) return null;

            Mesh srcMesh = GetSharedMesh(renderer);
            if (srcMesh == null)
            {
                Debug.LogError("[房を選んで髪色をかえるやつ] メッシュが見つかりません。対象にメッシュが設定されているか確認してください。");
                return null;
            }

            var s = new HairColorSession(rootFolder)
            {
                Renderer = renderer,
                Smr = renderer as SkinnedMeshRenderer,
                MeshFilter = renderer.GetComponent<MeshFilter>(),
                OriginalMesh = srcMesh,
                OriginalMaterials = (Material[])renderer.sharedMaterials.Clone(),
            };

            // --- メッシュをメモリ上に複製（ファイルは作らない）してレンダラーに差し替え ---
            Mesh working = Object.Instantiate(srcMesh);
            working.name = srcMesh.name + "_HairEdit";
            s.WorkingMesh = working;

            Undo.RecordObject(renderer, "髪ふさカラー編集開始");
            SetSharedMesh(renderer, working);

            // --- ふさ（連結成分）を検出 ---
            s.Islands = HairMeshIslands.Build(working);
            s.RecomputeWorldVertices();

            Debug.Log($"[房を選んで髪色をかえるやつ] 編集開始（プレビュー）。ふさ数: {s.Islands.IslandCount}");
            return s;
        }

        // =============================================================
        //  ワールド座標頂点（クリック判定・ハイライト用）
        // =============================================================

        /// <summary>現在のポーズ・姿勢でのワールド座標頂点を再計算してキャッシュする。</summary>
        public void RecomputeWorldVertices()
        {
            if (WorkingMesh == null) { _worldVertices = null; return; }

            if (Smr != null)
                _worldVertices = ComputeSkinnedWorldVertices(Smr, WorkingMesh);
            else
                _worldVertices = ComputeStaticWorldVertices(Renderer.transform, WorkingMesh);
        }

        private static Vector3[] ComputeStaticWorldVertices(Transform t, Mesh mesh)
        {
            Vector3[] local = mesh.vertices;
            Matrix4x4 m = t.localToWorldMatrix;
            var world = new Vector3[local.Length];
            for (int i = 0; i < local.Length; i++)
                world[i] = m.MultiplyPoint3x4(local[i]);
            return world;
        }

        /// <summary>
        /// スキンメッシュのワールド座標を、ボーンの現在姿勢とバインドポーズから手動スキニングで直接計算する。
        /// SkinnedMeshRenderer の Transform に依存しないため、アバターのスケールや階層構成に関わらず正確。
        /// </summary>
        private static Vector3[] ComputeSkinnedWorldVertices(SkinnedMeshRenderer smr, Mesh mesh)
        {
            Transform[] bones = smr.bones;
            Matrix4x4[] bindposes = mesh.bindposes;
            Vector3[] verts = mesh.vertices;
            BoneWeight[] weights = mesh.boneWeights;

            // ボーン情報が無い／不整合ならレンダラー変換でフォールバック
            if (bones == null || bones.Length == 0 ||
                bindposes == null || bindposes.Length == 0 ||
                weights == null || weights.Length != verts.Length)
            {
                return ComputeStaticWorldVertices(smr.transform, mesh);
            }

            int boneCount = Mathf.Min(bones.Length, bindposes.Length);
            var skin = new Matrix4x4[boneCount];
            Matrix4x4 fallback = smr.transform.localToWorldMatrix;
            for (int b = 0; b < boneCount; b++)
            {
                Matrix4x4 boneMtx = bones[b] != null ? bones[b].localToWorldMatrix : fallback;
                skin[b] = boneMtx * bindposes[b];
            }

            var world = new Vector3[verts.Length];
            for (int i = 0; i < verts.Length; i++)
            {
                BoneWeight bw = weights[i];
                Matrix4x4 m = new Matrix4x4(); // ゼロ行列
                float total = 0f;
                total += AddBone(ref m, skin, boneCount, bw.boneIndex0, bw.weight0);
                total += AddBone(ref m, skin, boneCount, bw.boneIndex1, bw.weight1);
                total += AddBone(ref m, skin, boneCount, bw.boneIndex2, bw.weight2);
                total += AddBone(ref m, skin, boneCount, bw.boneIndex3, bw.weight3);

                if (total <= 1e-6f)
                    world[i] = fallback.MultiplyPoint3x4(verts[i]); // ウェイト無しの頂点
                else
                    world[i] = m.MultiplyPoint3x4(verts[i]);
            }
            return world;
        }

        private static float AddBone(ref Matrix4x4 m, Matrix4x4[] skin, int boneCount, int index, float weight)
        {
            if (weight <= 0f || index < 0 || index >= boneCount) return 0f;
            Matrix4x4 a = skin[index];
            for (int c = 0; c < 4; c++)
                m.SetColumn(c, m.GetColumn(c) + a.GetColumn(c) * weight);
            return weight;
        }

        // =============================================================
        //  色の割り当て
        // =============================================================

        public void SetIslandColor(int islandId, Color color) => IslandColors[islandId] = color;
        public void ClearIslandColor(int islandId) => IslandColors.Remove(islandId);
        public bool TryGetIslandColor(int islandId, out Color color) => IslandColors.TryGetValue(islandId, out color);

        /// <summary>全ての色割り当てを消して初期状態に戻す。</summary>
        public void ResetAll()
        {
            IslandColors.Clear();
            Apply();
        }

        /// <summary>
        /// 現在の色割り当てを作業用メッシュ／マテリアルへ反映する（メモリ上プレビュー、アセットは生成しない）。
        /// </summary>
        public void Apply()
        {
            if (!IsActive) return;

            var islands = Islands;
            var untouched = new Dictionary<int, List<int>>();      // 元サブメッシュ -> 三角形
            var colored = new Dictionary<string, ColoredGroup>();  // キー -> グループ

            for (int t = 0; t < islands.TriangleCount; t++)
            {
                int island = islands.TriIslandId[t];
                int origSub = islands.TriOrigSubmesh[t];

                if (IslandColors.TryGetValue(island, out Color col))
                {
                    string key = origSub + "_" + (int)BlendMode + "_" + ColorUtility.ToHtmlStringRGBA(col);
                    if (!colored.TryGetValue(key, out var g))
                    {
                        g = new ColoredGroup { OrigSubmesh = origSub, Color = col, Triangles = new List<int>() };
                        colored[key] = g;
                    }
                    g.Triangles.Add(t);
                }
                else
                {
                    if (!untouched.TryGetValue(origSub, out var list))
                    {
                        list = new List<int>();
                        untouched[origSub] = list;
                    }
                    list.Add(t);
                }
            }

            var submeshTris = new List<int[]>();
            var materials = new List<Material>();

            int origSubCount = OriginalMaterials.Length;
            for (int s = 0; s < origSubCount; s++)
            {
                if (untouched.TryGetValue(s, out var list) && list.Count > 0)
                {
                    submeshTris.Add(BuildIndexArray(islands, list));
                    materials.Add(OriginalMaterials[s]);
                }
                foreach (var kv in colored)
                {
                    if (kv.Value.OrigSubmesh != s) continue;
                    submeshTris.Add(BuildIndexArray(islands, kv.Value.Triangles));
                    materials.Add(GetOrCreateColoredMaterial(kv.Key, OriginalMaterials[s], kv.Value.Color));
                }
            }

            WorkingMesh.subMeshCount = submeshTris.Count;
            for (int i = 0; i < submeshTris.Count; i++)
                WorkingMesh.SetTriangles(submeshTris[i], i);

            Undo.RecordObject(Renderer, "髪ふさカラー適用");
            Renderer.sharedMaterials = materials.ToArray();
            EditorUtility.SetDirty(Renderer);
        }

        private static int[] BuildIndexArray(HairMeshIslands islands, List<int> triangleList)
        {
            var arr = new int[triangleList.Count * 3];
            for (int i = 0; i < triangleList.Count; i++)
            {
                int t = triangleList[i];
                arr[i * 3] = islands.TriV0[t];
                arr[i * 3 + 1] = islands.TriV1[t];
                arr[i * 3 + 2] = islands.TriV2[t];
            }
            return arr;
        }

        private Material GetOrCreateColoredMaterial(string key, Material baseMat, Color color)
        {
            Color finalColor = ComputeBlendedColor(baseMat, color, BlendMode);

            if (_generatedMaterials.TryGetValue(key, out var cached) && cached != null)
            {
                SetMainColor(cached, finalColor);
                return cached;
            }

            var dup = new Material(baseMat);
            string suffix = BlendMode == ColorBlendMode.Multiply ? "_Mul" : "";
            dup.name = SanitizeFileName(baseMat != null ? baseMat.name : "Hair") + "_" + ColorUtility.ToHtmlStringRGB(color) + suffix;
            SetMainColor(dup, finalColor);

            _generatedMaterials[key] = dup; // メモリ上のみ。確定時にアセット化する
            return dup;
        }

        private static Color ComputeBlendedColor(Material baseMat, Color chosen, ColorBlendMode mode)
        {
            if (mode == ColorBlendMode.Normal) return chosen;
            Color baseCol = Color.white;
            string p = GetMainColorProperty(baseMat);
            if (p != null) baseCol = baseMat.GetColor(p);
            return baseCol * chosen;
        }

        // =============================================================
        //  確定（アセット化して保存）
        // =============================================================

        /// <summary>
        /// 現在の作業用メッシュ・色変えマテリアル・バックアップを複製フォルダへ保存する。
        /// これで初めてアセットファイルが生成され、VRChat へのアップロードに使える状態になる。
        /// </summary>
        public bool Commit()
        {
            if (!IsActive) return false;

            // --- 出力フォルダ（初回のみ作成） ---
            if (string.IsNullOrEmpty(OutputFolder))
            {
                string safeName = SanitizeFileName(Renderer.gameObject.name);
                string stamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
                OutputFolder = EnsureFolder(_rootFolder, safeName + "_" + stamp);
            }

            // --- バックアップ情報（初回のみ） ---
            if (Backup == null)
            {
                var backup = ScriptableObject.CreateInstance<HairColorBackup>();
                backup.OriginalMesh = OriginalMesh;
                backup.OriginalMaterials = (Material[])OriginalMaterials.Clone();
                backup.RendererName = Renderer.gameObject.name;
                backup.CreatedAt = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string backupPath = AssetDatabase.GenerateUniqueAssetPath(OutputFolder + "/_backup.asset");
                AssetDatabase.CreateAsset(backup, backupPath);
                Backup = backup;
            }

            // --- メッシュをアセット化（未アセットなら） ---
            if (!AssetDatabase.Contains(WorkingMesh))
            {
                string meshPath = AssetDatabase.GenerateUniqueAssetPath(OutputFolder + "/" + WorkingMesh.name + ".asset");
                AssetDatabase.CreateAsset(WorkingMesh, meshPath);
            }
            else
            {
                EditorUtility.SetDirty(WorkingMesh);
            }

            // --- 使用中の色変えマテリアルをアセット化（未アセットなら） ---
            foreach (var kv in _generatedMaterials)
            {
                Material mat = kv.Value;
                if (mat == null) continue;
                if (!AssetDatabase.Contains(mat))
                {
                    string path = AssetDatabase.GenerateUniqueAssetPath(OutputFolder + "/" + mat.name + ".mat");
                    AssetDatabase.CreateAsset(mat, path);
                }
                else
                {
                    EditorUtility.SetDirty(mat);
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Committed = true;
            Debug.Log($"[房を選んで髪色をかえるやつ] 確定して保存しました: {OutputFolder}");
            return true;
        }

        // =============================================================
        //  シェーダー別メインカラー対応
        // =============================================================

        private static readonly string[] MainColorCandidates =
        {
            "_Color", "_MainColor", "_BaseColor", "_TintColor", "_Tint"
        };

        public static void SetMainColor(Material mat, Color color)
        {
            if (mat == null) return;
            string prop = GetMainColorProperty(mat);
            if (prop != null) mat.SetColor(prop, color);
            else Debug.LogWarning($"[房を選んで髪色をかえるやつ] マテリアル '{mat.name}' にメインカラー・プロパティが見つかりませんでした。");
        }

        public static string GetMainColorProperty(Material mat)
        {
            if (mat == null || mat.shader == null) return null;
            Shader sh = mat.shader;
            int count = ShaderUtil.GetPropertyCount(sh);

            var colorProps = new HashSet<string>();
            for (int i = 0; i < count; i++)
            {
                if (ShaderUtil.GetPropertyType(sh, i) == ShaderUtil.ShaderPropertyType.Color)
                    colorProps.Add(ShaderUtil.GetPropertyName(sh, i));
            }

            foreach (var name in MainColorCandidates)
                if (colorProps.Contains(name)) return name;

            return null;
        }

        // =============================================================
        //  元データへ戻す
        // =============================================================

        public void RestoreOriginal()
        {
            if (Renderer == null) return;
            RestoreToRenderer(Renderer, OriginalMesh, OriginalMaterials);
        }

        public static bool RestoreFromBackup(Renderer renderer, HairColorBackup backup)
        {
            if (renderer == null || backup == null) return false;
            RestoreToRenderer(renderer, backup.OriginalMesh, backup.OriginalMaterials);
            return true;
        }

        private static void RestoreToRenderer(Renderer renderer, Mesh mesh, Material[] materials)
        {
            Undo.RecordObject(renderer, "髪ふさカラー 元に戻す");
            if (mesh != null) SetSharedMesh(renderer, mesh);
            if (materials != null) renderer.sharedMaterials = (Material[])materials.Clone();
            EditorUtility.SetDirty(renderer);
        }

        // =============================================================
        //  共通ユーティリティ
        // =============================================================

        public static Mesh GetSharedMesh(Renderer renderer)
        {
            if (renderer is SkinnedMeshRenderer smr) return smr.sharedMesh;
            var mf = renderer.GetComponent<MeshFilter>();
            return mf != null ? mf.sharedMesh : null;
        }

        public static void SetSharedMesh(Renderer renderer, Mesh mesh)
        {
            if (renderer is SkinnedMeshRenderer smr) { smr.sharedMesh = mesh; return; }
            var mf = renderer.GetComponent<MeshFilter>();
            if (mf != null)
            {
                Undo.RecordObject(mf, "髪ふさカラー メッシュ差し替え");
                mf.sharedMesh = mesh;
            }
        }

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

        private static string SanitizeFileName(string name)
        {
            foreach (char c in System.IO.Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name.Replace('/', '_').Replace('\\', '_');
        }

        private class ColoredGroup
        {
            public int OrigSubmesh;
            public Color Color;
            public List<int> Triangles;
        }
    }
}
