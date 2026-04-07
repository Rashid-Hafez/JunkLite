using UnityEngine;

namespace RayFire
{
    /// <summary>
    /// Interactive helper component for Rayfire Shatter component.
    /// </summary>
    [ExecuteInEditMode]
    public class RFInteractiveHelper : MonoBehaviour
    {
        [HideInInspector] public bool           previewGo;
        public                   GameObject     shatterGo;
        public                   RayfireShatter shatter;
        public                   bool           interactive;

        // Update is called once per frame
        void Update()
        {
            InitInteractiveChange();
        }

        void OnDestroy()
        {
            interactive = false;
            InitInteractiveChange();
        }

        void InitInteractiveChange()
        {
            if (interactive == false)
                return;

            // Shatter object destroyed
            if (shatterGo == null)
            {
                if (previewGo == true)
                    DestroyImmediate (gameObject);
                DestroyImmediate (this);
                return;
            }

            // Shatter component destroyed
            if (shatter == null)
            {
                if (previewGo == true)
                {
                    shatterGo.SetActive (true);
                    DestroyImmediate (gameObject);
                }
                DestroyImmediate (this);
                return;
            }

            // Interactive mode disabled
            if (shatter.interactive == false)
            {
                if (previewGo == true)
                {
                    shatterGo.SetActive (true);
                    DestroyImmediate (gameObject);
                }
                DestroyImmediate (this);
                return;
            }

            // No update for Interactive preview object
            if (previewGo == true)
                return;

            // Interactive helper transform changed
            if (transform.hasChanged == false)
                return;

            // Init change
            shatter.InteractiveChange();
            transform.hasChanged = false;
        }
    }
}