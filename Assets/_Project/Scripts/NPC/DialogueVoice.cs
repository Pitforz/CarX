using System.Collections;
using UnityEngine;

namespace CarX.NPC
{
    internal static class DialogueVoice
    {
        internal static AudioSource CreateSource(Transform speaker, string name, float minDistance, float maxDistance)
        {
            var emitter = new GameObject(name);
            emitter.transform.SetParent(speaker, false);
            emitter.transform.localPosition = Vector3.up * 1.55f;
            var voice = emitter.AddComponent<AudioSource>();
            voice.playOnAwake = false;
            voice.spatialBlend = 1f;
            voice.dopplerLevel = 0f;
            voice.rolloffMode = AudioRolloffMode.Linear;
            voice.minDistance = minDistance;
            voice.maxDistance = maxDistance;

            var reverb = emitter.AddComponent<AudioReverbFilter>();
            reverb.reverbPreset = AudioReverbPreset.User;
            reverb.room = -1800f;
            reverb.roomHF = -1300f;
            reverb.decayTime = 0.6f;
            reverb.decayHFRatio = 0.6f;
            reverb.reflectionsLevel = -400f;
            reverb.reflectionsDelay = 0.034f;
            reverb.reverbLevel = -250f;
            reverb.reverbDelay = 0.025f;
            return voice;
        }

        internal static AudioSource GetPlayerSource(AudioSource voice, Transform player)
        {
            if (voice != null && voice.transform.parent == player)
                return voice;
            if (voice != null)
                Object.Destroy(voice.gameObject);
            return CreateSource(player, "Alexey Voice", minDistance: 1.5f, maxDistance: 13f);
        }

        internal static IEnumerator Play(AudioSource voice, AudioClip clip)
        {
            voice.clip = clip;
            voice.Play();
            do
            {
                yield return null;
            }
            while (voice != null && (voice.isPlaying || (AudioListener.pause && !voice.ignoreListenerPause)));
        }

        internal static AudioClip GetClip(AudioClip[] clips, int index) =>
            clips != null && index < clips.Length ? clips[index] : null;
    }
}
