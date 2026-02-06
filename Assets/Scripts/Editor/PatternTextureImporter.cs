using UnityEngine;
using UnityEditor;
using System.IO;

/// <summary>
/// Automatically configures texture import settings for pattern textures in PixelPatterns folder.
/// Ensures Read/Write is enabled for runtime pixel reading.
/// </summary>
public class PatternTextureImporter : AssetPostprocessor
{
    // Automatically process textures when imported into PixelPatterns folder
    void OnPreprocessTexture()
    {
        if (assetPath.Contains("PixelPatterns"))
        {
            TextureImporter importer = (TextureImporter)assetImporter;
            
            // Enable Read/Write for GetPixels() to work at runtime
            importer.isReadable = true;
            
            // Pixel art settings - no filtering, no compression
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.mipmapEnabled = false;
            
            Debug.Log($"[PatternTextureImporter] Configured {Path.GetFileName(assetPath)} for pattern use (Read/Write enabled)");
        }
    }
    
    // Menu item to fix existing textures
    [MenuItem("Tools/Pattern System/Fix All Pattern Textures")]
    public static void FixAllPatternTextures()
    {
        string folderPath = "Assets/PixelPatterns";
        
        if (!AssetDatabase.IsValidFolder(folderPath))
        {
            Debug.LogError($"Folder not found: {folderPath}");
            return;
        }
        
        string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { folderPath });
        int fixedCount = 0;
        
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            
            if (importer != null)
            {
                bool needsReimport = false;
                
                if (!importer.isReadable)
                {
                    importer.isReadable = true;
                    needsReimport = true;
                }
                
                if (importer.filterMode != FilterMode.Point)
                {
                    importer.filterMode = FilterMode.Point;
                    needsReimport = true;
                }
                
                if (importer.textureCompression != TextureImporterCompression.Uncompressed)
                {
                    importer.textureCompression = TextureImporterCompression.Uncompressed;
                    needsReimport = true;
                }
                
                if (needsReimport)
                {
                    importer.SaveAndReimport();
                    fixedCount++;
                    Debug.Log($"Fixed: {Path.GetFileName(path)}");
                }
            }
        }
        
        Debug.Log($"[PatternTextureImporter] Fixed {fixedCount} textures in {folderPath}");
        
        if (fixedCount > 0)
        {
            EditorUtility.DisplayDialog("Pattern Textures Fixed", 
                $"Successfully configured {fixedCount} texture(s) for pattern use.\n\nYou can now Play the game!", 
                "OK");
        }
        else
        {
            EditorUtility.DisplayDialog("Pattern Textures", 
                "All textures are already configured correctly!", 
                "OK");
        }
    }
}
