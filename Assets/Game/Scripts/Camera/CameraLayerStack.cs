using System;
using System.Collections.Generic;

namespace junklite
{
    /// <summary>
    /// Decides which camera is live. Highest non-empty layer wins:
    /// Cinematic (newest lease) > Zone > Lane.
    /// Plain C# so the rules are testable without Cinemachine.
    /// </summary>
    public sealed class CameraLayerStack<T> where T : class
    {
        private struct CinematicEntry
        {
            public int Id;
            public T Camera;
        }

        private readonly List<CinematicEntry> cinematics = new List<CinematicEntry>(2);
        private int nextId;

        public T Lane { get; set; }
        public T Zone { get; private set; }
        public int CinematicCount => cinematics.Count;

        public T Top
        {
            get
            {
                if (cinematics.Count > 0) return cinematics[cinematics.Count - 1].Camera;
                return Zone ?? Lane;
            }
        }

        public void SetZone(T camera) => Zone = camera;

        /// <summary>Clears the zone only if <paramref name="camera"/> still owns it.</summary>
        public bool ClearZone(T camera)
        {
            if (camera == null || Zone != camera) return false;
            Zone = null;
            return true;
        }

        public int PushCinematic(T camera)
        {
            int id = ++nextId;
            cinematics.Add(new CinematicEntry { Id = id, Camera = camera });
            return id;
        }

        /// <summary>Returns false if the lease was already released or cleared.</summary>
        public bool ReleaseCinematic(int id)
        {
            for (int i = cinematics.Count - 1; i >= 0; i--)
            {
                if (cinematics[i].Id != id) continue;
                cinematics.RemoveAt(i);
                return true;
            }
            return false;
        }

        /// <summary>Drops zone and cinematic layers (death/respawn). Outstanding leases become no-ops.</summary>
        public void ClearOverlays()
        {
            Zone = null;
            cinematics.Clear();
        }

        /// <summary>Disposable wrapper so callers own exactly one release path.</summary>
        public sealed class Lease : IDisposable
        {
            private Action<int> release;
            private readonly int id;

            public Lease(int id, Action<int> release)
            {
                this.id = id;
                this.release = release;
            }

            public void Dispose()
            {
                Action<int> r = release;
                release = null;
                r?.Invoke(id);
            }
        }
    }
}
