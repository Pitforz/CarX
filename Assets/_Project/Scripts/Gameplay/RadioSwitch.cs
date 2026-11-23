using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(AudioSource))]
public class RadioSwitch : MonoBehaviour
{
    [SerializeField, Min(0f)] private float useDistance = 3f;

    private AudioSource music;
    private bool inRange;
    private bool paused;

    private void Awake()
    {
        music = GetComponent<AudioSource>();
    }

    private void Update()
    {
        Camera camera = Camera.main;
        inRange = camera != null && Time.timeScale > 0f &&
            (camera.transform.position - transform.position).sqrMagnitude <= useDistance * useDistance;

        if (!inRange || Keyboard.current?.yKey.wasPressedThisFrame != true)
            return;

        if (music.isPlaying)
        {
            music.Pause();
            paused = true;
        }
        else if (paused)
        {
            music.UnPause();
            paused = false;
        }
        else
        {
            music.Play();
        }
    }

    private void OnGUI()
    {
        if (!inRange || Time.timeScale == 0f)
            return;

        string prompt = music.isPlaying
            ? "Нажмите Y, чтобы выключить радио"
            : "Нажмите Y, чтобы включить радио";
        float width = Mathf.Min(350f, Screen.width);
        GUI.Box(new Rect((Screen.width - width) / 2f, Screen.height * 0.73f, width, 32f), prompt);
    }
}
