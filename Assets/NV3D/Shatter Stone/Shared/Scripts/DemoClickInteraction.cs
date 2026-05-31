
// This script is used in the demo scene to allow on-click iteractions with the Shatter Stone ore nodes. This will work with Unity's new input system, or when input is set to both.

using UnityEngine;
using UnityEngine.InputSystem;
using ShatterStone;

namespace ShatterStone
{

public class DemoClickInteraction : MonoBehaviour

{
    [SerializeField] private Camera mainCamera;
    [SerializeField] private float maxDistance = 100f;

    [Tooltip("Tag used to identify ore nodes in the scene.")]
    [SerializeField] private string oreNodeTag = "OreNode";
    [SerializeField] private string orePickupTag = "OrePickup";

    private void Update()
    {
        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            Ray ray = mainCamera.ScreenPointToRay(Mouse.current.position.ReadValue());

            if (Physics.Raycast(ray, out RaycastHit hit, maxDistance))
            {
                if (hit.collider.CompareTag(oreNodeTag))
                {
                    var oreNode = hit.collider.GetComponent<OreNode>();
                    if (oreNode != null)
                    {
                        oreNode.Interact();
                    }
                }
                if (hit.collider.CompareTag(orePickupTag))
                {
                    var orePickup = hit.collider.GetComponent<PickupController>();
                    if (orePickup != null)
                    {
                        orePickup.CollectItem();
                    }
                }
            }
        }
    }
}
}
