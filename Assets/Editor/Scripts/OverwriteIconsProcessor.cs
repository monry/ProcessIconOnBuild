using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Monry.ProcessIconOnBuild.Editor
{
    public class OverwriteIconsProcessor : IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        public int callbackOrder => 0;

        private static Texture2D? _coverTexture2D;
        private static Texture2D CoverTexture2D => _coverTexture2D ??= LoadCoverTexture2D();

        private static BuildTargetGroup CurrentBuildTargetGroup => BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget);
        private static NamedBuildTarget CurrentNamedBuildTarget => NamedBuildTarget.FromBuildTargetGroup(CurrentBuildTargetGroup);

        private static Texture2D[]? OriginalDefaultIconTextures { get; set; }
        private static Texture2D[]? CombinedDefaultIconTextures { get; set; }
        private static Dictionary<PlatformIconKind, Texture2D[]>? OriginalPlatformIconTexturesMap { get; set; }
        private static Dictionary<PlatformIconKind, Texture2D[]>? CombinedPlatformIconTexturesMap { get; set; }

        public void OnPreprocessBuild(BuildReport report)
        {
            OverwriteIcons();
        }

        public void OnPostprocessBuild(BuildReport report)
        {
            RevertIcons();
        }

        [MenuItem("Build/Overwrite Icons")]
        private static void OverwriteIcons()
        {
            OverwriteDefaultIcon();
            OverwritePlatformIcons();
        }

        [MenuItem("Build/Revert Icons")]
        private static void RevertIcons()
        {
            RevertDefaultIcon();
            RevertPlatformIcons();
        }

        private static void OverwriteDefaultIcon()
        {
            var icons = PlayerSettings.GetIcons(NamedBuildTarget.Unknown, IconKind.Any);
            OriginalDefaultIconTextures = icons;
            if (icons.Length == 0)
            {
                return;
            }

            CombinedDefaultIconTextures = CombineAndSaveIcons(icons, NamedBuildTarget.Unknown);
            PlayerSettings.SetIcons(NamedBuildTarget.Unknown, CombinedDefaultIconTextures, IconKind.Any);
        }

        private static void OverwritePlatformIcons()
        {
            var supportedIconKinds = PlayerSettings.GetSupportedIconKinds(CurrentNamedBuildTarget);
            var platformIconsMap = supportedIconKinds.ToDictionary(x => x, x => PlayerSettings.GetPlatformIcons(CurrentNamedBuildTarget, x));
            OriginalPlatformIconTexturesMap = new Dictionary<PlatformIconKind, Texture2D[]>();
            CombinedPlatformIconTexturesMap = new Dictionary<PlatformIconKind, Texture2D[]>();
            foreach (var (platformIconKind, platformIcons) in platformIconsMap)
            {
                var combinedPlatformIcons = new PlatformIcon[platformIcons.Length];
                foreach (var (platformIcon, index) in platformIcons.Select((platformIcon, index) => (platformIcon, index)))
                {
                    OriginalPlatformIconTexturesMap[platformIconKind] = platformIcon.GetTextures();
                    var combinedTextures = CombineAndSaveIcons(platformIcon.GetTextures(), CurrentNamedBuildTarget, platformIconKind);
                    platformIcon.SetTextures(combinedTextures);
                    combinedPlatformIcons[index] = platformIcon;
                    CombinedPlatformIconTexturesMap[platformIconKind] = combinedTextures;
                }
                PlayerSettings.SetPlatformIcons(CurrentNamedBuildTarget, platformIconKind, combinedPlatformIcons);
            }
        }

        private static void RevertDefaultIcon()
        {
            if (OriginalDefaultIconTextures == default || CombinedDefaultIconTextures == default)
            {
                return;
            }
            foreach (var iconTexture2D in CombinedDefaultIconTextures)
            {
                if (iconTexture2D == default)
                {
                    continue;
                }
                var assetPath = AssetDatabase.GetAssetPath(iconTexture2D);
                AssetDatabase.DeleteAsset(assetPath);
            }
            PlayerSettings.SetIcons(NamedBuildTarget.Unknown, OriginalDefaultIconTextures, IconKind.Any);
        }

        private static void RevertPlatformIcons()
        {
            if (OriginalPlatformIconTexturesMap == default || CombinedPlatformIconTexturesMap == default)
            {
                return;
            }
            foreach (var (platformIconKind, iconTexture2Ds) in CombinedPlatformIconTexturesMap)
            {
                foreach (var iconTexture2D in iconTexture2Ds)
                {
                    if (iconTexture2D == default)
                    {
                        continue;
                    }
                    var assetPath = AssetDatabase.GetAssetPath(iconTexture2D);
                    AssetDatabase.DeleteAsset(assetPath);
                }

                if (!OriginalPlatformIconTexturesMap.ContainsKey(platformIconKind))
                {
                    continue;
                }

                var originalPlatformIcons = PlayerSettings.GetPlatformIcons(CurrentNamedBuildTarget, platformIconKind);
                foreach (var (platformIcon, index) in originalPlatformIcons.Select((platformIcon, index) => (platformIcon, index)))
                {
                    platformIcon.SetTextures(OriginalPlatformIconTexturesMap[platformIconKind]);
                    originalPlatformIcons[index] = platformIcon;
                }
                PlayerSettings.SetPlatformIcons(CurrentNamedBuildTarget, platformIconKind, originalPlatformIcons);
            }
        }

        private static Texture2D LoadCoverTexture2D()
        {
            return AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Editor/Images/Icons/CoverForDevelopment.png");
        }

        private static Texture2D CombineCoverTexture(Texture2D sourceTexture2D)
        {
            var (sourceWidth, sourceHeight) = (sourceTexture2D.width, sourceTexture2D.height);

            var coverRenderTexture = RenderTexture.GetTemporary(sourceWidth, sourceHeight, 0, RenderTextureFormat.Default, RenderTextureReadWrite.Default);
            Graphics.Blit(CoverTexture2D, coverRenderTexture);

            var originalRenderTexture = RenderTexture.active;

            RenderTexture.active = coverRenderTexture;
            var coverTexture2D = new Texture2D(sourceWidth, sourceHeight, TextureFormat.RGBA32, false);
            coverTexture2D.ReadPixels(new Rect(0, 0, sourceWidth, sourceHeight), 0, 0);
            coverTexture2D.Apply();

            RenderTexture.active = originalRenderTexture;
            RenderTexture.ReleaseTemporary(coverRenderTexture);

            var sourcePixels = sourceTexture2D.GetPixels();
            var coverPixels = coverTexture2D.GetPixels();
            var combinedPixels = new Color[sourcePixels.Length];
            for (var i = 0; i < sourcePixels.Length; i++)
            {
                var sourcePixel = sourcePixels[i];
                var coverPixel = coverPixels[i];

                var sourceAlpha = sourcePixel.a - coverPixel.a;
                var coverAlpha = coverPixel.a;

                var (r, g, b, a) = (
                    sourcePixel.r * sourceAlpha + coverPixel.r * coverAlpha,
                    sourcePixel.g * sourceAlpha + coverPixel.g * coverAlpha,
                    sourcePixel.b * sourceAlpha + coverPixel.b * coverAlpha,
                    Mathf.Min(1.0f, sourceAlpha + coverAlpha)
                );
                if (Mathf.Approximately(0.0f, sourceAlpha) && Mathf.Approximately(0.0f, coverAlpha))
                {
                    a = 0.0f;
                }

                combinedPixels[i] = new Color(r, g, b, a);
            }

            Object.DestroyImmediate(coverTexture2D);

            var combinedTexture2D = new Texture2D(sourceWidth, sourceHeight, TextureFormat.RGBA32, false);
            combinedTexture2D.SetPixels(combinedPixels);
            combinedTexture2D.Apply();

            return combinedTexture2D;
        }

        private static Texture2D[] CombineAndSaveIcons(Texture2D[] iconTexture2Ds, NamedBuildTarget namedBuildTarget, PlatformIconKind? platformIconKind = default)
        {
            var importedCombinedIcons = new Texture2D[iconTexture2Ds.Length];
            var hasManyIcons = iconTexture2Ds.Length > 1;
            foreach (var (iconTexture2D, index) in iconTexture2Ds.Select((texture2D, index) => (texture2D, index)))
            {
                if (iconTexture2D == default)
                {
                    continue;
                }
                var combinedIconTexture2D = CombineCoverTexture(iconTexture2D);
                var iconName = $"Icon{(!string.IsNullOrEmpty(namedBuildTarget.TargetName) ? $".{namedBuildTarget.TargetName}" : string.Empty)}{(platformIconKind != default ? $".{platformIconKind}" : string.Empty)}{(hasManyIcons ? $".{index}" : string.Empty)}";
                var assetPath = $"Assets/Editor/Images/Icons/Combined.{iconName}.png";
                combinedIconTexture2D.name = iconName;
                File.WriteAllBytes(assetPath, combinedIconTexture2D.EncodeToPNG());
                AssetDatabase.ImportAsset(assetPath);
                if (AssetImporter.GetAtPath(assetPath) is not TextureImporter textureImporter)
                {
                    throw new InvalidOperationException($"Failed to get TextureImporter for '{assetPath}'");
                }
                textureImporter.textureType = TextureImporterType.GUI;
                textureImporter.alphaIsTransparency = true;
                textureImporter.textureCompression = TextureImporterCompression.Uncompressed;
                textureImporter.SaveAndReimport();
                importedCombinedIcons[index] = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            }
            return importedCombinedIcons;
        }
    }
}
