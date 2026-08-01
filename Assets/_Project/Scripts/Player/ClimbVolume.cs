using UnityEngine;

namespace LastWard.Player
{
    /// <summary>
    /// A space you can move through vertically — the inside of a ladder, in practice.
    ///
    /// A marker, nothing more: <see cref="FirstPersonMotor"/> counts how many of these it is standing
    /// inside and switches to climb steering while that count is above zero. Counted rather than
    /// flagged so overlapping volumes (a ladder whose top reaches into a hatch) can't cancel each
    /// other out when one of them is exited.
    ///
    /// The collider MUST be a trigger. PlayerInteractor raycasts with QueryTriggerInteraction.Ignore,
    /// so a solid one would both block walking and swallow the interaction ray aimed at whatever is
    /// behind it — which is exactly the bug that made the ladder mount an invisible wall.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class ClimbVolume : MonoBehaviour
    {
        private Collider col;

        [Tooltip("Axis the climber is penned to while inside. Gravity is off up here, so an " +
            "unconstrained ladder is a flying machine: you can strafe off the top of it and step " +
            "down onto anything within reach — which is how the vent duct got bypassed entirely by " +
            "walking sideways onto the rubble pile. The free axis is the one you dismount along.")]
        [SerializeField] private bool penZ = true;
        [SerializeField] private bool penX;

        public bool PenZ => penZ;
        public bool PenX => penX;
        public Bounds Bounds => col.bounds;

        private void Awake() => col = GetComponent<Collider>();
        private void Reset() => GetComponent<Collider>().isTrigger = true;
    }
}
