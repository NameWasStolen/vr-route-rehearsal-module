using UnityEngine;

/// <summary>
/// What kind of ground this is, for footstep audio. Kept deliberately small - these are the
/// surfaces the tutorial route actually crosses.
/// </summary>
public enum SurfaceKind
{
    Grass,
    Paving,
    Asphalt,
}

/// <summary>
/// Optional explicit label on a ground collider.
///
/// FootstepAudio works without this - it falls back to matching the renderer's material name -
/// but material-name matching is a guess that breaks quietly the moment a material is renamed.
/// Where it matters, put one of these on the collider and the guess is skipped entirely.
///
/// The generated environment does not have these yet, which is why the fallback exists: you
/// can have working footsteps today without rebuilding the scene and re-baking the lighting.
/// If you do rebuild later, adding one line to CreateSlab is the more robust answer.
/// </summary>
[DisallowMultipleComponent]
public class SurfaceMarker : MonoBehaviour
{
    [SerializeField] private SurfaceKind kind = SurfaceKind.Paving;
    public SurfaceKind Kind => kind;
}
