using System;
using UnityEngine;

namespace LastWard.Player
{
    public class PlayerInventory : MonoBehaviour
    {
        public static PlayerInventory Local { get; private set; }

        private const int SlotCount = 2;
        private readonly string[] slots = new string[SlotCount];

        [SerializeField] private PlayerInputReader input;

        public int SelectedSlot { get; private set; }
        public bool IsFull => Array.TrueForAll(slots, s => !string.IsNullOrEmpty(s));

        public event Action InventoryChanged;

        private void Awake() => Local = this;

        private void OnEnable()
        {
            input.InventorySlot1Pressed += OnSlot1;
            input.InventorySlot2Pressed += OnSlot2;
        }

        private void OnDisable()
        {
            input.InventorySlot1Pressed -= OnSlot1;
            input.InventorySlot2Pressed -= OnSlot2;
        }

        private void OnDestroy()
        {
            if (Local == this) Local = null;
        }

        public bool TryAddItem(string itemId)
        {
            for (int i = 0; i < SlotCount; i++)
            {
                if (string.IsNullOrEmpty(slots[i]))
                {
                    slots[i] = itemId;
                    InventoryChanged?.Invoke();
                    return true;
                }
            }
            return false;
        }

        public string GetSlot(int index) => slots[index];

        private void OnSlot1() => SelectSlot(0);
        private void OnSlot2() => SelectSlot(1);

        private void SelectSlot(int index)
        {
            SelectedSlot = index;
            InventoryChanged?.Invoke();
        }
    }
}
