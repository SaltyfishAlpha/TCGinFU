using System.Collections.Generic;
using UnityEngine;

namespace TCGinFU.View
{
    /// <summary>
    /// 3D 卡牌用到的网格与材质。
    /// <para>
    /// 整块桌面放在一个缩放为 1/<see cref="BattleLayoutAsset.PixelsPerUnit"/> 的根节点下，
    /// 所以这一层里所有尺寸都直接用**设计像素**，只在根节点换算一次到世界单位。
    /// </para>
    /// </summary>
    public static class CardMeshFactory
    {
        /// <summary>卡牌厚度（设计像素）。一张实体卡约 0.3mm，这里夸张一点才看得见。</summary>
        public const float CardThickness = 3.2f;

        /// <summary>
        /// 桌面局部空间里「朝向镜头」是哪个 Z 方向。
        /// <para>
        /// Unity 的约定：相机在 −Z 朝 +Z 看时，identity 旋转的平面/Canvas 内容才是正的
        /// （默认场景的 Main Camera 就在 (0,1,−10)）。所以桌面根节点的 +Z 是**背离**镜头的，
        /// 卡面、牌堆的抬升方向一律是局部 −Z。
        /// </para>
        /// <para>
        /// 把这个符号写成常量而不是到处手写负号：之前相机放到了 +Z，结果整个画面左右镜像、
        /// 所有面片被背面剔除，屏幕一片黑。所有涉及「前后」的偏移都应当乘上它。
        /// </para>
        /// </summary>
        public const float TowardCamera = -1f;

        private static Mesh _box;
        private static Mesh _quad;
        private static readonly Dictionary<Color, Material> Materials = new();
        private static Shader _shader;

        /// <summary>1×1×1 的立方体，轴心在中心。</summary>
        /// <summary>桌面网格用的 shader 名。</summary>
        public const string ShaderName = "Universal Render Pipeline/Unlit";

        /// <summary>
        /// 取 shader。
        /// <para>
        /// **这里必须大声失败。** 材质是运行时 new 出来的，没有任何资产引用这个 shader，
        /// 所以构建时它会被剥离——除非它在 Project Settings → Graphics →
        /// Always Included Shaders 里。少了它，Shader.Find 在播放器里返回 null，
        /// 一路退到 Sprites/Default（ZWrite Off 的透明 shader，还会乘顶点色），
        /// 结果就是编辑器里好好的，Build 出来区域底板、选中环、高亮全不见了。
        /// 这种只在构建产物里复现的问题最难查，所以宁可报错也不要悄悄降级。
        /// </para>
        /// </summary>
        private static Shader ResolveShader()
        {
            Shader shader = Shader.Find(ShaderName);
            if (shader != null) return shader;

            Debug.LogError(
                $"[桌面渲染] 找不到 shader「{ShaderName}」。"
                + "如果这是构建产物，说明它被剥离了——请把它加到 "
                + "Project Settings → Graphics → Always Included Shaders。"
                + "现在会退到备用 shader，画面大概率不对。");

            return Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default");
        }

        public static Mesh Box => _box ??= BuildBox();

        /// <summary>1×1 的面片，法线朝 +Z。</summary>
        public static Mesh Quad => _quad ??= BuildQuad();

        /// <summary>
        /// 取一个纯色材质。用的是 Unlit——场景里没有灯光，
        /// 卡的立体感靠「卡身比卡面暗」造出来，不靠光照。
        /// </summary>
        public static Material ColorMaterial(Color color)
        {
            if (Materials.TryGetValue(color, out Material cached) && cached != null) return cached;

            _shader ??= ResolveShader();

            var material = new Material(_shader) { name = $"Card_{ColorUtility.ToHtmlStringRGBA(color)}" };

            // URP Unlit 用 _BaseColor，内置 Unlit/Color 用 _Color，两边都设一下
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);

            Materials[color] = material;
            return material;
        }

        /// <summary>造一个带网格的子物体。</summary>
        public static Transform CreateMeshObject(string name, Transform parent, Mesh mesh, Color color)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);

            go.GetComponent<MeshFilter>().sharedMesh = mesh;

            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = ColorMaterial(color);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            return go.transform;
        }

        public static void SetColor(Transform meshObject, Color color)
        {
            if (meshObject == null) return;
            if (meshObject.TryGetComponent(out MeshRenderer renderer))
            {
                renderer.sharedMaterial = ColorMaterial(color);
            }
        }

        // 贴图材质按贴图缓存，和纯色材质一样——每张卡各 new 一个 Material
        // 会在切场景时漏一堆，而且每次 Refresh 都重建
        private static readonly Dictionary<Texture, Material> TexturedMaterials = new();

        /// <summary>
        /// 给网格贴图。<paramref name="texture"/> 为 null 时退回 <paramref name="fallback"/> 纯色——
        /// 美术没到位是常态，缺图应当安静地显示占位色而不是粉色方块。
        /// </summary>
        public static void SetTexture(Transform meshObject, Texture texture, Color fallback)
        {
            if (meshObject == null) return;
            if (!meshObject.TryGetComponent(out MeshRenderer renderer)) return;

            if (texture == null)
            {
                renderer.sharedMaterial = ColorMaterial(fallback);
                return;
            }

            if (!TexturedMaterials.TryGetValue(texture, out Material material) || material == null)
            {
                _shader ??= ResolveShader();
                material = new Material(_shader) { name = "CardArt_" + texture.name };

                if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
                if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", texture);

                // 贴图要原样显示，所以底色必须是白的，否则等于给图蒙了一层色
                if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);
                if (material.HasProperty("_Color")) material.SetColor("_Color", Color.white);

                TexturedMaterials[texture] = material;
            }

            renderer.sharedMaterial = material;
        }

        private static Mesh BuildBox()
        {
            // 直接用 Unity 内置立方体，省得自己摆 24 个顶点
            var temp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Mesh mesh = Object.Instantiate(temp.GetComponent<MeshFilter>().sharedMesh);
            mesh.name = "CardBox";
            Object.DestroyImmediate(temp);
            return mesh;
        }

        private static Mesh BuildQuad()
        {
            var mesh = new Mesh { name = "CardQuad" };
            mesh.SetVertices(new List<Vector3>
            {
                new(-0.5f, -0.5f, 0f), new(-0.5f, 0.5f, 0f),
                new(0.5f, 0.5f, 0f), new(0.5f, -0.5f, 0f),
            });
            mesh.SetUVs(0, new List<Vector2> { new(0f, 0f), new(0f, 1f), new(1f, 1f), new(1f, 0f) });

            // 刻意做成**双面**：两组相反缠绕。
            // 面片朝向搞反会被背面剔除、整屏漆黑，而这种错误肉眼不可能从代码里看出来，
            // 多两个三角形换「永远不会因为朝向反了而消失」很划算。
            mesh.SetTriangles(new[]
            {
                0, 1, 2, 0, 2, 3, // 正面
                0, 2, 1, 0, 3, 2, // 背面
            }, 0);

            mesh.SetNormals(new List<Vector3>
            {
                Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward,
            });

            // 显式写白色顶点色：有些 shader（例如 Sprites/Default）会把顶点色乘进去，
            // 网格没有颜色流时结果是未定义的，可能整片变黑或透明
            mesh.SetColors(new List<Color> { Color.white, Color.white, Color.white, Color.white });
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
