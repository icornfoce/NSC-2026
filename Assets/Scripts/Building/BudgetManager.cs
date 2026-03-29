using UnityEngine;
using UnityEngine.Events;

namespace BuildingSimulation.Building
{
    /// <summary>
    /// Tracks the player's budget. Singleton.
    /// </summary>
    public class BudgetManager : MonoBehaviour
    {
        public static BudgetManager Instance { get; private set; }

        [Header("Budget")]
        [SerializeField] private float startingBudget = 10000f;

        private float _currentBudget;
        public float CurrentBudget => _currentBudget;

        /// <summary>
        /// Fired whenever the budget changes. Passes the new budget value.
        /// </summary>
        public UnityEvent<float> OnBudgetChanged;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            _currentBudget = startingBudget;
        }

        private void Start()
        {
            OnBudgetChanged?.Invoke(_currentBudget);
        }

        public bool CanAfford(float cost)
        {
            return _currentBudget >= cost;
        }

        public bool Deduct(float cost)
        {
            if (!CanAfford(cost)) return false;
            _currentBudget -= cost;
            OnBudgetChanged?.Invoke(_currentBudget);
            return true;
        }

        public void Refund(float amount)
        {
            _currentBudget += amount;
            OnBudgetChanged?.Invoke(_currentBudget);
        }

        public void ResetBudget()
        {
            _currentBudget = startingBudget;
            OnBudgetChanged?.Invoke(_currentBudget);
        }
    }
}
