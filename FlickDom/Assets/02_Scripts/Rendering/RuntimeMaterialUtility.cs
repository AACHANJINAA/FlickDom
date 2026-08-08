using UnityEngine;

namespace FlickDom.Gameplay
{
    internal static class RuntimeMaterialUtility
    {
        private static Shader cachedPrimitiveShader;

        public static Material CreateMaterial(string materialName, Color color, params string[] shaderNames)
        {
            Shader shader = FindShader(shaderNames);
            if (shader == null)
            {
                Debug.LogError("Could not create material '" + materialName + "' because no runtime shader was found.");
                return null;
            }

            Material material = new Material(shader)
            {
                name = materialName
            };
            SetMaterialColor(material, color);
            return material;
        }

        public static Shader FindShader(params string[] shaderNames)
        {
            if (shaderNames != null)
            {
                for (int i = 0; i < shaderNames.Length; i++)
                {
                    if (string.IsNullOrEmpty(shaderNames[i]))
                    {
                        continue;
                    }

                    Shader shader = Shader.Find(shaderNames[i]);
                    if (shader != null)
                    {
                        return shader;
                    }
                }
            }

            return GetPrimitiveShader();
        }

        public static void SetMaterialColor(Material material, Color color)
        {
            if (material == null)
            {
                return;
            }

            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }

            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", color);
            }

            material.color = color;
        }

        private static Shader GetPrimitiveShader()
        {
            if (cachedPrimitiveShader != null)
            {
                return cachedPrimitiveShader;
            }

            GameObject primitive = GameObject.CreatePrimitive(PrimitiveType.Cube);
            primitive.hideFlags = HideFlags.HideAndDontSave;

            Renderer renderer = primitive.GetComponent<Renderer>();
            if (renderer != null && renderer.sharedMaterial != null)
            {
                cachedPrimitiveShader = renderer.sharedMaterial.shader;
            }

            Object.Destroy(primitive);
            return cachedPrimitiveShader;
        }
    }
}
