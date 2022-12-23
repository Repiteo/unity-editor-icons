using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Halak
{
    public static class IconMiner
    {
        [MenuItem("Unity Editor Icons/Generate README.md %g", priority = -1000)]
        private static void GenerateREADME()
        {
            var editorAssetBundle = GetEditorAssetBundle();
            var iconsPath = GetIconsPath();

            const string temp = "Temp/";
            FileUtil.DeleteFileOrDirectory(temp + iconsPath);

            var assetNames = EnumerateIcons(editorAssetBundle, iconsPath).ToArray();
            var total = assetNames.Length;
            var current = 0f;

            using (var writer = new StreamWriter("README.md"))
            {
                writer.WriteLine($"Unity Editor Built-in Icons");
                writer.WriteLine($"==============================");
                writer.WriteLine($"Unity version: {Application.unityVersion}");
                writer.WriteLine($"Icons what can load using `EditorGUIUtility.IconContent`");
                writer.WriteLine();
                writer.WriteLine($"File ID");
                writer.WriteLine($"-------------");
                writer.WriteLine($"You can change script icon by file id");
                writer.WriteLine($"1. Open `*.cs.meta` in Text Editor");
                writer.WriteLine($"2. Modify line `icon: {{instanceID: 0}}` to `icon: {{fileID: <FILE ID>, guid: 0000000000000000d000000000000000, type: 0}}`");
                writer.WriteLine($"3. Save and focus Unity Editor");
                writer.WriteLine();
                writer.WriteLine($"| Icon | Name | File ID |");
                writer.WriteLine($"|------|------|---------|");

                foreach (var assetName in assetNames)
                {
                    if (EditorUtility.DisplayCancelableProgressBar("Generate README.md", $"Generating… ({++current}/{total})", current / total))
                        break;

                    var icon = editorAssetBundle.LoadAsset<Texture2D>(assetName);
                    if (!icon)
                        continue;

                    var folderPath = Path.GetDirectoryName(Path.Combine(iconsPath, assetName.Substring(iconsPath.Length)));
                    if (!Directory.Exists(temp + folderPath))
                        Directory.CreateDirectory(temp + folderPath);

                    var iconPath = Path.Combine(folderPath, icon.name + ".png");
                    File.WriteAllBytes(temp + iconPath, icon.Decompress());

                    AssetDatabase.TryGetGUIDAndLocalFileIdentifier(icon, out string guid, out long localId);
                    ClampIcon(icon, out int clampedWidth, out int clampedHeight);

                    var escapedUrl = iconPath.Replace(" ", "%20").Replace('\\', '/');
                    var thumbnail = $"[<img src=\"{escapedUrl}\" width=\"{clampedWidth}px\" height=\"{clampedHeight}px\">]";
                    writer.WriteLine($"| {thumbnail}({escapedUrl} \"{icon.width}×{icon.height}\") | `{icon.name}` | `{localId}` |");
                }
            }

            FileUtil.ReplaceDirectory(temp + iconsPath, iconsPath);

            Debug.Log($"'README.md' has been generated, with {current} out of {total} icons exported");

            EditorUtility.ClearProgressBar();
        }

        public static byte[] Decompress(this Texture2D source)
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
            return readableText.EncodeToPNG();
        }

        private static void ClampIcon(Texture2D icon, out int clampedWidth, out int clampedHeight, int clampMax = 32)
        {
            clampedWidth = icon.width;
            clampedHeight = icon.height;
            if (clampedWidth > clampMax || clampedHeight > clampMax)
            {
                var div = MathF.Max(clampedWidth, clampedHeight) / clampMax;
                clampedWidth = (int)(clampedWidth / div);
                clampedHeight = (int)(clampedHeight / div);
            }
            return;
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
