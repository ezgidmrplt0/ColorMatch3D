using UnityEngine;

public class PlayerCube : MonoBehaviour
{
    public Color cubeColor;
    public int stackCount; // How many items are in this stack
    public bool isReadyToFire = false; // Controls if Turret should pick this up
    public Transform currentSlot; // Which slot this ammo belongs to (without parenting)
    private PatternGenerator gameManager;

    public void Initialize(Color color, PatternGenerator manager)
    {
        this.cubeColor = color;
        this.gameManager = manager;
        
        // Safety: Try to find renderer on self, otherwise look in children (for Drone prefabs)
        Renderer r = GetComponent<Renderer>();
        if (r == null) r = GetComponentInChildren<Renderer>();
        
        if (r != null)
        {
            r.material.color = color;
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
