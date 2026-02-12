using UnityEngine;
using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine.UI;

public class DeckManager : MonoBehaviour
{
    public static DeckManager Instance { get; private set; }

    [Header("Deck Settings")]
    public Transform deckCenterPoint;
    public Vector3 deckPositionOffset = Vector3.zero;
    public int visibleDeckCount = 3; 

    // State
    public List<PlayerCube> activeDeckButtons = new List<PlayerCube>();

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    public struct StackData
    {
        public Color color;
        public int count;
    }

    public void ClearDeck()
    {
        if (deckCenterPoint != null)
        {
             foreach(Transform child in deckCenterPoint) 
             {
                 Destroy(child.gameObject);
             }
        }
        activeDeckButtons.Clear();
    }

    public void GeneratePlayerDeck(List<StackData> stacks, GameObject prefab, PatternGenerator gameManager)
    {
        ClearDeck();

        int count = stacks.Count;
        if (count == 0) return;

        int cols = 3; 
        float xSpacing = 1.3f; 
        float ySpacing = 1.4f; 
        float startY = 0.5f; 

        for (int i = 0; i < count; i++)
        {
            StackData stack = stacks[i];
            int row = i / cols;
            int col = i % cols;

            // Alignment Logic
            int itemsInThisRow = cols;
             // If it's the last row
            if (row == (count - 1) / cols) 
            {
                int rem = count % cols;
                if (rem != 0) itemsInThisRow = rem;
            }
            
            float rowWidth = (itemsInThisRow - 1) * xSpacing;
            float rowStartX = -rowWidth / 2f; 

            Vector3 localPos = new Vector3(
                rowStartX + (col * xSpacing), 
                startY - (row * ySpacing), 
                0
            );

            GameObject playerDeckCube = Instantiate(prefab, deckCenterPoint);
            playerDeckCube.transform.localPosition = localPos + deckPositionOffset; 
            playerDeckCube.transform.localRotation = Quaternion.Euler(-90, 0, 0); 
            
            playerDeckCube.name = $"PlayerBtn_{i}_{stack.count}";

            // Safely get or add PlayerCube
            PlayerCube pc = playerDeckCube.GetComponent<PlayerCube>();
            if (pc == null) pc = playerDeckCube.AddComponent<PlayerCube>();
            
            pc.stackCount = stack.count; 
            pc.Initialize(stack.color, gameManager);
            
            // Update Text 
            Text stackText = playerDeckCube.GetComponentInChildren<Text>();
            if (stackText != null) stackText.text = stack.count.ToString(); 
            
            TMP_Text stackTextTMP = playerDeckCube.GetComponentInChildren<TMP_Text>();
            if (stackTextTMP != null) stackTextTMP.text = stack.count.ToString(); 
            
            activeDeckButtons.Add(pc); 
        }
        
        UpdateDeckStates();
    }

    public void RemoveButton(PlayerCube btn)
    {
        if (activeDeckButtons.Contains(btn))
        {
            activeDeckButtons.Remove(btn);
            RearrangeDeck();
        }
    }

    public bool IsInDeck(PlayerCube btn)
    {
        return activeDeckButtons.Contains(btn);
    }

    public bool IsClickable(PlayerCube btn)
    {
        if (!activeDeckButtons.Contains(btn)) return false;
        return activeDeckButtons.IndexOf(btn) < visibleDeckCount;
    }

    private void RearrangeDeck()
    {
        int count = activeDeckButtons.Count;
        int cols = 3;
        float xSpacing = 1.3f;
        float ySpacing = 1.4f;
        float startY = 0.5f;

        int totalRows = Mathf.CeilToInt((float)count / cols);
        
        for (int i = 0; i < count; i++)
        {
            PlayerCube btn = activeDeckButtons[i];
            if (btn == null) continue;

            int row = i / cols;
            int col = i % cols;

            int itemsInLastRow = count % cols;
            if (itemsInLastRow == 0) itemsInLastRow = cols;
            
            int itemsInThisRow = (row < totalRows - 1) ? cols : itemsInLastRow;
            
            float rowWidth = (itemsInThisRow - 1) * xSpacing;
            float rowStartX = -rowWidth / 2f;

            Vector3 targetLocalPos = new Vector3(
                rowStartX + (col * xSpacing),
                startY - (row * ySpacing),
                0
            ) + deckPositionOffset;

            btn.transform.DOLocalMove(targetLocalPos, 0.3f).SetEase(Ease.OutQuad);
        }
        UpdateDeckStates();
    }

    private void UpdateDeckStates()
    {
        for (int i = 0; i < activeDeckButtons.Count; i++)
        {
            PlayerCube btn = activeDeckButtons[i];
            if (btn == null) continue;
            // Visual update handled by PlayerCube.SetColor implicitly through prefab state, 
            // but we can enforce it if needed. 
            // Primarily this loop was for coloring based on order, but now they are always colored.
        }
    }
}
