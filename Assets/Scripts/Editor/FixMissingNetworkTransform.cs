using UnityEngine;
using UnityEditor;
using Fusion;
using System.IO;

namespace Com.MyCompany.MyGame.Editor
{
    [InitializeOnLoad]
    public class FixMissingNetworkTransform
    {
        static FixMissingNetworkTransform()
        {
            // Delay the execution to ensure AssetDatabase is ready
            EditorApplication.delayCall += CheckAndFixPlayerPrefab;
        }

        private static void CheckAndFixPlayerPrefab()
        {
            string prefabPath = "Assets/Prefab/Player.prefab";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);

            if (prefab == null)
            {
                Debug.LogWarning($"[FixMissingNetworkTransform] Could not load prefab at {prefabPath}. Please verify the path.");
                return;
            }

            // Check if NetworkTransform is already missing
            if (prefab.GetComponent<NetworkTransform>() == null)
            {
                Debug.Log($"[FixMissingNetworkTransform] NetworkTransform missing on {prefab.name}. Adding it now...");
                
                // Add the component
                prefab.AddComponent<NetworkTransform>();
                
                // Mark object as dirty to ensure save
                EditorUtility.SetDirty(prefab);
                
                // Save the asset
                AssetDatabase.SaveAssets(); // Use SaveAssets to write changes to disk
                AssetDatabase.Refresh(); // Refresh to ensure Unity picks up the change
                
                Debug.Log($"[FixMissingNetworkTransform] Successfully added NetworkTransform to {prefab.name} and saved.");
            }
            else
            {
                // Component already exists, no action needed.
                // Commenting out to avoid spamming console on every recompile
                // Debug.Log($"[FixMissingNetworkTransform] {prefab.name} already has NetworkTransform.");
            }
        }
    }
}
