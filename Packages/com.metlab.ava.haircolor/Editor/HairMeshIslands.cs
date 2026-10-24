// v1.0.0
// 髪メッシュを「連結した塊（ふさ＝アイランド）」に分割し、
// シーンビューのクリック位置からどのふさがヒットしたかを判定するためのコア。
// ・頂点位置を量子化して溶接（weld）し、分離した重複頂点も同じ塊として扱う
// ・Union-Find で三角形を連結成分に分類
// ・Möller–Trumbore でレイと三角形の交差判定（クリックピッキング）

using System.Collections.Generic;
using UnityEngine;

namespace Metlab.HairColor
{
    /// <summary>
    /// メッシュの連結成分（ふさ）情報と、ワールド空間でのクリック判定を担う。
    /// 位相（頂点・三角形の並び）が変わらない限り、アイランドIDは不変。
    /// </summary>
    public class HairMeshIslands
    {
        // --- 三角形テーブル（全サブメッシュを通し番号で保持） ---
        public int TriangleCount { get; private set; }
        public int[] TriV0;            // 三角形の頂点インデックス（元メッシュ基準）
        public int[] TriV1;
        public int[] TriV2;
        public int[] TriOrigSubmesh;   // その三角形が元々属していたサブメッシュ番号
        public int[] TriIslandId;      // その三角形が属するふさ（アイランド）ID

        public int IslandCount { get; private set; }

        /// <summary>島ID -> その島に含まれる三角形番号リスト</summary>
        public Dictionary<int, List<int>> IslandToTriangles { get; private set; }

        /// <summary>
        /// メッシュから連結成分を計算する。
        /// </summary>
        /// <param name="mesh">対象メッシュ（複製後の作業用メッシュを渡す）</param>
        /// <param name="weldThreshold">頂点溶接の距離しきい値（これ以下の距離は同一頂点扱い）</param>
        public static HairMeshIslands Build(Mesh mesh, float weldThreshold = 0.0001f)
        {
            var result = new HairMeshIslands();

            Vector3[] vertices = mesh.vertices;
            int vertexCount = vertices.Length;

            // --- 1. 頂点溶接：量子化した位置が同じ頂点を代表頂点にまとめる ---
            int[] weld = new int[vertexCount];
            var posToRep = new Dictionary<long, int>(vertexCount);
            float inv = 1f / Mathf.Max(weldThreshold, 1e-6f);
            for (int i = 0; i < vertexCount; i++)
            {
                long key = QuantizeKey(vertices[i], inv);
                if (posToRep.TryGetValue(key, out int rep))
                {
                    weld[i] = rep;
                }
                else
                {
                    posToRep[key] = i;
                    weld[i] = i;
                }
            }

            // --- 2. Union-Find 準備 ---
            int[] parent = new int[vertexCount];
            for (int i = 0; i < vertexCount; i++) parent[i] = i;

            // --- 3. 三角形を収集しつつ、溶接代表頂点どうしを連結 ---
            var triV0 = new List<int>();
            var triV1 = new List<int>();
            var triV2 = new List<int>();
            var triSub = new List<int>();

            int subMeshCount = mesh.subMeshCount;
            for (int s = 0; s < subMeshCount; s++)
            {
                int[] tris = mesh.GetTriangles(s);
                for (int t = 0; t < tris.Length; t += 3)
                {
                    int a = tris[t];
                    int b = tris[t + 1];
                    int c = tris[t + 2];

                    triV0.Add(a);
                    triV1.Add(b);
                    triV2.Add(c);
                    triSub.Add(s);

                    int wa = weld[a], wb = weld[b], wc = weld[c];
                    Union(parent, wa, wb);
                    Union(parent, wb, wc);
                }
            }

            result.TriangleCount = triV0.Count;
            result.TriV0 = triV0.ToArray();
            result.TriV1 = triV1.ToArray();
            result.TriV2 = triV2.ToArray();
            result.TriOrigSubmesh = triSub.ToArray();

            // --- 4. 各三角形にアイランドIDを割り当て（連結成分の根を詰めてID化） ---
            result.TriIslandId = new int[result.TriangleCount];
            result.IslandToTriangles = new Dictionary<int, List<int>>();
            var rootToIsland = new Dictionary<int, int>();
            int nextIsland = 0;

            for (int t = 0; t < result.TriangleCount; t++)
            {
                int root = Find(parent, weld[result.TriV0[t]]);
                if (!rootToIsland.TryGetValue(root, out int islandId))
                {
                    islandId = nextIsland++;
                    rootToIsland[root] = islandId;
                    result.IslandToTriangles[islandId] = new List<int>();
                }
                result.TriIslandId[t] = islandId;
                result.IslandToTriangles[islandId].Add(t);
            }

            result.IslandCount = nextIsland;
            return result;
        }

        /// <summary>
        /// ワールド空間の頂点配列に対してレイを飛ばし、最も近くでヒットした三角形番号を返す。
        /// ヒットしなければ -1。
        /// </summary>
        /// <param name="worldVertices">元メッシュと同じ頂点順のワールド座標配列</param>
        public int Raycast(Vector3[] worldVertices, Ray ray)
        {
            int hitTri = -1;
            float nearest = float.MaxValue;

            for (int t = 0; t < TriangleCount; t++)
            {
                Vector3 p0 = worldVertices[TriV0[t]];
                Vector3 p1 = worldVertices[TriV1[t]];
                Vector3 p2 = worldVertices[TriV2[t]];

                if (RayTriangle(ray, p0, p1, p2, out float dist))
                {
                    if (dist < nearest)
                    {
                        nearest = dist;
                        hitTri = t;
                    }
                }
            }
            return hitTri;
        }

        // =============================================================
        //  内部ユーティリティ
        // =============================================================

        private static long QuantizeKey(Vector3 v, float inv)
        {
            // 位置を整数格子にスナップして衝突の少ないキーに合成する
            long x = (long)Mathf.Round(v.x * inv);
            long y = (long)Mathf.Round(v.y * inv);
            long z = (long)Mathf.Round(v.z * inv);
            unchecked
            {
                long key = x * 73856093L;
                key = (key * 19349663L) ^ y;
                key = (key * 83492791L) ^ z;
                return key;
            }
        }

        private static int Find(int[] parent, int x)
        {
            while (parent[x] != x)
            {
                parent[x] = parent[parent[x]]; // 経路圧縮
                x = parent[x];
            }
            return x;
        }

        private static void Union(int[] parent, int a, int b)
        {
            int ra = Find(parent, a);
            int rb = Find(parent, b);
            if (ra != rb) parent[ra] = rb;
        }

        /// <summary>Möller–Trumbore 法によるレイ対三角形の交差判定（両面）。</summary>
        private static bool RayTriangle(Ray ray, Vector3 v0, Vector3 v1, Vector3 v2, out float distance)
        {
            const float EPS = 1e-8f;
            distance = 0f;

            Vector3 edge1 = v1 - v0;
            Vector3 edge2 = v2 - v0;
            Vector3 h = Vector3.Cross(ray.direction, edge2);
            float a = Vector3.Dot(edge1, h);
            if (a > -EPS && a < EPS) return false; // レイと三角形が平行

            float f = 1f / a;
            Vector3 s = ray.origin - v0;
            float u = f * Vector3.Dot(s, h);
            if (u < 0f || u > 1f) return false;

            Vector3 q = Vector3.Cross(s, edge1);
            float vv = f * Vector3.Dot(ray.direction, q);
            if (vv < 0f || u + vv > 1f) return false;

            float tHit = f * Vector3.Dot(edge2, q);
            if (tHit <= EPS) return false; // レイの後方

            distance = tHit;
            return true;
        }
    }
}
