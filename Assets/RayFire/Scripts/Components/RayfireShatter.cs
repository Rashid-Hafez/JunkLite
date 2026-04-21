using System;
using System.Collections.Generic;
using UnityEngine;

namespace RayFire
{
    [AddComponentMenu (RFLog.sht_path)]
    [HelpURL (RFLog.sht_link)]
    public class RayfireShatter : MonoBehaviour
    {
        
        // UI
        public FragType          type      = FragType.Voronoi;
        public RFVoronoi         voronoi   = new RFVoronoi();
        public RFSplinters       splinters = new RFSplinters();
        public RFSplinters       slabs     = new RFSplinters();
        public RFRadial          radial    = new RFRadial();
        public RFHexagon         hexagon   = new RFHexagon();
        public RFCustom          custom    = new RFCustom();
        public RFMirrored        mirrored  = new RFMirrored();
        public RFSlice           slice     = new RFSlice();
        public RFBricks          bricks    = new RFBricks();
        public RFVoxels          voxels    = new RFVoxels();
        public RFTets            tets      = new RFTets();
        public RFSurface         material  = new RFSurface();
        public RFShatterCluster  clusters  = new RFShatterCluster();
        public RFShatterAdvanced advanced  = new RFShatterAdvanced();
        public RFShell           shell     = new RFShell();
        
        // Components
        public MeshRenderer        meshRenderer;
        public SkinnedMeshRenderer skinnedMeshRend;
        
        // Hidden
        public bool    colorPreview;
        public bool    scalePreview = true;
        public float   previewScale;
        public Bounds  bound;
        public bool    resetState;

        // Interactive
        [NonSerialized] public bool             interactive;
        public                 List<MeshFilter> intMfs;
        public                 List<Renderer>   intMrs;
        
        // RFEngine props TODO move to advanced
        public RFEngine                       engine;
        public List<RFShatterBatch>           batches = new List<RFShatterBatch>();
        
        /// /////////////////////////////////////////////////////////
        /// Getters
        /// /////////////////////////////////////////////////////////
        
        public Transform  CenterBias { get { return advanced.CanUseCenter == true ? advanced.centerBias.transform : transform; }}
        public Vector3    CenterPos  { get { return advanced.CanUseCenter == true ? advanced.centerBias.transform.position : transform.position; }}
        public Quaternion CenterDir  { get { return advanced.CanUseCenter == true ? advanced.centerBias.transform.rotation : transform.rotation; }}

        /// /////////////////////////////////////////////////////////
        /// Common
        /// /////////////////////////////////////////////////////////
        
        // Reset
        private void Reset()
        {
            InteractiveStop();
        }
        
        /// /////////////////////////////////////////////////////////
        /// Methods
        /// /////////////////////////////////////////////////////////
        
        // Get bounds
        public Bounds GetBound()
        {
            // Mesh renderer
            if (meshRenderer == null)
            {
                meshRenderer = GetComponent<MeshRenderer>();
                if (meshRenderer != null)
                    return meshRenderer.bounds;
            }
            else
                return meshRenderer.bounds;

            // Skinned mesh
            if (skinnedMeshRend == null)
            {
                skinnedMeshRend = GetComponent<SkinnedMeshRenderer>();
                if (skinnedMeshRend != null)
                    return skinnedMeshRend.bounds;
            }

            return new Bounds();
        }

        /// /////////////////////////////////////////////////////////
        /// Methods
        /// /////////////////////////////////////////////////////////

        // Fragment this object by shatter properties  List<GameObject>
        public void Fragment()
        {
            System.Diagnostics.Stopwatch stopWatch = new System.Diagnostics.Stopwatch();
            stopWatch.Start();
            
            // Engine
            RFEngine.FragmentShatter (this);
            
            stopWatch.Stop();
            
            RayfireMan.Log(RFLog.sht_time + stopWatch.Elapsed.TotalMilliseconds.ToString("F2") + " ms");
        }

        // Fragment by limitations
        public void LimitationFragment(RFShatterBatch batch, int ind)
        {
            RayfireShatter shat = batch.fragments[ind].gameObject.AddComponent<RayfireShatter>();
            shat.voronoi.amount = 10;

            shat.Fragment();

            if (shat.batches[0].fragments.Count > 0)
            {
                // Reparent new frags
                foreach (var frag in batch.fragments)
                    frag.transform.parent = batch.fragments[ind].parent;
                
                // Add to source batch and remove original
                batch.fragments.AddRange (shat.batches[0].fragments);
                batch.fragments.RemoveAt (ind);
                
                // Destroy original and new frags parent
                DestroyImmediate (shat.batches[0].fragRoot.gameObject);
                DestroyImmediate (shat.gameObject);
            }
        }
        
        /// /////////////////////////////////////////////////////////
        /// Copy
        /// /////////////////////////////////////////////////////////

        // Copy shatter component
        public static void CopyRootMeshShatter(RayfireRigid source, List<RayfireRigid> targets)
        {
            // No shatter
            if (source.mshDemol.sht == null)
                return;

            // Copy shatter
            for (int i = 0; i < targets.Count; i++)
            {
                targets[i].mshDemol.sht = targets[i].gameObject.AddComponent<RayfireShatter>();
                targets[i].mshDemol.sht.CopyFrom (source.mshDemol.sht);
            }
        }

        // Copy from
        void CopyFrom(RayfireShatter shatter)
        {
            type = shatter.type;

            voronoi   = new RFVoronoi (shatter.voronoi);
            splinters = new RFSplinters (shatter.splinters);
            slabs     = new RFSplinters (shatter.slabs);
            radial    = new RFRadial (shatter.radial);
            custom    = new RFCustom (shatter.custom);
            slice     = new RFSlice (shatter.slice);
            tets      = new RFTets (shatter.tets);
            
            RFSurface.Copy (material, shatter.material);
            RFShatterCluster.Copy (clusters, shatter.clusters);
            RFShatterAdvanced.Copy (advanced, shatter.advanced);
        }

        /// /////////////////////////////////////////////////////////
        /// Interactive
        /// /////////////////////////////////////////////////////////
        
        // Fragment all meshes into own mesh
        public void InteractiveStart()
        {
            // Only in Editor mode
            if (Application.isEditor == false)
                return;
            
            // Start Interactive mode
            RFEngine.InteractiveStart (this);;
        }

        // Property changed
        public void InteractiveChange()
        {
            RFEngine.InteractiveChange (this);
        }

        // Create interactively cached fragments
        public void InteractiveFragment()
        {
            RFEngine.InteractiveFragment(this);
        }
        
        // Revert original mesh
        public void InteractiveStop()
        {
            RFEngine.InteractiveStop (this);
        }
        
        // Set original renderer state
        public void OriginalRenderer(bool state)
        {
            if (meshRenderer != null)
                meshRenderer.enabled = state;
            if (skinnedMeshRend != null)
                skinnedMeshRend.enabled = state;
        }
        
        // Final preview scale
        public float PreviewScale()
        {
            if (scalePreview == false)
                return 1f;
            return Mathf.Lerp (1f, 0.3f, previewScale);
        }
        
        // Add interactive helper component
        public void AddInteractiveHelper(Transform target, bool preview)
        {
            RFInteractiveHelper helper = target.gameObject.GetComponent<RFInteractiveHelper>();
            if (helper == null)
                helper = target.gameObject.AddComponent<RFInteractiveHelper>();
                
            helper.interactive = true;
            helper.previewGo   = preview;
            helper.shatter     = this;
            helper.shatterGo   = gameObject;
        }
        
        /// /////////////////////////////////////////////////////////
        /// Getters
        /// /////////////////////////////////////////////////////////
        
        public bool HasBatches { get {
            if (batches != null && batches.Count > 0)
                return true;
            return false; }}
    }
}