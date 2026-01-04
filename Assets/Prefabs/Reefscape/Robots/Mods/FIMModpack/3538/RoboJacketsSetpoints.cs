using UnityEngine;

namespace Prefabs.Reefscape.Robots.Mods.FIMModpack._3538
{
    [CreateAssetMenu(fileName = "Setpoint", menuName = "Robot/RoboJacketsSetpoints", order = 0)]
    public class RoboJacketsSetpoints : ScriptableObject
    {
        [Tooltip("Inches")] public float elevatorHeight;
        [Tooltip("Deg")] public float algaeArmAngle;
    }
}