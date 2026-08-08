using UnityEngine;

namespace FlickDom.Gameplay
{
    public static class FlickDomBgmPlayer
    {
        private const string StartBgmResourcePath = "Audio/BGM/Start_BGM";
        private const string InGameBgmResourcePath = "Audio/BGM/InGame_BGM";
        private const string BgmAudioObjectName = "FlickDom BGM Audio";
        private const float BgmVolume = 0.7f;

        private static AudioSource bgmAudioSource;
        private static AudioClip startBgmClip;
        private static AudioClip inGameBgmClip;
        private static string currentResourcePath;

        public static void PlayStartBgm()
        {
            PlayLoop(StartBgmResourcePath, ref startBgmClip);
        }

        public static void PlayInGameBgm()
        {
            PlayLoop(InGameBgmResourcePath, ref inGameBgmClip);
        }

        private static void PlayLoop(string resourcePath, ref AudioClip clip)
        {
            EnsureAudioSource();
            EnsureAudioClip(ref clip, resourcePath);
            if (bgmAudioSource == null || clip == null)
            {
                return;
            }

            if (bgmAudioSource.isPlaying
                && bgmAudioSource.clip == clip
                && string.Equals(currentResourcePath, resourcePath, System.StringComparison.Ordinal))
            {
                return;
            }

            bgmAudioSource.clip = clip;
            bgmAudioSource.loop = true;
            bgmAudioSource.Play();
            currentResourcePath = resourcePath;
        }

        private static void EnsureAudioSource()
        {
            if (bgmAudioSource != null)
            {
                return;
            }

            GameObject audioObject = GameObject.Find(BgmAudioObjectName);
            if (audioObject == null)
            {
                audioObject = new GameObject(BgmAudioObjectName);
                Object.DontDestroyOnLoad(audioObject);
            }

            if (!audioObject.TryGetComponent(out bgmAudioSource))
            {
                bgmAudioSource = audioObject.AddComponent<AudioSource>();
            }

            bgmAudioSource.playOnAwake = false;
            bgmAudioSource.loop = true;
            bgmAudioSource.spatialBlend = 0f;
            bgmAudioSource.volume = BgmVolume;
        }

        private static void EnsureAudioClip(ref AudioClip clip, string resourcePath)
        {
            if (clip != null)
            {
                return;
            }

            clip = Resources.Load<AudioClip>(resourcePath);
            if (clip == null)
            {
                Debug.LogWarning("[BGM] Could not load sound at Resources/" + resourcePath + ".", null);
            }
        }
    }
}
