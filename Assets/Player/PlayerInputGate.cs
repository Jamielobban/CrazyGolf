using UnityEngine;

public class PlayerInputGate : MonoBehaviour
{
    public bool uiOpen;
    public bool inSwing;

    public bool AllowLook   => !uiOpen;                 // no camera when UI open
    public bool AllowMove   => !uiOpen;                 // optional
    public bool AllowDrop   => !uiOpen && !inSwing;     // your request
    public bool AllowInteract => !uiOpen;               // optional
    public bool AllowPeek   => inSwing && !uiOpen;      // peek only in swing
    public bool AllowOrbit  => inSwing && !uiOpen;      // orbit only in swing
}
