using System;
using System.Collections.Generic;
using UnityEngine;

namespace CarX.NPC
{
    internal sealed class DialogueLipSync
    {
        const int FramesPerSecond = 60;
        const float Anticipation = 0.025f;

        static readonly Vector4 Open = new(0.94f, 0f, 0.08f, 0f);
        static readonly Vector4 Round = new(0.52f, 0.94f, 0f, 0f);
        static readonly Vector4 Spread = new(0.38f, 0f, 0.82f, 0f);
        static readonly Vector4 Closed = new(0f, 0f, 0f, 0.88f);
        static readonly Vector4 LipAndTeeth = new(0.15f, 0f, 0.18f, 0.58f);
        static readonly Vector4 Consonant = new(0.28f, 0.06f, 0.22f, 0f);

        sealed class Envelope
        {
            internal readonly float[] Levels;
            internal readonly float[] SpeechTime;

            internal Envelope(float[] levels, float[] speechTime)
            {
                Levels = levels;
                SpeechTime = speechTime;
            }
        }

        readonly Dictionary<AudioClip, Envelope> _envelopes = new();
        readonly List<float> _cueTimes = new();
        readonly List<Vector4> _cuePoses = new();
        Envelope _envelope;

        internal void Prepare(string text, AudioClip clip)
        {
            _envelope = null;
            if (clip != null && clip.loadState == AudioDataLoadState.Unloaded)
                clip.LoadAudioData();
            if (clip != null && !_envelopes.TryGetValue(clip, out _envelope))
            {
                _envelope = ReadEnvelope(clip);
                _envelopes.Add(clip, _envelope);
            }

            _cueTimes.Clear();
            _cuePoses.Clear();
            AddCue(0f, Vector4.zero);
            float offset = 0f;
            foreach (char letter in text)
            {
                var (pose, duration) = CueFor(char.ToLowerInvariant(letter));
                AddCue(offset + duration * 0.5f, pose);
                offset += duration;
            }
            AddCue(offset + 0.35f, Vector4.zero);

            float end = _cueTimes[^1];
            for (int i = 1; i < _cueTimes.Count; i++)
                _cueTimes[i] /= end;
        }

        internal Vector4 Evaluate(float elapsed, float duration)
        {
            float time = elapsed + Anticipation;
            float progress = Mathf.Clamp01(time / duration);
            float strength = 0.72f;
            if (_envelope != null)
            {
                float frame = time * FramesPerSecond;
                progress = Interpolate(_envelope.SpeechTime, frame);
                strength = Interpolate(_envelope.Levels, frame);
            }

            int index = _cueTimes.BinarySearch(progress);
            int next = Mathf.Clamp(index < 0 ? ~index : index, 1, _cueTimes.Count - 1);
            float blend = Mathf.InverseLerp(_cueTimes[next - 1], _cueTimes[next], progress);
            return Vector4.Lerp(_cuePoses[next - 1], _cuePoses[next], blend) * strength;
        }

        void AddCue(float time, Vector4 pose)
        {
            _cueTimes.Add(time);
            _cuePoses.Add(pose);
        }

        static (Vector4 pose, float length) CueFor(char letter) => letter switch
        {
            'а' or 'я' => (Open, 1.5f),
            'о' or 'у' or 'ё' or 'ю' => (Round, 1.5f),
            'э' or 'е' or 'и' or 'ы' => (Spread, 1.4f),
            'м' or 'б' or 'п' => (Closed, 0.95f),
            'ф' or 'в' => (LipAndTeeth, 0.9f),
            ' ' => (Vector4.zero, 0.18f),
            'ь' or 'ъ' => (Consonant, 0.1f),
            '.' or ',' or '!' or '?' or '…' or '—' => (Vector4.zero, 0.55f),
            _ => (Consonant, 0.95f)
        };

        static Envelope ReadEnvelope(AudioClip clip)
        {
            if (clip.loadState != AudioDataLoadState.Loaded || clip.samples <= 0 || clip.frequency <= 0)
                return null;

            var samples = new float[clip.samples * clip.channels];
            if (!clip.GetData(samples, 0))
                return null;

            int count = Mathf.Max(1, Mathf.CeilToInt(clip.length * FramesPerSecond));
            var levels = new float[count];
            int radius = Mathf.Max(1, clip.frequency / FramesPerSecond);
            for (int frame = 0; frame < count; frame++)
            {
                int center = frame * clip.frequency / FramesPerSecond;
                int start = Mathf.Max(0, center - radius) * clip.channels;
                int end = Mathf.Min(clip.samples, center + radius) * clip.channels;
                float squared = 0f;
                for (int i = start; i < end; i++)
                    squared += samples[i] * samples[i];
                levels[frame] = Mathf.Sqrt(squared / Mathf.Max(1, end - start));
            }

            var sorted = (float[])levels.Clone();
            Array.Sort(sorted);
            float reference = Mathf.Max(0.004f, sorted[Mathf.FloorToInt((count - 1) * 0.88f)]);
            var speechTime = new float[count];
            float spoken = 0f;
            for (int i = 0; i < count; i++)
            {
                float normalized = levels[i] / reference;
                // Integrate speech separately from silence so the articulation follows the recorded pauses.
                spoken += Mathf.Lerp(0.045f, 1f, Mathf.InverseLerp(0.09f, 0.52f, normalized));
                speechTime[i] = spoken;
                float gate = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.06f, 0.47f, normalized));
                levels[i] = gate * Mathf.Lerp(0.51f, 1f, Mathf.Clamp01(normalized));
            }

            for (int i = 0; i < count; i++)
                speechTime[i] /= spoken;
            return new Envelope(levels, speechTime);
        }

        static float Interpolate(float[] values, float frame)
        {
            int index = Mathf.Clamp(Mathf.FloorToInt(frame), 0, values.Length - 1);
            return Mathf.Lerp(values[index], values[Mathf.Min(index + 1, values.Length - 1)], frame - index);
        }
    }
}
