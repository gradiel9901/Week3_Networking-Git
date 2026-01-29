using UnityEngine;
using UnityEngine.UI;

namespace Com.MyCompany.MyGame
{
    public class HPBarController : MonoBehaviour
    {
        [SerializeField] private Slider hpSlider;

        private void Start()
        {
            if (hpSlider == null)
            {
                hpSlider = GetComponent<Slider>();
            }
        }

        public void UpdateHealth(int currentHealth, int maxHealth)
        {
            if (hpSlider != null)
            {
                hpSlider.maxValue = maxHealth;
                hpSlider.value = currentHealth;
            }
        }
    }
}
