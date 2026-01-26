using UnityEngine;

public class PlayerCube : MonoBehaviour
{
    public Color cubeColor;
    public int stackCount; // How many items are in this stack
    private PatternGenerator gameManager;

    public void Initialize(Color color, PatternGenerator manager)
    {
        this.cubeColor = color;
        this.gameManager = manager;
        
        // Set visual color
        GetComponent<Renderer>().material.color = color;
    }

    private void OnMouseDown()
    {
        if (gameManager != null)
        {
            gameManager.OnPlayerCubeClicked(this);
        }
    }
}
