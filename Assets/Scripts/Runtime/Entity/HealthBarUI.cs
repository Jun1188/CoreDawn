using UnityEngine;
using UnityEngine.UI;

namespace CoreDawn.Entities
{
    public class HealthBarUI : MonoBehaviour
    {
        [SerializeField] private EntityView entity;
        [SerializeField] private Image fillImage; // UI의 전경 이미지 (Image Type: Filled)

        private void Awake()
        {
            if (entity == null)
                entity = GetComponentInParent<EntityView>();
        }

        /// <summary>
        /// 런타임 생성 바(WorldHealthBar)용 배선 — 인스펙터를 못 쓰는 경우의 통로.
        /// 이미 활성화된 뒤라면 구독을 새 대상으로 옮긴다.
        /// </summary>
        public void Bind(EntityView target, Image fill)
        {
            if (isActiveAndEnabled && entity != null)
                entity.OnHealthChanged -= UpdateHealthBar;

            entity = target;
            fillImage = fill;

            if (isActiveAndEnabled && entity != null)
            {
                entity.OnHealthChanged += UpdateHealthBar;
                UpdateHealthBar(entity.Health.CurrentHealth, entity.Health.MaxHealth);
            }
        }

        private void OnEnable()
        {
            if (entity != null)
            {
                entity.OnHealthChanged += UpdateHealthBar;
                // 활성화 시점의 현재 체력 즉시 반영
                UpdateHealthBar(entity.Health.CurrentHealth, entity.Health.MaxHealth);
            }
        }

        private void OnDisable()
        {
            if (entity != null)
            {
                entity.OnHealthChanged -= UpdateHealthBar;
            }
        }

        private void UpdateHealthBar(float currentHealth, float maxHealth)
        {
            if (fillImage != null && maxHealth > 0)
            {
                fillImage.fillAmount = currentHealth / maxHealth;
            }
        }
    }
}
