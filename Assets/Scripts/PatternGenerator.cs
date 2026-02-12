using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using DG.Tweening;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class PatternGenerator : MonoBehaviour
{
    [Header("Settings")]
    public GameObject cubePrefab;

    public GameObject dronePrefab; 
    public GameObject turretPrefab; 
    public GameObject ammoPrefab;   
    public Transform mainTurretTransform; // NEW: The Single Main Turret
    public float turretRotationOffset = 0f; // Offset for Z-rotation
    public List<Texture2D> patternTextures;
    public float pixelScale = 1.0f;
    public Vector3 spacing = new Vector3(1.1f, 1.1f, 1.1f);
    public Transform centerPoint;
    public Vector3 rotationOffset = Vector3.zero;
    public Vector3 positionOffset = Vector3.zero;
    public float heightOffset = 0.1f; // Restored variable
    
    // Limits
    public float maxPatternWidth = 7f;
    public float maxPatternHeight = 8f;
    [Range(0.5f, 1f)] public float patternPadding = 0.85f; // Padding inside the frame

    [Header("Deck Settings")]
    public Transform deckCenterPoint;
    public float maxDeckWidth = 5f;
    public float maxDeckHeight = 2f;
    public Vector3 deckRotationOffset = Vector3.zero;

    public Vector3 deckPositionOffset = Vector3.zero;
    public int visibleDeckCount = 3; // How many buttons are clickable at the start?

    [Header("Slot Settings")]
    public List<Transform> manualSlots; 
    
    [Header("Drone Path Waypoints")]
    public Transform pointBottomLeft;
    public Transform pointTopLeft;
    public Transform pointTopRight;
    public Transform pointBottomRight;
    public Transform pointFifthWaypoint;

    private bool[] slotOccupied;
    private int maxSlots = 5;

    public bool autoSizeFromMesh = true;

    public static PatternGenerator Instance { get; private set; }

    // Runtime state
    public Dictionary<Color, List<GameObject>> activePixelCubes = new Dictionary<Color, List<GameObject>>();

    public List<PlayerCube> allActiveCubes = new List<PlayerCube>();

    private bool isGameActive = false;
    private bool isTurretBusy = false; 
    private Vector3 initialTurretLocalEuler; 
    
    private DeckManager deckManager;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
        
        deckManager = GetComponent<DeckManager>();
        if (deckManager == null) deckManager = gameObject.AddComponent<DeckManager>();
    }

    public void RegisterCube(PlayerCube cube)
    {
        if (!allActiveCubes.Contains(cube)) allActiveCubes.Add(cube);
    }

    public void UnregisterCube(PlayerCube cube)
    {
        if (allActiveCubes.Contains(cube)) allActiveCubes.Remove(cube);
    }

    // Level System
    private int currentLevelIndex = 0;
    
    // Main Update Loop for Queue
    private void Update()
    {
        if(!isGameActive || isTurretBusy || mainTurretTransform == null) return;
        
        ProcessTurretQueue();
    }

    private void ProcessTurretQueue()
    {
        // Scan slots for Ready Ammo
        // Optimized: Uses cached list instead of FindObjectsOfType
        
        for (int i = 0; i < manualSlots.Count; i++)
        {
             Transform slot = manualSlots[i];
             
             // Find ammo that belongs to this slot
             PlayerCube ammo = null;
             foreach(var pc in allActiveCubes)
             {
                 if(pc != null && pc.currentSlot == slot)
                 {
                     ammo = pc;
                     break;
                 }
             }
             
             if(ammo != null && ammo.isReadyToFire)
             {
                 // We found ready ammo!
                 StartCoroutine(MainTurretFireSequence(ammo, ammo.cubeColor, slot));
                 return; // Only process one at a time
             }
        }
    }

    // Billboard Text Logic
    private void LateUpdate()
    {
        if(Camera.main == null) return;
        
        // Robust Billboarding: Use cached list
        Quaternion camRot = Camera.main.transform.rotation;
        
        for(int i = 0; i < allActiveCubes.Count; i++)
        {
            PlayerCube pc = allActiveCubes[i];
            if(pc == null || !pc.gameObject.activeInHierarchy) continue;

            // Handle TMP
            TMP_Text t = pc.GetComponentInChildren<TMP_Text>();
            if(t != null) t.transform.rotation = camRot;

            // Handle Legacy Text
            Text tLeg = pc.GetComponentInChildren<Text>();
            if(tLeg != null) tLeg.transform.rotation = camRot;
        }
    }


    private void Start()
    {
        SetupHypercasualVisuals();
        InitializeLevels();

        if (cubePrefab == null) { Debug.LogError("Cube Prefab is not assigned!"); return; }
        
        // Clean up manual slots at start - Preserve Visuals!
        if(manualSlots != null)
        {
            foreach(Transform t in manualSlots)
            {
                if(t == null) continue;
                // Only destroy game logic objects (PlayerCube or Reserv), keep static meshes
                foreach(Transform child in t) 
                {
                    if (child.GetComponent<PlayerCube>() != null || child.name == "Reservation")
                        Destroy(child.gameObject); 
                }
            }
        }
        
        if(mainTurretTransform != null)
        {
            initialTurretLocalEuler = mainTurretTransform.localEulerAngles;
        }

        LoadLevel(currentLevelIndex);
    }
    
    private void InitializeLevels()
    {
        // PNG-based pattern system: Reads patterns from patternTextures list
        // Each texture in the list becomes a level
        // Transparent/white pixels are ignored, colored pixels become blocks
        
        if (patternTextures == null || patternTextures.Count == 0)
        {
            Debug.LogWarning("No pattern textures assigned! Add PNG files to patternTextures list in Inspector.");
            return;
        }
        
        Debug.Log($"Initialized {patternTextures.Count} levels from PNG textures.");
    }

    public void LoadLevel(int index)
    {
        if (patternTextures == null || patternTextures.Count == 0)
        {
            Debug.LogError("No pattern textures assigned! Add PNG files to patternTextures list.");
            return;
        }
        
        if(index < 0 || index >= patternTextures.Count) index = 0; // Loop back
        currentLevelIndex = index;

        GeneratePatternFromTexture(patternTextures[currentLevelIndex]);
        
        // Hide Manual UI
        if(winUIObject != null) winUIObject.SetActive(false);
        
        Debug.Log($"Level {index + 1} Loaded from texture: {patternTextures[currentLevelIndex].name}");
    }

    /// <summary>
    /// Generates a pattern from a PNG texture.
    /// Each colored pixel becomes a cube. Transparent/white pixels are ignored.
    /// Similar colors are merged to prevent slight variations from creating multiple drone types.
    /// </summary>
    public void GeneratePatternFromTexture(Texture2D texture)
    {
        if (texture == null)
        {
            Debug.LogError("Pattern texture is null!");
            return;
        }
        
        // Cleanup existing patterns
        foreach(var list in activePixelCubes.Values) { foreach(var obj in list) if(obj) Destroy(obj); }
        activePixelCubes.Clear();
        
        // Cleanup Deck Visuals too (Important for new level colors)
        // Handled by DeckManager.GeneratePlayerDeck later
        
        // Initialize occupied array based on manual slots count
        if (manualSlots != null) 
        {
            maxSlots = manualSlots.Count;
            slotOccupied = new bool[maxSlots];
            
            // Allow slots to be used again - Clear stray drones
            foreach(Transform t in manualSlots)
            {
                 foreach(Transform child in t) 
                {
                    if (child.GetComponent<PlayerCube>() != null || child.name == "Reservation" || child.name.StartsWith("Drone"))
                        Destroy(child.gameObject); 
                }
            }
        }
        
        int width = texture.width;
        int height = texture.height;
        
        // Read all pixels from texture - with fallback for non-readable textures
        Color[] pixels = GetTexturePixels(texture);
        
        // FIRST PASS: Find the actual bounding box of colored pixels
        int minX = width, maxX = 0, minY = height, maxY = 0;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Color pixelColor = pixels[y * width + x];
                
                // Skip transparent or nearly white pixels
                if (pixelColor.a < 0.1f) continue;
                if (pixelColor.r > 0.95f && pixelColor.g > 0.95f && pixelColor.b > 0.95f) continue;
                
                // This is a colored pixel - update bounds
                minX = Mathf.Min(minX, x);
                maxX = Mathf.Max(maxX, x);
                minY = Mathf.Min(minY, y);
                maxY = Mathf.Max(maxY, y);
            }
        }
        
        // Calculate actual content dimensions
        int contentWidth = maxX - minX + 1;
        int contentHeight = maxY - minY + 1;
        
        Debug.Log($"[PATTERN] Texture: {width}x{height}, Content bounds: ({minX},{minY}) to ({maxX},{maxY}), Content size: {contentWidth}x{contentHeight}");
        
        // Adjust sizing - Scale pattern to fill available area based on CONTENT size
        float widthSpacing = maxPatternWidth / (float)contentWidth;
        float heightSpacing = maxPatternHeight / (float)contentHeight;
        
        // Use the smaller of the two to ensure pattern fits in both dimensions
        // Apply padding multiplier to keep pattern inside the frame
        float calculatedSpacing = Mathf.Min(widthSpacing, heightSpacing) * patternPadding;

        Vector3 cubeScale = Vector3.one * calculatedSpacing * 0.90f; 
 
        Vector3 centerPos = (centerPoint != null) ? centerPoint.position : transform.position;
        Vector3 rightDir = (centerPoint != null) ? centerPoint.right : Vector3.right;
        Vector3 upDir = (centerPoint != null) ? centerPoint.up : Vector3.up;     
        Vector3 forwardDir = (centerPoint != null) ? centerPoint.forward : Vector3.forward; 

        // Calculate offset based on CONTENT center, not texture center
        float contentCenterX = (minX + maxX) / 2f;
        float contentCenterY = (minY + maxY) / 2f;

        List<Color> colorDiscoveryOrder = new List<Color>();
        HashSet<Color> discoveredColors = new HashSet<Color>();
        
        // Dictionary to map similar colors to a single representative color
        Dictionary<Color, Color> colorMapping = new Dictionary<Color, Color>();

        // SECOND PASS: Generate cubes (now centered correctly)
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Color pixelColor = pixels[y * width + x];
                
                // Skip transparent or nearly white pixels
                if (pixelColor.a < 0.1f) continue;
                if (pixelColor.r > 0.95f && pixelColor.g > 0.95f && pixelColor.b > 0.95f) continue;
                
                // Normalize the color to find similar colors (merge slightly different shades)
                Color normalizedColor = NormalizeColor(pixelColor);
                
                // Check if we already have a similar color mapped
                Color mappedColor = normalizedColor;
                if (!colorMapping.ContainsKey(normalizedColor))
                {
                    // Find existing similar color or use this one
                    bool foundSimilar = false;
                    foreach (var existingColor in colorMapping.Values)
                    {
                        if (AreColorsSimilar(normalizedColor, existingColor, 0.15f))
                        {
                            colorMapping[normalizedColor] = existingColor;
                            mappedColor = existingColor;
                            foundSimilar = true;
                            break;
                        }
                    }
                    if (!foundSimilar)
                    {
                        colorMapping[normalizedColor] = normalizedColor;
                        mappedColor = normalizedColor;
                    }
                }
                else
                {
                    mappedColor = colorMapping[normalizedColor];
                }

                // Position relative to CONTENT center (not texture center)
                float xPos = (x - contentCenterX) * calculatedSpacing;
                float yPos = (y - contentCenterY) * calculatedSpacing;

                Vector3 basePos = centerPos + (rightDir * xPos) + (upDir * yPos) + (forwardDir * heightOffset);
                Vector3 nudge = (rightDir * positionOffset.x) + (upDir * positionOffset.y) + (forwardDir * positionOffset.z);
                Vector3 finalPos = basePos + nudge;
                
                GameObject pixelCube = Instantiate(cubePrefab, finalPos, Quaternion.identity);
                pixelCube.GetComponent<Renderer>().material.color = mappedColor;
                pixelCube.transform.localScale = cubeScale;
                
                // Hide Text on Pattern Cubes
                Text textComp = pixelCube.GetComponentInChildren<Text>();
                if (textComp != null) textComp.enabled = false; 
                
                if (!activePixelCubes.ContainsKey(mappedColor)) activePixelCubes[mappedColor] = new List<GameObject>();
                activePixelCubes[mappedColor].Add(pixelCube);

                if (!discoveredColors.Contains(mappedColor))
                {
                    discoveredColors.Add(mappedColor);
                    colorDiscoveryOrder.Add(mappedColor);
                }
            }
        }

        isGameActive = true;
        
        Debug.Log($"Pattern generated: {width}x{height} pixels, {colorDiscoveryOrder.Count} unique colors, {activePixelCubes.Values.Sum(l => l.Count)} total cubes");
        
        // --- Generate Stacks Logic ---
        List<DeckManager.StackData> deckStacks = new List<DeckManager.StackData>();
        foreach (Color col in colorDiscoveryOrder)
        {
            // PROPORTIONAL AMMO: 20-20-10-REMAINDER Logic
            int totalAmmo = activePixelCubes.ContainsKey(col) ? activePixelCubes[col].Count : 10;
            
            // 1. Fill 20s
            while(totalAmmo >= 20)
            {
                deckStacks.Add(new DeckManager.StackData { color = col, count = 20 });
                totalAmmo -= 20;
            }

            // 2. If remainder > 10, make a 10 stack
            if(totalAmmo > 10)
            {
                deckStacks.Add(new DeckManager.StackData { color = col, count = 10 });
                totalAmmo -= 10;
            }

            // 3. Add whatever is left
            if(totalAmmo > 0)
            {
                deckStacks.Add(new DeckManager.StackData { color = col, count = totalAmmo });
            }
        }
        
        // Pass to DeckManager
        // Ensure DeckManager has references
        deckManager.deckCenterPoint = this.deckCenterPoint;
        deckManager.deckPositionOffset = this.deckPositionOffset;
        deckManager.visibleDeckCount = this.visibleDeckCount;

        deckManager.GeneratePlayerDeck(deckStacks, dronePrefab != null ? dronePrefab : cubePrefab, this);
    }
    
    /// <summary>
    /// Normalizes a color by rounding RGB values to reduce slight variations.
    /// </summary>
    private Color NormalizeColor(Color c)
    {
        // Round to nearest 0.1 to group similar colors
        float r = Mathf.Round(c.r * 10f) / 10f;
        float g = Mathf.Round(c.g * 10f) / 10f;
        float b = Mathf.Round(c.b * 10f) / 10f;
        return new Color(r, g, b, 1f);
    }
    
    /// <summary>
    /// Checks if two colors are similar within a given threshold.
    /// </summary>
    private bool AreColorsSimilar(Color a, Color b, float threshold)
    {
        return Mathf.Abs(a.r - b.r) < threshold &&
               Mathf.Abs(a.g - b.g) < threshold &&
               Mathf.Abs(a.b - b.b) < threshold;
    }
    
    /// <summary>
    /// Gets pixels from a texture, even if it's not marked as readable.
    /// Uses RenderTexture as a fallback for non-readable textures.
    /// </summary>
    private Color[] GetTexturePixels(Texture2D texture)
    {
        // First try direct read if texture is readable
        if (texture.isReadable)
        {
            return texture.GetPixels();
        }
        
        // Fallback: Use RenderTexture to read non-readable textures
        Debug.Log($"Texture '{texture.name}' is not readable. Using RenderTexture fallback.");
        
        RenderTexture tempRT = RenderTexture.GetTemporary(
            texture.width, 
            texture.height, 
            0, 
            RenderTextureFormat.ARGB32);
        
        // Copy texture to RenderTexture
        Graphics.Blit(texture, tempRT);
        
        // Store the current active RenderTexture
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = tempRT;
        
        // Create a new readable texture and read the pixels
        Texture2D readableTexture = new Texture2D(texture.width, texture.height, TextureFormat.ARGB32, false);
        readableTexture.ReadPixels(new Rect(0, 0, tempRT.width, tempRT.height), 0, 0);
        readableTexture.Apply();
        
        // Restore the previous RenderTexture
        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(tempRT);
        
        Color[] pixels = readableTexture.GetPixels();
        
        // Clean up the temporary texture
        Destroy(readableTexture);
        
        return pixels;
    }



    // Called by PlayerCube when clicked
    public void OnPlayerCubeClicked(PlayerCube senderCube)
    {
        if (!isGameActive) return;
        
        // --- DECK QUEUE CHECK ---
        // If this cube is in the deck (not a slot re-launch)
        if (deckManager.IsInDeck(senderCube))
        {
            // If the item is beyond the visible/allowed count, reject the click
            if (!deckManager.IsClickable(senderCube))
            {
                // Feedback: Shake to show it's locked
                senderCube.transform.DOShakeRotation(0.3f, 15f);
                return;
            }
        }
        // ------------------------

        Debug.Log($"Clicked: {senderCube.name}, Parent: {senderCube.transform.parent?.name}");
        Color color = senderCube.cubeColor;

        if (activePixelCubes.ContainsKey(color))
        {
            // Calculate Total Targets (Ammo)
            int totalTargets = activePixelCubes[color].Count;
            Debug.Log($"[DECK DEBUG] Clicked Color: {color}, Targets Found: {totalTargets}");

            if (totalTargets <= 0) 
            {
                // No targets left for this color? Maybe shape them slightly.
                senderCube.transform.DOShakePosition(0.3f, 0.1f);
                return;
            }
            
            GameObject droneObj = null;
            bool isNewSpawn = false;

            // Check source: Is this sender in the Manual Slots?
            if (manualSlots.Contains(senderCube.transform.parent))
            {
                // Re-launching from Slot
                droneObj = senderCube.gameObject;
                isNewSpawn = false;
                Debug.Log($"[DECK DEBUG] Re-launching from slot. isNewSpawn = false");
                
                // User clicked an item already in slot -> Force Fire
                senderCube.isReadyToFire = true;
            }
            else
            {
                // Spawning from Deck - THIS IS A NEW SPAWN!
                isNewSpawn = true;
                Debug.Log($"[DECK DEBUG] Spawning from Deck. isNewSpawn = true");
                
                // Find empty slot first - Only needed if spawning new
                int targetSlotIndex = -1;
                for (int i = 0; i < manualSlots.Count; i++) 
                { 
                    bool isOccupied = false;
                    foreach(Transform child in manualSlots[i])
                    {
                        if (child == senderCube.transform) continue; // Ignore self
                        if (child.GetComponent<PlayerCube>() != null || child.name == "Reservation")
                        {
                            isOccupied = true;
                            break;
                        }
                    }

                    if (!isOccupied) 
                    { 
                        targetSlotIndex = i; 
                        break; 
                    } 
                } // Close for loop

                if (targetSlotIndex == -1) { Debug.Log("[DECK DEBUG] Slots Full!"); return; } // Slots full
                Debug.Log($"[DECK DEBUG] Found empty slot at index: {targetSlotIndex}");

                // Spawn Drone Logic (Moved outside the loop)
            // The `isNewSpawn` flag is determined here based on whether it's from the deck or a slot.
            // If from deck, it's a new spawn. If from slot, it's a re-launch.
            // The `isNewSpawn` variable is initialized to `false` at the start of the method.

            if (isNewSpawn && dronePrefab != null)
            {
                 droneObj = Instantiate(dronePrefab, senderCube.transform.position, Quaternion.identity);
                 Debug.Log($"[DECK DEBUG] Instantiated new drone from dronePrefab");
            }
            else if (!isNewSpawn)
            {
                // Re-launching existing, usually already correct prefab type
                 droneObj = senderCube.gameObject;
            }
            else
            {
                 // Fallback
                 droneObj = Instantiate(cubePrefab, senderCube.transform.position, Quaternion.identity);
                 Debug.Log($"[DECK DEBUG] Instantiated new drone from cubePrefab (fallback)");
            }

            if (isNewSpawn)
            {
                Debug.Log($"[DECK DEBUG] isNewSpawn is TRUE - will remove from deck and rearrange");
                
                // DELEGATE TO DECK MANAGER
                deckManager.RemoveButton(senderCube);
                
                Destroy(senderCube.gameObject);
            }
            else
            {
                Debug.Log($"[DECK DEBUG] isNewSpawn is FALSE - NOT calling RearrangeDeck");
            }
            } // End of Else (Manual/Deck check)


            // Setup Drone
            droneObj.name = "Drone_" + color;
            
            // COLORING: Drone might have multiple renderers. Look for "Body" or color all suitable ones.
            Renderer[] rends = droneObj.GetComponentsInChildren<Renderer>();
            foreach(var r in rends)
            {
                // Simple heuristic: Don't color Props (usually black) or Text
                // If the prefab has specific material names, we could check. 
                // For now, let's color everything that isn't black/dark by default? 
                // Or just apply to the main mesh.
                // Safest approach for "Mini_Drone": Color everything except trails/particles for now.
                if(r.name.Contains("Propeller") || r.gameObject.GetComponent<TMP_Text>() != null) continue;
                r.material.color = color;
            }

            droneObj.transform.localScale = Vector3.one * 3.0f; // INCREASED SIZE

            // Setup PlayerCube Component on Drone (to hold ammo data and be clickable)
            PlayerCube dronePC = droneObj.GetComponent<PlayerCube>();
            if (dronePC == null) dronePC = droneObj.AddComponent<PlayerCube>();
            dronePC.Initialize(color, this);
            dronePC.stackCount = isNewSpawn ? senderCube.stackCount : dronePC.stackCount; 
            
            // SET READY FLAG: This tells the queue to pick it up and fire
            dronePC.isReadyToFire = true;


            // Update Text (TMP & Legacy support)
            TMP_Text droneText = droneObj.GetComponentInChildren<TMP_Text>();
            if (droneText != null) droneText.text = dronePC.stackCount.ToString();
            
            Text legacyText = droneObj.GetComponentInChildren<Text>();
            if (legacyText != null) legacyText.text = dronePC.stackCount.ToString();

            // --- Movement Logic ---
            
            Transform destSlot = null;
            if (!isNewSpawn && manualSlots.Contains(droneObj.transform.parent))
            {
                destSlot = droneObj.transform.parent; // Return to same slot
            }
            else
            {
                // Find first empty physically
                 for (int i = 0; i < manualSlots.Count; i++) 
                 { 
                    bool isOccupied = false;
                    foreach(Transform child in manualSlots[i])
                    {
                        if (child == senderCube.transform) continue; // Ignore self
                        if (child.GetComponent<PlayerCube>() != null || child.name == "Reservation")
                        {
                            isOccupied = true;
                            break;
                        }
                    }

                    if (!isOccupied) 
                    { 
                         destSlot = manualSlots[i]; 
                         break; 
                    } 
                 }
            }
            

            
            if(destSlot == null) { if(isNewSpawn) Destroy(droneObj); return; } // Should not happen easily

            // --- TURRET & AMMO LOGIC START ---
            
            // 1. Move Ammo to Slot (Reload Phase) -> REMOVED VISUAL MOVEMENT to Slot
            // The drone will stay where it spawned (Deck position) until the Turret picks it up.
            // But we must logically assign it to the slot so the Queue knows it's "next".
            
            droneObj.transform.SetParent(null); // Keep as root object
            
            // Store slot reference on PlayerCube for later lookup by ProcessTurretQueue
            dronePC.currentSlot = destSlot;
            
            // Generate Reservation to block slot immediately
            GameObject reservation = new GameObject("Reservation");
            reservation.transform.SetParent(destSlot);
            reservation.transform.localPosition = Vector3.zero;

            // VISUAL: Just jump in place to show "Selected/Ready"
            // Do NOT move to slot.
            droneObj.transform.DOLocalJump(droneObj.transform.localPosition, 0.5f, 1, 0.3f);
            
            // Ensure correct scale just in case
            if (isNewSpawn)
            {
                // If it was a button, maybe scale it up to Drone size?
                // The earlier code set scale to 3.0f, let's respect that or Tween it.
                 droneObj.transform.DOScale(Vector3.one * 0.7f, 0.3f); // Match turret load scale target approximately
            }
            
            // Update Deck Visual if it was a spawn
            if (isNewSpawn)
            {
                // senderCube was already destroyed/removed above if isNewSpawn is true
                // So nothing to punch here.
            }
        }
        else
        {
            senderCube.transform.DOShakePosition(0.3f, 0.1f);
        }
    }

    IEnumerator MainTurretFireSequence(PlayerCube ammoPC, Color color, Transform slot)
    {
        isTurretBusy = true;
        GameObject ammoObj = ammoPC.gameObject;
        
        // Safety Checks
        if (mainTurretTransform == null || pointBottomLeft == null || pointBottomRight == null || pointTopRight == null || pointTopLeft == null) 
        { 
            Debug.LogError("Waypoints or Turret Missing! Cannot Execute Path.");
            ammoObj.transform.DOScale(0, 0.2f).OnComplete(() => Destroy(ammoObj));
            isTurretBusy = false; 
            yield break; 
        }

        // Capture Original State
        Vector3 originalTurretPos = mainTurretTransform.position;
        Quaternion originalTurretRot = mainTurretTransform.rotation;
        
        // LOCK Z: Use Turret's Original Depth
        float fixedZ = originalTurretPos.z;

        // Calculate Flattened Waypoints (Inspector Order: BL -> TL -> TR -> BR)
        Vector3 p1 = new Vector3(pointBottomLeft.position.x, pointBottomLeft.position.y, fixedZ);
        Vector3 p2 = new Vector3(pointTopLeft.position.x, pointTopLeft.position.y, fixedZ);
        Vector3 p3 = new Vector3(pointTopRight.position.x, pointTopRight.position.y, fixedZ);
        Vector3 p4 = new Vector3(pointBottomRight.position.x, pointBottomRight.position.y, fixedZ);

        // Calculate Center Position (Average of Waypoints)
        Vector3 centerPos = (p1 + p2 + p3 + p4) / 4f;

        // Pre-Calculate Look Rotations for each Waypoint (Look at Center)
        // We use Vector3.back as 'up' for 2D plane orientation
        Quaternion rot1 = Quaternion.LookRotation(centerPos - p1, Vector3.back);
        Quaternion rot2 = Quaternion.LookRotation(centerPos - p2, Vector3.back);
        Quaternion rot3 = Quaternion.LookRotation(centerPos - p3, Vector3.back);
        Quaternion rot4 = Quaternion.LookRotation(centerPos - p4, Vector3.back);

        // 1. LOAD AMMO
        ammoObj.transform.SetParent(mainTurretTransform);
        Transform res = slot.Find("Reservation");
        if(res != null) Destroy(res.gameObject);
        
        Sequence loadSeq = DOTween.Sequence();
        loadSeq.Append(ammoObj.transform.DOLocalJump(new Vector3(0, 0.8f, 0), 2.0f, 1, 0.4f)); 
        loadSeq.Join(ammoObj.transform.DOScale(Vector3.one * 0.7f, 0.4f)); 
        loadSeq.Join(ammoObj.transform.DOLocalRotate(Vector3.zero, 0.4f));
        yield return loadSeq.WaitForCompletion();

        // 2. MOVE TO START (Waypoint 4: Bottom Right - Requested Start)
        float legDuration = 0.8f;
        float turnDuration = 0.4f; 

        // Prepare Start: Look at P4
        mainTurretTransform.rotation = Quaternion.LookRotation(p4 - mainTurretTransform.position, Vector3.back);
        
        // Move to P4
        Sequence startSeq = DOTween.Sequence();
        startSeq.Append(mainTurretTransform.DOMove(p4, legDuration).SetEase(Ease.Linear));
        startSeq.Join(mainTurretTransform.DORotateQuaternion(rot4, legDuration).SetEase(Ease.Linear));
        yield return startSeq.WaitForCompletion();

        // 3. MOVEMENT LOOP: 4 -> 1 -> 2 -> 3 -> 4
        Sequence pathSeq = DOTween.Sequence();
        
        // Leg 1: P4 -> P1 (End facing Center from P1)
        pathSeq.Append(mainTurretTransform.DOMove(p1, legDuration).SetEase(Ease.Linear)); 
        pathSeq.Join(mainTurretTransform.DORotateQuaternion(rot1, legDuration).SetEase(Ease.Linear));
        
        // Leg 2: P1 -> P2 (End facing Center from P2)
        pathSeq.Append(mainTurretTransform.DOMove(p2, legDuration).SetEase(Ease.Linear));
        pathSeq.Join(mainTurretTransform.DORotateQuaternion(rot2, legDuration).SetEase(Ease.Linear)); 
        
        // Leg 3: P2 -> P3 (End facing Center from P3)
        pathSeq.Append(mainTurretTransform.DOMove(p3, legDuration).SetEase(Ease.Linear));
        pathSeq.Join(mainTurretTransform.DORotateQuaternion(rot3, legDuration).SetEase(Ease.Linear)); 
        
        // Leg 4: P3 -> P4 (End facing Center from P4)
        pathSeq.Append(mainTurretTransform.DOMove(p4, legDuration).SetEase(Ease.Linear));
        pathSeq.Join(mainTurretTransform.DORotateQuaternion(rot4, legDuration).SetEase(Ease.Linear)); 

        // REMOVED Infinite Loop to stop after one round
        // pathSeq.SetLoops(-1); 

        // 4. FIRING LOOP
        float fireRate = 0.35f; 
        float totalLoopDuration = legDuration * 4.0f; // P1->P2->P3->P4->P1
        float endTime = Time.time + totalLoopDuration;

        TMP_Text droneText = ammoObj.GetComponentInChildren<TMP_Text>();
        Text legacyText = ammoObj.GetComponentInChildren<Text>();
        
        // Immediate Update
        if(droneText != null) droneText.text = ammoPC.stackCount.ToString();
        if(legacyText != null) legacyText.text = ammoPC.stackCount.ToString();
        
        // Fire ONLY while moving (one loop) and while ammo exists
        while(ammoPC.stackCount > 0 && Time.time < endTime)
        {
             if (!activePixelCubes.ContainsKey(color) || activePixelCubes[color].Count == 0) break;

             var validCubes = activePixelCubes[color].Where(t => t != null).ToList();
             if(validCubes.Count == 0) break;

             GameObject target = validCubes
                    .OrderBy(t => Vector3.Distance(mainTurretTransform.position, t.transform.position)) 
                    .FirstOrDefault();
             
             if(target != null)
             {
                 activePixelCubes[color].Remove(target);
                 ammoPC.stackCount--;
                 
                 // Update Text
                 if(droneText != null) droneText.text = ammoPC.stackCount.ToString();
                 if(legacyText != null) legacyText.text = ammoPC.stackCount.ToString();

                 mainTurretTransform.DOPunchScale(Vector3.one * 0.1f, 0.05f, 2, 1);
                 
                 GameObject projectile = Instantiate(cubePrefab, mainTurretTransform.position, Quaternion.identity);
                 projectile.GetComponent<Renderer>().material.color = color;
                 projectile.transform.localScale = Vector3.one * 0.15f;
                 
                 projectile.transform.DOMove(target.transform.position, 0.1f).SetEase(Ease.Linear).OnComplete(() => {
                     if(target != null)
                     {
                        CreateExplosion(target.transform.position, color);
                        Destroy(target);
                        CheckWinCondition();
                     }
                     if(Camera.main != null) Camera.main.transform.DOShakePosition(0.05f, 0.05f, 5, 90, false, true);
                     Destroy(projectile);
                 });
             }
             
             yield return new WaitForSeconds(fireRate);
        }
        
        // 5. CLEANUP
        pathSeq.Kill(); 
        
        if (ammoPC.stackCount > 0)
        {
            // RECYCLE: Return to Slot
            Transform targetSlot = null;
            PlayerCube[] allAmmo = FindObjectsOfType<PlayerCube>();
            
            // Find empty slot
            for(int i=0; i < manualSlots.Count; i++)
            {
                Transform checkSlot = manualSlots[i];
                bool isOccupied = false;
                foreach(var pc in allAmmo) { if(pc != ammoPC && pc.currentSlot == checkSlot) { isOccupied = true; break; } }
                
                if(!isOccupied) { targetSlot = checkSlot; break; }
            }
            
            if(targetSlot == null) targetSlot = slot; // Fallback to origin

            if(targetSlot != null)
            {
                ammoObj.transform.SetParent(null);
                ammoPC.currentSlot = targetSlot;
                ammoPC.isReadyToFire = false; // Must click again
                
                GameObject reservation = new GameObject("Reservation");
                reservation.transform.SetParent(targetSlot);
                reservation.transform.localPosition = Vector3.zero;

                Vector3 recycleTargetPos = targetSlot.position + new Vector3(0, 0, -2f);
                ammoObj.transform.DOJump(recycleTargetPos, 2.0f, 1, 0.5f);
                ammoObj.transform.DORotate(Vector3.zero, 0.5f);
                
                // Restore Scale
                Vector3 targetScale = Vector3.one;
                if(dronePrefab != null) targetScale = dronePrefab.transform.localScale;
                ammoObj.transform.DOScale(targetScale, 0.5f); 
            }
            else
            {
                ammoObj.transform.DOScale(0, 0.2f).OnComplete(() => Destroy(ammoObj));
            }
        }
        else
        {
            // EMPTY: Destroy
            ammoPC.currentSlot = null;
            ammoObj.transform.DOScale(0, 0.2f).OnComplete(() => Destroy(ammoObj));
        }
        
        // Return Turret Home
        Sequence returnSeq = DOTween.Sequence();
        returnSeq.Append(mainTurretTransform.DOMove(originalTurretPos, 0.5f).SetEase(Ease.InOutQuad));
        returnSeq.Join(mainTurretTransform.DORotateQuaternion(originalTurretRot, 0.5f));
        yield return returnSeq.WaitForCompletion();

        isTurretBusy = false;
    }



    IEnumerator FireBurst(PlayerCube dronePC, Color color, int shotCount, float duration, Transform destSlot)
    {
        float interval = duration / (float)shotCount;
        GameObject drone = dronePC.gameObject;
        TMP_Text ammoTextTMP = drone.GetComponentInChildren<TMP_Text>();
        Text ammoTextLegacy = drone.GetComponentInChildren<Text>();

        for (int i = 0; i < shotCount; i++)
        {
            if (drone == null) yield break;
            
            // Check Ammo
            if(dronePC.stackCount <= 0) yield break;

            if (activePixelCubes.ContainsKey(color) && activePixelCubes[color].Count > 0)
            {
                 var validCubes = activePixelCubes[color].Where(t => t != null).ToList();
                 GameObject target = null;
                 
                 // CLOSEST TARGET (Sweep Effect)
                 if(validCubes.Count > 0)
                 {
                    target = validCubes
                        .OrderBy(t => Vector3.Distance(drone.transform.position, t.transform.position)) 
                        .FirstOrDefault();
                 }

                 if (target != null)
                 {
                     activePixelCubes[color].Remove(target);
                     
                     // Decrement Ammo & Update Text
                     dronePC.stackCount--;
                     if(ammoTextTMP != null) ammoTextTMP.text = dronePC.stackCount.ToString();
                     if(ammoTextLegacy != null) ammoTextLegacy.text = dronePC.stackCount.ToString();

                     // IMMEDIATE DESTROY IF EMPTY (User Request: "Don't go to slot")
                     if(dronePC.stackCount <= 0)
                     {
                         // Cleanup Reservation
                         if(destSlot != null)
                         {
                             Transform res = destSlot.Find("Reservation");
                             if(res != null) Destroy(res.gameObject);
                         }

                         // Kill Movement & Destroy
                         drone.transform.DOKill();
                         DOTween.Kill(drone);
                         Destroy(drone);
                         
                         // Visual FX?
                         // CreateExplosion(drone.transform.position, color); // Optional self-destruct FX
                         
                         yield break;
                     }

                     // RECOIL
                     drone.transform.DOPunchScale(Vector3.one * 0.2f, 0.1f, 5, 1);

                     // Shoot Projectile
                     GameObject projectile = Instantiate(cubePrefab, drone.transform.position, Quaternion.identity);
                     projectile.GetComponent<Renderer>().material.color = color;
                     projectile.transform.localScale = Vector3.one * 0.15f; 

                     projectile.transform.DOMove(target.transform.position, 0.12f).SetEase(Ease.Linear).OnComplete(() => { 
                        if(target != null) 
                        { 
                            CreateExplosion(target.transform.position, color);
                            Destroy(target); 
                            CheckWinCondition();
                        }
                        if(Camera.main != null) Camera.main.transform.DOShakePosition(0.05f, 0.05f, 5, 90, false, true);
                        Destroy(projectile);
                     });
                 }
            }
            yield return new WaitForSeconds(interval); 
        }
    }

    void CreateExplosion(Vector3 pos, Color color)
    {
        int debrisCount = 5;
        for (int i = 0; i < debrisCount; i++)
        {
            GameObject debris = GameObject.CreatePrimitive(PrimitiveType.Cube);
            debris.transform.position = pos + Random.insideUnitSphere * 0.2f;
            debris.transform.localScale = Vector3.one * Random.Range(0.1f, 0.2f);
            debris.GetComponent<Renderer>().material.color = color;
            
            // Random explosion direction
            Vector3 randomDir = Random.insideUnitSphere.normalized * Random.Range(0.5f, 1.5f);
            
            // Allow physics-like movement with Tween
            debris.transform.DOMove(debris.transform.position + randomDir, 0.4f).SetEase(Ease.OutQuad);
            debris.transform.DORotate(Random.insideUnitSphere * 360, 0.4f, RotateMode.FastBeyond360);
            debris.transform.DOScale(0, 0.4f).OnComplete(()=> {
                if(debris != null) Destroy(debris);
            });
        }
    }




    private void GenerateManualPattern_OLD()
    {
        // ... (Existing clearing logic) ...
        foreach(var list in activePixelCubes.Values) { foreach(var obj in list) if(obj) Destroy(obj); }
        activePixelCubes.Clear();
        
        // Initialize occupied array based on manual slots count
        if (manualSlots != null) 
        {
            maxSlots = manualSlots.Count;
            slotOccupied = new bool[maxSlots];
        }

        // DEFINING THE PATTERN MANUALLY (The Heart)
        // ... (Pattern generation code remains same) ...
        // FIXED: All rows must have EQUAL length (13 chars to be safe)
        string[] rows = new string[] {
            ".............",
            "...KK...KK...",
            "..KRRK.KRRK..",
            ".KRRRRKRRRRK.",
            ".KRRRRRRRRRK.",
            "..KRRRRRRRK..",
            "...KRRRRRK...",
            "....KRRRK....",
            ".....KRK.....",
            "......K......"
        };
        
        // Settings for this generation
        int width = rows[0].Length;
        int height = rows.Length;
        
        // Adjust sizing
        float calculatedSpacing = 0.2f; 
        if (maxPatternWidth > 0) calculatedSpacing = (maxPatternWidth / width) * 0.8f;

        Vector3 cubeScale = Vector3.one * calculatedSpacing * 0.90f; 
 
        Vector3 centerPos = (centerPoint != null) ? centerPoint.position : transform.position;
        Vector3 rightDir = (centerPoint != null) ? centerPoint.right : Vector3.right;
        Vector3 upDir = (centerPoint != null) ? centerPoint.up : Vector3.up;     
        Vector3 forwardDir = (centerPoint != null) ? centerPoint.forward : Vector3.forward; 

        float widthOffset = (width - 1) * calculatedSpacing / 2f;
        float heightOffsetMap = (height - 1) * calculatedSpacing / 2f;

        List<Color> colorDiscoveryOrder = new List<Color>();
        HashSet<Color> discoveredColors = new HashSet<Color>();

        for (int y = 0; y < height; y++)
        {
            string row = rows[height - 1 - y]; 
            for (int x = 0; x < width; x++)
            {
                char c = row[x];
                Color pixelColor = Color.clear;
                
                if (c == 'R') pixelColor = Color.red;
                else if (c == 'K') pixelColor = Color.black;
                else if (c == '.' || c == ' ') pixelColor = Color.clear;

                if (pixelColor == Color.clear) continue;

                float xPos = (x * calculatedSpacing) - widthOffset;
                float yPos = (y * calculatedSpacing) - heightOffsetMap;

                Vector3 basePos = centerPos + (rightDir * xPos) + (upDir * yPos) + (forwardDir * heightOffset);
                Vector3 nudge = (rightDir * positionOffset.x) + (upDir * positionOffset.y) + (forwardDir * positionOffset.z);
                Vector3 finalPos = basePos + nudge;
                
                GameObject pixelCube = Instantiate(cubePrefab, finalPos, Quaternion.identity);
                pixelCube.GetComponent<Renderer>().material.color = pixelColor;
                pixelCube.transform.localScale = cubeScale;
                
                // Hide Text on Pattern Cubes
                Text textComp = pixelCube.GetComponentInChildren<Text>();
                if (textComp != null) textComp.enabled = false; // Disable text for pattern
                
                if (!activePixelCubes.ContainsKey(pixelColor)) activePixelCubes[pixelColor] = new List<GameObject>();
                activePixelCubes[pixelColor].Add(pixelCube);

                if (!discoveredColors.Contains(pixelColor))
                {
                    discoveredColors.Add(pixelColor);
                    colorDiscoveryOrder.Add(pixelColor);
                }
            }
        }

        isGameActive = true;
        
        // NO AUTO SLOT GENERATION

        // --- Generate Stacks Logic ---
        // Legacy testing method - update to use DeckManager types if needed, or remove
        List<DeckManager.StackData> deckStacks = new List<DeckManager.StackData>();
        
        // ... (rest of logic if needed)
        foreach (Color col in colorDiscoveryOrder)
        {
            // PROPORTIONAL AMMO: Split into stacks of max 20
            int totalAmmo = activePixelCubes.ContainsKey(col) ? activePixelCubes[col].Count : 10;
            int maxPerStack = 20;

            while (totalAmmo > 0)
            {
                int currentStackSize = Mathf.Min(totalAmmo, maxPerStack);
                // NEW: Use DeckManager type
                deckStacks.Add(new DeckManager.StackData { color = col, count = currentStackSize });
                totalAmmo -= currentStackSize;
            }
        }
        
        // GeneratePlayerDeck(deckStacks, cubeScale); // Disabled in OLD
        Debug.Log("Game Started! Pattern: Manual Heart");
    }

    /* LEGACY - REMOVING TO FIX COMPILE ERRORS
    private struct StackData_OLD
    {
        public Color color;
        public int count;
    }
    */

    // void GeneratePlayerDeck_OLD(List<StackData_OLD> stacks, Vector3 baseScale)
    /* 
    {
        if (deckCenterPoint == null) return;

        PlayerCube[] oldButtons = deckCenterPoint.GetComponentsInChildren<PlayerCube>();
        foreach (var btn in oldButtons) Destroy(btn.gameObject);

        int count = stacks.Count;
        if (count == 0) return;

        // ... (Legacy code removed)
    }
    */



    private Color GetHypercasualColor(char code)
    {
        switch (code)
        {
            case 'R': return new Color32(255, 77, 77, 255);   // Coral Red (Vibrant)
            case 'K': return new Color32(44, 62, 80, 255);    // Dark Slate (Soft Black)
            case 'Y': return new Color32(255, 206, 84, 255);  // Sunflower Yellow
            case 'B': return new Color32(74, 144, 226, 255);  // Sky Blue
            case 'G': return new Color32(46, 204, 113, 255);  // Emerald Green
            default: return Color.white;
        }
    }

    private void SetupHypercasualVisuals()
    {
        // 1. Camera Background (Clean, Modern Grey-Blue)
        if(Camera.main != null)
        {
            Camera.main.clearFlags = CameraClearFlags.SolidColor;
            Camera.main.backgroundColor = new Color32(240, 248, 255, 255); // Alice Blue
        }

        // 2. Lighting (Bright & Flat)
        Light mainLight = FindObjectOfType<Light>();
        if(mainLight != null && mainLight.type == LightType.Directional)
        {
            mainLight.color = Color.white;
            mainLight.intensity = 1.3f;
            // Lower angle for longer shadows (aesthetic) OR High angle for flat look. 
            // Let's go with classic 50/45
            mainLight.transform.rotation = Quaternion.Euler(50, -45, 0);
            mainLight.shadowStrength = 0.3f; // Very soft shadows
            mainLight.shadows = LightShadows.Soft;
        }
        
        // 3. Ambient Lighting (Bright, no pure black shadows)
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color32(180, 180, 180, 255); // High ambient light for "Low Poly" flat look

        // 4. Fog (Subtle depth)
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogStartDistance = 15f;
        RenderSettings.fogEndDistance = 40f;
        RenderSettings.fogColor = new Color32(240, 248, 255, 255); // Match BG
    }

    [Header("UI Settings")]
    public GameObject winUIObject; // Assign in Inspector

    void CheckWinCondition()
    {
        int totalRemaining = 0;
        System.Text.StringBuilder debugMsg = new System.Text.StringBuilder("WIN CHECK: ");
        bool hasRemaining = false;

        foreach (var kvp in activePixelCubes)
        {
            // Count only valid objects
            int count = 0;
            for(int i = kvp.Value.Count - 1; i >= 0; i--)
            {
                if (kvp.Value[i] != null) count++;
                else kvp.Value.RemoveAt(i); // Cleanup nulls while we are here
            }

            if (count > 0)
            {
                debugMsg.Append($"<color=#{ColorUtility.ToHtmlStringRGB(kvp.Key)}>{kvp.Key}: {count}</color> | ");
                totalRemaining += count;
                hasRemaining = true;
            }
        }

        if(hasRemaining)
        {
             Debug.Log($"{debugMsg} TOTAL LEFT: {totalRemaining}");
        }

        if (totalRemaining == 0 && isGameActive)
        {
            Debug.Log("<color=green><b>LEVEL COMPLETED! ALL BLOCKS DESTROYED.</b></color>");
            isGameActive = false;
            
            if(winUIObject != null) 
            {
                Debug.Log("Activating Win UI Object...");
                winUIObject.SetActive(true);
            }
            else
            {
                Debug.LogWarning("Win UI Object is NOT assigned in Inspector!");
            }
        }
    }

    // REMOVED: CreateWinUI() - User will create manually
    // Call this from the Button in Inspector
    public void NextLevel()
    {
        LoadLevel(currentLevelIndex + 1);
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
    // --- EDITOR TOOLS ---
#if UNITY_EDITOR
    [ContextMenu("Generate Simple Patterns")]
    public void Editor_GeneratePatterns()
    {
        string dir = "Assets/PixelPatterns";
        if (!System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);

        // Helper: String Map to Texture
        // Codes: R=Red, B=Blue, Y=Yellow, G=Green, W=White, .=Clear
        void CreateAndSave(string name, string[] rows)
        {
            int h = rows.Length;
            int w = rows[0].Length;
            Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Point;
            tex.wrapMode = TextureWrapMode.Clamp;
            
            // Unity textures start bottom-left, so we iterate rows inversely to match visual top-down
            for (int y = 0; y < h; y++)
            {
                string row = rows[h - 1 - y];
                for (int x = 0; x < w; x++)
                {
                    char c = row[x];
                    Color col = Color.clear;
                    if (c == 'R') col = Color.red;
                    else if (c == 'B') col = Color.blue;
                    else if (c == 'Y') col = Color.yellow;
                    else if (c == 'G') col = Color.green;
                    else if (c == 'W') col = Color.white;
                    else if (c == 'K') col = Color.black;
                    
                    tex.SetPixel(x, y, col);
                }
            }
            tex.Apply();
            string path = $"{dir}/{name}.png";
            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());

            // Force Re-import setting to Point (IMPORTANT)
            UnityEditor.AssetDatabase.Refresh();
            UnityEditor.TextureImporter importer = UnityEditor.AssetImporter.GetAtPath(path) as UnityEditor.TextureImporter;
            if(importer != null)
            {
                importer.textureType = UnityEditor.TextureImporterType.Default;
                importer.filterMode = FilterMode.Point;
                importer.textureCompression = UnityEditor.TextureImporterCompression.Uncompressed;
                importer.npotScale = UnityEditor.TextureImporterNPOTScale.None;
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = true;
                importer.SaveAndReimport();
            }
        }

        // 1. HEART (Red)
        CreateAndSave("pixel_heart", new string[] {
            "......",
            ".R..R.",
            "RRRRRR",
            "RRRRRR",
            ".RRRR.",
            "..RR..",
            "......"
        });

        // 2. SMILEY (Yellow + Black)
        CreateAndSave("pixel_smile", new string[] {
            ".YYYY.",
            "YKBKY.",
            "YYYYYY",
            "YKYYKY",
            "Y.KK.Y",
            ".YYYY."
        });

        // 3. SWORD (Blue + White/Grey)
        CreateAndSave("pixel_sword", new string[] {
            "......B.",
            ".....BBB",
            "....B.B.",
            "...B....",
            "..B.....",
            ".KK.....",
            "K..K....",
            "........"
        });

        // 4. CUSTOM HEART (Strictly Red & Black)
        CreateAndSave("pixel_heart_custom", new string[] {
            "..........",
            "..KK...KK.",
            ".KRRK.KRRK",
            "KRRRRKRRRRK",
            "KRRRRRRRRRK",
            ".KRRRRRRRK.",
            "..KRRRRRK..",
            "...KRRRK...",
            "....KRK....",
            ".....K....."
        });

        UnityEditor.AssetDatabase.Refresh();

        // Auto Assign
        patternTextures.Clear();
        // patternTextures.Add(UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>($"{dir}/pixel_heart.png")); // Old heart
        patternTextures.Add(UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>($"{dir}/pixel_heart_custom.png")); // New Custom Heart
        patternTextures.Add(UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>($"{dir}/pixel_smile.png"));
        patternTextures.Add(UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>($"{dir}/pixel_flower_4c.png"));
        
        Debug.Log($"<color=green>SUCCESS!</color> Patterns updated in {dir}.");
    }
#endif
}
