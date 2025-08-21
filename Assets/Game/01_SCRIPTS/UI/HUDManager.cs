using UnityEngine;

namespace junklite
{
   
    public class HUDManager : MonoBehaviour
    {
        [Header("UI Binder")]
        [SerializeField] private junklite.UI.UIBinder uiBinder;

        [SerializeField] private PlayerCharacter currentPlayer;

        private void Awake()
        {
            // Auto-find UI binder if not assigned
            if (uiBinder == null)
                uiBinder = GetComponent<junklite.UI.UIBinder>();
        }

        public void SetTarget(PlayerCharacter player)
        {
            currentPlayer = player;

            // Use the UI binder to connect to player
            if (uiBinder != null)
            {
                uiBinder.SetTarget(player);
            }

            Debug.Log("HUD connected to player via UI Binder");
        }

        public void UpdateCustomStatus(string statusName, bool active)
        {
            if (uiBinder != null)
            {
                uiBinder.UpdateCustomBinding(statusName, active);
            }
        }
    }
}