using UnityEngine;

/// <summary>
/// Marks where the player should be placed when this scene becomes active. Put it on the
/// scene's PlayerSpawn object - forward is the direction the player will face.
///
/// The XR rig lives in Bootstrap and persists across scene loads, so nothing moves the player
/// unless something explicitly does. Without this, entering the tutorial a second time leaves
/// them wherever they had walked to.
///
/// Registers itself statically rather than being wired up, because the scene holding it is
/// loaded at runtime and cannot be referenced from Bootstrap in the Inspector.
/// </summary>
[DisallowMultipleComponent]
public class SceneSpawnPoint : MonoBehaviour
{
    /// <summary>Most recently enabled spawn point, i.e. the one in the newest loaded scene.</summary>
    public static SceneSpawnPoint Active { get; private set; }

    [Tooltip("If several scenes are loaded at once, the highest priority wins.")]
    [SerializeField] private int priority = 0;

    public int Priority => priority;

    private void OnEnable()
    {
        if (Active == null || priority >= Active.priority) Active = this;
    }

    private void OnDisable()
    {
        if (Active == this) Active = null;
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        // The spawn object has no renderer, so without this it is invisible while authoring.
        Gizmos.color = new Color(0.3f, 1f, 0.5f, 0.9f);
        Gizmos.DrawWireSphere(transform.position + Vector3.up * 0.9f, 0.25f);
        Gizmos.DrawLine(transform.position, transform.position + Vector3.up * 1.8f);
        Gizmos.DrawRay(transform.position + Vector3.up * 0.9f, transform.forward * 1f);
    }
#endif
}
