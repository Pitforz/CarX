using UnityEngine;
using UnityEngine.UI;

namespace CarX.Flow
{
    /// <summary>
    /// Повесьте на ЛЮБУЮ кнопку (UI Button) и перетащите в поле Sequence файл катсцены: по клику запустится переход.
    /// Свою кнопку или картинку-кнопку можно подключить и без этого компонента:
    /// в Inspector кнопки (On Click) добавьте PlayCutsceneButton → Trigger.
    /// </summary>
    [AddComponentMenu("CarX/Кнопка запуска катсцены")]
    public class PlayCutsceneButton : MonoBehaviour
    {
        [Tooltip("Какую катсцену перехода запустить")]
        public CutsceneSequence sequence;

        void Awake()
        {
            var button = GetComponent<Button>();
            if (button != null) button.onClick.AddListener(Trigger);
        }

        public void Trigger()
        {
            if (SceneFlow.Busy) return;
            SceneFlow.Play(sequence);
        }
    }
}
