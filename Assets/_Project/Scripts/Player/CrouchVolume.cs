using UnityEngine;

namespace LastWard.Player
{
    /// <summary>
    /// A space too low to stand up in. Inside one, crouch is held down for you.
    ///
    /// Not a convenience. A CharacterController does not resolve being resized inside geometry — it
    /// simply overlaps it, and the player either jams or pops through the ceiling. Letting go of C
    /// halfway along a 1.35m duct would do exactly that, so the duct takes the decision away.
    ///
    /// Same trigger-collider requirement as <see cref="ClimbVolume"/>, for the same reason.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class CrouchVolume : MonoBehaviour
    {
        private void Reset() => GetComponent<Collider>().isTrigger = true;
    }
}
