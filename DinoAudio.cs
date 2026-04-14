using UnityEngine;

[DisallowMultipleComponent]
public class DinoAudio : MonoBehaviour
{
    [Header("Footstep Audio")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip[] footstepClips;
    [SerializeField, Range(0f, 1f)] private float volume = 0.8f;
    [SerializeField] private Vector2 pitchRange = new Vector2(0.95f, 1.05f);
    [SerializeField] private float minInterval = 0.05f;

    [Header("Random Growl")]
    [SerializeField] private AudioClip[] growlClips;
    [SerializeField, Range(0f, 1f)] private float growlVolume = 0.85f;
    [SerializeField, Range(0f, 1f)] private float growlChancePerInterval = 0.35f;
    [SerializeField] private float growlCheckInterval = 10f;
    [SerializeField] private Vector2 growlPitchRange = new Vector2(0.95f, 1.03f);

    [Header("Alert Roar")]
    [SerializeField] private AudioClip[] alertRoarClips;
    [SerializeField, Range(0f, 1f)] private float alertRoarVolume = 1f;
    [SerializeField] private Vector2 alertRoarPitchRange = new Vector2(0.95f, 1.05f);
    [SerializeField] private float alertRoarStimulusRadius = 35f;
    [Tooltip("Prevents double-roar when both code (RPC/state) and animation events call PlayAlertRoar().")]
    [SerializeField] private float alertRoarDuplicateIgnoreWindowSeconds = 0f;

    [Header("Distance Culling")]
    [SerializeField] private bool useDistanceCulling = true;
    [SerializeField] private bool useAudioSourceDistanceSettings = true;
    [SerializeField] private float footstepDistanceMultiplier = 1f;
    [SerializeField] private float growlDistanceMultiplier = 1f;
    [SerializeField] private float alertRoarDistanceMultiplier = 1f;
    [SerializeField] private float fallbackFootstepMaxDistance = 24f;
    [SerializeField] private float fallbackGrowlMaxDistance = 28f;
    [SerializeField] private float fallbackAlertRoarMaxDistance = 40f;

    private float nextAllowedPlayTime;
    private float nextGrowlCheckTime;
    private float lastAlertRoarPlayTime = -999f;

    private static AudioListener[] cachedListeners;
    private static float nextListenerRefreshTime = -999f;
    private const float ListenerRefreshIntervalSeconds = 0.5f;

    private void Awake()
    {
        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
        }

        nextGrowlCheckTime = Time.time + Mathf.Max(0.1f, growlCheckInterval);
    }

    private void Update()
    {
        TryPlayRandomGrowl();
    }

    // Animation Event target: add this exact function name in the clip event.
    public void PlayFootstepEvent()
    {
        if (audioSource == null || footstepClips == null || footstepClips.Length == 0)
        {
            return;
        }

        if (Time.time < nextAllowedPlayTime)
        {
            return;
        }

        if (!CanPlayForAnyListener(GetMaxAudibleDistance(footstepDistanceMultiplier, fallbackFootstepMaxDistance)))
        {
            return;
        }

        int index = Random.Range(0, footstepClips.Length);
        AudioClip clip = footstepClips[index];
        if (clip == null)
        {
            return;
        }

        float pitchMin = Mathf.Min(pitchRange.x, pitchRange.y);
        float pitchMax = Mathf.Max(pitchRange.x, pitchRange.y);
        audioSource.pitch = Random.Range(pitchMin, pitchMax);
        audioSource.PlayOneShot(clip, Mathf.Clamp01(volume));
        nextAllowedPlayTime = Time.time + Mathf.Max(0.01f, minInterval);
    }

    private void TryPlayRandomGrowl()
    {
        if (audioSource == null || growlClips == null || growlClips.Length == 0)
        {
            return;
        }

        if (Time.time < nextGrowlCheckTime)
        {
            return;
        }

        nextGrowlCheckTime = Time.time + Mathf.Max(0.1f, growlCheckInterval);
        if (Random.value > Mathf.Clamp01(growlChancePerInterval))
        {
            return;
        }

        if (!CanPlayForAnyListener(GetMaxAudibleDistance(growlDistanceMultiplier, fallbackGrowlMaxDistance)))
        {
            return;
        }

        int index = Random.Range(0, growlClips.Length);
        AudioClip clip = growlClips[index];
        if (clip == null)
        {
            return;
        }

        float pitchMin = Mathf.Min(growlPitchRange.x, growlPitchRange.y);
        float pitchMax = Mathf.Max(growlPitchRange.x, growlPitchRange.y);
        audioSource.pitch = Random.Range(pitchMin, pitchMax);
        audioSource.PlayOneShot(clip, Mathf.Clamp01(growlVolume));
    }

    // Called by DinoAI when entering alert-roar state.
    public void PlayAlertRoar()
    {
        if (audioSource == null)
        {
            return;
        }

        float ignoreWindow = Mathf.Max(0f, alertRoarDuplicateIgnoreWindowSeconds);
        if (Time.time - lastAlertRoarPlayTime < ignoreWindow)
        {
            return;
        }

        AudioClip clip = null;
        if (alertRoarClips != null && alertRoarClips.Length > 0)
        {
            clip = alertRoarClips[Random.Range(0, alertRoarClips.Length)];
        }
        else if (growlClips != null && growlClips.Length > 0)
        {
            // Fallback so roar still works even if dedicated roar clips are not assigned yet.
            clip = growlClips[Random.Range(0, growlClips.Length)];
        }

        if (clip == null)
        {
            return;
        }

        if (!CanPlayForAnyListener(GetMaxAudibleDistance(alertRoarDistanceMultiplier, fallbackAlertRoarMaxDistance)))
        {
            return;
        }

        float pitchMin = Mathf.Min(alertRoarPitchRange.x, alertRoarPitchRange.y);
        float pitchMax = Mathf.Max(alertRoarPitchRange.x, alertRoarPitchRange.y);
        audioSource.pitch = Random.Range(pitchMin, pitchMax);
        audioSource.PlayOneShot(clip, Mathf.Clamp01(alertRoarVolume));
        lastAlertRoarPlayTime = Time.time;

        float radius = Mathf.Max(0f, alertRoarStimulusRadius);
        if (radius > 0f)
        {
            WorldSoundStimulus.Emit(transform.position, radius);
        }
    }

    private bool CanPlayForAnyListener(float maxDistance)
    {
        if (!useDistanceCulling || maxDistance <= 0f)
        {
            return true;
        }

        float maxDistanceSqr = maxDistance * maxDistance;
        AudioListener[] listeners = GetCachedListeners();
        if (listeners == null || listeners.Length == 0)
        {
            return true;
        }

        Vector3 sourcePos = transform.position;
        for (int i = 0; i < listeners.Length; i++)
        {
            AudioListener listener = listeners[i];
            if (listener == null || !listener.enabled || !listener.gameObject.activeInHierarchy)
            {
                continue;
            }

            float sqrDist = (listener.transform.position - sourcePos).sqrMagnitude;
            if (sqrDist <= maxDistanceSqr)
            {
                return true;
            }
        }

        return false;
    }

    private static AudioListener[] GetCachedListeners()
    {
        if (cachedListeners == null || Time.time >= nextListenerRefreshTime)
        {
            cachedListeners = FindObjectsByType<AudioListener>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            nextListenerRefreshTime = Time.time + ListenerRefreshIntervalSeconds;
        }

        return cachedListeners;
    }

    private float GetMaxAudibleDistance(float multiplier, float fallbackDistance)
    {
        float safeMultiplier = Mathf.Max(0.01f, multiplier);
        if (useAudioSourceDistanceSettings && audioSource != null)
        {
            // MinDistance controls where falloff starts; MaxDistance is the audible cap.
            return Mathf.Max(0f, audioSource.maxDistance) * safeMultiplier;
        }

        return Mathf.Max(0f, fallbackDistance) * safeMultiplier;
    }
}
