using UnityEngine;

namespace Assets.Pipeline.Settings
{
    [System.Serializable]
    public class LightingSetting
    {
        [SerializeField]
        internal Cubemap EnvironmentMap;

        [SerializeField] [Range(0, 6)] internal int PathBounces = 1;
    }
}