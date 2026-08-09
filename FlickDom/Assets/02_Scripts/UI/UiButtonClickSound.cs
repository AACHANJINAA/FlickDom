using UnityEngine;
using UnityEngine.UI;

namespace FlickDom.Gameplay
{
    internal static class UiButtonClickSound
    {
        private const string AudioObjectName = "UI Button Click Audio";
        private const string ResourcePath = "Audio/Button_click";
        private const float VolumeScale = 1f;

        private static AudioSource cachedAudioSource;
        private static AudioClip cachedClip;

        public static void Attach(Button button)
        {
            if (button == null)
            {
                return;
            }

            button.onClick.AddListener(Play);
        }

        public static void Play()
        {
            EnsureAudioSource();
            EnsureClip();

            if (cachedAudioSource == null || cachedClip == null)
            {
                return;
            }

            cachedAudioSource.PlayOneShot(cachedClip, VolumeScale);
        }

        private static void EnsureAudioSource()
        {
            if (cachedAudioSource != null)
            {
                ConfigureAudioSource(cachedAudioSource);
                return;
            }

            GameObject audioObject = GameObject.Find(AudioObjectName);
            if (audioObject == null)
            {
                audioObject = new GameObject(AudioObjectName);
                Object.DontDestroyOnLoad(audioObject);
            }

            if (!audioObject.TryGetComponent(out cachedAudioSource))
            {
                cachedAudioSource = audioObject.AddComponent<AudioSource>();
            }

            ConfigureAudioSource(cachedAudioSource);
        }

        private static void ConfigureAudioSource(AudioSource audioSource)
        {
            if (audioSource == null)
            {
                return;
            }

            audioSource.gameObject.SetActive(true);
            audioSource.enabled = true;
            audioSource.playOnAwake = false;
            audioSource.loop = false;
            audioSource.spatialBlend = 0f;
            audioSource.volume = 1f;
            audioSource.mute = false;
            audioSource.ignoreListenerPause = true;
        }

        private static void EnsureClip()
        {
            if (cachedClip != null)
            {
                return;
            }

            cachedClip = Resources.Load<AudioClip>(ResourcePath);
            if (cachedClip == null)
            {
                Debug.LogWarning("[UI Audio] Could not load Button_click from Resources/Audio.", null);
            }
        }
    }
}
