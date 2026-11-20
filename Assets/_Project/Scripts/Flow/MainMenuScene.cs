using UnityEngine;

namespace CarX.Flow
{
    /// <summary>Лежит в сцене меню: освобождает курсор и снимает паузу, чтобы кнопки работали. Остальное в меню делайте как угодно.</summary>
    [AddComponentMenu("CarX/Сцена меню (курсор)")]
    public class MainMenuScene : MonoBehaviour
    {
        void Awake()
        {
            Time.timeScale = 1f;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }
}
