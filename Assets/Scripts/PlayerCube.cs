using UnityEngine;
using System.Collections.Generic;

public class PlayerCube : MonoBehaviour
{
    public Color cubeColor;
    public int stackCount; // How many items are in this stack
    public bool isReadyToFire = false; // Controls if Turret should pick this up
    public Transform currentSlot; // Which slot this ammo belongs to (without parenting)
    private PatternGenerator gameManager;

    // Cache specific renderers to avoid string checks every update
    [SerializeField] private List<Renderer> paintableRenderers = new List<Renderer>();
    private bool visualsInitialized = false;

    public void InitializeVisuals()
    {
        if (visualsInitialized) return;
        
        if (paintableRenderers.Count == 0)
        {
            Renderer[] allRends = GetComponentsInChildren<Renderer>();
            foreach(var r in allRends)
            {
                // Logic moved from PatternGenerator: Filter out propellers and text components
                if(r.name.Contains("Propeller") || r.gameObject.GetComponent<TMPro.TMP_Text>() != null || r.gameObject.GetComponent<UnityEngine.UI.Text>() != null) 
                    continue;
                
                paintableRenderers.Add(r);
            }
        }
        visualsInitialized = true;
    }

    public void SetColor(Color color)
    {
        InitializeVisuals();
        this.cubeColor = color;
        
        foreach(var r in paintableRenderers)
        {
            if (r != null) r.material.color = color;
        }
    }

    public void Initialize(Color color, PatternGenerator manager)
    {
        this.gameManager = manager;
        SetColor(color);
    }

    private void Start()
    {
        if (PatternGenerator.Instance != null)
        {
            PatternGenerator.Instance.RegisterCube(this);
            // If gameManager wasn't set locally (e.g. placed in scene manually), set it now
            if (gameManager == null) gameManager = PatternGenerator.Instance;
        }
    }

    private void OnDestroy()
    {
        if (PatternGenerator.Instance != null)
        {
            PatternGenerator.Instance.UnregisterCube(this);
        }
    }

    private void OnMouseDown()
    {
        if (gameManager != null)
        {
            gameManager.OnPlayerCubeClicked(this);
        }
    }
}
