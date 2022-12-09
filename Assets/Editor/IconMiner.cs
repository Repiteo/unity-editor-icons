using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEditor;

namespace Halak
{
    public static class IconMiner
    {
        [MenuItem("Unity Editor Icons/Generate README.md %g", priority = -1000)]
        private static void GenerateREADME()
        {
            var guidMaterial = new Material(Shader.Find("Unlit/Texture"));
            var guidMaterialId = "Assets/Editor/_GuidMaterial.mat";
            AssetDatabase.CreateAsset(guidMaterial, guidMaterialId);

            EditorUtility.DisplayProgressBar("Generate README.md", "Generating...", 0.0f);
            try
            {
                var editorAssetBundle = GetEditorAssetBundle();
                var iconsPath = GetIconsPath();
                var readmeContents = new StringBuilder();

                const string temp = "Temp/";
                FileUtil.DeleteFileOrDirectory(temp + iconsPath);

                readmeContents.AppendLine($"Unity Editor Built-in Icons");
                readmeContents.AppendLine($"==============================");
                readmeContents.AppendLine($"Unity version: {Application.unityVersion}");
                readmeContents.AppendLine($"Icons what can load using `EditorGUIUtility.IconContent`");
                readmeContents.AppendLine();
                readmeContents.AppendLine($"File ID");
                readmeContents.AppendLine($"-------------");
                readmeContents.AppendLine($"You can change script icon by file id");
                readmeContents.AppendLine($"1. Open `*.cs.meta` in Text Editor");
                readmeContents.AppendLine($"2. Modify line `icon: {{instanceID: 0}}` to `icon: {{fileID: <FILE ID>, guid: 0000000000000000d000000000000000, type: 0}}`");
                readmeContents.AppendLine($"3. Save and focus Unity Editor");
                readmeContents.AppendLine();
                readmeContents.AppendLine($"| Icon | Name | File ID |");
                readmeContents.AppendLine($"|------|------|---------|");

                var assetNames = EnumerateIcons(editorAssetBundle, iconsPath).ToArray();
                for (var i = 0; i < assetNames.Length; i++)
                {
                    var assetName = assetNames[i];
                    var icon = editorAssetBundle.LoadAsset<Texture2D>(assetName);
                    if (!icon)
                        continue;

                    EditorUtility.DisplayProgressBar("Generate README.md", $"Generating... ({i + 1}/{assetNames.Length})", (float)i / assetNames.Length);

                    var readableTexture = new Texture2D(icon.width, icon.height, icon.format, icon.mipmapCount > 1);

                    Graphics.CopyTexture(icon, readableTexture);

                    var folderPath = Path.GetDirectoryName(Path.Combine(iconsPath, assetName.Substring(iconsPath.Length)));
                    if (!Directory.Exists(temp + folderPath))
                        Directory.CreateDirectory(temp + folderPath);

                    var iconPath = Path.Combine(folderPath, icon.name + ".png");
                    File.WriteAllBytes(temp + iconPath, readableTexture.Decompress().EncodeToPNG());

                    guidMaterial.mainTexture = icon;
                    EditorUtility.SetDirty(guidMaterial);
                    AssetDatabase.SaveAssets();
                    var fileId = GetFileId(guidMaterialId);

                    var escapedUrl = iconPath.Replace(" ", "%20").Replace('\\', '/');
                    var thumbnail = ClampIcon(icon, out var w, out var h) ? $"[<img src=\"{escapedUrl}\" width=\"{w}px\" height=\"{h}px\">]" : $"![]";
                    readmeContents.AppendLine($"| {thumbnail}({escapedUrl} \"{icon.width}×{icon.height}\") | `{icon.name}` | `{fileId}` |");
                }

                FileUtil.ReplaceDirectory(temp + iconsPath, iconsPath);

                File.WriteAllText("README.md", FormatContents(readmeContents));

                Debug.Log($"'README.md' has been generated, with {assetNames.Length} icons exported");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                AssetDatabase.DeleteAsset(guidMaterialId);
            }
        }

        private static string FormatContents(StringBuilder contents)
        {
            var eol = string.Empty;
            switch (EditorSettings.lineEndingsForNewScripts)
            {
                case LineEndingsMode.OSNative:
                default:
                    eol = Environment.NewLine;
                    break;
                case LineEndingsMode.Unix:
                    eol = "\n";
                    break;
                case LineEndingsMode.Windows:
                    eol = "\r\n";
                    break;
            }
            return contents.ToString().Replace("\r\n", "\n").Replace("\n\r", "\n").Replace("\r", "\n").Replace("\n", eol);
        }

        public static Texture2D Decompress(this Texture2D source)
        {
            RenderTexture renderTex = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.Default, RenderTextureReadWrite.Linear);
            Graphics.Blit(source, renderTex);
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = renderTex;
            Texture2D readableText = new Texture2D(source.width, source.height);
            readableText.ReadPixels(new Rect(0, 0, renderTex.width, renderTex.height), 0, 0);
            readableText.Apply();
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(renderTex);
            return readableText;
        }

        private static bool ClampIcon(Texture2D icon, out int w, out int h)
        {
            w = icon.width;
            h = icon.height;
            const int max = 32;
            if (w <= max && h <= max)
                return false;
            var div = MathF.Max(w, h) / max;
            w = (int)(w / div);
            h = (int)(h / div);
            return true;
        }

        private static IEnumerable<string> EnumerateIcons(AssetBundle editorAssetBundle, string iconsPath)
        {
            foreach (var assetName in editorAssetBundle.GetAllAssetNames())
            {
                if (assetName.StartsWith(iconsPath, StringComparison.OrdinalIgnoreCase) == false)
                    continue;
                if (assetName.EndsWith(".png", StringComparison.OrdinalIgnoreCase) == false &&
                    assetName.EndsWith(".asset", StringComparison.OrdinalIgnoreCase) == false)
                    continue;

                yield return assetName;
            }
        }

        private static string GetFileId(string proxyAssetPath)
        {
            var serializedAsset = File.ReadAllText(proxyAssetPath);
            var index = serializedAsset.IndexOf("_MainTex:", StringComparison.Ordinal);
            if (index == -1)
                return string.Empty;

            const string FileId = "fileID:";
            var startIndex = serializedAsset.IndexOf(FileId, index) + FileId.Length;
            var endIndex = serializedAsset.IndexOf(',', startIndex);
            return serializedAsset.Substring(startIndex, endIndex - startIndex).Trim();
        }

        private static AssetBundle GetEditorAssetBundle()
        {
            var editorGUIUtility = typeof(EditorGUIUtility);
            var getEditorAssetBundle = editorGUIUtility.GetMethod(
                "GetEditorAssetBundle",
                BindingFlags.NonPublic | BindingFlags.Static);

            return (AssetBundle)getEditorAssetBundle.Invoke(null, new object[] { });
        }

        private static string GetIconsPath()
        {
#if UNITY_2018_3_OR_NEWER
            return UnityEditor.Experimental.EditorResources.iconsPath;
#else
            var assembly = typeof(EditorGUIUtility).Assembly;
            var editorResourcesUtility = assembly.GetType("UnityEditorInternal.EditorResourcesUtility");

            var iconsPathProperty = editorResourcesUtility.GetProperty(
                "iconsPath",
                BindingFlags.Static | BindingFlags.Public);

            return (string)iconsPathProperty.GetValue(null, new object[] { });
#endif
        }
    }
}
