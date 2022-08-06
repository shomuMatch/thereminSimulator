using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class Theremin : MonoBehaviour
{
    [SerializeField] private OVRHand _leftHand;
    [SerializeField] private OVRHand _rightHand;
    [SerializeField] private OVRSkeleton _leftSkeleton;
    [SerializeField] private OVRSkeleton _rightSkeleton;
    [SerializeField] private Transform _pitchAntenna;
    [SerializeField] private Transform _volumeAntenna;
    [SerializeField] private TextMeshProUGUI _frequencyText;
    [SerializeField] private TextMeshProUGUI _gainText;
    private const float ROOT_FREQUENCY = 27.5f;
    private readonly string[] SCALE_LIST = { "A", "A#", "B", "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", };
    private const float PITCH_INDUCTANCE = 10.0e-6f; //音程コントロール側に使用するコイルのインダクタンス
    private const float PITCH_CAPACITANCE = 1.0e-9f; //音程コントロール側に使用するコンデンサの静電容量
    private const float VOLUME_INDUCTANCE = 1.0e-4f; //音量コントロール側に使用するコイルのインダクタンス
    private const float VOLUME_CAPACITANCE = 2.0e-11f; //音量コントロール側に使用するコンデンサの静電容量
    private const float VOLUME_RESISTANCE = 1.0e1f; //音量コントロール側に使用する抵抗器の抵抗
    private const float PERMITTIVITY = 8.854e-12f; //真空の誘電率（真空中で演奏することにします）
    private readonly float[] HAND_AREAS = { //手の各部位の面積
        11.0e-4f,
        15.0e-4f,
        17.0e-4f,
        15.0e-4f,
        13.0e-4f,
        80.0e-4f
    };
    private float fix_frequency; //固定発振器の発振周波数
    private float increment;
    private float phase;
    private float sampling_frequency;
    private float frequency;
    private float gain;

    void OnAudioFilterRead(float[] data, int channels)
    {
        increment = frequency * 2 * Mathf.PI / sampling_frequency;

        for (var i = 0; i < data.Length; i = i + channels)
        {
            phase = phase + increment;
            data[i] = (gain * Mathf.Sin(phase));
            if (channels == 2) data[i + 1] = data[i];
            if (phase > 2 * Mathf.PI) phase = 0;
        }
    }
    // Start is called before the first frame update
    void Start()
    {
        sampling_frequency = AudioSettings.outputSampleRate;
        fix_frequency = getOscillatingFrequency(PITCH_INDUCTANCE, PITCH_CAPACITANCE);
    }

    // Update is called once per frame
    void Update()
    {
        frequency = getPitch();
        string scale = getScale(frequency);
        gain = Mathf.Min(getVolume() * 3, 1);
        if (gain < 0.1f)
        {
            gain = 0;
        }
        _frequencyText.text = frequency.ToString("0000.0") + " Hz : " + scale;
        _gainText.text = gain.ToString();
    }

    private string getScale(float frequency)
    {
        int octave = (int)Mathf.Floor(Mathf.Log(Mathf.Floor(frequency / ROOT_FREQUENCY), 2));
        float rootScale = ROOT_FREQUENCY * Mathf.Pow(2, octave);
        int scale = (int)Mathf.Log(frequency / rootScale, Mathf.Pow(2, 1.0f / 12.0f));
        return SCALE_LIST[Mathf.Max(Mathf.Min(scale, 11), 0)] + octave.ToString();
    }

    private Vector3[] getHandPositions(OVRSkeleton skeleton)
    {
        if (!(_leftHand.IsTracked && _rightHand.IsTracked))
        {
            return new Vector3[] { };
        }
        Vector3 palmPosition = (
            skeleton.Bones[(int)OVRSkeleton.BoneId.Hand_Thumb0].Transform.position +
            skeleton.Bones[(int)OVRSkeleton.BoneId.Hand_Index1].Transform.position +
            skeleton.Bones[(int)OVRSkeleton.BoneId.Hand_Middle1].Transform.position +
            skeleton.Bones[(int)OVRSkeleton.BoneId.Hand_Ring1].Transform.position +
            skeleton.Bones[(int)OVRSkeleton.BoneId.Hand_Pinky0].Transform.position
            ) / 5;
        return new Vector3[]{
            skeleton.Bones[(int) OVRSkeleton.BoneId.Hand_Thumb3].Transform.position,
            skeleton.Bones[(int) OVRSkeleton.BoneId.Hand_Index3].Transform.position,
            skeleton.Bones[(int) OVRSkeleton.BoneId.Hand_Middle3].Transform.position,
            skeleton.Bones[(int) OVRSkeleton.BoneId.Hand_Ring3].Transform.position,
            skeleton.Bones[(int) OVRSkeleton.BoneId.Hand_Pinky3].Transform.position,
            palmPosition
            };
    }
    private float getPitch()
    {
        Vector3[] handPositions = getHandPositions(_rightSkeleton);
        Vector3 antennaPosition = _pitchAntenna.position;
        float capacitance = 0;
        for (int i = 0; i < handPositions.Length; i++)
        {
            handPositions[i].y = antennaPosition.y;
            float dist = Vector3.Distance(handPositions[i], antennaPosition);
            capacitance += HAND_AREAS[i] / dist;
        }
        capacitance *= PERMITTIVITY;
        capacitance += PITCH_CAPACITANCE;
        return Mathf.Abs(fix_frequency - getOscillatingFrequency(PITCH_INDUCTANCE, capacitance));
    }

    private float getVolume()
    {
        Vector3[] handPositions = getHandPositions(_leftSkeleton);
        Vector3 antennaPosition = _volumeAntenna.position;
        float capacitance = 0;
        for (int i = 0; i < handPositions.Length; i++)
        {
            float dist = Mathf.Abs(handPositions[i].y - antennaPosition.y);
            capacitance += HAND_AREAS[i] / dist;
        }
        capacitance *= PERMITTIVITY;
        capacitance += VOLUME_CAPACITANCE;
        float gainFrequency = getOscillatingFrequency(VOLUME_INDUCTANCE, capacitance);
        float gain =
            1 /
                Mathf.Sqrt(
                    1 +
                    Mathf.Pow(
                        1 / (2 * Mathf.PI * gainFrequency * VOLUME_RESISTANCE * VOLUME_CAPACITANCE) -
                        2 * Mathf.PI * gainFrequency * VOLUME_INDUCTANCE / VOLUME_RESISTANCE,
                        2
                    )
                )
        ;
        return gain;
    }

    private float getOscillatingFrequency(float inductance, float capacitance)
    {
        return 1 / (2 * Mathf.PI * Mathf.Sqrt(inductance * capacitance));
    }
}
