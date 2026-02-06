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
    public float maxPatternWidth = 10f;
    public float maxPatternHeight = 10f;

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

    // Runtime state
    private Dictionary<Color, List<GameObject>> activePixelCubes = new Dictionary<Color, List<GameObject>>();
    private List<PlayerCube> activeDeckButtons = new List<PlayerCube>(); 
    private bool isGameActive = false;
    private bool isTurretBusy = false; // TURRET STATE
    private Vector3 initialTurretLocalEuler; // Stores scene start LOCAL rotation

    // Level System
    private int currentLevelIndex = 0;

    private List<string[]> levelPatterns = new List<string[]>();
    
    // Main Update Loop for Queue
    private void Update()
    {
        if(!isGameActive || isTurretBusy || mainTurretTransform == null) return;
        
        ProcessTurretQueue();
    }

    private void ProcessTurretQueue()
    {
        // Scan slots for Ready Ammo
        // Since drones are no longer children of slots (to avoid scale inheritance),
        // we look for PlayerCubes via their currentSlot reference
        
        // Find all active PlayerCubes with a currentSlot set
        PlayerCube[] allAmmo = FindObjectsOfType<PlayerCube>();
        
        for (int i = 0; i < manualSlots.Count; i++)
        {
             Transform slot = manualSlots[i];
             
             // Find ammo that belongs to this slot
             PlayerCube ammo = null;
             foreach(var pc in allAmmo)
             {
                 if(pc.currentSlot == slot)
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
        // Find all active Drones and force their text to face camera
        // Doing this in Update is easier than parenting tricks
        if(Camera.main == null) return;
        
        var texts = FindObjectsOfType<TMP_Text>();
        foreach(var t in texts)
        {
            if(t.transform.parent != null && t.transform.parent.name.StartsWith("Drone"))
            {
                // Force rotation to match camera (Billboard)
                t.transform.rotation = Camera.main.transform.rotation;
            }
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
        levelPatterns.Clear();
        
        // Level 1: HEART (Easy)
        levelPatterns.Add(new string[] {
            ".............",
            "...R.....R...",
            "..RRR...RRR..",
            ".RRRRR.RRRRR.",
            ".RRRRRRRRRRR.",
            "..RRRRRRRRR..",
            "...RRRRRRR...",
            "....RRRRR....",
            ".....RRR.....",
            "......R......"
        });

        // Level 2: SMILEY (Yellow & Black)
        levelPatterns.Add(new string[] {
            ".............",
            "...YYYYYYY...",
            "..YKKYYYKKY..",
            ".YYYYYYYYYYY.",
            ".YKYKYKYKYKY.",
            ".YYYYYYYYYYY.",
            "..YKKKKKKKY..",
            "...YYYYYYY...",
            "............."
        });
        
        // Level 3: SWORD (Blue & Gray/Black)
        levelPatterns.Add(new string[] {
            "......B......",
            "......B......",
            ".....BBB.....",
            ".....B.B.....",
            "....B...B....",
            "....B...B....",
            "....B...B....",
            "...KKKKKKK...",
            "......K......",
            "......K......"
        });

        // Level 4: COMPLEX HEART (Original)
        levelPatterns.Add(new string[] {
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
        });
    }

    public void LoadLevel(int index)
    {
        if(index < 0 || index >= levelPatterns.Count) index = 0; // Loop back
        currentLevelIndex = index;

        GeneratePatternFromMap(levelPatterns[currentLevelIndex]);
        
        // Hide Manual UI
        if(winUIObject != null) winUIObject.SetActive(false);
        
        Debug.Log($"Level {index + 1} Loaded!");
    }


    public void GeneratePatternFromMap(string[] rows)
    {
        // Cleanup existing patterns
        foreach(var list in activePixelCubes.Values) { foreach(var obj in list) if(obj) Destroy(obj); }
        activePixelCubes.Clear();
        
        // Cleanup Deck Visuals too (Important for new level colors)
        if (deckCenterPoint != null)
        {
             foreach(Transform child in deckCenterPoint) Destroy(child.gameObject);
        }
        activeDeckButtons.Clear();
        
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
                
                if (c == 'R') pixelColor = GetHypercasualColor('R'); // Coral Red
                else if (c == 'K') pixelColor = GetHypercasualColor('K'); // Dark Slate (Not pure black)
                else if (c == 'Y') pixelColor = GetHypercasualColor('Y'); // Vibrant Yellow
                else if (c == 'B') pixelColor = GetHypercasualColor('B'); // Sky Blue
                else if (c == 'G') pixelColor = GetHypercasualColor('G'); // Lime Green
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
                if (textComp != null) textComp.enabled = false; 
                
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
        
        // --- Generate Stacks Logic ---
        List<StackData> deckStacks = new List<StackData>();
        foreach (Color col in colorDiscoveryOrder)
        {
            // PROPORTIONAL AMMO: 20-20-10-REMAINDER Logic
            int totalAmmo = activePixelCubes.ContainsKey(col) ? activePixelCubes[col].Count : 10;
            
            // 1. Fill 20s
            while(totalAmmo >= 20)
            {
                deckStacks.Add(new StackData { color = col, count = 20 });
                totalAmmo -= 20;
            }

            // 2. If remainder > 10, make a 10 stack
            if(totalAmmo > 10)
            {
                deckStacks.Add(new StackData { color = col, count = 10 });
                totalAmmo -= 10;
            }

            // 3. Add whatever is left
            if(totalAmmo > 0)
            {
                deckStacks.Add(new StackData { color = col, count = totalAmmo });
            }
        }
        
        GeneratePlayerDeck(deckStacks, cubeScale);
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
        activeDeckButtons.Clear(); 

        int count = stacks.Count;
        if (count == 0) return;

        // Deck Sizing Logic - Simplified Grid (TIGHT FILTER)
        // Goal: Fit inside the "Red Box" area below slots
        int cols = 3; 
        float xSpacing = 1.3f; // Reduced from 2.0f to fit screen width
        float ySpacing = 1.4f; // Reduced vertical spacing
        
        // Calculate total width of one row to center it
        // If col count is less than max cols, center based on actual count? No, keep it stable.
        float totalRowWidth = (Mathf.Min(count, cols) - 1) * xSpacing;
        float startX = -totalRowWidth / 2f; 

        // Vertical Start Offset (Adjust this to move the WHOLE group Up/Down)
        // 0 is the pivot of DeckCenter. If DeckCenter is too low, we might need positive Y.
        // Looking at images, they are too low. Let's add a positive Y offset base.
        float startY = 0.5f; 

        for (int i = 0; i < count; i++)
        {
            StackData stack = stacks[i];

            int row = i / cols;
            int col = i % cols;

            // Alignment: Start from Left (+ col * spacing)
            // If it's the 2nd row, and it only has 1 item, should it be centered? 
            // For now, let's keep left-aligned within the centered block for consistency.
            
            // Actually, let's force center align for the last row if it's incomplete?
            // That looks nicer.
            int itemsInThisRow = cols;
            // If it's the last row
            if (row == (count - 1) / cols) 
            {
                itemsInThisRow = count % cols;
                if (itemsInThisRow == 0) itemsInThisRow = cols;
            }
            
            float rowWidth = (itemsInThisRow - 1) * xSpacing;
            float rowStartX = -rowWidth / 2f; // Center this specific row

            Vector3 localPos = new Vector3(
                rowStartX + (col * xSpacing), 
                startY - (row * ySpacing), 
                0
            );

            // Use Instantiate with parent directly to keep hierarchy clean
            GameObject playerDeckCube = Instantiate(dronePrefab != null ? dronePrefab : cubePrefab, deckCenterPoint);
            playerDeckCube.transform.localPosition = localPos + deckPositionOffset; 
            playerDeckCube.transform.localRotation = Quaternion.Euler(-90, 0, 0); // Face camera
            
            // Adjusted Scale removed to respect Prefab scale
            // playerDeckCube.transform.localScale = Vector3.one * 1.8f; 
            
            playerDeckCube.name = $"PlayerBtn_{i}_{stack.count}";

            // Safely get or add PlayerCube
            PlayerCube pc = playerDeckCube.GetComponent<PlayerCube>();
            if (pc == null) pc = playerDeckCube.AddComponent<PlayerCube>();
            
            pc.Initialize(stack.color, this);
            pc.stackCount = stack.count; 
            
            // Update Text 
            Text stackText = playerDeckCube.GetComponentInChildren<Text>();
            if (stackText != null) stackText.text = stack.count.ToString(); 
            
            TMP_Text stackTextTMP = playerDeckCube.GetComponentInChildren<TMP_Text>();
            if (stackTextTMP != null) stackTextTMP.text = stack.count.ToString(); 
            
            // Colorizing Logic
            if (dronePrefab != null)
            {
                 Renderer[] rends = playerDeckCube.GetComponentsInChildren<Renderer>();
                 foreach(var r in rends)
                 {
                    if(r.name.Contains("Propeller") || r.gameObject.GetComponent<TMP_Text>() != null) continue;
                    r.material.color = stack.color;
                 }
            } 
            
            activeDeckButtons.Add(pc); 
        }
        
        // Initial Visual Update for the Queue System
        UpdateDeckStates();
    }

    // New method to rearrange deck buttons
    private void RearrangeDeck()
    {
        if (deckCenterPoint == null || activeDeckButtons.Count == 0) return;

        float buttonSize = 0.6f; 
        float itemSpacing = buttonSize * 1.5f; 
 
        Vector3 rightDir = deckCenterPoint.right;
        Vector3 upDir = deckCenterPoint.up; 
        Vector3 forwardDir = deckCenterPoint.forward;

        Vector3 deckOrigin = deckCenterPoint.position 
                           + (rightDir * deckPositionOffset.x) 
                           + (upDir * deckPositionOffset.y) 
                           + (forwardDir * deckPositionOffset.z);

        Vector3 startPos = deckOrigin 
                         - (rightDir * (maxDeckWidth * 0.5f - buttonSize * 0.5f)) 
                         + (upDir * (maxDeckHeight * 0.5f - buttonSize * 0.5f));

        int col = 0;
        int row = 0;

        for (int i = 0; i < activeDeckButtons.Count; i++)
        {
            PlayerCube btn = activeDeckButtons[i];
            if (btn == null) continue;

            float xDist = col * itemSpacing;
            float yDist = row * itemSpacing;

            if (xDist + buttonSize > maxDeckWidth + 0.1f) 
            {
                col = 0;
                row++;
                xDist = 0;
                yDist = row * itemSpacing;
            }

            Vector3 targetPos = startPos + (rightDir * xDist) - (upDir * yDist);
            
            // Move smoothly to new position
            btn.transform.DOMove(targetPos, 0.3f).SetEase(Ease.OutQuad);
            
            col++;
        }

        UpdateDeckStates();
    }

    // Controls the "Deck Queue" Visuals
    private void UpdateDeckStates()
    {
        for (int i = 0; i < activeDeckButtons.Count; i++)
        {
            PlayerCube btn = activeDeckButtons[i];
            if (btn == null) continue;

            bool isUnlocked = i < visibleDeckCount;
            
            // Visual Feedback
            Renderer[] rends = btn.GetComponentsInChildren<Renderer>();
            foreach(var r in rends)
            {
               if(r.name.Contains("Propeller") || r.gameObject.GetComponent<TMP_Text>() != null) continue;
               
               // If Unlocked -> Show Real Color. If Locked -> Show Grey/Shadow
               r.material.color = isUnlocked ? btn.cubeColor : new Color32(100, 100, 100, 255);
            }
        }
    }

    // Called by PlayerCube when clicked
    public void OnPlayerCubeClicked(PlayerCube senderCube)
    {
        if (!isGameActive) return;
        
        // --- DECK QUEUE CHECK ---
        // If this cube is in the deck (not a slot re-launch)
        if (activeDeckButtons.Contains(senderCube))
        {
            int index = activeDeckButtons.IndexOf(senderCube);
            // If the item is beyond the visible/allowed count, reject the click
            if (index >= visibleDeckCount)
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
            // Debug.Log($"Clicked Color: {color}, Targets Found: {totalTargets}"); // Debug

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
                
                // User clicked an item already in slot -> Force Fire
                senderCube.isReadyToFire = true;
            }
            else
            {
                // Spawning from Deck
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

                if (targetSlotIndex == -1) { Debug.Log("Slots Full!"); return; } // Slots full

                // Spawn Drone Logic (Moved outside the loop)
            // The `isNewSpawn` flag is determined here based on whether it's from the deck or a slot.
            // If from deck, it's a new spawn. If from slot, it's a re-launch.
            // The `isNewSpawn` variable is initialized to `false` at the start of the method.

            if (isNewSpawn && dronePrefab != null)
            {
                 droneObj = Instantiate(dronePrefab, senderCube.transform.position, Quaternion.identity);
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
            }

            if (isNewSpawn)
            {
                // USER REQUEST: Destroy the deck button immediately so it cannot be clicked again
                if (activeDeckButtons.Contains(senderCube)) activeDeckButtons.Remove(senderCube);
                Destroy(senderCube.gameObject);
                
                // Rearrange remaining buttons
                RearrangeDeck();
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
            
            // 1. Move Ammo to Slot (Reload Phase)
            // DO NOT parent to slot (slot may have non-uniform scale causing distortion)
            // Instead, keep as root and move to slot's WORLD position
            droneObj.transform.SetParent(null); // Keep as root object
            
            // Store slot reference on PlayerCube for later lookup
            dronePC.currentSlot = destSlot;
            
            // Generate Reservation to block slot immediately
            GameObject reservation = new GameObject("Reservation");
            reservation.transform.SetParent(destSlot);
            reservation.transform.localPosition = Vector3.zero;

            Sequence ammoSeq = DOTween.Sequence();
            
            // Jump to Slot's WORLD position (not local) AND Shrink
            // Add Z offset of -2 so drone sits in front of slot
            Vector3 slotTargetPos = destSlot.position + new Vector3(0, 0, -2f);
            ammoSeq.Append(droneObj.transform.DOJump(slotTargetPos, 2.0f, 1, 0.6f));
            ammoSeq.Join(droneObj.transform.DORotate(Vector3.zero, 0.6f));
            ammoSeq.Join(droneObj.transform.DOScale(Vector3.one * 0.01f, 0.6f)); // Shrink to near-zero
            
            // NO AUTO FIRE - The Update loop will pick it up when it lands/settles
            // But we need to make sure the loop doesn't pick it up WHILE it is jumping?
            // The loop checks "ammo.transform.parent == slot". 
            // We set parent immediately above. 
            // Issue: Loop might grab it mid-air.
            // Fix: We can use a flag on the ammo or just wait. 
            // Or, let the Queue pickup logic be slightly tolerant. 
            // Actually, visually picking it up mid-air is weird.
            // Let's add a temporary component or tag "Moving"?
            // Simpler: Don't destroy deck button until landed?
            
            // Let's add a "IsReady" flag to PlayerCube? Or just let the sequence verify.
            
            // Update Deck Visual if it was a spawn
            if (isNewSpawn)
            {
                senderCube.transform.DOPunchScale(Vector3.one * 0.1f, 0.15f);
                if (senderCube.stackCount <= 0) Destroy(senderCube.gameObject);
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
        
        if (mainTurretTransform == null) { isTurretBusy = false; yield break; }

        // A. Move Ammo from Slot -> Turret (Spinning!)
        ammoObj.transform.SetParent(mainTurretTransform);
        
        // Visual Flourish: Spin and Move
        float loadDuration = 0.5f;
        ammoObj.transform.DOLocalMove(new Vector3(0, 1.5f, 0), loadDuration).SetEase(Ease.InOutBack); // Move above turret
        ammoObj.transform.DOLocalRotate(new Vector3(0, 360, 0), loadDuration, RotateMode.FastBeyond360);
        // Removed Scale modification to keep prefab scale

        yield return new WaitForSeconds(loadDuration);

        // B. Fire Loop
        // We will shoot one by one
        int shotsFiredThisTurn = 0;
        int maxShotsPerTurn = 10;

        while(ammoPC.stackCount > 0 && shotsFiredThisTurn < maxShotsPerTurn)
        {
             if (!activePixelCubes.ContainsKey(color) || activePixelCubes[color].Count == 0) break;

             var validCubes = activePixelCubes[color].Where(t => t != null).ToList();
             if(validCubes.Count == 0) break;

             GameObject target = validCubes
                    .OrderBy(t => Vector3.Distance(mainTurretTransform.position, t.transform.position)) 
                    .FirstOrDefault();
             
             if(target == null) break;

             // 1. Rotate Turret to Target (Local X-Axis Only, Fixed Local Y/Z)
             Vector3 dir = target.transform.position - mainTurretTransform.position;

             if (dir != Vector3.zero)
             {
                 // Calculate World Look
                 Quaternion worldLook = Quaternion.LookRotation(dir);
                 
                 // Convert to Local Space
                 Quaternion targetLocalRot = (mainTurretTransform.parent != null) 
                     ? Quaternion.Inverse(mainTurretTransform.parent.rotation) * worldLook 
                     : worldLook;
                 
                 // Construct Final Local Euler: Keep Initial Y & Z, Use New X (Pitch) with Offset
                 // Note: Euler X behaves typically as Pitch.
                 Vector3 finalEuler = new Vector3(targetLocalRot.eulerAngles.x + turretRotationOffset, initialTurretLocalEuler.y, initialTurretLocalEuler.z);
                 
                 // Tween Local Rotation
                 mainTurretTransform.DOLocalRotate(finalEuler, 0.15f);
             }
             yield return new WaitForSeconds(0.15f);

             // 2. Fire Projectile
             activePixelCubes[color].Remove(target);
             ammoPC.stackCount--;
             shotsFiredThisTurn++;
             
             // Recoil
             mainTurretTransform.DOPunchScale(Vector3.one * 0.1f, 0.1f, 5, 1);

             GameObject projectile = Instantiate(ammoPrefab != null ? ammoPrefab : cubePrefab, mainTurretTransform.position + mainTurretTransform.forward * 1.5f, Quaternion.identity);
             projectile.GetComponent<Renderer>().material.color = color;
             // Set projectile scale to 0.1 as requested
             projectile.transform.localScale = Vector3.one * 0.1f;
             
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

             yield return new WaitForSeconds(0.1f); // Fire Rate
        }
        
        // C. Cleanup / Recycle
        
        // Cleanup Slot Reservation (Always clear previous slot reservation first)
        Transform res = slot.Find("Reservation");
        if(res != null) {
            res.SetParent(null); // Detach to ensure childCount updates immediately if possible
            Destroy(res.gameObject);
        }

        if (ammoPC.stackCount > 0)
        {
            // RECYCLE: Find first empty slot
            Transform targetSlot = null;
            
            // Find all PlayerCubes to check which slots are occupied
            PlayerCube[] allAmmo = FindObjectsOfType<PlayerCube>();
            
            // First try to find ANY empty slot (check via currentSlot references, not childCount)
            for(int i=0; i < manualSlots.Count; i++)
            {
                Transform checkSlot = manualSlots[i];
                bool isOccupied = false;
                
                // Check if any ammo claims this slot
                foreach(var pc in allAmmo)
                {
                    if(pc != ammoPC && pc.currentSlot == checkSlot) // Ignore self
                    {
                        isOccupied = true;
                        break;
                    }
                }
                
                if(!isOccupied)
                {
                    targetSlot = checkSlot;
                    break;
                }
            }
            
            // Fallback: Use the origin slot. We just emptied it (by moving ammo to turret and destroying reservation).
            if(targetSlot == null) targetSlot = slot;

            if(targetSlot != null)
            {
                // Move back to slot (WITHOUT parenting to avoid scale inheritance)
                ammoObj.transform.SetParent(null); // Keep as root
                
                // Update slot reference
                ammoPC.currentSlot = targetSlot;
                
                // IMPORTANT: Disable Auto-Fire for Recycled Ammo
                // User must click again to fire.
                ammoPC.isReadyToFire = false;
                
                // Create new Reservation
                GameObject reservation = new GameObject("Reservation");
                reservation.transform.SetParent(targetSlot);
                reservation.transform.localPosition = Vector3.zero;

                // Animate back to slot's WORLD position (with Z offset)
                Vector3 recycleTargetPos = targetSlot.position + new Vector3(0, 0, -2f);
                ammoObj.transform.DOJump(recycleTargetPos, 2.0f, 1, 0.5f);
                ammoObj.transform.DORotate(Vector3.zero, 0.5f);
                
                // Restore IDLE scale (Prefab Scale)
                Vector3 targetScale = Vector3.one;
                if(dronePrefab != null) targetScale = dronePrefab.transform.localScale;
                
                ammoObj.transform.DOScale(targetScale, 0.5f); 
            }
            else
            {
                // Should never happen with fallback
                Debug.LogWarning("Critical: No slots to recycle ammo! Destroying.");
                ammoObj.transform.DOScale(0, 0.2f).OnComplete(() => Destroy(ammoObj));
            }
        }
        else
        {
            // EMPTY: Destroy
            // Clear slot reference so it's available for others
            ammoPC.currentSlot = null;
            ammoObj.transform.DOScale(0, 0.2f).OnComplete(() => Destroy(ammoObj));
        }
        
        // Reset Turret Rotation to Initial (Idle)
        mainTurretTransform.DOLocalRotate(initialTurretLocalEuler, 0.5f);

        // Wait for jump/rotate animations (0.5f) + buffer to finish
        yield return new WaitForSeconds(0.6f);
        isTurretBusy = false; // Next!
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
        List<StackData> deckStacks = new List<StackData>();
        foreach (Color col in colorDiscoveryOrder)
        {
            // PROPORTIONAL AMMO: Split into stacks of max 20
            int totalAmmo = activePixelCubes.ContainsKey(col) ? activePixelCubes[col].Count : 10;
            int maxPerStack = 20;

            while (totalAmmo > 0)
            {
                int currentStackSize = Mathf.Min(totalAmmo, maxPerStack);
                deckStacks.Add(new StackData { color = col, count = currentStackSize });
                totalAmmo -= currentStackSize;
            }
        }
        
        // GeneratePlayerDeck(deckStacks, cubeScale); // Disabled in OLD
        Debug.Log("Game Started! Pattern: Manual Heart");
    }

    private struct StackData_OLD
    {
        public Color color;
        public int count;
    }

    void GeneratePlayerDeck_OLD(List<StackData_OLD> stacks, Vector3 baseScale)
    {
        if (deckCenterPoint == null) return;

        PlayerCube[] oldButtons = deckCenterPoint.GetComponentsInChildren<PlayerCube>();
        foreach (var btn in oldButtons) Destroy(btn.gameObject);

        int count = stacks.Count;
        if (count == 0) return;

        // Deck Sizing Logic - Fixed Size as requested
        float buttonSize = 0.6f; 
        
        float itemSpacing = buttonSize * 1.5f; // More spacing since we have a fixed small size
 
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
            StackData_OLD stack = stacks[i];

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
            
            GameObject playerDeckCube = Instantiate(cubePrefab, spawnPos, Quaternion.identity, deckCenterPoint);
            playerDeckCube.name = $"PlayerBtn_{i}_{stack.count}";

            // FIXED: Ensure World Scale is always a perfect cube, ignoring parent's distortion
            Vector3 targetWorldScale = new Vector3(buttonSize, buttonSize, buttonSize);
            Vector3 parentScale = deckCenterPoint.lossyScale;
            
            // Calculate necessary local scale to achieve target world scale
            Vector3 finalLocalScale = new Vector3(
                targetWorldScale.x / parentScale.x,
                targetWorldScale.y / parentScale.y,
                targetWorldScale.z / parentScale.z
            );

            playerDeckCube.transform.localScale = finalLocalScale;
            playerDeckCube.transform.localRotation = Quaternion.identity; 

            PlayerCube pc = playerDeckCube.AddComponent<PlayerCube>();
            pc.Initialize(stack.color, this);
            pc.stackCount = stack.count; 
            
            col++;
        }
    }



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
