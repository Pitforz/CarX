using UnityEngine;

namespace CarX.NPC
{
    // Makes looping NPCs out of sync: random start time and slightly different speed.
    [RequireComponent(typeof(Animator))]
    public class NpcLoopVariation : MonoBehaviour
    {
        public Vector2 speedRange = new Vector2(0.9f, 1.1f);

        void Start()
        {
            var animator = GetComponent<Animator>();
            animator.speed = Random.Range(speedRange.x, speedRange.y);
            var state = animator.GetCurrentAnimatorStateInfo(0);
            animator.Play(state.fullPathHash, 0, Random.value);
        }
    }
}
