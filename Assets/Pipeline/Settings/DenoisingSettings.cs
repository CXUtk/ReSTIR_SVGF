using UnityEngine;

namespace Assets.Pipeline.Settings
{
    [System.Serializable]
    public class DenoisingSettings
    {
        [SerializeField] [Range(0, 10f)] internal float SigmaZ = 1;
        [SerializeField] [Range(0, 10f)] internal float SigmaX = 1;
        [SerializeField] [Range(0, 10f)] internal float SigmaC = 4;
        [SerializeField] internal int SigmaN = 32;
        [SerializeField] [Range(0, 1f)] internal float TemporalFactor = 0.1f;
        [SerializeField] internal bool GroundTruth = false;
        [SerializeField] internal bool TemporalPlus = false;
        [SerializeField] internal bool EnableReSTIR = false;
    }
}