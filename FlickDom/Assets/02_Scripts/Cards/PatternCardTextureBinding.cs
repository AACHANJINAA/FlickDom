using System;
using UnityEngine;

namespace FlickDom.Gameplay
{
    [Serializable]
    public sealed class PatternCardTextureBinding
    {
        [SerializeField] private string cardId;
        [SerializeField] private Texture2D texture;

        public string CardId
        {
            get { return cardId; }
        }

        public Texture2D Texture
        {
            get { return texture; }
        }

        public bool Matches(PatternCardData card)
        {
            return card != null
                && texture != null
                && !string.IsNullOrEmpty(cardId)
                && string.Equals(cardId, card.CardId, StringComparison.Ordinal);
        }

        public static Texture2D Resolve(PatternCardTextureBinding[] bindings, PatternCardData card)
        {
            if (bindings == null || card == null)
            {
                return null;
            }

            for (int i = 0; i < bindings.Length; i++)
            {
                PatternCardTextureBinding binding = bindings[i];
                if (binding != null && binding.Matches(card))
                {
                    return binding.Texture;
                }
            }

            return null;
        }
    }
}
