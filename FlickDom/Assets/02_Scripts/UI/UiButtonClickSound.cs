using UnityEngine;
using UnityEngine.UI;

namespace FlickDom.Gameplay
{
    internal static class UiButtonClickSound
    {
        private const string AudioObjectName = "UI Button Click Audio";
        private const string ResourcePath = "Audio/Button_click";

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

            cachedAudioSource.PlayOneShot(cachedClip);
        }

        private static void EnsureAudioSource()
        {
            if (cachedAudioSource != null)
            {
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

            cachedAudioSource.playOnAwake = false;
            cachedAudioSource.loop = false;
            cachedAudioSource.spatialBlend = 0f;
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
                Debug.LogWarning("[UI Audio] Could not load Button_click_1 from Resources/Audio.", null);
            }
        }
    }
}
