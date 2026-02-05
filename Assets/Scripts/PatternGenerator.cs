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
    public GameObject dronePrefab; // New Drone Prefab
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
    private List<PlayerCube> activeDeckButtons = new List<PlayerCube>(); // Track deck buttons for re-ordering
    private bool isGameActive = false;
    
    // Level System
    private int currentLevelIndex = 0;

    private List<string[]> levelPatterns = new List<string[]>();

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
            
            // Adjust Scale slightly down to ensure fit
            playerDeckCube.transform.localScale = Vector3.one * 1.8f; 
            
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
                }
                
                if (targetSlotIndex == -1) { Debug.Log("Slots Full!"); return; } // Slots full

            // Spawn Drone Logic
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

            // --- CREATE RESERVATION IMMEDIATELY ---
            GameObject reservation = new GameObject("Reservation");
            reservation.transform.SetParent(destSlot);
            reservation.transform.localPosition = Vector3.zero;

            // KILL any existing tweens on this object to prevent conflicts
            DOTween.Kill(droneObj);
            droneObj.transform.DOKill(); // Ensure transform tweens are also killed

            // --- MOVEMENT LOGIC WITH CONTAINER (Fixes Distortion) ---
            
            // 1. Create a Container for clean rotation
            GameObject droneContainer = new GameObject("DroneContainer_" + color);
            droneContainer.transform.position = droneObj.transform.position;
            
            // 2. Parent Drone to Container
            // Unparent first to be safe
            droneObj.transform.SetParent(null);
            droneObj.transform.SetParent(droneContainer.transform);
            droneObj.transform.localPosition = Vector3.zero;
            // FORCE VISUAL to Face Camera (Fixed Local Rotation)
            droneObj.transform.localRotation = Quaternion.Euler(-90, 0, 0);

            // Waypoints
            if (pointBottomLeft == null || pointTopLeft == null || pointTopRight == null || pointBottomRight == null || pointFifthWaypoint == null) return;
            Vector3 p1 = pointBottomLeft.position;
            Vector3 p2 = pointTopLeft.position;
            Vector3 p3 = pointTopRight.position;
            Vector3 p4 = pointBottomRight.position;
            Vector3 p5 = pointFifthWaypoint.position;
            
            Sequence droneSeq = DOTween.Sequence();
            droneSeq.SetId(droneContainer); // Tag for killing container
            
            // Path: BottomLeft -> BottomRight -> TopRight -> TopLeft -> BottomLeft
            float moveDur = 1.0f; 

            // 0. Move Container to Start
            // Face UP (Z=0)
            droneSeq.Append(droneContainer.transform.DOMove(p1, 0.5f).SetEase(Ease.OutQuad));
            droneSeq.Join(droneContainer.transform.DORotate(new Vector3(0, 0, 0), 0.5f)); 

            // 1. P1 -> P4 (Bottom Edge) -> Fire 2
            droneSeq.Append(droneContainer.transform.DOMove(p4, moveDur).SetEase(Ease.Linear)
                .OnPlay(() => StartCoroutine(FireBurst(dronePC, color, 2, moveDur, destSlot))));
            
            // Rotate Container to Face LEFT (Z=90) -> No Distortion!
            droneSeq.Append(droneContainer.transform.DORotate(new Vector3(0, 0, 90), 0.2f));

            // 2. P4 -> P3 (Right Edge)
            droneSeq.Append(droneContainer.transform.DOMove(p3, moveDur).SetEase(Ease.Linear)
                .OnPlay(() => StartCoroutine(FireBurst(dronePC, color, 3, moveDur, destSlot))));

            // Rotate Container to Face DOWN (Z=180)
            droneSeq.Append(droneContainer.transform.DORotate(new Vector3(0, 0, 180), 0.2f));

            // 3. P3 -> P2 (Top Edge)
            droneSeq.Append(droneContainer.transform.DOMove(p2, moveDur).SetEase(Ease.Linear)
                .OnPlay(() => StartCoroutine(FireBurst(dronePC, color, 2, moveDur, destSlot))));

            // Rotate Container to Face RIGHT (Z=270 or -90)
            droneSeq.Append(droneContainer.transform.DORotate(new Vector3(0, 0, 270), 0.2f));

            // 4. P2 -> P1 (Left Edge)
            droneSeq.Append(droneContainer.transform.DOMove(p1, moveDur).SetEase(Ease.Linear)
                .OnPlay(() => StartCoroutine(FireBurst(dronePC, color, 3, moveDur, destSlot))));
            
            // Rotate back to Up (Z=0)
            droneSeq.Append(droneContainer.transform.DORotate(new Vector3(0, 0, 0), 0.2f));
            
            // Land (Move Container to Slot)
            droneSeq.Append(droneContainer.transform.DOMove(destSlot.position, 0.6f).SetEase(Ease.OutBack));
            
            // NO FIRING AFTER LANDING
            // StartCoroutine(DroneRapidFire(dronePC, color, destSlot)); 

            // On Complete
            droneSeq.OnComplete(() => {
                if(droneObj != null)
                {
                    // Unparent form Container BEFORE destroying container
                    droneObj.transform.SetParent(null);
                    if(droneContainer != null) Destroy(droneContainer);

                    // Destroy Reservation
                    Transform res = destSlot.Find("Reservation");
                    if(res != null) Destroy(res.gameObject);
                    
                    // CHECK AMMO
                    if (dronePC.stackCount <= 0)
                    {
                         droneObj.transform.DOScale(0, 0.2f).OnComplete(() => Destroy(droneObj));
                         return; 
                    }

                    // LANDING LOGIC
                    droneObj.transform.SetParent(destSlot);
                    
                    // Reset Local Position to Zero first to center it
                    droneObj.transform.localPosition = Vector3.zero;
                    
                    // Global Rotation to face Camera (Standard View)
                    droneObj.transform.rotation = Quaternion.Euler(-90, 0, 0); 
                    
                    // Final Position Adjustment (Visual Offset)
                    // We move it slightly towards the camera (Assuming Camera is at Z < 0) or just Z offset
                    // Trying Z = -0.3f (Closer to camera if camera is looking from -Z)
                    droneObj.transform.localPosition = new Vector3(0, 0, -0.3f); 
                    
                    // FIXED Scale Logic
                    float targetGlobalScale = 1.2f; 
                    Vector3 pScale = destSlot.lossyScale;
                    
                    // Normalize divisor
                     float px = Mathf.Abs(pScale.x) < 0.001f ? 1 : pScale.x;
                     float py = Mathf.Abs(pScale.y) < 0.001f ? 1 : pScale.y;
                     float pz = Mathf.Abs(pScale.z) < 0.001f ? 1 : pScale.z;

                    droneObj.transform.localScale = new Vector3(targetGlobalScale/px, targetGlobalScale/py, targetGlobalScale/pz);
                    
                     // Force refresh text
                     TMP_Text dText = droneObj.GetComponentInChildren<TMP_Text>();
                     if(dText != null) { dText.enabled = false; dText.enabled = true; }
                     Text lText = droneObj.GetComponentInChildren<Text>();
                     if(lText != null) { lText.enabled = false; lText.enabled = true; }

                     // Ensure Visible
                     droneObj.SetActive(true);
                     Renderer[] rnds = droneObj.GetComponentsInChildren<Renderer>();
                     foreach(var r in rnds) r.enabled = true;

                    CheckWinCondition();
                }
                else
                {
                    if(droneContainer != null) Destroy(droneContainer);
                }
            });
            
            // Update Deck Visual
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
