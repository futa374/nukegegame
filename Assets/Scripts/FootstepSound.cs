using UnityEngine;

/// <summary>
/// 移動している間だけ足音（ループ音源）を鳴らし、止まると一時停止する。
/// 移動するオブジェクト（人）にアタッチ。AudioSource が無ければ自動追加。
/// </summary>
public class FootstepSound : MonoBehaviour
{
    [Tooltip("足音（連続歩行のループ音）")]
    public AudioClip footstep;
    [Range(0f, 1f)] public float volume = 0.8f;
    [Tooltip("これ以上の速さで“歩行中”とみなす")]
    public float moveThreshold = 0.05f;

    AudioSource _audio;
    Vector3 _lastPos;

    void Awake()
    {
        _audio = GetComponent<AudioSource>();
        if (_audio == null) _audio = gameObject.AddComponent<AudioSource>();
        _audio.playOnAwake = false;
        _audio.loop = true;
        _audio.spatialBlend = 0f;
        if (_audio.clip == null && footstep != null) _audio.clip = footstep;
        _lastPos = transform.position;
    }

    void Update()
    {
        if (_audio.clip == null) _audio.clip = footstep;
        _audio.volume = volume;

        Vector3 d = transform.position - _lastPos;
        _lastPos = transform.position;
        float speed = Time.deltaTime > 0f ? new Vector3(d.x, 0f, d.z).magnitude / Time.deltaTime : 0f;
        bool moving = speed > moveThreshold && _audio.clip != null;

        if (moving)
        {
            if (!_audio.isPlaying)
            {
                if (_audio.time > 0f) _audio.UnPause();
                else _audio.Play();
            }
        }
        else
        {
            if (_audio.isPlaying) _audio.Pause();
        }
    }
}
