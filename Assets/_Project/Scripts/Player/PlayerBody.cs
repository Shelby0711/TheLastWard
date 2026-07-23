using Unity.Netcode;
using UnityEngine;

namespace LastWard.Player
{
    /// <summary>
    /// The visible body teammates see. Until now a player was invisible except for their torch beam,
    /// which made co-op nearly unreadable — and would have become impossible once torches can run
    /// out of battery.
    ///
    /// Two rules make this work:
    /// <list type="bullet">
    /// <item><b>Hidden for its owner.</b> A first-person camera sits inside the head; without this
    /// you see the inside of your own skull. The body is disabled locally rather than culled by
    /// layer, so no project layer setup is required.</item>
    /// <item><b>Parented to the root, not the camera pivot.</b> On the pivot it would pitch with the
    /// look direction and the body would fold over when you looked down.</item>
    /// </list>
    ///
    /// Which model a player gets is chosen from their client id, so everyone sees the same person as
    /// the same figure and teammates stay distinguishable at a glance.
    /// </summary>
    public class PlayerBody : NetworkBehaviour
    {
        [Tooltip("One per character model. The client id picks which is shown.")]
        [SerializeField] private GameObject[] variants;
        [Tooltip("Animator on the chosen variant, driven by movement speed.")]
        [SerializeField] private Animator animator;

        private GameObject active;
        private Vector3 lastPosition;

        public override void OnNetworkSpawn()
        {
            lastPosition = transform.position;

            if (variants == null || variants.Length == 0) return;

            // Same id -> same model on every machine, so players can describe each other.
            int index = (int)(OwnerClientId % (ulong)variants.Length);
            for (int i = 0; i < variants.Length; i++)
            {
                if (variants[i] == null) continue;
                bool chosen = i == index;
                variants[i].SetActive(chosen && !IsOwner);
                if (chosen) active = variants[i];
            }

            if (active != null) animator = active.GetComponentInChildren<Animator>();
        }

        private void Update()
        {
            if (active == null || animator == null) return;

            // Speed from the transform: this runs on remote copies where there is no motor and no
            // agent, only replicated position.
            float speed = Time.deltaTime > 0f
                ? Vector3.ProjectOnPlane(transform.position - lastPosition, Vector3.up).magnitude / Time.deltaTime
                : 0f;
            lastPosition = transform.position;

            // The models carry a single clip each, so playback rate stands in for a walk/run blend.
            // Never fully stops — a frozen body reads as a corpse or a bug.
            animator.speed = Mathf.Clamp(0.35f + speed * 0.4f, 0.35f, 2.2f);
        }
    }
}
