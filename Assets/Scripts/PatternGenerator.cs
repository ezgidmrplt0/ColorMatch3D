using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using DG.Tweening;

public class PatternGenerator : MonoBehaviour
{
    [Header("Settings")]
    public GameObject cubePrefab;
    [Tooltip("Images to convert into cube patterns.")]
    public List<Texture2D> patternTextures; 

    [Header("Pattern Position & Scaling")]
    public Transform centerPoint; 
    public bool autoSizeFromMesh = false; 
    public float maxPatternWidth = 2.0f; 
    public float maxPatternHeight = 3.0f; 
    public float heightOffset = 0.1f; 
    public Vector3 positionOffset = Vector3.zero; 
    public Vector3 rotationOffset = Vector3.zero; 

    [Header("Player Deck Settings")]
    public Transform deckCenterPoint; 
    public float maxDeckWidth = 4.0f; 
    public float maxDeckHeight = 2.0f;
    public float deckSpacing = 1.2f; 
    public float deckScaleMultiplier = 2.0f; 
    public Vector3 deckPositionOffset = Vector3.zero; 
    public Vector3 deckRotationOffset = Vector3.zero;
    [Tooltip("Max items in one stack of buttons")]
    public int maxStackSize = 20;

    // Game State
    private Dictionary<Color, List<GameObject>> activePixelCubes = new Dictionary<Color, List<GameObject>>();
    private bool isGameActive = false;

    private void Start()
    {
        if (cubePrefab == null) { Debug.LogError("Cube Prefab is not assigned!"); return; }
        if (patternTextures != null && patternTextures.Count > 0) GeneratePatternFromTexture();
    }

    public void GeneratePatternFromTexture()
    {
        // Clear old pattern
        foreach(var list in activePixelCubes.Values) { foreach(var obj in list) if(obj) Destroy(obj); }
        activePixelCubes.Clear();

        Texture2D texture = patternTextures[Random.Range(0, patternTextures.Count)];
        if (texture == null) return;
        if (texture.width > 64 || texture.height > 64) { Debug.LogError("Texture too large!"); return; }

        // --- Auto Size ---
        if (autoSizeFromMesh && centerPoint != null)
        {
            MeshFilter meshFilter = centerPoint.GetComponent<MeshFilter>();
            if (meshFilter != null)
            {
                float realWidth = meshFilter.sharedMesh.bounds.size.x * centerPoint.transform.lossyScale.x;
                float realHeight = meshFilter.sharedMesh.bounds.size.y * centerPoint.transform.lossyScale.y;
                maxPatternWidth = realWidth * 0.9f;
                maxPatternHeight = realHeight * 0.9f;
            }
        }

        // --- Calculate Pattern ---
        float ratioX = maxPatternWidth / (float)texture.width;
        float ratioY = maxPatternHeight / (float)texture.height;
        float calculatedSpacing = Mathf.Min(ratioX, ratioY);
        Vector3 cubeScale = Vector3.one * calculatedSpacing * 0.95f; 

        Vector3 centerPos = (centerPoint != null) ? centerPoint.position : transform.position;
        // ... (standard setup)
        Vector3 rightDir = (centerPoint != null) ? centerPoint.right : Vector3.right;
        Vector3 upDir = (centerPoint != null) ? centerPoint.up : Vector3.up;     
        Vector3 forwardDir = (centerPoint != null) ? centerPoint.forward : Vector3.forward; 

        float widthOffset = (texture.width - 1) * calculatedSpacing / 2f;
        float heightOffsetMap = (texture.height - 1) * calculatedSpacing / 2f;

        // Generate Pixels - SCAN Y FIRST (Rows) then X (Columns)
        // This ensures objects are spawned Bottom-Up, and Color Buttons appear in that order
        List<Color> colorDiscoveryOrder = new List<Color>();
        HashSet<Color> discoveredColors = new HashSet<Color>();

        for (int y = 0; y < texture.height; y++)
        {
            for (int x = 0; x < texture.width; x++)
            {
                Color pixelColor = texture.GetPixel(x, y);
                if (pixelColor.a < 0.1f) continue;

                float xPos = (x * calculatedSpacing) - widthOffset;
                float yPos = (y * calculatedSpacing) - heightOffsetMap;

                Vector3 basePos = centerPos + (rightDir * xPos) + (upDir * yPos) + (forwardDir * heightOffset);
                Vector3 nudge = (rightDir * positionOffset.x) + (upDir * positionOffset.y) + (forwardDir * positionOffset.z);
                Vector3 finalPos = basePos + nudge;
                Quaternion finalRot = (centerPoint != null) ? centerPoint.rotation * Quaternion.Euler(rotationOffset) : Quaternion.Euler(rotationOffset);

                GameObject pixelCube = SpawnCube(finalPos, pixelColor, cubeScale, finalRot);
                
                if (!activePixelCubes.ContainsKey(pixelColor)) activePixelCubes[pixelColor] = new List<GameObject>();
                activePixelCubes[pixelColor].Add(pixelCube);

                // Track order
                if (!discoveredColors.Contains(pixelColor))
                {
                    discoveredColors.Add(pixelColor);
                    colorDiscoveryOrder.Add(pixelColor);
                }
            }
        }

        isGameActive = true;
        
        // --- Generate Stacks Logic ---
        List<StackData> deckStacks = new List<StackData>();
        
        // Use the strict discovery order (Bottom-Up)
        foreach (Color col in colorDiscoveryOrder)
        {
            int totalNeeded = activePixelCubes[col].Count;
            // ONE STACK PER COLOR (No splitting)
            deckStacks.Add(new StackData { color = col, count = totalNeeded });
        }
        
        GeneratePlayerDeck(deckStacks, cubeScale);
        Debug.Log($"Game Started! Pattern: {texture.name}");
    }

    private struct StackData
    {
        public Color color;
        public int count;
    }

    void GeneratePlayerDeck(List<StackData> stacks, Vector3 baseScale)
    {
        if (deckCenterPoint == null) return;

        PlayerCube[] oldButtons = deckCenterPoint.GetComponentsInChildren<PlayerCube>();
        foreach (var btn in oldButtons) Destroy(btn.gameObject);

        int count = stacks.Count;
        if (count == 0) return;

        float buttonSize = baseScale.x * deckScaleMultiplier;
        if (buttonSize > maxDeckHeight) buttonSize = maxDeckHeight;
        
        float itemSpacing = buttonSize * deckSpacing; 
        
        Vector3 rightDir = deckCenterPoint.right;
        Vector3 upDir = deckCenterPoint.up; 
        Vector3 forwardDir = deckCenterPoint.forward;

        Vector3 deckOrigin = deckCenterPoint.position 
                           + (rightDir * deckPositionOffset.x) 
                           + (upDir * deckPositionOffset.y) 
                           + (forwardDir * deckPositionOffset.z);

        // Top Left Logic
        Vector3 startPos = deckOrigin 
                         - (rightDir * (maxDeckWidth * 0.5f - buttonSize * 0.5f)) 
                         + (upDir * (maxDeckHeight * 0.5f - buttonSize * 0.5f));

        int col = 0;
        int row = 0;

        for (int i = 0; i < count; i++)
        {
            StackData stack = stacks[i];

            float xDist = col * itemSpacing;
            float yDist = row * itemSpacing;

            if (xDist + buttonSize > maxDeckWidth + 0.1f) 
            {
                col = 0;
                row++;
                xDist = 0;
                yDist = row * itemSpacing;
            }

            Vector3 spawnPos = startPos + (rightDir * xDist) - (upDir * yDist);
            Quaternion spawnRot = deckCenterPoint.rotation * Quaternion.Euler(deckRotationOffset);

            GameObject playerDeckCube = Instantiate(cubePrefab, spawnPos, spawnRot, deckCenterPoint);
            playerDeckCube.name = $"PlayerBtn_{i}_{stack.count}";

            // Visual Height Clamp
            Vector3 finalScale = new Vector3(buttonSize, buttonSize, buttonSize); 
            float stackHeightFactor = 1.0f + (stack.count * 0.05f); 
            if(stackHeightFactor > 5.0f) stackHeightFactor = 5.0f; // Max visual height 5x
            
            playerDeckCube.transform.localScale = new Vector3(finalScale.x, finalScale.y, finalScale.z * stackHeightFactor);

            PlayerCube pc = playerDeckCube.AddComponent<PlayerCube>();
            pc.Initialize(stack.color, this);
            pc.stackCount = stack.count; 

            col++;
        }
    }

    // Called by PlayerCube when clicked
    public void OnPlayerCubeClicked(PlayerCube senderCube)
    {
        if (!isGameActive) return;
        Color color = senderCube.cubeColor;

        if (activePixelCubes.ContainsKey(color) && activePixelCubes[color].Count > 0)
        {
            List<GameObject> allTargets = activePixelCubes[color].Where(t => t != null).ToList();
            if (allTargets.Count == 0) return;

            // Find lowest Y of ALL targets of this color
            float lowestY = allTargets.Min(t => t.transform.position.y);
            
            // Get all targets that are at this lowest row
            List<GameObject> bottomRowTargets = allTargets
                .Where(t => Mathf.Abs(t.transform.position.y - lowestY) < 0.15f)
                .OrderBy(t => t.transform.position.x)
                .ToList();

            // How many ammo do we have?
            int ammo = senderCube.stackCount;
            // We shoot as many as we can from the bottom row
            int targetsToShoot = Mathf.Min(bottomRowTargets.Count, ammo);
            
            for (int i = 0; i < targetsToShoot; i++)
            {
                GameObject target = bottomRowTargets[i];
                activePixelCubes[color].Remove(target);

                GameObject projectile = Instantiate(cubePrefab, senderCube.transform.position, Quaternion.identity);
                projectile.GetComponent<Renderer>().material.color = color;
                projectile.transform.localScale = target.transform.localScale;

                // Staggered shoot
                float delay = i * 0.05f; 
                projectile.transform.DOMove(target.transform.position, 0.4f).SetDelay(delay).SetEase(Ease.OutQuad).OnComplete(() => {
                    Destroy(projectile);
                    Destroy(target);
                    CheckWinCondition();
                });
            }

            senderCube.stackCount -= targetsToShoot;
            
            // Visual feedback
            senderCube.transform.DOPunchScale(Vector3.one * 0.15f, 0.2f);
            if(senderCube.stackCount > 0) 
            {
                 Vector3 currentScale = senderCube.transform.localScale;
                 float newZ = currentScale.z * 0.85f; 
                 if (newZ < currentScale.x) newZ = currentScale.x; 
                 senderCube.transform.localScale = new Vector3(currentScale.x, currentScale.y, newZ);
            }

            if (senderCube.stackCount <= 0)
            {
                Destroy(senderCube.gameObject);
            }
        }
        else
        {
            senderCube.transform.DOShakePosition(0.3f, 0.1f);
        }
    }

    void CheckWinCondition()
    {
        bool won = true;
        foreach (var list in activePixelCubes.Values) { if (list.Count > 0) { won = false; break; } }

        if (won && isGameActive)
        {
            isGameActive = false;
            Debug.Log("LEVEL COMPLETED!");
        }
    }

    GameObject SpawnCube(Vector3 position, Color color, Vector3 scale, Quaternion rotation)
    {
        GameObject newCube = Instantiate(cubePrefab, position, rotation);
        // newCube.transform.SetParent(this.transform); // Optional parent
        newCube.transform.localScale = scale;
        Renderer rend = newCube.GetComponent<Renderer>();
        if (rend != null) rend.material.color = color;
        return newCube;
    }

    private void OnDrawGizmos()
    {
        if (centerPoint != null)
        {
            Gizmos.color = Color.green;
            Vector3 pCenter = centerPoint.position + (centerPoint.forward * heightOffset) + (centerPoint.right * positionOffset.x) + (centerPoint.up * positionOffset.y) + (centerPoint.forward * positionOffset.z);
            Vector3 pSize = new Vector3(maxPatternWidth, maxPatternHeight, 0.1f);
            Gizmos.matrix = Matrix4x4.TRS(pCenter, centerPoint.rotation, Vector3.one);
            Gizmos.DrawWireCube(Vector3.zero, pSize);
        }

        if (deckCenterPoint != null)
        {
            Gizmos.color = Color.red;
            Vector3 rightDir = deckCenterPoint.right;
            Vector3 upDir = deckCenterPoint.up;
            Vector3 forwardDir = deckCenterPoint.forward;
            
            Vector3 dCenter = deckCenterPoint.position + (rightDir * deckPositionOffset.x) + (upDir * deckPositionOffset.y) + (forwardDir * deckPositionOffset.z);
            Vector3 dSize = new Vector3(maxDeckWidth, maxDeckHeight, 0.2f); 
            
            Gizmos.matrix = Matrix4x4.TRS(dCenter, deckCenterPoint.rotation, Vector3.one);
            Gizmos.DrawWireCube(Vector3.zero, dSize);
        }
    }
}
