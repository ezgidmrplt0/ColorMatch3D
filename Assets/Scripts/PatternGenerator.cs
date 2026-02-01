using UnityEngine;
using UnityEngine.UI;
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

    private void Start()
    {
        SetupHypercasualVisuals();

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
        
        GenerateManualPattern();
        
        Debug.Log($"Pattern Generated. Colors Logic: {activePixelCubes.Count}");
    }
    
    public void GenerateManualPattern()
    {
        // Cleanup existing patterns
        foreach(var list in activePixelCubes.Values) { foreach(var obj in list) if(obj) Destroy(obj); }
        activePixelCubes.Clear();
        
        // Initialize occupied array based on manual slots count
        if (manualSlots != null) 
        {
            maxSlots = manualSlots.Count;
            slotOccupied = new bool[maxSlots];
        }

        // DEFINING THE PATTERN MANUALLY (The Heart)
        // FIXED: All rows must have EQUAL length
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
            // PROPORTIONAL AMMO
            int ammoCount = activePixelCubes.ContainsKey(col) ? activePixelCubes[col].Count : 10;
            deckStacks.Add(new StackData { color = col, count = ammoCount });
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
            
            Text stackText = playerDeckCube.GetComponentInChildren<Text>();
            if (stackText != null) stackText.text = stack.count.ToString(); 
            
            activeDeckButtons.Add(pc); // Add to tracking list

            col++;
        }
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
    }

    // Called by PlayerCube when clicked
    public void OnPlayerCubeClicked(PlayerCube senderCube)
    {
        if (!isGameActive) return;
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

                // Spawn Drone
                droneObj = Instantiate(cubePrefab, senderCube.transform.position, Quaternion.identity);
                isNewSpawn = true;
                
                // USER REQUEST: Destroy the deck button immediately so it cannot be clicked again
                if (activeDeckButtons.Contains(senderCube)) activeDeckButtons.Remove(senderCube);
                Destroy(senderCube.gameObject);
                
                // Rearrange remaining buttons
                RearrangeDeck();
            }

            // Setup Drone
            droneObj.name = "Drone_" + color;
            droneObj.GetComponent<Renderer>().material.color = color;
            droneObj.transform.localScale = Vector3.one * 0.8f;

            // Setup PlayerCube Component on Drone (to hold ammo data and be clickable)
            PlayerCube dronePC = droneObj.GetComponent<PlayerCube>();
            if (dronePC == null) dronePC = droneObj.AddComponent<PlayerCube>();
            dronePC.Initialize(color, this);
            dronePC.stackCount = totalTargets; // Set Ammo to Target Count

            // Update Text (Legacy)
            Text ammoText = droneObj.GetComponentInChildren<Text>();
            if (ammoText != null) ammoText.text = totalTargets.ToString();

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

            // KILL any existing tweens on this object to prevent conflicts
            DOTween.Kill(droneObj);
            droneObj.transform.DOKill(); // Ensure transform tweens are also killed

            // Unparent while flying
            droneObj.transform.SetParent(null); 
            
            // FIX: Reset Scale AFTER unparenting to ensure correct World Scale
            droneObj.transform.localScale = Vector3.one * 0.8f;
            droneObj.transform.localRotation = Quaternion.identity; 
            
            // Re-Parent a dummy/placeholder to reserve the slot? 
            // Better yet, just trust that no one else takes it because we are single-threaded here mostly.
            // But if user clicks fast, we might have issues.
            // Let's set parent to destSlot IMMEDIATELY but keep world position?
            // No, that messes up Tween.
            
            // We can create a placeholder object.
            GameObject reservation = new GameObject("Reservation");
            reservation.transform.SetParent(destSlot); 
            // We will destroy reservation when drone lands.
            
            // ... (rest of waypoints logic) ...
            
            // Waypoints
            if (pointBottomLeft == null || pointTopLeft == null || pointTopRight == null || pointBottomRight == null || pointFifthWaypoint == null) return;
            Vector3 p1 = pointBottomLeft.position;
            Vector3 p2 = pointTopLeft.position;
            Vector3 p3 = pointTopRight.position;
            Vector3 p4 = pointBottomRight.position;
            Vector3 p5 = pointFifthWaypoint.position;
            
            Sequence droneSeq = DOTween.Sequence();
            droneSeq.SetId(droneObj); // Tag for killing
            
            // Move to start point
            droneSeq.Append(droneObj.transform.DOMove(p1, 0.2f).SetEase(Ease.OutQuad));
            droneSeq.Append(droneObj.transform.DOMove(p2, 0.4f).SetEase(Ease.Linear));
            droneSeq.Append(droneObj.transform.DOMove(p3, 0.4f).SetEase(Ease.Linear));
            droneSeq.Append(droneObj.transform.DOMove(p4, 0.4f).SetEase(Ease.Linear));
            droneSeq.Append(droneObj.transform.DOMove(p5, 0.3f).SetEase(Ease.Linear));
            
            // Land
            droneSeq.Append(droneObj.transform.DOMove(destSlot.position, 0.3f).SetEase(Ease.OutBack));
            
            StartCoroutine(DroneRapidFire(dronePC, color, destSlot));

            // On Complete
            droneSeq.OnComplete(() => {
                if(droneObj != null)
                {
                    // Destroy Reservation
                    Transform res = destSlot.Find("Reservation");
                    if(res != null) Destroy(res.gameObject);

                    droneObj.transform.SetParent(destSlot);
                    droneObj.transform.localPosition = Vector3.zero;
                    droneObj.transform.localRotation = Quaternion.identity;
                    
                    // Fixed Slot Scale
                    Vector3 targetSize = Vector3.one * 0.5f; 
                    Vector3 parentScale = destSlot.lossyScale;
                    if (parentScale.x != 0) 
                        droneObj.transform.localScale = new Vector3(targetSize.x/parentScale.x, targetSize.y/parentScale.y, targetSize.z/parentScale.z);
                    else 
                        droneObj.transform.localScale = Vector3.one;

                    CheckWinCondition();
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


    IEnumerator DroneRapidFire(PlayerCube dronePC, Color color, Transform targetSlot)
    {
        bool isFlying = true;
        GameObject drone = dronePC.gameObject;
        Text ammoText = drone.GetComponentInChildren<Text>();

        while (drone != null && isFlying)
        {
            // Check Landing
            if (drone.transform.parent == targetSlot) { isFlying = false; break; }

            if (activePixelCubes.ContainsKey(color) && activePixelCubes[color].Count > 0)
            {
                 GameObject target = activePixelCubes[color]
                    .Where(t => t != null)
                    .OrderBy(t => Vector3.Distance(drone.transform.position, t.transform.position)) 
                    .FirstOrDefault();

                 if (target != null)
                 {
                     activePixelCubes[color].Remove(target);
                     
                     // Decrement Ammo & Update Text
                     dronePC.stackCount--;
                     if(ammoText != null) ammoText.text = dronePC.stackCount.ToString();

                     // RECOIL: Punch scale on firing
                     drone.transform.DOPunchScale(Vector3.one * 0.2f, 0.1f, 5, 1);

                     // Shoot Projectile
                     GameObject projectile = Instantiate(cubePrefab, drone.transform.position, Quaternion.identity);
                     projectile.GetComponent<Renderer>().material.color = color;
                     projectile.transform.localScale = Vector3.one * 0.15f; 

                     projectile.transform.DOMove(target.transform.position, 0.12f).SetEase(Ease.Linear).OnComplete(() => { // Faster bullet
                        if (target != null) 
                        { 
                            // JUICE 1: Explosion Debris
                            CreateExplosion(target.transform.position, color);
                            Destroy(target); 
                        }
                        
                        // JUICE 2: Screen Shake (Subtle)
                        if(Camera.main != null) Camera.main.transform.DOShakePosition(0.05f, 0.05f, 5, 90, false, true);

                        Destroy(projectile);
                     });
                     
                     // CHECK DEATH CONDITION: Ammo exhausted mid-flight
                     if(dronePC.stackCount <= 0)
                     {
                         // Kill Movement
                         drone.transform.DOKill();
                         // Explosion Effect?
                         drone.transform.DOScale(0, 0.1f).OnComplete(() => Destroy(drone));
                         
                         // Free the slot logic? For now simple destroy.
                         int slotIdx = manualSlots.IndexOf(targetSlot);
                         if(slotIdx != -1) slotOccupied[slotIdx] = false;

                         yield break; // STOP COROUTINE
                     }
                 }
            }
            yield return new WaitForSeconds(0.12f); // Faster fire rate
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
            debris.transform.DOScale(0, 0.4f).OnComplete(()=> Destroy(debris));
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
            // PROPORTIONAL AMMO: Set stack count equal to the number of pixels in pattern
            int ammo = activePixelCubes.ContainsKey(col) ? activePixelCubes[col].Count : 10;
            deckStacks.Add(new StackData { color = col, count = ammo });
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



    private void SetupHypercasualVisuals()
    {
        // 1. Camera Background (Soft Light Blue/White)
        if(Camera.main != null)
        {
            Camera.main.clearFlags = CameraClearFlags.SolidColor;
            Camera.main.backgroundColor = new Color32(235, 245, 255, 255); // Very soft blue-white
        }

        // 2. Lighting (Warm Sun)
        Light mainLight = FindObjectOfType<Light>();
        if(mainLight != null && mainLight.type == LightType.Directional)
        {
            mainLight.color = new Color32(255, 250, 240, 255); // Warm white
            mainLight.intensity = 1.1f;
            mainLight.transform.rotation = Quaternion.Euler(50, -30, 0);
            mainLight.shadowStrength = 0.4f; // Softer shadows
            mainLight.shadows = LightShadows.Soft;
        }
        
        // 3. Ambient Lighting (Trilight for contrast)
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color32(255, 255, 255, 255);
        RenderSettings.ambientEquatorColor = new Color32(200, 220, 230, 255); // Blue-ish grey
        RenderSettings.ambientGroundColor = new Color32(150, 150, 150, 255);

        // 4. Fog (Depth)
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = new Color32(235, 245, 255, 255); // Match BG
        RenderSettings.fogDensity = 0.015f;
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
