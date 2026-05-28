using UnityEngine;

namespace ShatterStone
{
[RequireComponent(typeof(AudioSource))]
public class MiningNodeAudio : MonoBehaviour
{
    [Header("Clip Pools")]
    public AudioClip[] impactClips;
    public AudioClip[] shatterClips;

    [Header("Audio Settings")]
    public float volume = 1f;
    public Vector2 pitchRange = new Vector2(0.95f, 1.05f);

    private AudioSource audioSource;


    // Audio manager for Shatter Stone mining nodes. Should be used in conjuction with OreNode.cs


    void Awake()
    {
        audioSource = GetComponent<AudioSource>();
        audioSource.spatialBlend = 1f; // 3D sound
        audioSource.playOnAwake = false;
    }

    public void PlayImpactSound()
    {
        PlayRandomClip(impactClips);
    }

    public void PlayShatterSound()
    {
        PlayRandomClip(shatterClips);
    }

    private void PlayRandomClip(AudioClip[] clipArray)
    {
        if (clipArray == null || clipArray.Length == 0) return;

        var clip = clipArray[Random.Range(0, clipArray.Length)];
        audioSource.pitch = Random.Range(pitchRange.x, pitchRange.y);
        audioSource.PlayOneShot(clip, volume);
    }
}
}
