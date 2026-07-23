using LastWard.Audio;
using UnityEngine;
using UnityEngine.UI;

namespace LastWard.UI
{
    /// <summary>
    /// The last thing a player sees. Fills the screen and screams, then hands over to the death
    /// screen.
    ///
    /// This is a UI flash rather than a camera move onto the Entity's model, for one reason: the
    /// model has a single idle animation and no scream pose, so framing it up close would show a
    /// calm figure standing still — which is funny, not frightening. A full-bleed image with the
    /// scream over it lands the beat and never has to survive close inspection.
    ///
    /// The image is optional. With none assigned it flashes to near-black, which still reads as
    /// something filling your vision.
    /// </summary>
    public class JumpscareUI : MonoBehaviour
    {
        public static JumpscareUI Instance { get; private set; }

        [SerializeField] private CanvasGroup group;
        [SerializeField] private Image image;
        [Tooltip("Seconds the scare holds before the death screen takes over. Should roughly match " +
            "the Entity's charge time so the tint peaks as it arrives.")]
        [SerializeField] private float holdSeconds = 1.6f;
        [Tooltip("Peak opacity of the red bloom. Deliberately partial — the Entity must stay visible " +
            "through it.")]
        [SerializeField, Range(0f, 1f)] private float maxTint = 0.45f;

        private void Awake()
        {
            Instance = this;
            if (group != null) group.alpha = 0f;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>Plays the scare. <paramref name="slamFirst"/> adds a door slam before it.</summary>
        public void Play(bool slamFirst) => StartCoroutine(Routine(slamFirst));

        private System.Collections.IEnumerator Routine(bool slamFirst)
        {
            if (slamFirst)
            {
                // The door goes first and alone. A slam from somewhere you cannot see, followed by
                // a beat of nothing, is the part that actually frightens.
                PlayAt(GameSfx.DoorSlam, 1f);
                yield return new WaitForSeconds(0.55f);
            }

            PlayAt(GameSfx.Scream, 1f);

            // No full-screen image any more. Covering the view with the Entity's texture hid the
            // one thing worth seeing — it closing the distance — and at that scale the texture read
            // as a flat smear rather than a creature. The Entity itself now charges the camera and
            // this is only a faint red bloom over it.
            if (group == null) yield break;

            float elapsed = 0f;
            while (elapsed < holdSeconds)
            {
                elapsed += Time.deltaTime;
                // Rises as it closes, so the screen is reddest at the moment of contact.
                group.alpha = Mathf.Clamp01(elapsed / holdSeconds) * maxTint;
                if (image != null)
                {
                    float shake = Mathf.Sin(elapsed * 55f) * 6f;
                    image.rectTransform.anchoredPosition = new Vector2(shake, -shake * 0.4f);
                }
                yield return null;
            }
            group.alpha = 0f;
        }

        private static void PlayAt(AudioClip clip, float volume)
        {
            if (clip == null) return;
            var listener = FindAnyObjectByType<AudioListener>();
            AudioSource.PlayClipAtPoint(clip, listener != null ? listener.transform.position : Vector3.zero, volume);
        }
    }
}
