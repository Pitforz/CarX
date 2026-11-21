using System.Collections.Generic;
using UnityEngine;

namespace CarX.NPC
{
    [DisallowMultipleComponent]
    public sealed class NpcSpeechState : MonoBehaviour
    {
        [Tooltip("True while this NPC is delivering a line. Ambient voice emitters can use this to avoid overlap.")]
        public bool is_talking;

        static readonly string[] ShapeNames =
            { "CarX_MouthOpen", "CarX_MouthRound", "CarX_MouthSpread", "CarX_MouthClosed" };

        Face[] _faces;
        DialogueLipSync _sync;
        AudioSource _voice;
        AudioClip _clip;
        Vector4 _pose;
        float _startsAt, _duration;
        int _startFrame;
        bool _lineActive;

        // RequireComponent does not backfill components on existing scene objects.
        internal static NpcSpeechState GetOrAdd(Component npc) =>
            npc.TryGetComponent<NpcSpeechState>(out var state) ? state : npc.gameObject.AddComponent<NpcSpeechState>();

        internal void SetTalking(bool talking)
        {
            is_talking = talking && isActiveAndEnabled;
            if (!is_talking)
                ClearLine();
        }

        internal void BeginLine(string text, float duration, AudioSource voice = null, AudioClip clip = null)
        {
            if (!isActiveAndEnabled || duration <= 0f || string.IsNullOrWhiteSpace(text))
                return;

            is_talking = true;
            _faces ??= FindFaces();
            _voice = voice;
            _clip = clip;
            _startsAt = Time.time;
            _duration = clip != null ? clip.length : duration;
            _startFrame = Time.frameCount;
            _lineActive = true;
            if (_faces.Length == 0)
                return;

            _sync ??= new DialogueLipSync();
            _sync.Prepare(text, clip);
        }

        internal void EndLine(AudioSource owner)
        {
            if (_lineActive && _voice == owner)
                SetTalking(false);
        }

        internal static string SpokenText(string subtitle)
        {
            int speaker = subtitle.IndexOf(':');
            int hint = subtitle.IndexOf('[');
            int start = speaker >= 0 && (hint < 0 || speaker < hint) ? speaker + 1 : 0;
            return subtitle[start..(hint < 0 ? subtitle.Length : hint)].Trim();
        }

        void LateUpdate()
        {
            Vector4 target = Vector4.zero;
            if (_lineActive)
            {
                float elapsed = Time.time - _startsAt;
                if (_clip != null)
                {
                    bool playing = _voice != null && _voice.clip == _clip &&
                        (_voice.isPlaying || (AudioListener.pause && !_voice.ignoreListenerPause));
                    if (playing)
                        elapsed = (float)_voice.timeSamples / _clip.frequency;
                    else if (Time.frameCount > _startFrame + 1)
                        elapsed = _duration;
                    else
                        elapsed = 0f;
                }

                if (elapsed >= _duration)
                    SetTalking(false);
                else if (_sync != null)
                    target = _sync.Evaluate(elapsed, _duration);
            }

            if (_pose == Vector4.zero && target == Vector4.zero)
                return;

            _pose = Vector4.Lerp(_pose, target, 1f - Mathf.Exp(-32f * Time.deltaTime));
            if (target == Vector4.zero && _pose.sqrMagnitude < 0.00001f)
                _pose = Vector4.zero;
            ApplyPose();
        }

        Face[] FindFaces()
        {
            var faces = new List<Face>();
            foreach (var renderer in GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mesh = renderer.sharedMesh;
                if (mesh == null || mesh.GetBlendShapeIndex(ShapeNames[0]) < 0)
                    continue;

                var indices = new int[ShapeNames.Length];
                for (int i = 0; i < indices.Length; i++)
                    indices[i] = mesh.GetBlendShapeIndex(ShapeNames[i]);
                faces.Add(new Face(renderer, indices));
            }
            return faces.ToArray();
        }

        void ApplyPose()
        {
            foreach (var face in _faces)
            {
                if (face.Renderer == null)
                    continue;
                for (int i = 0; i < face.Indices.Length; i++)
                    if (face.Indices[i] >= 0)
                        face.Renderer.SetBlendShapeWeight(face.Indices[i], Mathf.Clamp01(_pose[i]) * 100f);
            }
        }

        void ClearLine()
        {
            _lineActive = false;
            _voice = null;
            _clip = null;
        }

        void OnDisable()
        {
            SetTalking(false);
            if (_pose == Vector4.zero)
                return;
            _pose = Vector4.zero;
            ApplyPose();
        }

        readonly struct Face
        {
            internal readonly SkinnedMeshRenderer Renderer;
            internal readonly int[] Indices;

            internal Face(SkinnedMeshRenderer renderer, int[] indices) =>
                (Renderer, Indices) = (renderer, indices);
        }
    }
}
